// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

using System.Collections.Concurrent;
using TGBot.Access;
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
/// 消息路由器：访问控制 → 指令 / 文件（cookies 上传）/ 链接校验与模式选择 → 下载任务入队。
/// <para>语言在消息入口解析一次（<see cref="UserLanguageResolver"/>），随后随消息显式传播。</para>
/// </summary>
public sealed class MessageRouter
{
    private const string CallbackPrefix = "dl:";
    private static readonly TimeSpan ChoiceTimeout = TimeSpan.FromMinutes(2);

    private readonly AccessControlService _access;
    private readonly UrlValidator _urlValidator;
    private readonly DownloadCoordinator _coordinator;
    private readonly CommandHandler _commands;
    private readonly CookieService _cookies;
    private readonly ITelegramClient _client;
    private readonly AppConfig _config;
    private readonly II18n _i18n;
    private readonly UserLanguageStore _languageStore;
    private readonly UserLanguageResolver _languageResolver;
    private readonly IAppLogger _logger;
    private readonly ConcurrentDictionary<string, PendingChoice> _pendingChoices = new();

    private sealed record PendingChoice(long ChatId, long? SenderUserId, string Url, int TriggerMessageId, string DefaultMode, string Language);

    /// <summary>
    /// 初始化 <see cref="MessageRouter"/>。
    /// </summary>
    /// <param name="access">访问控制。</param>
    /// <param name="urlValidator">URL 校验器。</param>
    /// <param name="coordinator">下载协调器。</param>
    /// <param name="commands">指令处理器。</param>
    /// <param name="cookies">cookies 服务。</param>
    /// <param name="client">Telegram 客户端。</param>
    /// <param name="config">配置。</param>
    /// <param name="logger">日志器。</param>
    /// <param name="i18n">国际化服务。</param>
    /// <param name="languageStore">用户语言存储（首次弹窗判定）。</param>
    /// <param name="languageResolver">用户语言解析器（解析链）。</param>
    public MessageRouter(
        AccessControlService access,
        UrlValidator urlValidator,
        DownloadCoordinator coordinator,
        CommandHandler commands,
        CookieService cookies,
        ITelegramClient client,
        AppConfig config,
        IAppLogger logger,
        II18n i18n,
        UserLanguageStore languageStore,
        UserLanguageResolver languageResolver)
    {
        _access = access;
        _urlValidator = urlValidator;
        _coordinator = coordinator;
        _commands = commands;
        _cookies = cookies;
        _client = client;
        _config = config;
        _i18n = i18n;
        _languageStore = languageStore;
        _languageResolver = languageResolver;
        _logger = logger;
    }

