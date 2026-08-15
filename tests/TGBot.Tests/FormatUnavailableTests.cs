using TGBot.Application;
using TGBot.Config;
using TGBot.Cookie;
using TGBot.Download;
using TGBot.Logging;
using TGBot.Messaging;
using TGBot.Security;
using Xunit;

namespace TGBot.Tests;

/// <summary>
/// <see cref="YtDlpOutputParser.IsFormatUnavailableMessage"/> 测试。
/// </summary>
public class FormatUnavailableDetectionTests
{
    [Fact]
    public void IsFormatUnavailable_RealYoutubeError()
    {
        const string err = "ERROR: [youtube] SeINrH9Bsb4: Requested format is not available. Use --list-formats for a list of available formats";
        Assert.True(YtDlpOutputParser.IsFormatUnavailableMessage(err));
    }

    [Fact]
    public void IsFormatUnavailable_NormalError_False()
    {
        Assert.False(YtDlpOutputParser.IsFormatUnavailableMessage("ERROR: Unable to download webpage"));
        Assert.False(YtDlpOutputParser.IsFormatUnavailableMessage(""));
    }
}

/// <summary>
/// 协调器对 FormatUnavailable 的智能回退测试。
/// </summary>
public class FormatUnavailableFallbackTests : IDisposable
{
    private readonly string _dir;

    public FormatUnavailableFallbackTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "tgdl-fmt-" + Guid.NewGuid().ToString("N")[..8]);
    }

    public void Dispose() => Directory.Delete(_dir, true);

    private (DownloadCoordinator Coordinator, FakeTelegramClient Client) Build(FakeDownloader downloader)
    {
        var client = new FakeTelegramClient();
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
            new CookieStore(System.IO.Path.Combine(_dir, "cookies"), logger),
            client,
            logger);
        var coordinator = new DownloadCoordinator(downloader, gate, registry, tempDir, upload, client, cookieService, config, logger);
        return (coordinator, client);
    }

    [Fact]
    public async Task FormatUnavailable_ProbesAndRetriesWithPickedFormats_ThenSucceeds()
    {
        var calls = new List<(string Merge, string? Expr, string? Clients)>();
        var downloader = new FakeDownloader
        {
            ProbeHandler = (_, _) => Task.FromResult<string?>("137+140"),
            Handler = (opts, _, _) =>
            {
                calls.Add((opts.MergeFormat, opts.FormatExpression, opts.YoutubePlayerClients));
                if (calls.Count == 1)
                {
                    throw new DownloadException(DownloadFailureReason.FormatUnavailable, "x", "fmt");
                }

                return Task.FromResult(new DownloadedMedia
                {
                    FilePath = System.IO.Path.Combine(_dir, "out.mp4"),
                    Title = "t",
                    Extension = "mp4",
                    SizeBytes = 10,
                    IsAudio = false,
                    SourceUrl = "https://youtube.com/v",
                });
            },
        };
        var (coordinator, client) = Build(downloader);

        var msg = new InboundMessage { ChatId = 1000, IsPrivate = true, SenderUserId = 1000, Text = "https://youtube.com/v" };
        Assert.True(await coordinator.EnqueueAsync(msg, "https://youtube.com/v", "video", CancellationToken.None));

        for (var i = 0; i < 50 && calls.Count < 2; i++)
        {
            await Task.Delay(100);
        }

        Assert.Equal(2, calls.Count);
        // 第一次：默认参数（player_client 默认开启）；第二次：挑选结果 + mkv
        Assert.Equal("mp4/mkv", calls[0].Merge);
        Assert.Null(calls[0].Expr);
        Assert.Equal("android,ios,web_embedded,tv", calls[0].Clients);
        Assert.Equal("mkv", calls[1].Merge);
        Assert.Equal("137+140", calls[1].Expr);
        Assert.Equal("android,ios,web_embedded,tv", calls[1].Clients);
        Assert.Contains(client.Videos, v => v.ChatId == -100111);
    }

    [Fact]
    public async Task FormatUnavailable_ProbeNull_UsesBest()
    {
        var calls = 0;
        var downloader = new FakeDownloader
        {
            ProbeHandler = (_, _) => Task.FromResult<string?>(null),
            Handler = (opts, _, _) =>
            {
                calls++;
                if (calls == 1)
                {
                    throw new DownloadException(DownloadFailureReason.FormatUnavailable, "x", "fmt");
                }

                return Task.FromResult(new DownloadedMedia
                {
                    FilePath = System.IO.Path.Combine(_dir, "out.mp4"),
                    Title = "t",
                    Extension = "mp4",
                    SizeBytes = 10,
                    IsAudio = false,
                    SourceUrl = "https://youtube.com/v",
                });
            },
        };
        var (coordinator, _) = Build(downloader);

        var msg = new InboundMessage { ChatId = 1000, IsPrivate = true, SenderUserId = 1000, Text = "https://youtube.com/v" };
        Assert.True(await coordinator.EnqueueAsync(msg, "https://youtube.com/v", "video", CancellationToken.None));

        for (var i = 0; i < 50 && calls < 2; i++)
        {
            await Task.Delay(100);
        }

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task FormatUnavailable_AllFail_StopsAfterOneFallback()
    {
        var calls = 0;
        var downloader = new FakeDownloader
        {
            ProbeHandler = (_, _) => Task.FromResult<string?>("137+140"),
            Handler = (_, _, _) =>
            {
                calls++;
                throw new DownloadException(DownloadFailureReason.FormatUnavailable, TGBot.Texts.UserTexts.FormatUnavailable, "fmt");
            },
        };
        var (coordinator, client) = Build(downloader);

        var msg = new InboundMessage { ChatId = 1000, IsPrivate = true, SenderUserId = 1000, Text = "https://youtube.com/v" };
        Assert.True(await coordinator.EnqueueAsync(msg, "https://youtube.com/v", "video", CancellationToken.None));

        for (var i = 0; i < 50 && calls < 2; i++)
        {
            await Task.Delay(100);
        }
        await Task.Delay(300);

        Assert.Equal(2, calls); // 默认 1 次 + 挑选回退 1 次，不再盲目重试 4 次
        Assert.Contains(client.Messages, m => m.Text.Contains("可用格式不足", StringComparison.Ordinal));
    }
}

