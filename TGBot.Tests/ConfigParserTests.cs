// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

using TGBot.Config;
using TGBot.Logging;
using Xunit;

namespace TGBot.Tests;

/// <summary>
/// <see cref="ConfigParser"/> 单元测试。
/// </summary>
public class ConfigParserTests
{
    private const string ValidBase = """
        BotToken = 123456:ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghij
        LocalApiBaseUrl = http://127.0.0.1:8081
        TargetChannelIds = -1001234567890, 200
        AllowedUserIds = 111, 222, 333
        DownloadTempDir = /tmp/tgdl
        """;

    private static string Build(string body) => string.Join("\n", body.Split('\n').Select(l => l.Trim()));

    [Fact]
    public void Parse_ValidConfig_ReturnsExpectedValues()
    {
        var result = ConfigParser.Parse(Build(ValidBase), "/etc/tgdl/config.conf");

        Assert.Equal("123456:ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghij", result.Config.BotToken);
        Assert.Equal("http://127.0.0.1:8081", result.Config.LocalApiBaseUrl);
        Assert.Equal(new long[] { -1001234567890, 200 }, result.Config.TargetChannelIds);
        Assert.Equal(new long[] { 111, 222, 333 }, result.Config.AllowedUserIds);
        Assert.Equal("/tmp/tgdl", result.Config.DownloadTempDir);
        Assert.Equal(LogLevel.Info, result.Config.LogLevel);
        Assert.Equal(2, result.Config.MaxConcurrentDownloads);
    }

    [Fact]
    public void Parse_ConfigWithoutApiCredentials_Loads()
    {
        // TelegramApiId/Hash 已从 config.conf 移除，由 api.env 单独提供
        var result = ConfigParser.Parse(Build(ValidBase), "x.conf");
        Assert.NotNull(result.Config);
    }

