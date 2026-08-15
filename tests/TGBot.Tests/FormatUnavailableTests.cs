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
    public async Task FormatUnavailable_RetriesOnceWithMkv_ThenSucceeds()
    {
        var mergeFormats = new List<string>();
        var calls = 0;
        var downloader = new FakeDownloader
        {
            Handler = (opts, _, _) =>
            {
                calls++;
                mergeFormats.Add(opts.MergeFormat);
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
        var (coordinator, client) = Build(downloader);

        var msg = new InboundMessage { ChatId = 1000, IsPrivate = true, SenderUserId = 1000, Text = "https://youtube.com/v" };
        Assert.True(await coordinator.EnqueueAsync(msg, "https://youtube.com/v", CancellationToken.None));

        for (var i = 0; i < 50 && calls < 2; i++)
        {
            await Task.Delay(100);
        }

        Assert.Equal(2, calls);
        Assert.Equal(new[] { "mp4/mkv", "mkv" }, mergeFormats);
        Assert.Contains(client.Videos, v => v.ChatId == -100111);
    }

    [Fact]
    public async Task FormatUnavailable_BothFail_StopsAfterFallback()
    {
        var calls = 0;
        var downloader = new FakeDownloader
        {
            Handler = (_, _, _) =>
            {
                calls++;
                throw new DownloadException(DownloadFailureReason.FormatUnavailable, TGBot.Texts.UserTexts.FormatUnavailable, "fmt");
            },
        };
        var (coordinator, client) = Build(downloader);

        var msg = new InboundMessage { ChatId = 1000, IsPrivate = true, SenderUserId = 1000, Text = "https://youtube.com/v" };
        Assert.True(await coordinator.EnqueueAsync(msg, "https://youtube.com/v", CancellationToken.None));

        for (var i = 0; i < 50 && calls < 2; i++)
        {
            await Task.Delay(100);
        }
        await Task.Delay(300);

        Assert.Equal(2, calls); // 配置格式 1 次 + mkv 回退 1 次，不再盲目重试 4 次
        Assert.Contains(client.Messages, m => m.Text.Contains("可用格式不足", StringComparison.Ordinal));
    }
}
