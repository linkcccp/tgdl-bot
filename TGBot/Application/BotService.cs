// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

using TGBot.Logging;
using TGBot.Messaging;

namespace TGBot.Application;

/// <summary>
/// Bot 服务：Long Polling 主循环，带异常兜底与重启退避。
/// <para>就绪后、开始轮询前消费 pending-notify（重启生效通知，防重复发送）。</para>
/// </summary>
public sealed class BotService
{
    private readonly ITelegramClient _client;
    private readonly MessageRouter _router;
    private readonly IAppLogger _logger;
    private readonly PendingNotifySender? _notifySender;

    /// <summary>
    /// 初始化 <see cref="BotService"/>。
    /// </summary>
    /// <param name="client">Telegram 客户端。</param>
    /// <param name="router">消息路由器。</param>
    /// <param name="logger">日志器。</param>
    /// <param name="notifySender">重启通知发送器（可为空，测试注入便利）。</param>
    public BotService(ITelegramClient client, MessageRouter router, IAppLogger logger, PendingNotifySender? notifySender = null)
    {
        _client = client;
        _router = router;
        _logger = logger;
        _notifySender = notifySender;
    }

    /// <summary>
    /// 运行 Bot 直到被取消。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await PrepareAsync(cancellationToken).ConfigureAwait(false);

        // 就绪后、开始轮询前消费重启通知（防重复：rename .sending 原子认领）。
        if (_notifySender is not null)
        {
            await _notifySender.SendPendingAsync(cancellationToken).ConfigureAwait(false);
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _client.RunLongPollingAsync(
                    _router.HandleAsync,
                    (ex, _) =>
                    {
                        _logger.Error("轮询错误", ex);
                        return Task.CompletedTask;
                    },
                    cancellationToken).ConfigureAwait(false);

                // 轮询正常退出（异常情况）：退避后重启
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Error("轮询循环异常，5 秒后重试", ex);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task PrepareAsync(CancellationToken cancellationToken)
    {
        try
        {
            var username = await _client.GetBotUsernameAsync(cancellationToken).ConfigureAwait(false);
            _logger.Info($"已连接 Bot @{username}");
        }
        catch (Exception ex)
        {
            _logger.Error("无法连接 Telegram（请确认本地 Bot API Server 已启动且配置正确）", ex);
            throw;
        }

        try
        {
            await _client.DropPendingUpdatesAsync(cancellationToken).ConfigureAwait(false);
            await _client.SetCommandsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warn($"初始化指令菜单失败：{ex.Message}");
        }
    }
}
