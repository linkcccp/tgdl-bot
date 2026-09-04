// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

using TGBot.Config.Overlay;
using TGBot.Logging;
using TGBot.Messaging;
using TGBot.Texts.I18n;

namespace TGBot.Application;

/// <summary>
/// 启动期消费 pending-notify：认领（rename .sending 防重复）→ 按存储语言渲染并发送 →
/// 成功删除 / 失败递增计数待下次启动重试（上限后丢弃）。
/// <para>发送时机：BotService 就绪后、开始轮询前，由 <see cref="TGBot.Application.BotService"/> 调用一次。</para>
/// </summary>
public sealed class PendingNotifySender
{
    private readonly PendingNotifyStore _store;
    private readonly ITelegramClient _client;
    private readonly II18n _i18n;
    private readonly IAppLogger _logger;

    /// <summary>
    /// 初始化 <see cref="PendingNotifySender"/>。
    /// </summary>
    /// <param name="store">待通知存储。</param>
    /// <param name="client">Telegram 客户端。</param>
    /// <param name="i18n">国际化服务（按通知存储的语言渲染）。</param>
    /// <param name="logger">日志器。</param>
    public PendingNotifySender(PendingNotifyStore store, ITelegramClient client, II18n i18n, IAppLogger logger)
    {
        _store = store;
        _client = client;
        _i18n = i18n;
        _logger = logger;
    }

    /// <summary>
    /// 发送挂起的重启通知（幂等：无待通知或已超过失败上限时无操作）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>完成异步操作。</returns>
    public async Task SendPendingAsync(CancellationToken cancellationToken)
    {
        var notify = _store.Claim();
        if (notify is null)
        {
            return;
        }

        try
        {
            var text = _i18n.Get(notify.Lang, notify.TextKey, notify.Args.ToArray());
            await _client.SendMessageAsync(notify.ChatId, text, 0, null, cancellationToken).ConfigureAwait(false);
            _store.Succeed();
            _logger.Info($"重启通知已送达（{notify.TextKey} → chat {notify.ChatId}）");
        }
        catch (Exception ex)
        {
            _logger.Warn($"重启通知发送失败（第 {notify.Attempts + 1}/{PendingNotifyStore.MaxAttempts} 次）：{ex.Message}");
            if (_store.Fail())
            {
                _logger.Warn($"重启通知超过 {PendingNotifyStore.MaxAttempts} 次未送达，已丢弃");
            }
        }
    }
}