/// <summary>
/// <see cref="YtDlpFormatPicker"/> 单元测试。
/// </summary>
public class YtDlpFormatPickerTests
{
    private const string SampleJson = """
        {"formats":[
          {"format_id":"137","vcodec":"avc1.640028","acodec":"none","height":1080,"tbr":4000},
          {"format_id":"136","vcodec":"avc1.4d401f","acodec":"none","height":720,"tbr":2000},
          {"format_id":"140","vcodec":"none","acodec":"mp4a.40.2","abr":128},
          {"format_id":"251","vcodec":"none","acodec":"opus","abr":160},
          {"format_id":"18","vcodec":"avc1.42001E","acodec":"mp4a.40.2","height":360,"tbr":500,"abr":96}
        ]}
        """;

    [Fact]
    public void Pick_SelectsHighestVideoAndAudio()
    {
        var formats = YtDlpFormatPicker.ParseFormats(SampleJson);
        var expr = YtDlpFormatPicker.PickBestExpression(formats);
        Assert.Equal("137+251", expr);
    }

    [Fact]
    public void Pick_SkipsDrm()
    {
        var json = """
            {"formats":[
              {"format_id":"a","vcodec":"avc1","acodec":"none","height":2160,"has_drm":true},
              {"format_id":"b","vcodec":"avc1","acodec":"none","height":720},
              {"format_id":"c","vcodec":"none","acodec":"opus","abr":128}
            ]}
            """;
        var formats = YtDlpFormatPicker.ParseFormats(json);
        var expr = YtDlpFormatPicker.PickBestExpression(formats);
        Assert.Equal("b+c", expr);
    }

    [Fact]
    public void Pick_OnlyCombinedFormats_ReturnsNull()
    {
        var json = """
            {"formats":[
              {"format_id":"18","vcodec":"avc1","acodec":"mp4a","height":360}
            ]}
            """;
        var formats = YtDlpFormatPicker.ParseFormats(json);
        Assert.Null(YtDlpFormatPicker.PickBestExpression(formats));
    }

    [Fact]
    public void Pick_MissingVideoOrAudio_ReturnsNull()
    {
        var json = """
            {"formats":[
              {"format_id":"v","vcodec":"avc1","acodec":"none","height":720}
            ]}
            """;
        Assert.Null(YtDlpFormatPicker.PickBestExpression(YtDlpFormatPicker.ParseFormats(json)));
    }

