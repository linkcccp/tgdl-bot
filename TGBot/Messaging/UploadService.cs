// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

using TGBot.Download;
using TGBot.Logging;
using TGBot.Security;
using TGBot.Texts.I18n;

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
    private readonly II18n _i18n;
    private readonly IAppLogger _logger;

    /// <summary>
    /// 初始化 <see cref="UploadService"/>。
    /// </summary>
    /// <param name="client">Telegram 客户端。</param>
    /// <param name="maxRetries">上传失败重试次数。</param>
    /// <param name="alsoSendToRequester">是否将媒体同时发送给私聊请求者。</param>
    /// <param name="logger">日志器。</param>
    /// <param name="i18n">国际化服务（媒体说明文案渲染）。</param>
    public UploadService(ITelegramClient client, int maxRetries, bool alsoSendToRequester, IAppLogger logger, II18n i18n)
    {
        _client = client;
        _maxRetries = maxRetries;
        _alsoSendToRequester = alsoSendToRequester;
        _i18n = i18n;
        _logger = logger;
    }

    /// <summary>
    /// 将一组媒体上传到所有目标会话（同一任务可能含多个产物，如 flac+mp3）。
    /// </summary>
    /// <param name="media">下载产物列表。</param>
    /// <param name="targetChatIds">目标会话 ID 列表。</param>
    /// <param name="requesterChatId">请求者会话 ID（私聊，可空）。</param>
    /// <param name="lang">触发消息的语言（媒体说明文案使用）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>上传结果。</returns>
    public async Task<UploadResult> UploadAsync(
        IReadOnlyList<DownloadedMedia> media,
        IReadOnlyList<long> targetChatIds,
        long? requesterChatId,
        string lang,
        CancellationToken cancellationToken)
    {
        var failures = new List<long>();
        var success = 0;

        foreach (var chatId in targetChatIds)
        {
            if (await SendMediaListAsync(media, chatId, lang, cancellationToken).ConfigureAwait(false))
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
            if (await SendMediaListAsync(media, requester, lang, cancellationToken).ConfigureAwait(false))
            {
                success++;
            }
        }

        return new UploadResult(success, failures);
    }

    private async Task<bool> SendMediaListAsync(IReadOnlyList<DownloadedMedia> media, long chatId, string lang, CancellationToken cancellationToken)
    {
        foreach (var item in media)
        {
            if (!await SendToChatAsync(item, chatId, lang, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }
        }

        return true;
    }

    private async Task<bool> SendToChatAsync(DownloadedMedia media, long chatId, string lang, CancellationToken cancellationToken)
    {
        var attempts = _maxRetries + 1;
        for (var i = 0; i < attempts; i++)
        {
            try
            {
                var caption = CaptionBuilder.Build(_i18n, lang, media.RawTitle ?? media.Title, media.SourceUrl);
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
