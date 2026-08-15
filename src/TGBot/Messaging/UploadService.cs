using TGBot.Download;
using TGBot.Logging;
using TGBot.Security;

namespace TGBot.Messaging;

/// <summary>
/// 上传结果。
/// </summary>
/// <param name="SuccessCount">成功推送的会话数。</param>
/// <param name="FailedChats">失败的会话 ID 列表。</param>
public sealed record UploadResult(int SuccessCount, IReadOnlyList<long> FailedChats);

/// <summary>
/// 媒体上传服务：将下载产物推送到目标会话（含重试与状态动作）。
/// </summary>
public sealed class UploadService
{
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "mp3", "m4a", "opus", "ogg", "oga", "wav", "flac", "aac", "m4b", "mp2",
    };

    private readonly ITelegramClient _client;
    private readonly int _maxRetries;
    private readonly bool _alsoSendToRequester;
    private readonly IAppLogger _logger;

    /// <summary>
    /// 初始化 <see cref="UploadService"/>。
    /// </summary>
    /// <param name="client">Telegram 客户端。</param>
    /// <param name="maxRetries">上传失败重试次数。</param>
    /// <param name="alsoSendToRequester">是否将媒体同时发送给私聊请求者。</param>
    /// <param name="logger">日志器。</param>
    public UploadService(ITelegramClient client, int maxRetries, bool alsoSendToRequester, IAppLogger logger)
    {
        _client = client;
        _maxRetries = maxRetries;
        _alsoSendToRequester = alsoSendToRequester;
        _logger = logger;
    }

    /// <summary>
    /// 将一组媒体上传到所有目标会话（同一任务可能含多个产物，如 flac+mp3）。
    /// </summary>
    /// <param name="media">下载产物列表。</param>
    /// <param name="targetChatIds">目标会话 ID 列表。</param>
    /// <param name="requesterChatId">请求者会话 ID（私聊，可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>上传结果。</returns>
    public async Task<UploadResult> UploadAsync(
        IReadOnlyList<DownloadedMedia> media,
        IReadOnlyList<long> targetChatIds,
        long? requesterChatId,
        CancellationToken cancellationToken)
    {
        var failures = new List<long>();
        var success = 0;

        foreach (var chatId in targetChatIds)
        {
            if (await SendMediaListAsync(media, chatId, cancellationToken).ConfigureAwait(false))
            {
                success++;
            }
            else
            {
                failures.Add(chatId);
            }
        }

        if (_alsoSendToRequester && requesterChatId is { } requester &&
            !targetChatIds.Contains(requester))
        {
            if (await SendMediaListAsync(media, requester, cancellationToken).ConfigureAwait(false))
            {
                success++;
            }
        }

        return new UploadResult(success, failures);
    }

    private async Task<bool> SendMediaListAsync(IReadOnlyList<DownloadedMedia> media, long chatId, CancellationToken cancellationToken)
    {
        foreach (var item in media)
        {
            if (!await SendToChatAsync(item, chatId, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }
        }

        return true;
    }

    private async Task<bool> SendToChatAsync(DownloadedMedia media, long chatId, CancellationToken cancellationToken)
    {
        var attempts = _maxRetries + 1;
        for (var i = 0; i < attempts; i++)
        {
            try
            {
                var caption = CaptionBuilder.Build(media.RawTitle ?? media.Title, media.SourceUrl);
                var fileName = $"{media.Title}.{media.Extension}";

                await _client.SendChatActionAsync(chatId, MediaAction(media), cancellationToken).ConfigureAwait(false);

                if (media.IsAudio || AudioExtensions.Contains(media.Extension))
                {
                    await _client.SendAudioAsync(chatId, media.FilePath, fileName, caption, null, media.RawTitle, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await _client.SendVideoAsync(chatId, media.FilePath, fileName, caption, cancellationToken).ConfigureAwait(false);
                }

                _logger.Info($"已推送 {chatId}：{media.FileNameSafe()}");
                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.Warn($"上传 {chatId} 失败（第 {i + 1}/{attempts} 次）：{ex.Message}");
                if (i < attempts - 1)
                {
                    await Task.Delay(TimeSpan.FromSeconds(3 * (i + 1)), cancellationToken).ConfigureAwait(false);
                }
            }
        }

        return false;
    }

    private static BotChatAction MediaAction(DownloadedMedia media)
        => media.IsAudio || AudioExtensions.Contains(media.Extension)
            ? BotChatAction.UploadAudio
            : BotChatAction.UploadVideo;
}

/// <summary>
/// <see cref="DownloadedMedia"/> 的展示辅助。
/// </summary>
internal static class DownloadedMediaExtensions
{
    /// <summary>
    /// 安全的文件名（日志用）。
    /// </summary>
    /// <param name="media">媒体。</param>
    /// <returns>文件名。</returns>
    public static string FileNameSafe(this DownloadedMedia media)
        => $"{PathSanitizer.SanitizeFileName(media.Title)}.{media.Extension}";
}
