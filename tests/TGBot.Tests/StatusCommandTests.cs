// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

using TGBot.Application;
using TGBot.Messaging;
using Xunit;

namespace TGBot.Tests;

/// <summary>
/// /status 指令链路测试：bot 自身版本行（tgdl-bot vX.Y.Z）与模板完整性。
/// </summary>
public class StatusCommandTests
{
    [Fact]
    public async Task Status_ShowsBotVersionLine_AsFirstLine()
    {
        var client = new FakeTelegramClient();
        var router = MessageRouterTests.Build(client, new FakeDownloader(), out _);

        await router.HandleAsync(Dm(1000, "/status"), CancellationToken.None);

        var versionLine = TestI18n.Zh("StatusBotVersion", AppInfo.Version) + "\n";
        Assert.Contains(client.Messages, m => m.Text.StartsWith(versionLine, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Status_ContainsTemplateSections()
    {
        var client = new FakeTelegramClient();
        var router = MessageRouterTests.Build(client, new FakeDownloader(), out _);

        await router.HandleAsync(Dm(1000, "/status"), CancellationToken.None);

        var text = Assert.Single(client.Messages).Text;
        // 未配置 yt-dlp/ffmpeg 路径 → 工具版本显示"未知"（不触发真实进程调用）
        Assert.Contains(TestI18n.Zh("Unknown"), text, StringComparison.Ordinal);
        Assert.Contains(TestI18n.Zh("UptimeMinutes", 0), text, StringComparison.Ordinal);
    }

    private static InboundMessage Dm(long userId, string text) => new()
    {
        ChatId = userId,
        IsPrivate = true,
        SenderUserId = userId,
        Text = text,
        TriggerMessageId = 5,
        Language = "zh",
    };
}