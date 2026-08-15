using TGBot.Access;
using TGBot.Config;
using TGBot.Cookie;
using TGBot.Download;
using TGBot.Logging;
using TGBot.Messaging;
using TGBot.Security;
using TGBot.Texts;

namespace TGBot.Application;

/// <summary>
/// 消息路由器：访问控制 → 指令 / 文件（cookies 上传）/ 链接校验 → 下载任务入队。
/// </summary>
public sealed class MessageRouter
{
    private readonly AccessControlService _access;
    private readonly UrlValidator _urlValidator;
    private readonly DownloadCoordinator _coordinator;
    private readonly CommandHandler _commands;
    private readonly CookieService _cookies;
    private readonly ITelegramClient _client;
    private readonly AppConfig _config;
    private readonly IAppLogger _logger;

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
    public MessageRouter(
        AccessControlService access,
        UrlValidator urlValidator,
        DownloadCoordinator coordinator,
        CommandHandler commands,
        CookieService cookies,
        ITelegramClient client,
        AppConfig config,
        IAppLogger logger)
    {
        _access = access;
        _urlValidator = urlValidator;
        _coordinator = coordinator;
        _commands = commands;
        _cookies = cookies;
        _client = client;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// 处理一条入站消息。
    /// </summary>
    /// <param name="msg">入站消息。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task HandleAsync(InboundMessage msg, CancellationToken cancellationToken)
    {
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

    private async Task HandleCookieFileAsync(InboundMessage msg, CancellationToken cancellationToken)
    {
        var result = await _cookies.ConsumePendingAsync(
            msg.ChatId,
            msg.DocumentFileId,
            msg.DocumentSizeBytes,
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
            var decision = _access.Evaluate(TriggerArea.Private, msg.SenderUserId, msg.ChatId);
            await SendDeniedOrNoteAsync(msg, decision, "该指令仅支持在私聊中使用。", cancellationToken).ConfigureAwait(false);
            return;
        }

        var access = _access.Evaluate(TriggerArea.Private, msg.SenderUserId, msg.ChatId);
        if (!access.Allowed)
        {
            await SendToAsync(msg, access.Reason ?? UserTexts.UnauthorizedPrivate, cancellationToken).ConfigureAwait(false);
            return;
        }

        await _commands.HandleAsync(msg, commandText, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleUrlAsync(InboundMessage msg, CancellationToken cancellationToken)
    {
        var area = msg.IsPrivate ? TriggerArea.Private : TriggerArea.GroupOrChannel;
        var decision = _access.Evaluate(area, msg.SenderUserId, msg.ChatId);

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
                await SendToAsync(msg, UserTexts.NoValidUrl, cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        string? lastError = null;
        foreach (var candidate in candidates)
        {
            var result = await _urlValidator.ValidateAsync(candidate, _config.AllowPrivateUrls, cancellationToken).ConfigureAwait(false);
            if (!result.IsValid)
            {
                lastError = result.Error;
                continue;
            }

            var enqueued = await _coordinator.EnqueueAsync(msg, result.NormalizedUrl, cancellationToken).ConfigureAwait(false);
            if (!enqueued && msg.IsPrivate)
            {
                await SendToAsync(msg, "该链接正在处理中，请稍候。", cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        await SendToAsync(msg, lastError ?? UserTexts.NoValidUrl, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendDeniedOrNoteAsync(InboundMessage msg, AccessDecision decision, string note, CancellationToken cancellationToken)
    {
        if (decision.Allowed)
        {
            await SendToAsync(msg, note, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await SendToAsync(msg, decision.Reason ?? UserTexts.UnauthorizedPrivate, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SendToAsync(InboundMessage msg, string text, CancellationToken cancellationToken)
    {
        try
        {
            await _client.SendMessageAsync(msg.ChatId, text, msg.IsPrivate ? msg.TriggerMessageId : 0, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warn($"回复发送失败：{ex.Message}");
        }
    }
}
