// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

using System.Text.Json;
using TGBot.Application;
using TGBot.Config;
using TGBot.Config.Overlay;
using TGBot.Logging;
using TGBot.Messaging;
using TGBot.Texts.I18n;
using Xunit;

namespace TGBot.Tests;

/// <summary>
/// Overlay 存储、配置合并、白名单合并与 pending-notify 的单元测试。
/// </summary>
public class OverlayTests : IDisposable
{
    private readonly string _stateDir = Path.Combine(Path.GetTempPath(), "tgdl-ov-" + Guid.NewGuid().ToString("N")[..8]);

    private OverlayStore NewStore() => new(_stateDir);

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_stateDir))
            {
                Directory.Delete(_stateDir, recursive: true);
            }
        }
        catch (IOException)
        {
            // 清理失败不影响断言结果
        }
    }

    private static AppConfig BaseConfig() => new()
    {
        BotToken = "123:abc",
        LocalApiBaseUrl = "http://127.0.0.1:8081",
        TargetChannelIds = new long[] { -100111 },
        AllowedUserIds = new long[] { 1000 },
        DownloadTempDir = "/tmp/tgdl",
        MaxConcurrentDownloads = 2,
    };

    // —— OverlayStore ——

    [Fact]
    public void Store_ConfigRoundtrip()
    {
        var store = NewStore();
        Assert.Empty(store.LoadConfig());

        Assert.True(store.SetConfigValue("MaxConcurrentDownloads", "5"));
        Assert.Equal("5", store.LoadConfig()["MaxConcurrentDownloads"]);

        Assert.True(store.RemoveConfigKey("MaxConcurrentDownloads"));
        Assert.Empty(store.LoadConfig());
    }

    [Fact]
    public void Store_RemoveMissingKey_ReturnsFalse()
    {
        var store = NewStore();
        Assert.False(store.RemoveConfigKey("NoSuchKey"));
    }

    [Fact]
    public void Store_ClearConfig_Works()
    {
        var store = NewStore();
        store.SetConfigValue("A", "1");
        store.SetConfigValue("B", "2");

        Assert.True(store.ClearConfig());
        Assert.Empty(store.LoadConfig());
    }

    [Fact]
    public void Store_AtomicWrite_NoTempLeftover()
    {
        var store = NewStore();
        store.SetConfigValue("A", "1");
        store.RemoveConfigKey("A");

        Assert.Empty(Directory.GetFiles(_stateDir, "*.tmp"));
        Assert.Empty(Directory.GetFiles(_stateDir, "*.sending"));
    }

    [Fact]
    public void Store_StateFiles_Have0600Mode()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Unix 权限仅 Linux/macOS 有意义
        }

        var store = NewStore();
        store.SetConfigValue("A", "1");
        store.AddAccessUser(5000);

        var configPath = Path.Combine(_stateDir, OverlayStore.ConfigFileName);
        var accessPath = Path.Combine(_stateDir, OverlayStore.AccessFileName);
        var expected = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        Assert.Equal(expected, File.GetUnixFileMode(configPath) & ~UnixFileMode.OtherExecute);
        Assert.Equal(expected, File.GetUnixFileMode(accessPath) & ~UnixFileMode.OtherExecute);
    }

    [Fact]
    public void Notify_StateFile_Has0600Mode()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var store = new PendingNotifyStore(_stateDir);
        Assert.True(store.Save(NewNotify()));

        var expected = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        Assert.Equal(expected, File.GetUnixFileMode(Path.Combine(_stateDir, PendingNotifyStore.FileName)) & ~UnixFileMode.OtherExecute);
    }

    [Fact]
    public void Store_CorruptConfigFile_LoadsEmpty()
    {
        Directory.CreateDirectory(_stateDir);
        File.WriteAllText(Path.Combine(_stateDir, OverlayStore.ConfigFileName), "{ not valid json ]");

        var store = NewStore();
        Assert.Empty(store.LoadConfig());
    }

    [Fact]
    public void Store_CorruptConfigFile_RecoversOnNextWrite()
    {
        // 损坏文件按空覆盖处理，且不阻塞后续写入：set 后文件恢复为合法 JSON 并可再读
        Directory.CreateDirectory(_stateDir);
        var path = Path.Combine(_stateDir, OverlayStore.ConfigFileName);
        File.WriteAllText(path, "{ broken ]");

        var store = NewStore();
        Assert.Empty(store.LoadConfig());
        Assert.True(store.SetConfigValue("MaxConcurrentDownloads", "4"));

        var reloaded = NewStore().LoadConfig();
        Assert.Equal("4", reloaded["MaxConcurrentDownloads"]);
        using (var doc = JsonDocument.Parse(File.ReadAllText(path)))
        {
            Assert.Equal("4", doc.RootElement.GetProperty("MaxConcurrentDownloads").GetString());
        }
    }

    [Fact]
    public void Store_Access_AddDedupRemove()
    {
        var store = NewStore();
        Assert.True(store.AddAccessUser(5000));
        Assert.False(store.AddAccessUser(5000));
        Assert.True(store.AddAccessChannel(-100999));
        Assert.False(store.AddAccessChannel(-100999));

        var access = store.LoadAccess();
        Assert.Equal(new long[] { 5000 }, access.ExtraAllowedUsers);
        Assert.Equal(new long[] { -100999 }, access.ExtraTargetChannels);

        Assert.True(store.RemoveAccessUser(5000));
        Assert.False(store.RemoveAccessUser(5000));
        Assert.Empty(store.LoadAccess().ExtraAllowedUsers);
    }

    [Fact]
    public void Store_CorruptAccessFile_LoadsEmpty()
    {
        Directory.CreateDirectory(_stateDir);
        File.WriteAllText(Path.Combine(_stateDir, OverlayStore.AccessFileName), "not json");

        var access = NewStore().LoadAccess();
        Assert.Empty(access.ExtraAllowedUsers);
        Assert.Empty(access.ExtraTargetChannels);
    }

    // —— OverlayApplier ——

    [Fact]
    public void Applier_AppliesValidKeys()
    {
        var overlay = new Dictionary<string, string>
        {
            ["MaxConcurrentDownloads"] = "8",
            ["TgdlLanguage"] = "zh",
            ["LogFile"] = "/var/log/tgdl.log",
        };

        var applied = OverlayApplier.Apply(BaseConfig(), overlay, out var warnings);

        Assert.Empty(warnings);
        Assert.Equal(8, applied.MaxConcurrentDownloads);
        Assert.Equal("zh", applied.TgdlLanguage);
        Assert.Equal("/var/log/tgdl.log", applied.LogFile);
        Assert.Equal("http://127.0.0.1:8081", applied.LocalApiBaseUrl); // 未覆盖项保持 base
    }

    [Fact]
    public void Applier_SkipsLockedAndUnknownKeys_WithWarnings()
    {
        var overlay = new Dictionary<string, string>
        {
            ["BotToken"] = "evil:token",
            ["AllowedUserIds"] = "1,2",
            ["StateDir"] = "/tmp/other-state",
            ["NoSuchKey"] = "x",
        };

        var applied = OverlayApplier.Apply(BaseConfig(), overlay, out var warnings);

        Assert.Equal("123:abc", applied.BotToken);
        Assert.Equal(new long[] { 1000 }, applied.AllowedUserIds);
        Assert.Equal(string.Empty, applied.StateDir); // 状态目录为安装锁键，overlay 覆盖被跳过
        Assert.Equal(4, warnings.Count); // 3 个锁键 + 1 个未知键
    }

    [Fact]
    public void Applier_NormalizesValues()
    {
        var overlay = new Dictionary<string, string>
        {
            ["TgdlDefaultMode"] = "VIDEO",
            ["TgdlLanguage"] = "AUTO",
            ["LocalApiBaseUrl"] = "http://127.0.0.1:8081/",
            ["LogFile"] = "",
        };

        var applied = OverlayApplier.Apply(BaseConfig(), overlay, out _);

        Assert.Equal("video", applied.TgdlDefaultMode);
        Assert.Equal("auto", applied.TgdlLanguage);
        Assert.Equal("http://127.0.0.1:8081", applied.LocalApiBaseUrl);
        Assert.Null(applied.LogFile);
    }

    [Fact]
    public void Applier_QuotedValues_AreNormalizedBeforeApply()
    {
        // 含引号/首尾空白的覆盖值：与 config.conf 解析一致先归一化再应用（防引号值错乱）。
        var overlay = new Dictionary<string, string>
        {
            ["MergeFormat"] = "\"mp4/mkv\"",
            ["DownloadTempDir"] = " \"/tmp/tgdl with space\" ",
        };

        var applied = OverlayApplier.Apply(BaseConfig(), overlay, out var warnings);

        Assert.Empty(warnings);
        Assert.Equal("mp4/mkv", applied.MergeFormat);
        Assert.Equal("/tmp/tgdl with space", applied.DownloadTempDir);
    }

    // —— AccessListMerge ——

    [Fact]
    public void Merge_UnionsAndDedups_WithSources()
    {
        var result = AccessListMerge.Merge(
            new long[] { 1000, 2000 },
            new long[] { -100111, -100222 },
            new AccessOverlayData(new long[] { 2000, 3000 }, new long[] { -100222, -100333 }));

        Assert.Equal(new long[] { 1000, 2000, 3000 }, result.UserIds);
        // 数值升序：-100333 < -100222 < -100111
        Assert.Equal(new long[] { -100333, -100222, -100111 }, result.ChannelIds);

        var user2000 = Assert.Single(result.Users, e => e.Id == 2000);
        Assert.Equal(AccessEntrySource.Config, user2000.Source);
        var user3000 = Assert.Single(result.Users, e => e.Id == 3000);
        Assert.Equal(AccessEntrySource.Overlay, user3000.Source);
    }

    [Fact]
    public void Merge_EmptyOverlay_ReturnsConfigOnly()
    {
        var result = AccessListMerge.Merge(new long[] { 1 }, new long[] { -2 }, AccessOverlayData.Empty);

        Assert.Equal(new long[] { 1 }, result.UserIds);
        Assert.Equal(new long[] { -2 }, result.ChannelIds);
    }

    // —— PendingNotifyStore ——

    [Fact]
    public void Notify_SaveClaimSucceed_Deletes()
    {
        var store = new PendingNotifyStore(_stateDir);
        var notify = NewNotify();

        Assert.True(store.Save(notify));
        Assert.True(File.Exists(Path.Combine(_stateDir, PendingNotifyStore.FileName)));

        var claimed = store.Claim();
        Assert.NotNull(claimed);
        Assert.Equal(1000, claimed.ChatId);
        Assert.Equal("ConfigApplied", claimed.TextKey);
        Assert.Equal("zh", claimed.Lang);
        Assert.False(File.Exists(Path.Combine(_stateDir, PendingNotifyStore.FileName)));
        Assert.True(File.Exists(Path.Combine(_stateDir, PendingNotifyStore.SendingFileName)));

        store.Succeed();
        Assert.False(File.Exists(Path.Combine(_stateDir, PendingNotifyStore.SendingFileName)));
    }

    [Fact]
    public void Notify_Claim_ResumesFromSendingAfterCrash()
    {
        var store = new PendingNotifyStore(_stateDir);
        store.Save(NewNotify());

        // 认领：pending → .sending；若发送前崩溃，.sending 保留，下次启动可恢复（不丢通知）
        var first = store.Claim();
        Assert.NotNull(first);
        Assert.False(File.Exists(Path.Combine(_stateDir, PendingNotifyStore.FileName)));
        Assert.True(File.Exists(Path.Combine(_stateDir, PendingNotifyStore.SendingFileName)));

        var resumed = store.Claim();
        Assert.NotNull(resumed);
        Assert.Equal(0, resumed.Attempts);
        Assert.Equal(first!.TextKey, resumed.TextKey);
    }

    [Fact]
    public void Notify_Fail_IncrementsAttempts_ThenDrops()
    {
        var store = new PendingNotifyStore(_stateDir);
        store.Save(NewNotify());

        PendingNotify? last = null;
        for (var i = 0; i < PendingNotifyStore.MaxAttempts; i++)
        {
            last = store.Claim();
            Assert.NotNull(last);
            Assert.Equal(i, last.Attempts);
            store.Fail();
        }

        // 第 3 次失败后达到上限，丢弃：再 Claim 为空，且无任何文件残留
        Assert.Equal(PendingNotifyStore.MaxAttempts - 1, last!.Attempts);
        Assert.Null(store.Claim());
        Assert.Empty(Directory.GetFiles(_stateDir, "pending-notify*"));
    }

    [Fact]
    public void Notify_NoPending_ClaimReturnsNull()
    {
        var store = new PendingNotifyStore(_stateDir);
        Assert.Null(store.Claim());
    }

    private static PendingNotify NewNotify() => new(
        ChatId: 1000,
        TextKey: "ConfigApplied",
        Args: new[] { "MaxConcurrentDownloads" },
        Lang: "zh",
        CreatedAt: DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        Attempts: 0);

    // —— PendingNotifySender ——

    [Fact]
    public async Task Sender_Success_DeletesPending()
    {
        var client = new FakeTelegramClient();
        var store = new PendingNotifyStore(_stateDir);
        store.Save(NewNotify());

        var sender = new PendingNotifySender(store, client, TestI18n.Instance, NullLogger.Instance);
        await sender.SendPendingAsync(CancellationToken.None);

        var sent = Assert.Single(client.Messages);
        Assert.Equal(1000, sent.ChatId);
        Assert.Equal(TestI18n.Zh("ConfigApplied", "MaxConcurrentDownloads"), sent.Text);
        Assert.Empty(Directory.GetFiles(_stateDir, "pending-notify*"));
    }

    [Fact]
    public async Task Sender_Failure_RetainsInSending_AndCounts()
    {
        var client = new ThrowingTelegramClient();
        var store = new PendingNotifyStore(_stateDir);
        store.Save(NewNotify());

        var sender = new PendingNotifySender(store, client, TestI18n.Instance, NullLogger.Instance);
        await sender.SendPendingAsync(CancellationToken.None);

        // 发送失败：通知保留在 .sending（崩溃可恢复），Attempts 递增为 1
        Assert.True(File.Exists(Path.Combine(_stateDir, PendingNotifyStore.SendingFileName)));
        var claimed = store.Claim();
        Assert.NotNull(claimed);
        Assert.Equal(1, claimed.Attempts);
    }
}