    [Fact]
    public void Parse_LegacyApiCredentialsInConfig_AreIgnoredAsWarning()
    {
        var text = Build(ValidBase + "\nTelegramApiId = 123456\nTelegramApiHash = abc");
        var result = ConfigParser.Parse(text, "x.conf");
        Assert.Contains(result.Warnings, w => w.Contains("TelegramApiId", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, w => w.Contains("TelegramApiHash", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_WithCommentsAndSections_IgnoresThem()
    {
        var text = Build($"""
            # 这是一行注释
            ; 分号注释
            [telegram]
            {ValidBase}
            [limits]
            MaxConcurrentDownloads = 4
            """);
        var result = ConfigParser.Parse(text, "x.conf");

        Assert.Equal(4, result.Config.MaxConcurrentDownloads);
    }

    [Fact]
    public void Parse_UnknownKey_AddsWarning()
    {
        var text = Build($"{ValidBase}\nNoSuchKey = 1");
        var result = ConfigParser.Parse(text, "x.conf");

        Assert.Contains(result.Warnings, w => w.Contains("NoSuchKey", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_MissingRequiredKey_ThrowsWithKeyNames()
    {
        var text = Build(ValidBase.Replace("AllowedUserIds", "NotAllowedUserIds", StringComparison.Ordinal));
        var ex = Assert.Throws<ConfigParseException>(() => ConfigParser.Parse(text, "x.conf"));

        Assert.Contains("AllowedUserIds", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_MalformedLine_ThrowsWithLineNumber()
    {
        var text = Build($"# comment\n{ValidBase}\nBotToken  12345");
        var ex = Assert.Throws<ConfigParseException>(() => ConfigParser.Parse(text, "x.conf"));

        Assert.Contains("缺少“=”", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_DuplicateKey_Throws()
    {
        var text = Build($"{ValidBase}\nBotToken = another");
        var ex = Assert.Throws<ConfigParseException>(() => ConfigParser.Parse(text, "x.conf"));

        Assert.Contains("重复", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_InvalidLong_Throws()
    {
        var text = Build(ValidBase.Replace("-1001234567890, 200", "abc", StringComparison.Ordinal));
        var ex = Assert.Throws<ConfigParseException>(() => ConfigParser.Parse(text, "x.conf"));

        Assert.Contains("TargetChannelIds", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_InvalidLogLevel_Throws()
    {
        var text = Build($"{ValidBase}\nLogLevel = Verbose");
        var ex = Assert.Throws<ConfigParseException>(() => ConfigParser.Parse(text, "x.conf"));

        Assert.Contains("LogLevel", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_InvalidBool_Throws()
    {
        var text = Build($"{ValidBase}\nExtractAudio = maybe");
        var ex = Assert.Throws<ConfigParseException>(() => ConfigParser.Parse(text, "x.conf"));

        Assert.Contains("ExtractAudio", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_DuplicateListId_Throws()
    {
        var text = Build(ValidBase.Replace("111, 222, 333", "111, 111", StringComparison.Ordinal));
        var ex = Assert.Throws<ConfigParseException>(() => ConfigParser.Parse(text, "x.conf"));

        Assert.Contains("重复", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_InvalidBaseUrl_Throws()
    {
        var text = Build(ValidBase.Replace("http://127.0.0.1:8081", "file:///tmp/x", StringComparison.Ordinal));
        var ex = Assert.Throws<ConfigParseException>(() => ConfigParser.Parse(text, "x.conf"));

        Assert.Contains("LocalApiBaseUrl", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_EmptyFile_ThrowsMissingKeys()
    {
        var ex = Assert.Throws<ConfigParseException>(() => ConfigParser.Parse("", "x.conf"));

        Assert.Contains("BotToken", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_OptionalValues_UseDefaults()
    {
        var result = ConfigParser.Parse(Build(ValidBase), "x.conf");

        Assert.Equal(3600, result.Config.DownloadTimeoutSeconds);
        Assert.Equal(3, result.Config.DownloadRetries);
        Assert.False(result.Config.ExtractAudio);
        Assert.True(result.Config.UpdateYtDlp);
        Assert.Equal("mp4/mkv", result.Config.MergeFormat);
        Assert.Null(result.Config.LogFile);
    }

    [Fact]
    public void Parse_QuotedValue_StripsQuotes()
    {
        var text = Build(ValidBase.Replace("/tmp/tgdl", "\"/tmp/tgdl with space\"", StringComparison.Ordinal));
        var result = ConfigParser.Parse(text, "x.conf");

        Assert.Equal("/tmp/tgdl with space", result.Config.DownloadTempDir);
    }

    [Fact]
    public void Load_MissingFile_ThrowsChineseError()
    {
        var ex = Assert.Throws<ConfigLoadException>(() => ConfigLoader.Load("/nonexistent/path/x.conf"));

        Assert.Contains("找不到配置文件", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_ErrorMessages_AreBilingual()
    {
        // 启动期无用户上下文：错误消息中英双行并列（设计 §2.7）
        var text = Build($"{ValidBase}\nMaxConcurrentDownloads = 99");
        var ex = Assert.Throws<ConfigParseException>(() => ConfigParser.Parse(text, "x.conf"));

        Assert.Contains("配置错误：", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Config error:", ex.Message, StringComparison.Ordinal);
        Assert.Contains("MaxConcurrentDownloads", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_TgdlLanguage_DefaultsToAuto_AndAcceptsAlias()
    {
        var result = ConfigParser.Parse(Build(ValidBase), "x.conf");
        Assert.Equal("auto", result.Config.TgdlLanguage);

        var zh = ConfigParser.Parse(Build(ValidBase + "\nLanguage = zh"), "x.conf");
        Assert.Equal("zh", zh.Config.TgdlLanguage);

        var en = ConfigParser.Parse(Build(ValidBase + "\nTgdlLanguage = EN"), "x.conf");
        Assert.Equal("en", en.Config.TgdlLanguage);
    }

    [Fact]
    public void Parse_InvalidTgdlLanguage_Throws()
    {
        var text = Build(ValidBase + "\nTgdlLanguage = fr");
        var ex = Assert.Throws<ConfigParseException>(() => ConfigParser.Parse(text, "x.conf"));

        Assert.Contains("TgdlLanguage", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Config error:", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_StateDir_ValueAndDefault()
    {
        var result = ConfigParser.Parse(Build(ValidBase), "x.conf");
        Assert.Equal(string.Empty, result.Config.StateDir);

        var withState = ConfigParser.Parse(Build(ValidBase + "\nStateDir = /opt/tgdl-bot/api-data"), "x.conf");
        Assert.Equal("/opt/tgdl-bot/api-data", withState.Config.StateDir);
    }

    [Fact]
    public void Parse_StateDirTooLong_Throws()
    {
        var text = Build(ValidBase + $"\nStateDir = /{new string('x', 600)}");
        var ex = Assert.Throws<ConfigParseException>(() => ConfigParser.Parse(text, "x.conf"));

        Assert.Contains("StateDir", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RawValues_ExposeFileKeys()
    {
        var result = ConfigParser.Parse(Build(ValidBase + "\nMaxConcurrentDownloads = 4"), "x.conf");

        Assert.Equal("4", result.RawValues["MaxConcurrentDownloads"]);
        Assert.True(result.RawValues.ContainsKey("BotToken"));
        Assert.False(result.RawValues.ContainsKey("LogLevel")); // 未写入文件 → 不含
    }

    [Fact]
    public void ValidateValue_AcceptsValidValues()
    {
        Assert.Null(ConfigParser.ValidateValue("MaxConcurrentDownloads", "4"));
        Assert.Null(ConfigParser.ValidateValue("Concurrency", "16")); // 别名
        Assert.Null(ConfigParser.ValidateValue("DownloadRetries", "0"));
        Assert.Null(ConfigParser.ValidateValue("DownloadTimeoutSeconds", "60"));
        Assert.Null(ConfigParser.ValidateValue("MaxMediaSizeBytes", "1900000000"));
        Assert.Null(ConfigParser.ValidateValue("LogLevel", "warn"));
        Assert.Null(ConfigParser.ValidateValue("ExtractAudio", "yes"));
        Assert.Null(ConfigParser.ValidateValue("TgdlDefaultMode", "AUDIO"));
        Assert.Null(ConfigParser.ValidateValue("TgdlLanguage", "zh"));
        Assert.Null(ConfigParser.ValidateValue("Language", "auto"));
        Assert.Null(ConfigParser.ValidateValue("MergeFormat", "mp4/mkv"));
        Assert.Null(ConfigParser.ValidateValue("YtDlpExtraArgs", "--foo bar"));
        Assert.Null(ConfigParser.ValidateValue("LocalApiBaseUrl", "http://127.0.0.1:8081"));
        Assert.Null(ConfigParser.ValidateValue("StateDir", "/opt/tgdl-bot/api-data"));
        Assert.Null(ConfigParser.ValidateValue("StateDir", "")); // 空 = 推导
        Assert.Null(ConfigParser.ValidateValue("CookieStoreDir", "")); // 空 = 未配置
        Assert.Null(ConfigParser.ValidateValue("BotToken", "123456:ABCdef-_"));
    }

    [Fact]
    public void ValidateValue_RejectsInvalidValues()
    {
        Assert.NotNull(ConfigParser.ValidateValue("MaxConcurrentDownloads", "99"));
        Assert.NotNull(ConfigParser.ValidateValue("MaxConcurrentDownloads", "abc"));
        Assert.NotNull(ConfigParser.ValidateValue("DownloadRetries", "-1"));
        Assert.NotNull(ConfigParser.ValidateValue("DownloadTimeoutSeconds", "59"));
        Assert.NotNull(ConfigParser.ValidateValue("LogLevel", "Verbose"));
        Assert.NotNull(ConfigParser.ValidateValue("ExtractAudio", "maybe"));
        Assert.NotNull(ConfigParser.ValidateValue("TgdlDefaultMode", "h265"));
        Assert.NotNull(ConfigParser.ValidateValue("TgdlLanguage", "fr"));
        Assert.NotNull(ConfigParser.ValidateValue("TgdlLanguage", "")); // 显式空非法
        Assert.NotNull(ConfigParser.ValidateValue("MergeFormat", "mp4;rm"));
        Assert.NotNull(ConfigParser.ValidateValue("LocalApiBaseUrl", "file:///tmp/x"));
        Assert.NotNull(ConfigParser.ValidateValue("StateDir", new string('x', 600)));
        Assert.NotNull(ConfigParser.ValidateValue("TargetChannelIds", "abc"));
        Assert.NotNull(ConfigParser.ValidateValue("TargetChannelIds", "1, 1"));
        Assert.NotNull(ConfigParser.ValidateValue("BotToken", ""));
    }

    [Fact]
    public void ValidateValue_UnknownKey_ReturnsError()
    {
        var error = ConfigParser.ValidateValue("TelegramApiId", "123");

        Assert.NotNull(error);
        Assert.Contains("TelegramApiId", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryResolveKey_IsCaseInsensitive()
    {
        // 与 subcommand 的 ToLowerInvariant 解析一致：键名大小写不敏感
        Assert.True(ConfigParser.TryResolveKey("maxconcurrentdownloads", out var canonical));
        Assert.Equal("MaxConcurrentDownloads", canonical);
        Assert.True(ConfigParser.TryResolveKey("CONCURRENCY", out var viaAlias));
        Assert.Equal("MaxConcurrentDownloads", viaAlias);
        Assert.False(ConfigParser.TryResolveKey("NoSuchKey", out _));
    }

    [Fact]
    public void Parse_ConfigWithLowercaseKey_Resolves()
    {
        // config.conf 手工写入小写键名同样归一解析（大小写不敏感）
        var text = Build(ValidBase + "\nmaxconcurrentdownloads = 4");
        var result = ConfigParser.Parse(text, "x.conf");

        Assert.Equal(4, result.Config.MaxConcurrentDownloads);
        Assert.Equal("4", result.RawValues["MaxConcurrentDownloads"]);
    }

    [Fact]
    public void ValidateValue_StripsQuotesAndTrims()
    {
        Assert.Null(ConfigParser.ValidateValue("MergeFormat", "\"mp4/mkv\""));
        Assert.Null(ConfigParser.ValidateValue("MaxConcurrentDownloads", " 4 "));
    }

    [Fact]
    public void ValidateValue_QuotedUrlValue_Accepted()
    {
        // P1-1 验收：/config set 带引号值（如 LocalApiBaseUrl "http://x"）须校验通过（先归一化再校验，与落盘同源）
        Assert.Null(ConfigParser.ValidateValue("LocalApiBaseUrl", "\"http://127.0.0.1:8081\""));
    }

    [Fact]
    public void NormalizeValue_StripsQuotesAndTrims()
    {
        // 归一化与 ValidateValue 内部逻辑同一来源：落盘前必须经此处理，保证重启后解析一致。
        Assert.Equal("mp4/mkv", ConfigParser.NormalizeValue("\"mp4/mkv\""));
        Assert.Equal("4", ConfigParser.NormalizeValue(" 4 "));
        Assert.Equal("quoted", ConfigParser.NormalizeValue("'quoted'")); // 单引号对也剥
        Assert.Equal("/tmp/x y", ConfigParser.NormalizeValue("\"/tmp/x y\""));
        Assert.Equal(string.Empty, ConfigParser.NormalizeValue("   "));
    }

    [Fact]
    public void MutableKeys_ExcludesLockedKeys()
    {
        Assert.DoesNotContain("BotToken", ConfigParser.MutableKeys);
        Assert.DoesNotContain("AllowedUserIds", ConfigParser.MutableKeys);
        Assert.DoesNotContain("TargetChannelIds", ConfigParser.MutableKeys);
        Assert.DoesNotContain("StateDir", ConfigParser.MutableKeys); // 状态目录列为安装锁键
        Assert.Contains("TgdlLanguage", ConfigParser.MutableKeys);
        Assert.Contains("MaxConcurrentDownloads", ConfigParser.MutableKeys);
    }

    [Fact]
    public void RequiresRiskWarning_MarksConnectionAndPathKeys()
    {
        Assert.True(ConfigParser.RequiresRiskWarning("LocalApiBaseUrl"));
        Assert.True(ConfigParser.RequiresRiskWarning("DownloadTempDir"));
        Assert.False(ConfigParser.RequiresRiskWarning("StateDir")); // 锁键不可经 /config 修改，无风险警告路径
        Assert.False(ConfigParser.RequiresRiskWarning("MaxConcurrentDownloads"));
        Assert.False(ConfigParser.RequiresRiskWarning("TgdlLanguage"));
    }

    [Fact]
    public void DisplayValue_ReturnsEffectiveString()
    {
        var config = new AppConfig
        {
            BotToken = "123:abc",
            LocalApiBaseUrl = "http://127.0.0.1:8081",
            TargetChannelIds = new long[] { -100111 },
            AllowedUserIds = new long[] { 1000 },
            DownloadTempDir = "/tmp/tgdl",
            MaxConcurrentDownloads = 4,
            LogLevel = LogLevel.Warn,
            ExtractAudio = true,
            TgdlDefaultMode = "audio",
            TgdlLanguage = "zh",
        };

        Assert.Equal("4", ConfigParser.DisplayValue(config, "MaxConcurrentDownloads"));
        Assert.Equal("Warn", ConfigParser.DisplayValue(config, "LogLevel"));
        Assert.Equal("true", ConfigParser.DisplayValue(config, "ExtractAudio"));
        Assert.Equal("audio", ConfigParser.DisplayValue(config, "TgdlDefaultMode"));
        Assert.Equal("zh", ConfigParser.DisplayValue(config, "TgdlLanguage"));
        Assert.Equal(string.Empty, ConfigParser.DisplayValue(config, "YtDlpProxy"));
    }

    [Fact]
    public void DisplayValue_MasksSensitiveCredentials()
    {
        var config = new AppConfig
        {
            LocalApiBaseUrl = "http://user:s3cret@127.0.0.1:8081?api_id=123456&api_hash=deadbeef",
            YtDlpProxy = "http://user:s3cret@127.0.0.1:8080",
        };

        // 查询串凭据与 userinfo 均脱敏回显（/config list 防泄露）
        Assert.Equal("http://***@127.0.0.1:8081?***", ConfigParser.DisplayValue(config, "LocalApiBaseUrl"));
        Assert.Equal("http://***@127.0.0.1:8080", ConfigParser.DisplayValue(config, "YtDlpProxy"));
        Assert.DoesNotContain("user", ConfigParser.DisplayValue(config, "LocalApiBaseUrl"), StringComparison.Ordinal);
        Assert.DoesNotContain("s3cret", ConfigParser.DisplayValue(config, "LocalApiBaseUrl"), StringComparison.Ordinal);
        Assert.DoesNotContain("123456", ConfigParser.DisplayValue(config, "LocalApiBaseUrl"), StringComparison.Ordinal);
        Assert.DoesNotContain("deadbeef", ConfigParser.DisplayValue(config, "LocalApiBaseUrl"), StringComparison.Ordinal);
        Assert.DoesNotContain("s3cret", ConfigParser.DisplayValue(config, "YtDlpProxy"), StringComparison.Ordinal);
    }

    [Fact]
    public void DisplayValue_NoCredentials_Unchanged()
    {
        var config = new AppConfig
        {
            LocalApiBaseUrl = "http://127.0.0.1:8081",
            YtDlpProxy = "socks5://127.0.0.1:1080",
        };

        Assert.Equal("http://127.0.0.1:8081", ConfigParser.DisplayValue(config, "LocalApiBaseUrl"));
        Assert.Equal("socks5://127.0.0.1:1080", ConfigParser.DisplayValue(config, "YtDlpProxy"));
    }
}

/// <summary>
/// <see cref="ConfigLoader"/> 实际文件加载测试。
/// </summary>
public class ConfigLoaderFileTests : IDisposable
{
    private readonly string _dir;

    public ConfigLoaderFileTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "tgdl-cfg-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void Load_ValidFile_ReturnsConfig()
    {
        var path = Path.Combine(_dir, "config.conf");
        File.WriteAllText(path, """
            BotToken = 123456:AAAA
            LocalApiBaseUrl = http://127.0.0.1:8081
            TargetChannelIds = -100111
            AllowedUserIds = 1000
            DownloadTempDir = /tmp/x
            """);

        var result = ConfigLoader.Load(path);
        Assert.Equal("123456:AAAA", result.Config.BotToken);
    }

    [Fact]
    public void Load_NonexistentFile_ThrowsChineseError()
    {
        var path = Path.Combine(_dir, "nonexistent.conf");
        var ex = Assert.Throws<ConfigLoadException>(() => ConfigLoader.Load(path));
        Assert.Contains("找不到配置文件", ex.Message, StringComparison.Ordinal);
    }
}
