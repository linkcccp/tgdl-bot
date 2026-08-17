// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

using TGBot.Config;
using TGBot.Cookie;
using TGBot.Download;
using TGBot.Logging;
using TGBot.Messaging;
using TGBot.Security;
using TGBot.Texts;
using TGBot.Texts.I18n;

namespace TGBot.Application;

/// <summary>
/// 下载协调器：负责排队、并发控制、模式（视频/音频）、重试、进度通知与临时文件清理。
/// </summary>
public sealed class DownloadCoordinator
{
    private readonly IDownloader _downloader;
    private readonly DownloadGate _gate;
    private readonly JobRegistry _registry;
    private readonly TempDirManager _tempDir;
    private readonly UploadService _upload;
    private readonly ITelegramClient _client;
    private readonly CookieService _cookies;
    private readonly AppConfig _config;
    private readonly II18n _i18n;
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
    /// <param name="cookies">cookies 服务（按域名解析站点 cookie）。</param>
    /// <param name="config">配置。</param>
    /// <param name="logger">日志器。</param>
    /// <param name="i18n">国际化服务（用户消息渲染）。</param>
    public DownloadCoordinator(
        IDownloader downloader,
        DownloadGate gate,
        JobRegistry registry,
        TempDirManager tempDir,
        UploadService upload,
        ITelegramClient client,
        CookieService cookies,
        AppConfig config,
        IAppLogger logger,
        II18n i18n)
    {
        _downloader = downloader;
        _gate = gate;
        _registry = registry;
        _tempDir = tempDir;
        _upload = upload;
        _client = client;
        _cookies = cookies;
        _config = config;
        _i18n = i18n;
        _logger = logger;
    }

    /// <summary>
    /// 探测媒体是否仅音频（用于决定下载模式）。
    /// </summary>
    /// <param name="url">规范化 URL。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>仅音频返回 <see langword="true"/>；探测失败时返回 <see langword="false"/>（按含视频处理）。</returns>
    public async Task<bool> IsAudioOnlyAsync(string url, CancellationToken cancellationToken)
    {
        var jobDir = _tempDir.CreateJobDirectory();
        try
        {
            var options = BuildOptions(url, jobDir);
            var formats = await _downloader.ProbeFormatsAsync(options, cancellationToken).ConfigureAwait(false);
            return formats is not null && YtDlpFormatPicker.IsAudioOnly(formats);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Warn($"媒体分类探测失败：{ex.Message}");
            return false;
        }
        finally
        {
            _tempDir.CleanupJobDirectory(jobDir);
        }
    }

    /// <summary>
    /// 入队一个下载任务。URL 去重失败时返回 <see langword="false"/>。
    /// </summary>
    /// <param name="msg">触发消息。</param>
    /// <param name="normalizedUrl">规范化 URL。</param>
    /// <param name="mode">下载模式：<c>video</c>（合并）或 <c>audio</c>（仅音频）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>入队成功返回 <see langword="true"/>。</returns>
    public Task<bool> EnqueueAsync(InboundMessage msg, string normalizedUrl, string mode, CancellationToken cancellationToken)
    {
        if (!_registry.TryReserveUrl(normalizedUrl))
        {
            return Task.FromResult(false);
        }

        _registry.OnEnqueue();
        var position = _registry.Queued;

        _ = Task.Run(() => ProcessJobAsync(msg, normalizedUrl, mode, position, cancellationToken), CancellationToken.None);
        return Task.FromResult(true);
    }