/// <summary>
/// 发送总是失败的客户端（pending-notify 失败路径测试用）。
/// </summary>
public sealed class ThrowingTelegramClient : ITelegramClient
{
    /// <inheritdoc />
    public Task SendMessageAsync(long chatId, string text, int replyToMessageId, IReadOnlyList<InlineButton>? inlineKeyboard, CancellationToken cancellationToken)
        => throw new IOException("模拟发送失败");

    /// <inheritdoc />
    public Task<string> GetBotUsernameAsync(CancellationToken cancellationToken) => throw new NotImplementedException();

    /// <inheritdoc />
    public Task SendChatActionAsync(long chatId, BotChatAction action, CancellationToken cancellationToken) => throw new NotImplementedException();

    /// <inheritdoc />
    public Task SendVideoAsync(long chatId, string filePath, string fileName, string? caption, CancellationToken cancellationToken) => throw new NotImplementedException();

    /// <inheritdoc />
    public Task SendAudioAsync(long chatId, string filePath, string fileName, string? caption, string? performer, string? title, CancellationToken cancellationToken) => throw new NotImplementedException();

    /// <inheritdoc />
    public Task SendDocumentAsync(long chatId, string filePath, string fileName, string? caption, CancellationToken cancellationToken) => throw new NotImplementedException();

    /// <inheritdoc />
    public Task SetCommandsAsync(CancellationToken cancellationToken) => throw new NotImplementedException();

    /// <inheritdoc />
    public Task DropPendingUpdatesAsync(CancellationToken cancellationToken) => throw new NotImplementedException();

    /// <inheritdoc />
    public Task DownloadFileAsync(string fileId, string destinationPath, CancellationToken cancellationToken) => throw new NotImplementedException();

    /// <inheritdoc />
    public Task RunLongPollingAsync(
        Func<InboundMessage, CancellationToken, Task> onUpdate,
        Func<Exception, CancellationToken, Task> onPollError,
        CancellationToken cancellationToken) => throw new NotImplementedException();
}
