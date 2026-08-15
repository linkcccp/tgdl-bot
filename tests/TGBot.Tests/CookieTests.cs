using TGBot.Application;
using TGBot.Config;
using TGBot.Cookie;
using TGBot.Download;
using TGBot.Logging;
using TGBot.Messaging;
using TGBot.Security;
using TGBot.Texts;
using Xunit;

namespace TGBot.Tests;

/// <summary>
/// <see cref="SiteCookieRegistry"/> 单元测试。
/// </summary>
public class SiteCookieRegistryTests
{
    private static SiteCookieRegistry Create()
        => new(new CookieSite[] { new YoutubeCookieSite(), new TwitterCookieSite(), new BilibiliCookieSite() });

    [Theory]
    [InlineData("youtube.com", "youtube")]
    [InlineData("www.youtube.com", "youtube")]
    [InlineData("youtu.be", "youtube")]
    [InlineData("m.youtube.com", "youtube")]
    [InlineData("music.youtube.com", "youtube")]
    [InlineData("twitter.com", "twitter")]
    [InlineData("x.com", "twitter")]
    [InlineData("www.x.com", "twitter")]
    [InlineData("b23.tv", "bilibili")]
    public void ResolveHost_MapsCorrectly(string host, string key)
    {
        Assert.Equal(key, Create().ResolveHost(host)?.Key);
    }

    [Theory]
    [InlineData("example.com")]
    [InlineData("twitch.tv")]
    public void ResolveHost_Unknown_Null(string host)
    {
        Assert.Null(Create().ResolveHost(host));
    }

    [Fact]
    public void ResolveKey_IsCaseInsensitive()
    {
        Assert.NotNull(Create().ResolveKey("YOUTUBE"));
        Assert.NotNull(Create().ResolveKey("  youtube  "));
        Assert.Null(Create().ResolveKey("unknown"));
    }
}

/// <summary>
/// <see cref="CookieStore"/> 单元测试。
/// </summary>
public class CookieStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly CookieStore _store;

    public CookieStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "tgdl-ck-" + Guid.NewGuid().ToString("N")[..8]);
        _store = new CookieStore(Path.Combine(_dir, "cookies"), NullLogger.Instance);
        _store.Initialize();
    }

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void Save_List_Get_Delete_Works()
    {
        var src = Path.Combine(_dir, "src.txt");
        File.WriteAllText(src, "# Netscape HTTP Cookie File\n");

        Assert.True(_store.Save("youtube", src));
        Assert.Contains("youtube", _store.List());
        Assert.NotNull(_store.GetFile("youtube"));
        Assert.True(File.Exists(_store.GetFile("youtube")));

        Assert.True(_store.Delete("youtube"));
        Assert.Null(_store.GetFile("youtube"));
        Assert.DoesNotContain("youtube", _store.List());
    }

    [Theory]
    [InlineData("../evil")]
    [InlineData("a/b")]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("a.txt")]
    public void Save_UnsafeKey_Rejected(string key)
    {
        var src = Path.Combine(_dir, "src.txt");
        File.WriteAllText(src, "x");
        Assert.False(_store.Save(key, src));
        Assert.Null(_store.GetFile(key));
    }

    [Fact]
    public void GetFile_FileIsNotSymlink()
    {
        var src = Path.Combine(_dir, "src.txt");
        File.WriteAllText(src, "x");
        _store.Save("youtube", src);
        var path = _store.GetFile("youtube")!;
        Assert.False(PathSanitizer.IsSymbolicLink(path));
    }
}

/// <summary>
/// <see cref="CookieService"/> 单元测试。
/// </summary>
public class CookieServiceTests : IDisposable
{
    private readonly string _dir;
    private readonly FakeTelegramClient _client;
    private readonly CookieService _service;