    private async Task ProcessJobAsync(InboundMessage msg, string url, string mode, int position, CancellationToken shutdownToken)
    {
        var requesterChatId = msg.IsPrivate ? msg.ChatId : (long?)null;
        try
        {
            if (requesterChatId is not null)
            {
                await NotifyAsync(requesterChatId.Value, _i18n.Get(msg.Language, UserTexts.Queued, position), CancellationToken.None);
            }

            await using var slot = await _gate.AcquireDownloadAsync(CancellationToken.None).ConfigureAwait(false);
            _registry.OnStart();

            var jobDir = _tempDir.CreateJobDirectory();
            try
            {
                await NotifyAsync(requesterChatId, _i18n.Get(msg.Language, UserTexts.Downloading), CancellationToken.None);
                await _client.SendChatActionAsync(msg.ChatId, mode == "audio" ? BotChatAction.UploadAudio : BotChatAction.UploadVideo, CancellationToken.None);

                var mediaList = mode == "audio"
                    ? await DownloadAudioWithRetriesAsync(url, jobDir, requesterChatId, msg.Language, CancellationToken.None)
                    : await DownloadWithRetriesAsync(url, jobDir, requesterChatId, msg.Language, CancellationToken.None);

                if (requesterChatId is not null)
                {
                    await NotifyAsync(requesterChatId.Value, _i18n.Get(msg.Language, UserTexts.Uploading), CancellationToken.None);
                }

                var result = await _upload.UploadAsync(mediaList, _config.TargetChannelIds, requesterChatId, msg.Language, CancellationToken.None);

                if (result.FailedChats.Count > 0)
                {
                    _logger.Warn($"部分会话上传失败：{string.Join(", ", result.FailedChats)}");
                }

                if (requesterChatId is not null)
                {
                    var text = ComposeDoneText(mediaList, result, msg.Language);
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
            await NotifyAsync(requesterChatId, ReasonText(ex.Reason, msg.Language), CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.Error($"任务异常：{MaskUrl(url)}", ex);
            await NotifyAsync(requesterChatId, _i18n.Get(msg.Language, UserTexts.DownloadFailed), CancellationToken.None);
        }
        finally
        {
            _registry.OnFinish();
            _registry.ReleaseUrl(url);
        }
    }

    /// <summary>
    /// 按失败原因渲染用户提示（<see cref="DownloadException.UserMessage"/> 仅作内部日志，不直接发送）。
    /// </summary>
    /// <param name="reason">失败原因。</param>
    /// <param name="lang">消息语言。</param>
    /// <returns>用户提示。</returns>
    private string ReasonText(DownloadFailureReason reason, string lang)
        => reason switch
        {
            DownloadFailureReason.TooLarge => _i18n.Get(lang, UserTexts.FileTooLarge),
            DownloadFailureReason.NoDiskSpace => _i18n.Get(lang, UserTexts.NoDiskSpace),
            DownloadFailureReason.AuthRequired => _i18n.Get(lang, UserTexts.AuthRequired),
            DownloadFailureReason.FormatUnavailable => _i18n.Get(lang, UserTexts.FormatUnavailable),
            _ => _i18n.Get(lang, UserTexts.DownloadFailed),
        };

    private string ComposeDoneText(IReadOnlyList<DownloadedMedia> mediaList, UploadResult result, string lang)
    {
        var suffix = result.FailedChats.Count > 0 ? _i18n.Get(lang, UserTexts.PartialFailures) : string.Empty;
        if (mediaList.Count >= 2 && mediaList.All(m => m.IsAudio))
        {
            var flac = mediaList.FirstOrDefault(m => m.Extension == "flac");
            var mp3 = mediaList.FirstOrDefault(m => m.Extension == "mp3");
            return _i18n.Get(
                lang,
                UserTexts.AudioBundleDone,
                flac is null ? "audio.flac" : $"{flac.Title}.flac",
                mp3 is null ? "audio.mp3" : $"{mp3.Title}.mp3") + suffix;
        }

        return _i18n.Get(lang, UserTexts.UploadDone, result.SuccessCount) + suffix;
    }

    private async Task<IReadOnlyList<DownloadedMedia>> DownloadWithRetriesAsync(
        string url,
        string jobDir,
        long? requesterChatId,
        string lang,
        CancellationToken shutdownToken)
    {
        var options = BuildOptions(url, jobDir);
        var attempts = _config.DownloadRetries + 1;
        var lastError = string.Empty;
        var formatFallbackTried = false;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                var media = await _downloader.DownloadAsync(
                    options,
                    p => OnProgress(p, requesterChatId, lang),
                    shutdownToken).ConfigureAwait(false);
                return new[] { media };
            }
            catch (DownloadException ex) when (
                ex.Reason is DownloadFailureReason.TooLarge
                    or DownloadFailureReason.NoDiskSpace
                    or DownloadFailureReason.Timeout
                    or DownloadFailureReason.Cancelled
                    or DownloadFailureReason.AuthRequired)
            {
                throw;
            }
            catch (DownloadException ex) when (ex.Reason == DownloadFailureReason.FormatUnavailable)
            {
                // 可用格式不足：后台自动列出格式 → 挑最高视频+音频 → 用 ffmpeg 合并重试一次
                if (!formatFallbackTried)
                {
                    formatFallbackTried = true;
                    _logger.Warn($"可用格式不足，自动挑选最高视频/音频重试：{MaskUrl(url)}");
                    try
                    {
                        var expression = await _downloader.ProbeBestFormatAsync(options, shutdownToken).ConfigureAwait(false);
                        options = options with
                        {
                            FormatExpression = expression ?? "best",
                            MergeFormat = "mkv",
                        };
                        continue;
                    }
                    catch (Exception probeEx) when (probeEx is not OperationCanceledException)
                    {
                        _logger.Warn($"格式探测失败：{probeEx.Message}");
                        throw;
                    }
                }

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

        throw new DownloadException(DownloadFailureReason.Failed, _i18n.Get(lang, UserTexts.DownloadFailed), lastError);
    }

    private async Task<IReadOnlyList<DownloadedMedia>> DownloadAudioWithRetriesAsync(
        string url,
        string jobDir,
        long? requesterChatId,
        string lang,
        CancellationToken shutdownToken)
    {
        var options = BuildOptions(url, jobDir);
        var attempts = _config.DownloadRetries + 1;
        var lastError = string.Empty;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                return await _downloader.DownloadAudioBundleAsync(
                    options,
                    p => OnProgress(p, requesterChatId, lang),
                    shutdownToken).ConfigureAwait(false);
            }
            catch (DownloadException ex) when (
                ex.Reason is DownloadFailureReason.TooLarge
                    or DownloadFailureReason.NoDiskSpace
                    or DownloadFailureReason.Timeout
                    or DownloadFailureReason.Cancelled
                    or DownloadFailureReason.AuthRequired)
            {
                throw;
            }
            catch (DownloadException ex)
            {
                lastError = ex.Message;
                if (attempt < attempts)
                {
                    _logger.Warn($"音频下载重试 {attempt}/{attempts}：{MaskUrl(url)}");
                    await Task.Delay(TimeSpan.FromSeconds(3 * attempt), shutdownToken).ConfigureAwait(false);
                }
            }
        }

        throw new DownloadException(DownloadFailureReason.Failed, _i18n.Get(lang, UserTexts.DownloadFailed), lastError);
    }

    private DownloadOptions BuildOptions(string url, string jobDir)
    {
        var ffmpegPath = string.IsNullOrEmpty(_config.FfmpegPath) ? null : Path.GetFullPath(_config.FfmpegPath);
        return new DownloadOptions(
            url,
            jobDir,
            _config.YtDlpPath,
            string.IsNullOrEmpty(_config.FfmpegPath) ? null : Path.GetDirectoryName(ffmpegPath),
            _config.MergeFormat,
            _config.ExtractAudio,
            _config.AllowPlaylists,
            _config.MaxMediaSizeBytes,
            TimeSpan.FromSeconds(_config.DownloadTimeoutSeconds))
        {
            CookiesFile = _cookies.ResolveCookieFile(url),
            Proxy = string.IsNullOrEmpty(_config.YtDlpProxy) ? null : _config.YtDlpProxy,
            ExtraArgs = string.IsNullOrWhiteSpace(_config.YtDlpExtraArgs)
                ? null
                : _config.YtDlpExtraArgs.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            YoutubePlayerClients = string.IsNullOrEmpty(_config.YtDlpYoutubePlayerClients) ? null : _config.YtDlpYoutubePlayerClients,
            FfmpegPath = ffmpegPath,
        };
    }

    private DateTime _lastProgressSent = DateTime.MinValue;
    private double _lastProgressPercent = -1;

    private async void OnProgress(DownloadProgress p, long? requesterChatId, string lang)
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
                _i18n.Get(lang, UserTexts.DownloadProgress, percent.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), p.SpeedText ?? _i18n.Get(lang, UserTexts.Unknown)),
                0,
                null,
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
            await _client.SendMessageAsync(chatId.Value, text, 0, null, ct).ConfigureAwait(false);
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