    [Fact]
    public void Parse_InvalidJson_ReturnsEmpty()
    {
        Assert.Empty(YtDlpFormatPicker.ParseFormats("not json"));
        Assert.Empty(YtDlpFormatPicker.ParseFormats(""));
    }
}

/// <summary>
/// <see cref="YtDlpArgumentBuilder"/> -f 与 player_client 参数测试。
/// </summary>
public class YtDlpFormatArgsTests
{
    private static DownloadOptions Options(string url, string? formatExpr = null, string? clients = null)
        => new(url, "/tmp/job", "/usr/local/bin/yt-dlp", null, "mp4/mkv", false, false, 1_900_000_000, TimeSpan.FromMinutes(10))
        {
            FormatExpression = formatExpr,
            YoutubePlayerClients = clients,
        };

    [Fact]
    public void Build_WithFormatExpression_AddsF()
    {
        var args = YtDlpArgumentBuilder.Build(Options("https://youtube.com/v", formatExpr: "137+140"));
        Assert.Contains("-f", args);
        Assert.Contains("137+140", args);
    }

    [Fact]
    public void Build_YoutubeHostWithClients_AddsExtractorArgs()
    {
        var args = YtDlpArgumentBuilder.Build(Options("https://youtu.be/abc", clients: "default,android,ios"));
        Assert.Contains("--extractor-args", args);
        Assert.Contains("youtube:player_client=default,android,ios", args);
    }

    [Fact]
    public void Build_NonYoutubeHost_NoExtractorArgs()
    {
        var args = YtDlpArgumentBuilder.Build(Options("https://x.com/someone/status/1", clients: "default,android,ios"));
        Assert.DoesNotContain("--extractor-args", args);
    }

    [Fact]
    public void Build_NoFormatAndNoClients_NothingAdded()
    {
        var args = YtDlpArgumentBuilder.Build(Options("https://youtube.com/v"));
        Assert.DoesNotContain("-f", args);
        Assert.DoesNotContain("--extractor-args", args);
    }

    [Fact]
    public void IsYoutubeUrl_DetectsAliases()
    {
        Assert.True(YtDlpArgumentBuilder.IsYoutubeUrl("https://www.youtube.com/watch?v=1"));
        Assert.True(YtDlpArgumentBuilder.IsYoutubeUrl("https://youtu.be/abc"));
        Assert.True(YtDlpArgumentBuilder.IsYoutubeUrl("https://music.youtube.com/x"));
        Assert.False(YtDlpArgumentBuilder.IsYoutubeUrl("https://example.com/v"));
    }
}

/// <summary>
/// <see cref="YtDlpFormatPicker"/> 音视频分类测试。
/// </summary>
public class MediaKindTests
{
    [Fact]
    public void HasVideo_RealVideoFormats_True()
    {
        var formats = new List<FormatInfo>
        {
            new("137", "avc1", "none", 1080, 4000, null, false),
            new("140", "none", "mp4a.40.2", null, null, 128, false),
        };
        Assert.True(YtDlpFormatPicker.HasVideo(formats));
        Assert.False(YtDlpFormatPicker.IsAudioOnly(formats));
    }

    [Fact]
    public void HasVideo_AudioOnly_False()
    {
        var formats = new List<FormatInfo>
        {
            new("140", "none", "mp4a.40.2", null, null, 128, false),
            new("251", "none", "opus", null, null, 160, false),
        };
        Assert.False(YtDlpFormatPicker.HasVideo(formats));
        Assert.True(YtDlpFormatPicker.IsAudioOnly(formats));
    }

    [Fact]
    public void HasVideo_StoryboardOnly_False()
    {
        var formats = new List<FormatInfo>
        {
            new("sb0", null, null, 240, null, null, false),
        };
        Assert.False(YtDlpFormatPicker.HasVideo(formats));
    }

    [Fact]
    public void HasVideo_DrmVideo_Excluded()
    {
        var formats = new List<FormatInfo>
        {
            new("a", "avc1", "none", 2160, 10000, null, true),
            new("b", "none", "opus", null, null, 160, false),
        };
        Assert.False(YtDlpFormatPicker.HasVideo(formats));
    }
}