    /// <summary>
    /// 处理一条入站消息或回调。
    /// </summary>
    /// <param name="msg">入站消息。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task HandleAsync(InboundMessage msg, CancellationToken cancellationToken)
    {
        // 语言入口解析一次，随消息流动（显式传播，拒绝隐式上下文）。
        var lang = _languageResolver.Resolve(msg);
        if (!string.Equals(msg.Language, lang, StringComparison.Ordinal))
        {
            msg = msg.WithLanguage(lang);
        }

        if (msg.IsCallback)
        {
            await HandleCallbackAsync(msg, cancellationToken).ConfigureAwait(false);
            return;
        }

        // 首次私聊自动弹语言选择（去重防并发重复弹窗）。
        if (msg.IsPrivate)
        {
            await MaybePromptLanguageAsync(msg, cancellationToken).ConfigureAwait(false);
        }

        var text = msg.Text?.Trim();
        var isCommand = !string.IsNullOrEmpty(text) && text.StartsWith("/", StringComparison.Ordinal);

        if (isCommand)
        {
            await HandleCommandAsync(msg, text!, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (msg.DocumentFileId is not null)
        {
            await HandleCookieFileAsync(msg, cancellationToken).ConfigureAwait(false);
            return;
        }

        await HandleUrlAsync(msg, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleCallbackAsync(InboundMessage msg, CancellationToken cancellationToken)
    {
        var data = msg.CallbackData;
        if (string.IsNullOrEmpty(data))
        {
            return;
        }

        if (data.StartsWith("lang:", StringComparison.Ordinal))
        {
            await _commands.HandleLanguageCallbackAsync(msg, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!data.StartsWith(CallbackPrefix, StringComparison.Ordinal))
        {
            return;
        }

        var parts = data.Split(':');
        if (parts.Length != 3)
        {
            return;
        }

        var token = parts[1];
        var mode = parts[2];
        if (mode is not ("video" or "audio"))
        {
            return;
        }

        if (!_pendingChoices.TryRemove(token, out var pending))
        {
            return;
        }

        // 仅允许发起该选择的用户选择
        if (pending.ChatId != msg.ChatId || pending.SenderUserId != msg.SenderUserId)
        {
            return;
        }

        var jobMsg = new InboundMessage
        {
            ChatId = pending.ChatId,
            IsPrivate = true,
            SenderUserId = pending.SenderUserId,
            Text = pending.Url,
            TriggerMessageId = pending.TriggerMessageId,
            Language = pending.Language,
        };

        var enqueued = await _coordinator.EnqueueAsync(jobMsg, pending.Url, mode, cancellationToken).ConfigureAwait(false);
        if (!enqueued)
        {
            await SendToAsync(msg, _i18n.Get(msg.Language, UserTexts.Busy), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task HandleCookieFileAsync(InboundMessage msg, CancellationToken cancellationToken)
    {
        // 调用方已保证 DocumentFileId 非空（仅文件消息进入此路径）。
        var fileId = msg.DocumentFileId!;
        var result = await _cookies.ConsumePendingAsync(
            msg.ChatId,
            fileId,
            msg.DocumentSizeBytes,
            msg.Language,
            cancellationToken).ConfigureAwait(false);

        // 无待上传请求时静默忽略普通文件消息
        if (result is not null)
        {
            await SendToAsync(msg, result.Message, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task HandleCommandAsync(InboundMessage msg, string commandText, CancellationToken cancellationToken)
    {
        if (!msg.IsPrivate)
        {
            var decision = _access.Evaluate(TriggerArea.Private, msg.SenderUserId, msg.ChatId, msg.Language);
            await SendDeniedOrNoteAsync(msg, decision, _i18n.Get(msg.Language, UserTexts.CommandPrivateOnly), cancellationToken).ConfigureAwait(false);
            return;
        }

        var access = _access.Evaluate(TriggerArea.Private, msg.SenderUserId, msg.ChatId, msg.Language);
        if (!access.Allowed)
        {
            await SendToAsync(msg, access.Reason ?? _i18n.Get(msg.Language, UserTexts.UnauthorizedPrivate), cancellationToken).ConfigureAwait(false);
            return;
        }

        await _commands.HandleAsync(msg, commandText, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleUrlAsync(InboundMessage msg, CancellationToken cancellationToken)
    {
        var area = msg.IsPrivate ? TriggerArea.Private : TriggerArea.GroupOrChannel;
        var decision = _access.Evaluate(area, msg.SenderUserId, msg.ChatId, msg.Language);

        var candidates = UrlValidator.ExtractCandidates(msg.UrlSearchText);
        if (!decision.Allowed)
        {
            if (candidates.Count > 0)
            {
                _logger.Warn($"拒绝未授权触发：area={area}, chat={msg.ChatId}, sender={msg.SenderUserId?.ToString() ?? "-"}");
                await SendToAsync(msg, decision.Reason ?? string.Empty, cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        if (candidates.Count == 0)
        {
            if (msg.IsPrivate)
            {
                await SendToAsync(msg, _i18n.Get(msg.Language, UserTexts.NoValidUrl), cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        string? lastError = null;
        foreach (var candidate in candidates)
        {
            var result = await _urlValidator.ValidateAsync(candidate, _config.AllowPrivateUrls, cancellationToken).ConfigureAwait(false);
            if (!result.IsValid)
            {
                // ErrorKey 为资源键，按消息语言渲染（UrlValidator 保持 i18n 无关、可单测）。
                lastError = _i18n.Get(msg.Language, result.ErrorKey);
                continue;
            }

            var url = result.NormalizedUrl;

            // 探测：仅音频 → 直接走音频模式；含视频 → 私聊询问，否则默认模式
            var audioOnly = await _coordinator.IsAudioOnlyAsync(url, cancellationToken).ConfigureAwait(false);
            if (audioOnly)
            {
                await EnqueueOrNotifyAsync(msg, url, "audio", cancellationToken).ConfigureAwait(false);
                return;
            }

            if (msg.IsPrivate)
            {
                await PromptModeAsync(msg, url, cancellationToken).ConfigureAwait(false);
                return;
            }

            await EnqueueOrNotifyAsync(msg, url, _config.TgdlDefaultMode, cancellationToken).ConfigureAwait(false);
            return;
        }

        await SendToAsync(msg, lastError ?? _i18n.Get(msg.Language, UserTexts.NoValidUrl), cancellationToken).ConfigureAwait(false);
    }

    private async Task EnqueueOrNotifyAsync(InboundMessage msg, string url, string mode, CancellationToken cancellationToken)
    {
        var enqueued = await _coordinator.EnqueueAsync(msg, url, mode, cancellationToken).ConfigureAwait(false);
        if (!enqueued && msg.IsPrivate)
        {
            await SendToAsync(msg, _i18n.Get(msg.Language, UserTexts.Busy), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task PromptModeAsync(InboundMessage msg, string url, CancellationToken cancellationToken)
    {
        var token = Guid.NewGuid().ToString("N");
        var pending = new PendingChoice(msg.ChatId, msg.SenderUserId, url, msg.TriggerMessageId, _config.TgdlDefaultMode, msg.Language);
        _pendingChoices[token] = pending;

        var keyboard = new[]
        {
            new InlineButton(_i18n.Get(msg.Language, UserTexts.ModeVideoButton), $"{CallbackPrefix}{token}:video"),
            new InlineButton(_i18n.Get(msg.Language, UserTexts.ModeAudioButton), $"{CallbackPrefix}{token}:audio"),
        };
        await SendToAsync(msg, _i18n.Get(msg.Language, UserTexts.ModeChoice), keyboard, cancellationToken).ConfigureAwait(false);

        // 超时未选择 → 回退默认模式
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(ChoiceTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (_pendingChoices.TryRemove(token, out var expired))
            {
                var fallbackMsg = new InboundMessage
                {
                    ChatId = expired.ChatId,
                    IsPrivate = true,
                    SenderUserId = expired.SenderUserId,
                    Text = expired.Url,
                    TriggerMessageId = expired.TriggerMessageId,
                    Language = expired.Language,
                };
                _logger.Info($"下载选择超时，回退默认模式（{expired.DefaultMode}）：{MaskUrl(expired.Url)}");
                try
                {
                    await _coordinator.EnqueueAsync(fallbackMsg, expired.Url, expired.DefaultMode, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Warn($"超时回退入队失败：{ex.Message}");
                }
            }
        }, CancellationToken.None);
    }

    private async Task MaybePromptLanguageAsync(InboundMessage msg, CancellationToken cancellationToken)
    {
        if (msg.SenderUserId is not { } userId || _languageStore.Has(userId))
        {
            return;
        }

        // 仅在授权用户处触发：未授权用户的拒绝回复已足够，不额外弹语言选择（避免无谓打扰）。
        if (!_access.Evaluate(TriggerArea.Private, msg.SenderUserId, msg.ChatId, msg.Language).Allowed)
        {
            return;
        }

        // 内存 TryAdd 去重：防并发重复弹窗（设计文档遗留问题 3）。
        if (!_commands.RegisterLanguagePrompt(userId))
        {
            return;
        }

        await _commands.PromptLanguageAsync(msg, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendDeniedOrNoteAsync(InboundMessage msg, AccessDecision decision, string note, CancellationToken cancellationToken)
    {
        if (decision.Allowed)
        {
            await SendToAsync(msg, note, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await SendToAsync(msg, decision.Reason ?? _i18n.Get(msg.Language, UserTexts.UnauthorizedPrivate), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SendToAsync(InboundMessage msg, string text, CancellationToken cancellationToken)
        => await SendToAsync(msg, text, null, cancellationToken).ConfigureAwait(false);

    private async Task SendToAsync(InboundMessage msg, string text, IReadOnlyList<InlineButton>? keyboard, CancellationToken cancellationToken)
    {
        try
        {
            await _client.SendMessageAsync(msg.ChatId, text, msg.IsPrivate ? msg.TriggerMessageId : 0, keyboard, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warn($"回复发送失败：{ex.Message}");
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
