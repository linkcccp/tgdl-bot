using System.Net;
using TGBot.Access;
using TGBot.Application;
using TGBot.Config;
using TGBot.Download;
using TGBot.Logging;
using TGBot.Messaging;
using TGBot.Security;
using TGBot.Update;
using Xunit;

namespace TGBot.Tests;

/// <summary>
/// 测试用 Telegram 客户端（记录发送调用）。
/// </summary>
public sealed class FakeTelegramClient : ITelegramClient
{
    public List<(long ChatId, string Text, int ReplyTo)> Messages { get; } = new();

    public List<(long ChatId, string FilePath, string FileName, string? Caption)> Videos { get; } = new();

    public List<(long ChatId, string FilePath, string FileName, string? Caption)> Audios { get; } = new();

    public List<(long ChatId, string FilePath, string FileName, string? Caption)> Documents { get; } = new();

    public int PollRuns;

    public Func<Exception, bool>? PollErrorHandler;

    public Task<string> GetBotUsernameAsync(CancellationToken cancellationToken) => Task.FromResult("testbot");

    public Task SendMessageAsync(long chatId, string text, int replyToMessageId, CancellationToken cancellationToken)
    {
        Messages.Add((chatId, text, replyToMessageId));
        return Task.CompletedTask;
    }

    public Task SendChatActionAsync(long chatId, BotChatAction action, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task SendVideoAsync(long chatId, string filePath, string fileName, string? caption, CancellationToken cancellationToken)
    {
        Videos.Add((chatId, filePath, fileName, caption));
        return Task.CompletedTask;
    }

    public Task SendAudioAsync(long chatId, string filePath, string fileName, string? caption, string? performer, string? title, CancellationToken cancellationToken)
    {
        Audios.Add((chatId, filePath, fileName, caption));
        return Task.CompletedTask;
    }

    public Task SendDocumentAsync(long chatId, string filePath, string fileName, string? caption, CancellationToken cancellationToken)
    {
        Documents.Add((chatId, filePath, fileName, caption));
        return Task.CompletedTask;
    }

    public Task SetCommandsAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task DropPendingUpdatesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task DownloadFileAsync(string fileId, string destinationPath, CancellationToken cancellationToken)
    {
        // 生成一个合法的最小 cookies 文本，便于 cookie 上传流程测试。
        File.WriteAllText(destinationPath, "# Netscape HTTP Cookie File\n");
        return Task.CompletedTask;
    }

    public async Task RunLongPollingAsync(
        Func<InboundMessage, CancellationToken, Task> onUpdate,
        Func<Exception, CancellationToken, Task> onPollError,
        CancellationToken cancellationToken)
    {
        PollRuns++;
        if (PollErrorHandler is { } h)
        {
            if (h(new IOException("fake poll error")))
            {
                await onPollError(new IOException("fake poll error"), cancellationToken);
            }
        }

        try
        {
            await Task.Delay(TimeSpan.FromDays(1), cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }
}

/// <summary>
/// 测试用下载器。
/// </summary>
public sealed class FakeDownloader : IDownloader
{
    public Func<DownloadOptions, Action<DownloadProgress>?, CancellationToken, Task<DownloadedMedia>> Handler { get; set; } =
        (_, _, _) => throw new DownloadException(DownloadFailureReason.Failed, "模拟失败");

    public Task<DownloadedMedia> DownloadAsync(DownloadOptions options, Action<DownloadProgress>? progress, CancellationToken cancellationToken)
        => Handler(options, progress, cancellationToken);
}

/// <summary>
/// 解析器的简单伪造（解析为公网地址）。
/// </summary>
public sealed class FakeResolverAlwaysPublic : IHostResolver
{
    public Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<IPAddress>>(new[] { IPAddress.Parse("93.184.216.34") });
}

/// <summary>
/// <see cref="CaptionBuilder"/> 单元测试。
/// </summary>
public class CaptionBuilderTests
{
    [Fact]
    public void Build_ContainsTitleAndUrl()
    {
        var c = CaptionBuilder.Build("测试标题", "https://example.com/v");
        Assert.Contains("测试标题", c, StringComparison.Ordinal);
        Assert.Contains("https://example.com/v", c, StringComparison.Ordinal);
    }

    [Fact]
    public void Sanitize_StripsControlChars()
    {
        Assert.DoesNotContain('\0', CaptionBuilder.Sanitize("a\0b"));
    }

    [Fact]
    public void Sanitize_CapsLength()
    {
        var result = CaptionBuilder.Sanitize(new string('x', 5000));
        Assert.True(result.Length <= 1000);
    }
}

/// <summary>
/// <see cref="MessageRouter"/> 单元测试。
/// </summary>
public class MessageRouterTests
{
    private static MessageRouter Build(
        FakeTelegramClient client,
        FakeDownloader downloader,
        out DownloadCoordinator coordinator,
        AppConfig? config = null,
        AccessControlService? access = null)
    {
        config ??= new AppConfig
        {
            BotToken = "123:abc",
            LocalApiBaseUrl = "http://127.0.0.1:8081",
            TargetChannelIds = new long[] { -100111 },
            AllowedUserIds = new long[] { 1000 },
            DownloadTempDir = Path.Combine(Path.GetTempPath(), "tgdl-rt-" + Guid.NewGuid().ToString("N")[..6]),
            MaxConcurrentDownloads = 2,
        };
        Directory.CreateDirectory(config.DownloadTempDir);

        var logger = NullLogger.Instance;
        var gate = new DownloadGate(config.MaxConcurrentDownloads);
        var registry = new JobRegistry();
        var tempDir = new TempDirManager(config.DownloadTempDir, logger);
        var upload = new UploadService(client, 0, false, logger);
        var cookieStore = new TGBot.Cookie.CookieStore(Path.Combine(config.DownloadTempDir, "cookies"), logger);
        var cookieService = new TGBot.Cookie.CookieService(
            new TGBot.Cookie.SiteCookieRegistry(new TGBot.Cookie.CookieSite[] { new TGBot.Cookie.YoutubeCookieSite(), new TGBot.Cookie.TwitterCookieSite() }),
            cookieStore,
            client,
            logger);
        coordinator = new DownloadCoordinator(downloader, gate, registry, tempDir, upload, client, cookieService, config, logger);
        access ??= new AccessControlService(config.AllowedUserIds, config.TargetChannelIds);
        var urlValidator = new UrlValidator(new FakeResolverAlwaysPublic());
        var runner = new SystemProcessRunner();
        var commands = new CommandHandler(client, new FakeUpdater(), gate, registry, config.DownloadTempDir, cookieService, config, runner, logger);
        return new MessageRouter(access, urlValidator, coordinator, commands, cookieService, client, config, logger);
    }

    private static InboundMessage Dm(long userId, string text) => new()
    {
        ChatId = userId,
        IsPrivate = true,
        SenderUserId = userId,
        Text = text,
        TriggerMessageId = 5,
    };

    [Fact]
    public async Task Private_UnauthorizedUser_RepliesDeny_NoDownload()
    {
        var client = new FakeTelegramClient();
        var downloader = new FakeDownloader();
        Build(client, downloader, out _);

        await BuildRouterAndHandle(client, downloader, Dm(999, "https://example.com/v"));
        Assert.Contains(client.Messages, m => m.Text.Contains("名单", StringComparison.Ordinal));
        Assert.Empty(client.Videos);
        Assert.Empty(client.Audios);
    }

    private static async Task BuildRouterAndHandle(FakeTelegramClient client, FakeDownloader downloader, InboundMessage msg)
    {
        var router = Build(client, downloader, out _);
        await router.HandleAsync(msg, CancellationToken.None);
    }

    [Fact]
    public async Task Private_AuthorizedUser_EnqueuesDownload()
    {
        var client = new FakeTelegramClient();
        var downloader = new FakeDownloader
        {
            Handler = (_, _, _) => Task.FromResult(new DownloadedMedia
            {
                FilePath = "/tmp/x.mp4",
                Title = "t",
                Extension = "mp4",
                SizeBytes = 10,
                IsAudio = false,
                SourceUrl = "https://example.com/v",
            }),
        };
        var router = Build(client, downloader, out _);
        await router.HandleAsync(Dm(1000, "https://example.com/v"), CancellationToken.None);

        await Task.Delay(500);
        Assert.Contains(client.Messages, m => m.Text.Contains("已收到", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Group_UnauthorizedChat_RepliesDeny_NoDownload()
    {
        var client = new FakeTelegramClient();
        var downloader = new FakeDownloader();
        var router = Build(client, downloader, out _);
        await router.HandleAsync(new InboundMessage
        {
            ChatId = -100999,
            IsPrivate = false,
            Text = "https://example.com/v",
        }, CancellationToken.None);

        Assert.Contains(client.Messages, m => m.Text.Contains("未获得授权", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Group_AuthorizedChat_Enqueues()
    {
        var client = new FakeTelegramClient();
        var downloader = new FakeDownloader
        {
            Handler = (_, _, _) => Task.FromResult(new DownloadedMedia
            {
                FilePath = "/tmp/x.mp4",
                Title = "t",
                Extension = "mp4",
                SizeBytes = 10,
                IsAudio = false,
                SourceUrl = "https://example.com/v",
            }),
        };
        var router = Build(client, downloader, out _);
        await router.HandleAsync(new InboundMessage
        {
            ChatId = -100111,
            IsPrivate = false,
            Text = "https://example.com/v",
        }, CancellationToken.None);

        await Task.Delay(300);
        Assert.DoesNotContain(client.Messages, m => m.Text.Contains("未获得授权", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AuthorizedUser_NoUrl_RepliesNoValidUrl()
    {
        var client = new FakeTelegramClient();
        var router = Build(client, new FakeDownloader(), out _);
        await router.HandleAsync(Dm(1000, "只是普通文字"), CancellationToken.None);
        Assert.Contains(client.Messages, m => m.Text.Contains("未找到有效", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Private_CommandInDm_Allowed()
    {
        var client = new FakeTelegramClient();
        var router = Build(client, new FakeDownloader(), out _);
        await router.HandleAsync(Dm(1000, "/help"), CancellationToken.None);
        Assert.Contains(client.Messages, m => m.Text.Contains("可用指令", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Unauthorized_CommandInDm_Denied()
    {
        var client = new FakeTelegramClient();
        var router = Build(client, new FakeDownloader(), out _);
        await router.HandleAsync(Dm(999, "/help"), CancellationToken.None);
        Assert.Contains(client.Messages, m => m.Text.Contains("名单", StringComparison.Ordinal));
    }
}

/// <summary>
/// 无操作的更新器（测试用）。
/// </summary>
public sealed class FakeUpdater : IUpdater
{
    public Task<UpdateReport> UpdateAsync(bool includeYtDlp, bool includeFfmpeg, Action<string>? progress, CancellationToken cancellationToken)
        => Task.FromResult(new UpdateReport(new[]
        {
            new ToolUpdateResult("yt-dlp", null, null, ToolUpdateStatus.AlreadyUpToDate),
            new ToolUpdateResult("ffmpeg", null, null, ToolUpdateStatus.AlreadyUpToDate),
        }));
}
