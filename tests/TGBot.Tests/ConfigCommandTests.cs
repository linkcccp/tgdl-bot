// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

using System.Text.Json;
using TGBot.Access;
using TGBot.Application;
using TGBot.Config;
using TGBot.Config.Overlay;
using TGBot.Messaging;
using TGBot.Texts.I18n;
using Xunit;

namespace TGBot.Tests;

/// <summary>
/// /config、/access 命令链路测试：白名单门禁、overlay 落盘、pending-notify 与重启触发。
/// </summary>
public class ConfigCommandTests
{
    private AppConfig? _config;

    private MessageRouter Build(FakeTelegramClient client, Action? onRestart = null, TimeSpan? restartThrottleWindow = null, AppConfig? config = null, IReadOnlyDictionary<string, string>? configRawValues = null)
    {
        var router = MessageRouterTests.Build(client, new FakeDownloader(), out _, config: config, onRestart: onRestart, onConfig: c => _config = c, restartThrottleWindow: restartThrottleWindow, configRawValues: configRawValues);
        return router;
    }

    private string StateDir => Path.Combine(_config!.DownloadTempDir, "state");

    [Fact]
    public async Task Config_Set_Valid_WritesOverlay_Notifies_AndRestarts()
    {
        var client = new FakeTelegramClient();
        var restartCount = 0;
        var router = Build(client, onRestart: () => restartCount++);

        await router.HandleAsync(Dm(1000, "/config set MaxConcurrentDownloads 5"), CancellationToken.None);

        Assert.Contains(client.Messages, m => m.Text == TestI18n.Zh("ConfigSetApplied", "MaxConcurrentDownloads"));
        Assert.Equal(1, restartCount);
        Assert.True(File.Exists(Path.Combine(StateDir, "config-overlay.json")));
        using (var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(StateDir, "config-overlay.json"))))
        {
            Assert.Equal("5", doc.RootElement.GetProperty("MaxConcurrentDownloads").GetString());
        }

        Assert.True(File.Exists(Path.Combine(StateDir, "pending-notify.json")));
        using (var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(StateDir, "pending-notify.json"))))
        {
            Assert.Equal("ConfigApplied", doc.RootElement.GetProperty("TextKey").GetString());
            Assert.Equal("MaxConcurrentDownloads", doc.RootElement.GetProperty("Args")[0].GetString());
            Assert.Equal("zh", doc.RootElement.GetProperty("Lang").GetString());
        }
    }

    [Fact]
    public async Task Config_Set_Invalid_Rejected_NoOverlay_NoRestart()
    {
        var client = new FakeTelegramClient();
        var restarted = false;
        var router = Build(client, onRestart: () => restarted = true);

        await router.HandleAsync(Dm(1000, "/config set MaxConcurrentDownloads 99"), CancellationToken.None);

        var message = Assert.Single(client.Messages);
        Assert.StartsWith(TestI18n.Zh("ConfigRejected", ""), message.Text, StringComparison.Ordinal);
        Assert.Contains("Config error:", message.Text, StringComparison.Ordinal);
        Assert.False(restarted);
        Assert.False(File.Exists(Path.Combine(StateDir, "config-overlay.json")));
    }

    [Fact]
    public async Task Config_Set_LockedKey_Rejected()
    {
        var client = new FakeTelegramClient();
        var restarted = false;
        var router = Build(client, onRestart: () => restarted = true);

        await router.HandleAsync(Dm(1000, "/config set BotToken xxx"), CancellationToken.None);

        Assert.Contains(client.Messages, m => m.Text == TestI18n.Zh("ConfigLockedKey", "BotToken"));
        Assert.False(restarted);
    }

    [Fact]
    public async Task Config_Set_StateDir_LockedKey_Rejected()
    {
        // StateDir 为安装锁键：状态目录不应由 bot 远程改动（防状态分裂），拒绝且不重启
        var client = new FakeTelegramClient();
        var restartCount = 0;
        var router = Build(client, onRestart: () => restartCount++);

        await router.HandleAsync(Dm(1000, "/config set StateDir /tmp/other-state"), CancellationToken.None);

        Assert.Contains(client.Messages, m => m.Text == TestI18n.Zh("ConfigLockedKey", "StateDir"));
        Assert.Equal(0, restartCount);
        Assert.False(File.Exists(Path.Combine(StateDir, "config-overlay.json")));
    }

    [Fact]
    public async Task Config_Set_UnknownKey_Rejected()
    {
        var client = new FakeTelegramClient();
        var router = Build(client);

        await router.HandleAsync(Dm(1000, "/config set NoSuchKey 1"), CancellationToken.None);

        Assert.Contains(client.Messages, m => m.Text == TestI18n.Zh("ConfigUnknownKey", "NoSuchKey"));
    }

    [Fact]
    public async Task Config_Set_RiskKey_AppendsWarning()
    {
        var client = new FakeTelegramClient();
        var router = Build(client);

        await router.HandleAsync(Dm(1000, "/config set DownloadTempDir /tmp/other-dl"), CancellationToken.None);

        var ack = TestI18n.Zh("ConfigSetApplied", "DownloadTempDir") + "\n" + TestI18n.Zh("ConfigRiskWarning");
        Assert.Contains(client.Messages, m => m.Text == ack);
    }

    [Fact]
    public async Task Config_Set_QuotedValue_StoresNormalized_NoQuoteLeak()
    {
        // 引号往返：校验与落盘必须一致（同一次归一化），重启后解析不崩溃、值不带引号。
        // 注意：mkv 与默认值 mp4/mkv 不同，避免命中同值去重（P2-3）而走 NoChange 路径。
        var client = new FakeTelegramClient();
        var router = Build(client);

        await router.HandleAsync(Dm(1000, "/config set MergeFormat \"mkv\""), CancellationToken.None);

        using (var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(StateDir, "config-overlay.json"))))
        {
            Assert.Equal("mkv", doc.RootElement.GetProperty("MergeFormat").GetString());
        }
    }

    [Fact]
    public async Task Config_Set_QuotedLocalApiBaseUrl_StoresNormalized_NoQuoteLeak()
    {
        // P1-1 验收：LocalApiBaseUrl "http://x" 引号值 → 校验通过 → overlay 落盘为归一化无引号值 → 重启后解析一致（不崩溃）。
        // 当前生效值为 9999（与目标不同），避免命中同值去重（P2-3）而走 NoChange 路径。
        var client = new FakeTelegramClient();
        var restartCount = 0;
        var router = Build(client, onRestart: () => restartCount++, config: new AppConfig
        {
            BotToken = "123:abc",
            LocalApiBaseUrl = "http://127.0.0.1:9999",
            TargetChannelIds = new long[] { -100111 },
            AllowedUserIds = new long[] { 1000 },
            DownloadTempDir = Path.Combine(Path.GetTempPath(), "tgdl-cfgq-" + Guid.NewGuid().ToString("N")[..6]),
        });

        await router.HandleAsync(Dm(1000, "/config set LocalApiBaseUrl \"http://127.0.0.1:8081\""), CancellationToken.None);

        // 校验通过：收到应用回执（LocalApiBaseUrl 为风险键，附警告）并触发重启
        var ack = TestI18n.Zh("ConfigSetApplied", "LocalApiBaseUrl") + "\n" + TestI18n.Zh("ConfigRiskWarning");
        Assert.Contains(client.Messages, m => m.Text == ack);
        Assert.Equal(1, restartCount);

        // overlay 落盘值归一化（无引号残留）
        using (var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(StateDir, "config-overlay.json"))))
        {
            Assert.Equal("http://127.0.0.1:8081", doc.RootElement.GetProperty("LocalApiBaseUrl").GetString());
        }

        // 重启后解析：overlay 应用结果与 config.conf 解析语义一致（引号不残留、不崩溃）
        var overlay = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(StateDir, "config-overlay.json")))!;
        var applied = OverlayApplier.Apply(
            new AppConfig
            {
                BotToken = "123:abc",
                LocalApiBaseUrl = "http://127.0.0.1:8081",
                TargetChannelIds = new long[] { -100111 },
                AllowedUserIds = new long[] { 1000 },
                DownloadTempDir = "/tmp/tgdl",
            },
            overlay,
            out _);
        Assert.Equal("http://127.0.0.1:8081", applied.LocalApiBaseUrl);
    }

    [Fact]
    public async Task Config_Set_QuotedValueWithSpaces_RoundTripsThroughOverlayApplier()
    {
        // 含空格引号值：归一化后落盘 → overlay 应用结果与 config.conf 解析语义一致。
        var client = new FakeTelegramClient();
        var router = Build(client);

        await router.HandleAsync(Dm(1000, "/config set LogFile \"/var/log/tgdl bot.log\""), CancellationToken.None);

        var overlay = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(StateDir, "config-overlay.json")))!;
        var applied = OverlayApplier.Apply(
            new AppConfig
            {
                BotToken = "123:abc",
                LocalApiBaseUrl = "http://127.0.0.1:8081",
                TargetChannelIds = new long[] { -100111 },
                AllowedUserIds = new long[] { 1000 },
                DownloadTempDir = "/tmp/tgdl",
            },
            overlay,
            out _);

        Assert.Equal("/var/log/tgdl bot.log", applied.LogFile);
    }

    [Fact]
    public async Task Config_List_ShowsEffectiveValuesAndSources()
    {
        var client = new FakeTelegramClient();
        var router = Build(client);

        await router.HandleAsync(Dm(1000, "/config list"), CancellationToken.None);

        var expectedLine = TestI18n.Zh("ConfigListLine", "MaxConcurrentDownloads", "2", TestI18n.Zh("ConfigSourceDefault"));
        Assert.Contains(client.Messages, m => m.Text.Contains(expectedLine, StringComparison.Ordinal));
        Assert.Contains(client.Messages, m => m.Text.Contains(TestI18n.Zh("ValueEmpty"), StringComparison.Ordinal)); // 未配置路径键 → 显示占位
    }

    [Fact]
    public async Task Config_List_MasksSensitiveCredentials()
    {
        var client = new FakeTelegramClient();
        var router = Build(client, config: new AppConfig
        {
            BotToken = "123:abc",
            LocalApiBaseUrl = "http://127.0.0.1:8081?api_id=123456&api_hash=deadbeef",
            TargetChannelIds = new long[] { -100111 },
            AllowedUserIds = new long[] { 1000 },
            DownloadTempDir = "/tmp/tgdl",
            YtDlpProxy = "http://user:s3cret@127.0.0.1:8080",
        });

        await router.HandleAsync(Dm(1000, "/config list"), CancellationToken.None);

        var listText = Assert.Single(client.Messages).Text;
        Assert.Contains(TestI18n.Zh("ConfigListLine", "YtDlpProxy", "http://***@127.0.0.1:8080", TestI18n.Zh("ConfigSourceDefault")), listText, StringComparison.Ordinal);
        Assert.Contains(TestI18n.Zh("ConfigListLine", "LocalApiBaseUrl", "http://127.0.0.1:8081?***", TestI18n.Zh("ConfigSourceDefault")), listText, StringComparison.Ordinal);
        Assert.DoesNotContain("s3cret", listText, StringComparison.Ordinal);
        Assert.DoesNotContain("123456", listText, StringComparison.Ordinal);
        Assert.DoesNotContain("deadbeef", listText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Config_List_AfterSet_ShowsOverlaySource()
    {
        var client = new FakeTelegramClient();
        var router = Build(client);

        await router.HandleAsync(Dm(1000, "/config set MaxConcurrentDownloads 3"), CancellationToken.None);
        client.Messages.Clear();
        await router.HandleAsync(Dm(1000, "/config list"), CancellationToken.None);

        Assert.Contains(client.Messages, m => m.Text.Contains(TestI18n.Zh("ConfigSourceOverlay"), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Config_Reset_RemovesOverlay_AndRestarts()
    {
        var client = new FakeTelegramClient();
        var restartCount = 0;
        var router = Build(client, onRestart: () => restartCount++);
        await router.HandleAsync(Dm(1000, "/config set MaxConcurrentDownloads 5"), CancellationToken.None);
        client.Messages.Clear();

        await router.HandleAsync(Dm(1000, "/config reset MaxConcurrentDownloads"), CancellationToken.None);

        Assert.Contains(client.Messages, m => m.Text == TestI18n.Zh("ConfigResetApplied", "MaxConcurrentDownloads"));
        Assert.Equal(1, restartCount); // set 触发 1 次；reset 在节流窗口内合并，不重复触发
        using (var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(StateDir, "config-overlay.json"))))
        {
            Assert.False(doc.RootElement.TryGetProperty("MaxConcurrentDownloads", out _)); // 覆盖项已移除（空对象）
        }
    }

    [Fact]
    public async Task Config_Reset_NotOverridden_NoRestart()
    {
        var client = new FakeTelegramClient();
        var restarted = false;
        var router = Build(client, onRestart: () => restarted = true);

        await router.HandleAsync(Dm(1000, "/config reset MaxConcurrentDownloads"), CancellationToken.None);

        Assert.Contains(client.Messages, m => m.Text == TestI18n.Zh("ConfigNotOverridden", "MaxConcurrentDownloads"));
        Assert.False(restarted);
    }

    [Fact]
    public async Task Config_ResetAll_ClearsEverything_AndRestarts()
    {
        var client = new FakeTelegramClient();
        var restartCount = 0;
        var router = Build(client, onRestart: () => restartCount++);
        await router.HandleAsync(Dm(1000, "/config set MaxConcurrentDownloads 5"), CancellationToken.None);
        await router.HandleAsync(Dm(1000, "/access add user 5000"), CancellationToken.None);
        client.Messages.Clear();

        await router.HandleAsync(Dm(1000, "/config reset-all"), CancellationToken.None);

        Assert.Contains(client.Messages, m => m.Text == TestI18n.Zh("ConfigResetAllApplied"));
        Assert.Equal(1, restartCount); // 三次变更在节流窗口内合并为一次重启
        using (var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(StateDir, "config-overlay.json"))))
        {
            Assert.False(doc.RootElement.TryGetProperty("MaxConcurrentDownloads", out _)); // 配置覆盖已清空
        }

        Assert.True(File.Exists(Path.Combine(StateDir, "access-overlay.json"))); // 白名单独立，不受 reset-all 影响
    }

    [Fact]
    public async Task Config_Usage_Shown_ForBareOrBadCommand()
    {
        var client = new FakeTelegramClient();
        var router = Build(client);

        await router.HandleAsync(Dm(1000, "/config"), CancellationToken.None);

        Assert.Contains(client.Messages, m => m.Text == TestI18n.Zh("ConfigUsage"));
    }

    [Fact]
    public async Task Access_Add_User_WritesOverlay_Notifies_AndRestarts()
    {
        var client = new FakeTelegramClient();
        var restartCount = 0;
        var router = Build(client, onRestart: () => restartCount++);
        var typeLabel = TestI18n.Zh("AccessTypeUser");

        await router.HandleAsync(Dm(1000, "/access add user 5000"), CancellationToken.None);

        Assert.Contains(client.Messages, m => m.Text == TestI18n.Zh("AccessAdded", typeLabel, "5000"));
        Assert.Equal(1, restartCount);
        Assert.True(File.Exists(Path.Combine(StateDir, "access-overlay.json")));
        using (var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(StateDir, "access-overlay.json"))))
        {
            Assert.Equal(5000L, doc.RootElement.GetProperty("ExtraAllowedUsers")[0].GetInt64());
        }

        using (var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(StateDir, "pending-notify.json"))))
        {
            Assert.Equal("AccessAdded", doc.RootElement.GetProperty("TextKey").GetString());
        }
    }

    [Fact]
    public async Task Access_Add_Channel_NegativeId_Allowed()
    {
        var client = new FakeTelegramClient();
        var router = Build(client);
        var typeLabel = TestI18n.Zh("AccessTypeChannel");

        await router.HandleAsync(Dm(1000, "/access add channel -100999"), CancellationToken.None);

        Assert.Contains(client.Messages, m => m.Text == TestI18n.Zh("AccessAdded", typeLabel, "-100999"));
    }

    [Fact]
    public async Task Access_Add_Duplicate_Rejected()
    {
        var client = new FakeTelegramClient();
        var router = Build(client);
        await router.HandleAsync(Dm(1000, "/access add user 5000"), CancellationToken.None);
        client.Messages.Clear();

        await router.HandleAsync(Dm(1000, "/access add user 5000"), CancellationToken.None);

        Assert.Contains(client.Messages, m => m.Text == TestI18n.Zh("AccessAlreadyAdded"));
    }

    [Fact]
    public async Task Access_Add_InvalidId_Rejected()
    {
        var client = new FakeTelegramClient();
        var router = Build(client);

        await router.HandleAsync(Dm(1000, "/access add user abc"), CancellationToken.None);

        Assert.Contains(client.Messages, m => m.Text == TestI18n.Zh("AccessInvalidId", "abc"));
    }

    [Fact]
    public async Task Access_Del_OverlayItem_RemovedAndRestarts()
    {
        var client = new FakeTelegramClient();
        var restartCount = 0;
        var router = Build(client, onRestart: () => restartCount++);
        await router.HandleAsync(Dm(1000, "/access add user 5000"), CancellationToken.None);
        client.Messages.Clear();
        var typeLabel = TestI18n.Zh("AccessTypeUser");

        await router.HandleAsync(Dm(1000, "/access del user 5000"), CancellationToken.None);

        Assert.Contains(client.Messages, m => m.Text == TestI18n.Zh("AccessRemoved", typeLabel, "5000"));
        Assert.Equal(1, restartCount); // add 触发；del 在节流窗口内合并
        using (var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(StateDir, "access-overlay.json"))))
        {
            Assert.Equal(0, doc.RootElement.GetProperty("ExtraAllowedUsers").GetArrayLength());
        }
    }

    [Fact]
    public async Task Access_Del_ConfigSourceItem_Refused()
    {
        var client = new FakeTelegramClient();
        var router = Build(client);

        // 1000 来自安装配置（AllowedUserIds），不可通过 /access 删除
        await router.HandleAsync(Dm(1000, "/access del user 1000"), CancellationToken.None);

        Assert.Contains(client.Messages, m => m.Text == TestI18n.Zh("AccessRemovedFromConfig"));
    }

    [Fact]
    public async Task Access_Del_NotFound_Rejected()
    {
        var client = new FakeTelegramClient();
        var router = Build(client);

        await router.HandleAsync(Dm(1000, "/access del user 7777"), CancellationToken.None);

        Assert.Contains(client.Messages, m => m.Text == TestI18n.Zh("AccessNotFound"));
    }

    [Fact]
    public async Task Access_List_ShowsMergedSources()
    {
        var client = new FakeTelegramClient();
        var router = Build(client);
        await router.HandleAsync(Dm(1000, "/access add user 5000"), CancellationToken.None);
        client.Messages.Clear();

        await router.HandleAsync(Dm(1000, "/access list"), CancellationToken.None);

        Assert.Contains(client.Messages, m => m.Text.Contains(
            TestI18n.Zh("AccessListLine", TestI18n.Zh("AccessTypeUser"), "1000", TestI18n.Zh("AccessSourceConfig")), StringComparison.Ordinal));
        Assert.Contains(client.Messages, m => m.Text.Contains(
            TestI18n.Zh("AccessListLine", TestI18n.Zh("AccessTypeUser"), "5000", TestI18n.Zh("AccessSourceOverlay")), StringComparison.Ordinal));
        Assert.Contains(client.Messages, m => m.Text.Contains(
            TestI18n.Zh("AccessListLine", TestI18n.Zh("AccessTypeChannel"), "-100111", TestI18n.Zh("AccessSourceConfig")), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Access_Usage_Shown_ForBareCommand()
    {
        var client = new FakeTelegramClient();
        var router = Build(client);

        await router.HandleAsync(Dm(1000, "/access"), CancellationToken.None);

        Assert.Contains(client.Messages, m => m.Text == TestI18n.Zh("AccessUsage"));
    }

    [Fact]
    public async Task Config_Set_Throttled_SecondChangeMerges_AndWindowExpiryTriggers()
    {
        var client = new FakeTelegramClient();
        var restartCount = 0;
        // 注入短节流窗口（40ms），验证窗口内合并、窗口过期后再次触发
        var router = Build(client, onRestart: () => restartCount++, restartThrottleWindow: TimeSpan.FromMilliseconds(40));
        await router.HandleAsync(Dm(1000, "/config set MaxConcurrentDownloads 5"), CancellationToken.None);
        Assert.Equal(1, restartCount);

        // 窗口内第二次变更：不重复触发，但 overlay 与 pending-notify 均被最新变更覆盖
        client.Messages.Clear();
        await router.HandleAsync(Dm(1000, "/config set DownloadRetries 4"), CancellationToken.None);
        Assert.Equal(1, restartCount);
        using (var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(StateDir, "config-overlay.json"))))
        {
            Assert.Equal("5", doc.RootElement.GetProperty("MaxConcurrentDownloads").GetString());
            Assert.Equal("4", doc.RootElement.GetProperty("DownloadRetries").GetString());
        }

        using (var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(StateDir, "pending-notify.json"))))
        {
            Assert.Equal("ConfigApplied", doc.RootElement.GetProperty("TextKey").GetString());
            Assert.Equal("DownloadRetries", doc.RootElement.GetProperty("Args")[0].GetString()); // 通知为最后一次变更
        }

        // 窗口过期后的变更再次触发重启
        await Task.Delay(80);
        client.Messages.Clear();
        await router.HandleAsync(Dm(1000, "/config set UploadRetries 1"), CancellationToken.None);
        Assert.Equal(2, restartCount);
    }

    [Fact]
    public async Task Config_Set_SameValue_AlreadyInEffect_NoRestart()
    {
        var client = new FakeTelegramClient();
        var restartCount = 0;
        var router = Build(client, onRestart: () => restartCount++);

        await router.HandleAsync(Dm(1000, "/config set MaxConcurrentDownloads \"5\""), CancellationToken.None);
        Assert.Equal(1, restartCount);

        client.Messages.Clear();
        await router.HandleAsync(Dm(1000, "/config set MaxConcurrentDownloads 5"), CancellationToken.None);

        // 归一化后同值（引号/无引号）→ 已生效，不落盘不重启
        Assert.Contains(client.Messages, m => m.Text == TestI18n.Zh("ConfigNoChange", "MaxConcurrentDownloads"));
        Assert.Equal(1, restartCount);
        using (var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(StateDir, "config-overlay.json"))))
        {
            Assert.Equal("5", doc.RootElement.GetProperty("MaxConcurrentDownloads").GetString());
        }
    }

    [Fact]
    public async Task Config_Set_SameAsConfigFileValue_NoChange_NoRestart()
    {
        // P2-3 验收：config.conf 中已有相同值（未覆盖）→ /config set 同值 → 不写 overlay、不重启
        var client = new FakeTelegramClient();
        var restartCount = 0;
        var router = Build(client, onRestart: () => restartCount++, configRawValues: new Dictionary<string, string>
        {
            ["MaxConcurrentDownloads"] = "5",
        });

        await router.HandleAsync(Dm(1000, "/config set MaxConcurrentDownloads 5"), CancellationToken.None);

        Assert.Contains(client.Messages, m => m.Text == TestI18n.Zh("ConfigNoChange", "MaxConcurrentDownloads"));
        Assert.Equal(0, restartCount);
        Assert.False(File.Exists(Path.Combine(StateDir, "config-overlay.json")));
    }

    [Fact]
    public async Task Config_Set_SameAsConfigFileBoolValue_SemanticNoChange()
    {
        // P2-3 验收：bool 键语义比较（config.conf 为 true，/config set yes → 等价 → 不重启）
        var client = new FakeTelegramClient();
        var restartCount = 0;
        var router = Build(client, onRestart: () => restartCount++, configRawValues: new Dictionary<string, string>
        {
            ["ExtractAudio"] = "true",
        });

        await router.HandleAsync(Dm(1000, "/config set ExtractAudio yes"), CancellationToken.None);

        Assert.Contains(client.Messages, m => m.Text == TestI18n.Zh("ConfigNoChange", "ExtractAudio"));
        Assert.Equal(0, restartCount);
        Assert.False(File.Exists(Path.Combine(StateDir, "config-overlay.json")));
    }

    [Fact]
    public async Task Config_Set_SameAsDefaultValue_NoChange_NoRestart()
    {
        // P2-3 验收：config.conf/overlay 均无该键，set 默认值 → 生效值不变 → 不写 overlay、不重启
        var client = new FakeTelegramClient();
        var restartCount = 0;
        var router = Build(client, onRestart: () => restartCount++);

        await router.HandleAsync(Dm(1000, "/config set MaxConcurrentDownloads 2"), CancellationToken.None);

        Assert.Contains(client.Messages, m => m.Text == TestI18n.Zh("ConfigNoChange", "MaxConcurrentDownloads"));
        Assert.Equal(0, restartCount);
        Assert.False(File.Exists(Path.Combine(StateDir, "config-overlay.json")));
    }

    [Fact]
    public async Task Config_Set_LowercaseKey_ResolvesAndApplies()
    {
        // 键名大小写不敏感：小写键名正常解析为规范名并回显
        var client = new FakeTelegramClient();
        var router = Build(client);

        await router.HandleAsync(Dm(1000, "/config set maxconcurrentdownloads 7"), CancellationToken.None);

        Assert.Contains(client.Messages, m => m.Text == TestI18n.Zh("ConfigSetApplied", "MaxConcurrentDownloads"));
        using (var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(StateDir, "config-overlay.json"))))
        {
            Assert.Equal("7", doc.RootElement.GetProperty("MaxConcurrentDownloads").GetString());
        }
    }

    [Fact]
    public async Task Config_UnauthorizedUser_Denied()
    {
        var client = new FakeTelegramClient();
        var router = Build(client);

        await router.HandleAsync(Dm(999, "/config list"), CancellationToken.None);

        Assert.Contains(client.Messages, m => m.Text.Contains("名单", StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(StateDir, "config-overlay.json")));
    }

    [Fact]
    public async Task Access_UnauthorizedUser_Denied_NoOverlayWrite()
    {
        var client = new FakeTelegramClient();
        var restarted = false;
        var router = Build(client, onRestart: () => restarted = true);

        // 非白名单用户（999）执行 /access add：与 /config 同一入口门禁，拒绝且不落盘不重启
        await router.HandleAsync(Dm(999, "/access add user 2000"), CancellationToken.None);

        Assert.Contains(client.Messages, m => m.Text.Contains("名单", StringComparison.Ordinal));
        Assert.False(restarted);
        Assert.False(File.Exists(Path.Combine(StateDir, "access-overlay.json")));
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
