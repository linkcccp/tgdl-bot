using TGBot.Config;
using TGBot.Download;
using TGBot.Logging;
using TGBot.Messaging;
using TGBot.Security;
using TGBot.Texts;

namespace TGBot.Application;

/// <summary>
/// 下载协调器：负责排队、并发控制、重试、进度通知与临时文件清理。
/// </summary>
public sealed class DownloadCoordinator
{
    private readonly IDownloader _downloader;
    private readonly DownloadGate _gate;
    private readonly JobRegistry _registry;
    private readonly TempDirManager _tempDir;
    private readonly UploadService _upload;
    private readonly ITelegramClient _client;
    private readonly AppConfig _config;
    private readonly IAppLogger _logger;

    /// <summary>
    /// 初始化 <see cref="DownloadCoordinator"/>。
    /// </summary>
    /// <param name="downloader">下载器。</param>
    /// <param name="gate">并发闸门。</param>
    /// <param name="registry">任务注册表。</param>
    /// <param name="tempDir">临时目录管理器。</param>
    /// <param name="upload">上传服务。</param>
    /// <param name="client">Telegram 客户端。</param>
    /// <param name="config">配置。</param>
    /// <param name="logger">日志器。</param>
    public DownloadCoordinator(
        IDownloader downloader,
        DownloadGate gate,
        JobRegistry registry,
        TempDirManager tempDir,
        UploadService upload,
        ITelegramClient client,
        AppConfig config,
        IAppLogger logger)
    {
        _downloader = downloader;
        _gate = gate;
        _registry = registry;
        _tempDir = tempDir;
        _upload = upload;
        _client = client;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// 入队一个下载任务。URL 去重失败时返回 <see langword="false"/>。
    /// </summary>
    /// <param name="msg">触发消息。</param>
    /// <param name="normalizedUrl">规范化 URL。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>入队成功返回 <see langword="true"/>。</returns>
    public Task<bool> EnqueueAsync(InboundMessage msg, string normalizedUrl, CancellationToken cancellationToken)
    {
        if (!_registry.TryReserveUrl(normalizedUrl))
        {
            return Task.FromResult(false);
        }

        _registry.OnEnqueue();
        var position = _registry.Queued;

        _ = Task.Run(() => ProcessJobAsync(msg, normalizedUrl, position, cancellationToken), CancellationToken.None);
        return Task.FromResult(true);
    }

    private async Task ProcessJobAsync(InboundMessage msg, string url, int position, CancellationToken shutdownToken)
    {
        var requesterChatId = msg.IsPrivate ? msg.ChatId : (long?)null;
        try
        {
            if (requesterChatId is not null)
            {
                await NotifyAsync(requesterChatId.Value, string.Format(UserTexts.Queued, position), CancellationToken.None);
            }

            await using var slot = await _gate.AcquireDownloadAsync(CancellationToken.None).ConfigureAwait(false);
            _registry.OnStart();

            var jobDir = _tempDir.CreateJobDirectory();
            try
            {
                await NotifyAsync(requesterChatId, UserTexts.Downloading, CancellationToken.None);
                await _client.SendChatActionAsync(msg.ChatId, BotChatAction.UploadVideo, CancellationToken.None);

                var media = await DownloadWithRetriesAsync(url, jobDir, requesterChatId, CancellationToken.None);

                if (requesterChatId is not null)
                {
                    await NotifyAsync(requesterChatId.Value, UserTexts.Uploading, CancellationToken.None);
                }

                var result = await _upload.UploadAsync(media, _config.TargetChannelIds, requesterChatId, CancellationToken.None);

                if (result.FailedChats.Count > 0)
                {
                    _logger.Warn($"部分会话上传失败：{string.Join(", ", result.FailedChats)}");
                }

                if (requesterChatId is not null)
                {
                    var text = result.FailedChats.Count > 0
                        ? UserTexts.UploadDone.Format(result.SuccessCount) + " 部分会话失败，请查看日志。"
                        : UserTexts.UploadDone.Format(result.SuccessCount);
                    await NotifyAsync(requesterChatId.Value, text, CancellationToken.None);
                }
            }
            finally
            {
                _tempDir.CleanupJobDirectory(jobDir);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Warn($"任务取消：{MaskUrl(url)}");
        }
        catch (DownloadException ex)
        {
            _logger.Warn($"任务失败：{MaskUrl(url)}，{ex.Message}");
            await NotifyAsync(requesterChatId, ex.UserMessage, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.Error($"任务异常：{MaskUrl(url)}", ex);
            await NotifyAsync(requesterChatId, UserTexts.DownloadFailed, CancellationToken.None);
        }
        finally
        {
            _registry.OnFinish();
            _registry.ReleaseUrl(url);
        }
    }

    private async Task<DownloadedMedia> DownloadWithRetriesAsync(
        string url,
        string jobDir,
        long? requesterChatId,
        CancellationToken shutdownToken)
    {
        var options = new DownloadOptions(
            url,
            jobDir,
            _config.YtDlpPath,
            string.IsNullOrEmpty(_config.FfmpegPath) ? null : Path.GetDirectoryName(Path.GetFullPath(_config.FfmpegPath)),
            _config.MergeFormat,
            _config.ExtractAudio,
            _config.AllowPlaylists,
            _config.MaxMediaSizeBytes,
            TimeSpan.FromSeconds(_config.DownloadTimeoutSeconds));

        var attempts = _config.DownloadRetries + 1;
        var lastError = string.Empty;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                return await _downloader.DownloadAsync(
                    options,
                    p => OnProgress(p, requesterChatId),
                    shutdownToken).ConfigureAwait(false);
            }
            catch (DownloadException ex) when (
                ex.Reason is DownloadFailureReason.TooLarge or DownloadFailureReason.NoDiskSpace or DownloadFailureReason.Timeout or DownloadFailureReason.Cancelled)
            {
                throw;
            }
            catch (DownloadException ex)
            {
                lastError = ex.Message;
                if (attempt < attempts)
                {
                    _logger.Warn($"下载重试 {attempt}/{attempts}：{MaskUrl(url)}");
                    await Task.Delay(TimeSpan.FromSeconds(3 * attempt), shutdownToken).ConfigureAwait(false);
                }
            }
        }

        throw new DownloadException(DownloadFailureReason.Failed, UserTexts.DownloadFailed, lastError);
    }

    private DateTime _lastProgressSent = DateTime.MinValue;
    private double _lastProgressPercent = -1;

    private async void OnProgress(DownloadProgress p, long? requesterChatId)
    {
        if (requesterChatId is null || p.Percent is not { } percent)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (now - _lastProgressSent < TimeSpan.FromSeconds(10) && percent - _lastProgressPercent < 10)
        {
            return;
        }

        _lastProgressSent = now;
        _lastProgressPercent = percent;
        try
        {
            await _client.SendMessageAsync(
                requesterChatId.Value,
                string.Format(UserTexts.DownloadProgress, percent.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), p.SpeedText ?? "未知"),
                0,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // 进度通知失败不影响任务
        }
    }

    private async Task NotifyAsync(long? chatId, string text, CancellationToken ct)
    {
        if (chatId is null)
        {
            return;
        }

        try
        {
            await _client.SendMessageAsync(chatId.Value, text, 0, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warn($"通知发送失败：{ex.Message}");
        }
    }

    private static string MaskUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            return $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}";
        }
        catch
        {
            return "<无效URL>";
        }
    }
}

/// <summary>
/// 字符串格式化辅助。
/// </summary>
internal static class FormatExtensions
{
    /// <summary>
    /// 格式化模板。
    /// </summary>
    /// <param name="template">模板。</param>
    /// <param name="args">参数。</param>
    /// <returns>格式化结果。</returns>
    public static string Format(this string template, params object[] args) => string.Format(template, args);
}
