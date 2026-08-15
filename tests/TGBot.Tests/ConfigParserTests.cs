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
        Assert.Equal("mp4", result.Config.MergeFormat);
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