    public CookieServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "tgdl-cs-" + Guid.NewGuid().ToString("N")[..8]);
        _client = new FakeTelegramClient();
        var store = new CookieStore(Path.Combine(_dir, "cookies"), NullLogger.Instance);
        store.Initialize();
        _service = new CookieService(
            new SiteCookieRegistry(new CookieSite[] { new YoutubeCookieSite(), new TwitterCookieSite() }),
            store,
            _client,
            NullLogger.Instance);
    }

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public async Task BeginAndConsumePending_SavesCookie()
    {
        Assert.NotNull(_service.BeginPendingUpload(1000, "youtube"));

        var result = await _service.ConsumePendingAsync(1000, "file1", 100, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.Contains("YouTube", result.Message, StringComparison.Ordinal);
        Assert.Contains("已保存", result.Message, StringComparison.Ordinal);
        Assert.NotNull(_service.ResolveCookieFile("https://www.youtube.com/watch?v=1"));
        Assert.Null(_service.ResolveCookieFile("https://example.com/v"));
    }

    [Fact]
    public async Task ConsumeWithoutPending_ReturnsNull()
    {
        var result = await _service.ConsumePendingAsync(1000, "file1", 100, CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task FileTooLarge_Rejected()
    {
        _service.BeginPendingUpload(1000, "youtube");
        var result = await _service.ConsumePendingAsync(1000, "file1", 1_500_000, CancellationToken.None);
        Assert.NotNull(result);
        Assert.False(result!.Success);
        Assert.Contains("过大", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BeginPending_UnknownSite_Null()
    {
        Assert.Null(_service.BeginPendingUpload(1000, "nosuchsite"));
    }

    [Fact]
    public void ResolveCookieFile_OnlyWhenSaved()
    {
        Assert.Null(_service.ResolveCookieFile("https://x.com/someone/status/1"));
        Assert.NotNull(_service.BeginPendingUpload(1000, "twitter"));
        _service.ConsumePendingAsync(1000, "f", 10, CancellationToken.None).GetAwaiter().GetResult();
        Assert.NotNull(_service.ResolveCookieFile("https://x.com/someone/status/1"));
    }

    [Fact]
    public void Clear_RemovesCookie()
    {
        _service.BeginPendingUpload(1000, "youtube");
        _service.ConsumePendingAsync(1000, "f", 10, CancellationToken.None).GetAwaiter().GetResult();
        Assert.True(_service.Clear("youtube"));
        Assert.Null(_service.ResolveCookieFile("https://youtube.com/watch?v=1"));
    }
}

/// <summary>
/// <see cref="YtDlpOutputParser"/> 认证错误识别测试。
/// </summary>
public class AuthRequiredDetectionTests
{
    [Fact]
    public void IsAuthRequired_RealYoutubeError()
    {
        const string err = "ERROR: [youtube] kqj7b59D85Y: Sign in to confirm you're not a bot. Use --cookies-from-browser or --cookies for the authentication.";
        Assert.True(YtDlpOutputParser.IsAuthRequiredMessage(err));
    }

    [Theory]
    [InlineData("ERROR: [youtube] abc: Sign in to confirm you're not a bot")]
    [InlineData("This video is private. Sign in to view")]
    [InlineData("Use --cookies for authentication")]
    [InlineData("ERROR: Login required")]
    [InlineData("po_token is required")]
    public void IsAuthRequired_Various(string text)
    {
        Assert.True(YtDlpOutputParser.IsAuthRequiredMessage(text));
    }

    [Fact]
    public void IsAuthRequired_NormalError_False()
    {
        Assert.False(YtDlpOutputParser.IsAuthRequiredMessage("ERROR: [generic] Unable to download webpage: 404"));
        Assert.False(YtDlpOutputParser.IsAuthRequiredMessage(""));
    }
}

/// <summary>
/// <see cref="YtDlpArgumentBuilder"/> cookies/proxy/extra 参数测试。
/// </summary>
public class YtDlpCookiesArgsTests
{
    private static DownloadOptions Options(string? cookies = null, string? proxy = null, string? extra = null)
        => new("https://youtube.com/v", "/tmp/job", "/usr/local/bin/yt-dlp", null, "mp4", false, false, 1_900_000_000, TimeSpan.FromMinutes(10))
        {
            CookiesFile = cookies,
            Proxy = proxy,
            ExtraArgs = string.IsNullOrWhiteSpace(extra) ? null : extra.Split(' ', StringSplitOptions.RemoveEmptyEntries),
        };

    [Fact]
    public void Build_WithCookies_AddsArgs()
    {
        var args = YtDlpArgumentBuilder.Build(Options(cookies: "/opt/cookies/youtube.txt"));
        Assert.Contains("--cookies", args);
        Assert.Contains("/opt/cookies/youtube.txt", args);
    }

    [Fact]
    public void Build_WithProxy_AddsArgs()
    {
        var args = YtDlpArgumentBuilder.Build(Options(proxy: "http://127.0.0.1:8888"));
        Assert.Contains("--proxy", args);
        Assert.Contains("http://127.0.0.1:8888", args);
    }

    [Fact]
    public void Build_WithExtraArgs_AddsTokens()
    {
        var args = YtDlpArgumentBuilder.Build(Options(extra: "--extractor-args youtube:player_client=android,ios"));
        Assert.Contains("--extractor-args", args);
        Assert.Contains("youtube:player_client=android,ios", args);
    }

    [Fact]
    public void Build_WithoutAny_NoExtraFlags()
    {
        var args = YtDlpArgumentBuilder.Build(Options());
        Assert.DoesNotContain("--cookies", args);
        Assert.DoesNotContain("--proxy", args);
    }
}

/// <summary>
/// 认证错误不重试的协调器测试。
/// </summary>
public class AuthRequiredNoRetryTests : IDisposable
{
    private readonly string _dir;

    public AuthRequiredNoRetryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "tgdl-auth-" + Guid.NewGuid().ToString("N")[..8]);
    }

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public async Task AuthRequired_FailsFast_NoRetry()
    {
        var client = new FakeTelegramClient();
        var calls = 0;
        var downloader = new FakeDownloader
        {
            Handler = (_, _, _) =>
            {
                calls++;
                throw new DownloadException(DownloadFailureReason.AuthRequired, UserTexts.AuthRequired, "auth");
            },
        };

        var config = new AppConfig
        {
            BotToken = "123:abc",
            LocalApiBaseUrl = "http://127.0.0.1:8081",
            TargetChannelIds = new long[] { -100111 },
            AllowedUserIds = new long[] { 1000 },
            DownloadTempDir = _dir,
            DownloadRetries = 3,
            MaxConcurrentDownloads = 2,
        };

        var logger = NullLogger.Instance;
        var gate = new DownloadGate(2);
        var registry = new JobRegistry();
        var tempDir = new TempDirManager(_dir, logger);
        var upload = new UploadService(client, 0, false, logger);
        var cookieService = new CookieService(
            new SiteCookieRegistry(new CookieSite[] { new YoutubeCookieSite() }),
            new CookieStore(Path.Combine(_dir, "cookies"), logger),
            client,
            logger);
        var coordinator = new DownloadCoordinator(downloader, gate, registry, tempDir, upload, client, cookieService, config, logger);

        var msg = new InboundMessage
        {
            ChatId = 1000,
            IsPrivate = true,
            SenderUserId = 1000,
            Text = "https://youtube.com/watch?v=1",
        };
        Assert.True(await coordinator.EnqueueAsync(msg, "https://youtube.com/watch?v=1", CancellationToken.None));

        for (var i = 0; i < 50 && calls == 0; i++)
        {
            await Task.Delay(100);
        }

        for (var i = 0; i < 50 && registry.Running > 0; i++)
        {
            await Task.Delay(100);
        }

        Assert.Equal(1, calls);
        Assert.Contains(client.Messages, m => m.Text.Contains("登录/认证", StringComparison.Ordinal));
    }
}
