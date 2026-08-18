// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

using System.Collections.Concurrent;
using System.Net;
using TGBot.Access;
using TGBot.Application;
using TGBot.Config;
using TGBot.Download;
using TGBot.Logging;
using TGBot.Messaging;
using TGBot.Security;
using TGBot.Texts;
using TGBot.Texts.I18n;
using TGBot.Update;
using Xunit;

namespace TGBot.Tests;

/// <summary>
/// 测试用 Telegram 客户端（记录发送调用）。
/// </summary>
public sealed class FakeTelegramClient : ITelegramClient
{
    public List<(long ChatId, string Text, int ReplyTo)> Messages { get; } = new();

    public List<IReadOnlyList<InlineButton>> PromptButtons { get; } = new();

    public List<(long ChatId, string FilePath, string FileName, string? Caption)> Videos { get; } = new();

    public List<(long ChatId, string FilePath, string FileName, string? Caption)> Audios { get; } = new();

    public List<(long ChatId, string FilePath, string FileName, string? Caption)> Documents { get; } = new();

    public int PollRuns;

    public Func<Exception, bool>? PollErrorHandler;

    public Task<string> GetBotUsernameAsync(CancellationToken cancellationToken) => Task.FromResult("testbot");

    public Task SendMessageAsync(long chatId, string text, int replyToMessageId, IReadOnlyList<InlineButton>? inlineKeyboard, CancellationToken cancellationToken)
    {
        Messages.Add((chatId, text, replyToMessageId));
        if (inlineKeyboard is { Count: > 0 })
        {
            PromptButtons.Add(inlineKeyboard);
        }

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

    public Func<DownloadOptions, CancellationToken, Task<string?>>? ProbeHandler { get; set; }

    public Func<DownloadOptions, CancellationToken, Task<IReadOnlyList<FormatInfo>?>>? ProbeFormatsHandler { get; set; }

    public Func<DownloadOptions, Action<DownloadProgress>?, CancellationToken, Task<IReadOnlyList<DownloadedMedia>>>? AudioBundleHandler { get; set; }

    public Task<DownloadedMedia> DownloadAsync(DownloadOptions options, Action<DownloadProgress>? progress, CancellationToken cancellationToken)
        => Handler(options, progress, cancellationToken);

    public Task<string?> ProbeBestFormatAsync(DownloadOptions options, CancellationToken cancellationToken)
        => ProbeHandler is null ? Task.FromResult<string?>(null) : ProbeHandler(options, cancellationToken);

    public Task<IReadOnlyList<FormatInfo>?> ProbeFormatsAsync(DownloadOptions options, CancellationToken cancellationToken)
        => ProbeFormatsHandler is null ? Task.FromResult<IReadOnlyList<FormatInfo>?>(null) : ProbeFormatsHandler(options, cancellationToken);

    public Task<IReadOnlyList<DownloadedMedia>> DownloadAudioBundleAsync(DownloadOptions options, Action<DownloadProgress>? progress, CancellationToken cancellationToken)
        => AudioBundleHandler is null
            ? throw new DownloadException(DownloadFailureReason.Failed, "音频下载未模拟")
            : AudioBundleHandler(options, progress, cancellationToken);
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
        var c = CaptionBuilder.Build(TestI18n.Instance, "zh", "测试标题", "https://example.com/v");
        Assert.Contains("测试标题", c, StringComparison.Ordinal);
        Assert.Contains("https://example.com/v", c, StringComparison.Ordinal);
        Assert.Contains("标题：", c, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_EnglishLanguage_UsesEnglishLabels()
    {
        var c = CaptionBuilder.Build(TestI18n.Instance, "en", "Test Title", "https://example.com/v");
        Assert.Contains("Title:", c, StringComparison.Ordinal);
        Assert.Contains("Source:", c, StringComparison.Ordinal);
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
    internal static MessageRouter Build(
        FakeTelegramClient client,
        FakeDownloader downloader,
        out DownloadCoordinator coordinator,
        AppConfig? config = null,
        AccessControlService? access = null,
        UserLanguageStore? languageStore = null,
        Action? onRestart = null,
        Action<AppConfig>? onConfig = null,
        IUpdater? updater = null,
        TimeSpan? restartThrottleWindow = null,
        IReadOnlyDictionary<string, string>? configRawValues = null)
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
        var upload = new UploadService(client, 0, false, logger, TestI18n.Instance);
        var cookieStore = new TGBot.Cookie.CookieStore(Path.Combine(config.DownloadTempDir, "cookies"), logger);
        var cookieService = new TGBot.Cookie.CookieService(
            new TGBot.Cookie.SiteCookieRegistry(new TGBot.Cookie.CookieSite[] { new TGBot.Cookie.YoutubeCookieSite(), new TGBot.Cookie.TwitterCookieSite() }),
            cookieStore,
            client,
            logger,
            TestI18n.Instance);
        coordinator = new DownloadCoordinator(downloader, gate, registry, tempDir, upload, client, cookieService, config, logger, TestI18n.Instance);
        access ??= new AccessControlService(config.AllowedUserIds, config.TargetChannelIds, TestI18n.Instance);
        var urlValidator = new UrlValidator(new FakeResolverAlwaysPublic());
        var runner = new SystemProcessRunner();
        var stateDir = Path.Combine(config.DownloadTempDir, "state");
        languageStore ??= new UserLanguageStore(stateDir);
        // 模拟测试用户已显式选择中文（否则解析链按无信号回退 en，中文断言失效）。
        languageStore.Set(1000, "zh");
        languageStore.Set(999, "zh");
        var languageResolver = new UserLanguageResolver(languageStore, m => m.LanguageCode);
        var overlayStore = new TGBot.Config.Overlay.OverlayStore(stateDir);
        var notifyStore = new TGBot.Config.Overlay.PendingNotifyStore(stateDir);
        var commands = new CommandHandler(
            client,
            updater ?? new FakeUpdater(),
            gate,
            registry,
            config.DownloadTempDir,
            cookieService,
            config,
            runner,
            logger,
            TestI18n.Instance,
            languageStore,
            overlayStore,
            notifyStore,
            () => onRestart?.Invoke(),
            restartThrottleWindow: restartThrottleWindow,
            configRawValues: configRawValues);
        onConfig?.Invoke(config);
        return new MessageRouter(access, urlValidator, coordinator, commands, cookieService, client, config, logger, TestI18n.Instance, languageStore, languageResolver);
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
    public async Task Private_AuthorizedUser_VideoDm_ShowsModeChoice()
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

        // 探测失败 → 视为含视频；私聊 → 弹选择，不直接入队
        await Task.Delay(300);
        Assert.Contains(client.Messages, m => m.Text.Contains("请选择下载方式", StringComparison.Ordinal));
        Assert.DoesNotContain(client.Messages, m => m.Text.Contains("已收到", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Private_AuthorizedUser_AudioOnly_EnqueuesAudio()
    {
        var client = new FakeTelegramClient();
        var downloader = new FakeDownloader
        {
            ProbeFormatsHandler = (_, _) => Task.FromResult<IReadOnlyList<FormatInfo>?>(
                new List<FormatInfo> { new("140", "none", "mp4a.40.2", null, null, 128, false) }),
            AudioBundleHandler = (_, _, _) => Task.FromResult<IReadOnlyList<DownloadedMedia>>(
                new[]
                {
                    new DownloadedMedia { FilePath = "/tmp/a.flac", Title = "t", Extension = "flac", SizeBytes = 1, IsAudio = true, SourceUrl = "https://example.com/v" },
                    new DownloadedMedia { FilePath = "/tmp/a.mp3", Title = "t", Extension = "mp3", SizeBytes = 1, IsAudio = true, SourceUrl = "https://example.com/v" },
                }),
        };
        var router = Build(client, downloader, out _);
        await router.HandleAsync(Dm(1000, "https://example.com/v"), CancellationToken.None);

        // 入队是 fire-and-forget 后台任务：轮询等待 Queued 消息（CI 慢时固定延迟可能不足）
        for (var i = 0; i < 50; i++)
        {
            if (client.Messages.Any(m => m.Text.Contains("已收到", StringComparison.Ordinal))) break;
            await Task.Delay(100);
        }

        Assert.Contains(client.Messages, m => m.Text.Contains("已收到", StringComparison.Ordinal));
        Assert.DoesNotContain(client.Messages, m => m.Text.Contains("请选择下载方式", StringComparison.Ordinal));
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
            Language = "zh",
            LanguageCode = "zh",
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
            Language = "zh",
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

/// <summary>
/// 按指定原因抛 <see cref="UpdateException"/> 的更新器（更新失败 i18n 渲染测试用）。
/// </summary>
public sealed class ThrowingUpdater : IUpdater
{
    private readonly UpdateFailureReason _reason;

    /// <summary>
    /// 初始化 <see cref="ThrowingUpdater"/>。
    /// </summary>
    /// <param name="reason">更新失败原因。</param>
    public ThrowingUpdater(UpdateFailureReason reason)
    {
        _reason = reason;
    }

    /// <inheritdoc />
    public Task<UpdateReport> UpdateAsync(bool includeYtDlp, bool includeFfmpeg, Action<string>? progress, CancellationToken cancellationToken)
        => throw new UpdateException(_reason, "模拟更新失败（内部日志）");
}

/// <summary>
/// /update 失败路径的 i18n 渲染测试（对齐 DownloadException 处理：按 Reason 分类渲染，不直发硬编码中文）。
/// </summary>
public class UpdateCommandTests
{
    [Theory]
    [InlineData(UpdateFailureReason.LocalVersionUnavailable, "UpdateFailedLocalVersion")]
    [InlineData(UpdateFailureReason.LatestVersionUnavailable, "UpdateFailedLatestVersion")]
    [InlineData(UpdateFailureReason.DownloadFailed, "UpdateFailedDownload")]
    [InlineData(UpdateFailureReason.ReplaceFailed, "UpdateFailedReplace")]
    [InlineData(UpdateFailureReason.Failed, "UpdateFailed")]
    public async Task Update_Failure_RendersI18nByReason(UpdateFailureReason reason, string key)
    {
        var client = new FakeTelegramClient();
        var router = MessageRouterTests.Build(client, new FakeDownloader(), out _, updater: new ThrowingUpdater(reason));

        await router.HandleAsync(Dm(1000, "/update"), CancellationToken.None);

        Assert.Contains(client.Messages, m => m.Text == TestI18n.Zh(key));
        // 内部 detail 不得直发用户（无硬编码中文外泄）
        Assert.DoesNotContain(client.Messages, m => m.Text.Contains("内部日志", StringComparison.Ordinal));
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

/// <summary>
/// 回调选择路由测试。
/// </summary>
public class MessageRouterCallbackTests
{
    [Fact]
    public async Task Callback_AudioMode_EnqueuesAudio()
    {
        var client = new FakeTelegramClient();
        var downloader = new FakeDownloader
        {
            // 含视频 → 触发选择
            ProbeFormatsHandler = (_, _) => Task.FromResult<IReadOnlyList<FormatInfo>?>(
                new List<FormatInfo> { new("137", "avc1", "none", 720, null, null, false) }),
            AudioBundleHandler = (_, _, _) => Task.FromResult<IReadOnlyList<DownloadedMedia>>(
                new[]
                {
                    new DownloadedMedia { FilePath = "/tmp/a.flac", Title = "t", Extension = "flac", SizeBytes = 1, IsAudio = true, SourceUrl = "https://example.com/v" },
                    new DownloadedMedia { FilePath = "/tmp/a.mp3", Title = "t", Extension = "mp3", SizeBytes = 1, IsAudio = true, SourceUrl = "https://example.com/v" },
                }),
        };

        var router = MessageRouterTests.Build(client, downloader, out _);

        var dm = new InboundMessage { ChatId = 1000, IsPrivate = true, SenderUserId = 1000, Text = "https://example.com/v", TriggerMessageId = 5, Language = "zh" };
        await router.HandleAsync(dm, CancellationToken.None);

        await Task.Delay(300);
        Assert.NotEmpty(client.PromptButtons);
        var audioButton = client.PromptButtons.SelectMany(b => b).First(b => b.Text.Contains("仅音频", StringComparison.Ordinal));

        var cb = new InboundMessage
        {
            ChatId = 1000,
            IsPrivate = true,
            SenderUserId = 1000,
            IsCallback = true,
            CallbackData = audioButton.CallbackData,
            Language = "zh",
        };
        await router.HandleAsync(cb, CancellationToken.None);

        // 入队与下载是 fire-and-forget 后台任务：轮询等待 Queued 消息与 Audios 全部就绪
        //（CI 负载高时线程池调度可能延迟，固定等待会偶发失败，故与"已收到"同一模式轮询）
        for (var i = 0; i < 50; i++)
        {
            var ack = client.Messages.Any(m => m.Text.Contains("已收到", StringComparison.Ordinal));
            var flac = client.Audios.Any(a => a.FileName.EndsWith(".flac", StringComparison.Ordinal));
            var mp3 = client.Audios.Any(a => a.FileName.EndsWith(".mp3", StringComparison.Ordinal));
            if (ack && flac && mp3) break;
            await Task.Delay(100);
        }

        Assert.Contains(client.Messages, m => m.Text.Contains("已收到", StringComparison.Ordinal));
        Assert.Contains(client.Audios, a => a.FileName.EndsWith(".flac", StringComparison.Ordinal));
        Assert.Contains(client.Audios, a => a.FileName.EndsWith(".mp3", StringComparison.Ordinal));
    }
}

/// <summary>
/// 首次语言弹窗与 <c>lang:</c> 回调路由测试。
/// </summary>
public class LanguagePromptTests : IDisposable
{
    private readonly string _dir;

    public LanguagePromptTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "tgdl-lp-" + Guid.NewGuid().ToString("N")[..8]);
    }

    public void Dispose() => Directory.Delete(_dir, true);

    private const long NewUser = 3000;

    /// <summary>
    /// 构建路由器：NewUser 视为授权用户（语言弹窗仅在授权用户触发），其余与共享 Build 一致。
    /// </summary>
    private MessageRouter Build(FakeTelegramClient client, UserLanguageStore store)
        => MessageRouterTests.Build(
            client,
            new FakeDownloader(),
            out _,
            languageStore: store,
            access: new AccessControlService(new long[] { NewUser, 1000 }, new long[] { -100111 }, TestI18n.Instance));

    [Fact]
    public async Task FirstPrivateMessage_PromptsLanguageOnce()
    {
        var client = new FakeTelegramClient();
        var store = new UserLanguageStore(_dir);
        var router = Build(client, store);

        await router.HandleAsync(new InboundMessage
        {
            ChatId = NewUser,
            IsPrivate = true,
            SenderUserId = NewUser,
            Text = "https://example.com/v",
            TriggerMessageId = 1,
            Language = "en",
            LanguageCode = "en",
        }, CancellationToken.None);

        // 首次弹语言键盘（中文按钮文案来自当前语言目录，语言名不翻译）
        Assert.Contains(client.PromptButtons.SelectMany(b => b), b => b.Text == "简体中文");
        Assert.Contains(client.Messages, m => m.Text.Contains("Choose your language", StringComparison.Ordinal));
        Assert.False(store.Has(NewUser));

        // 同用户第二条消息不再重复弹窗（内存去重）
        await router.HandleAsync(new InboundMessage
        {
            ChatId = NewUser,
            IsPrivate = true,
            SenderUserId = NewUser,
            Text = "hi",
            TriggerMessageId = 2,
            Language = "en",
            LanguageCode = "en",
        }, CancellationToken.None);

        Assert.Single(client.Messages, m => m.Text.Contains("Choose your language", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnauthorizedUser_PrivateMessage_NoLanguagePrompt()
    {
        var client = new FakeTelegramClient();
        var store = new UserLanguageStore(_dir);
        var router = MessageRouterTests.Build(client, new FakeDownloader(), out _, languageStore: store);

        // 4000 未在白名单（语言解析回退 en）：只回复拒绝，不弹语言选择（弹窗仅在授权用户触发）
        await router.HandleAsync(new InboundMessage
        {
            ChatId = 4000,
            IsPrivate = true,
            SenderUserId = 4000,
            Text = "https://example.com/v",
            TriggerMessageId = 1,
            Language = "en",
        }, CancellationToken.None);

        Assert.Contains(client.Messages, m => m.Text.Contains("whitelist", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(client.Messages, m => m.Text.Contains("Choose your language", StringComparison.Ordinal));
        Assert.Empty(client.PromptButtons);
        Assert.False(store.Has(4000));
    }

    [Fact]
    public async Task LanguageCallback_Valid_SavesAndRepliesInChosenLanguage()
    {
        var client = new FakeTelegramClient();
        var store = new UserLanguageStore(_dir);
        var router = Build(client, store);

        // 触发弹窗注册
        await router.HandleAsync(new InboundMessage
        {
            ChatId = NewUser,
            IsPrivate = true,
            SenderUserId = NewUser,
            Text = "hi",
            TriggerMessageId = 1,
            Language = "en",
        }, CancellationToken.None);

        await router.HandleAsync(new InboundMessage
        {
            ChatId = NewUser,
            IsPrivate = true,
            SenderUserId = NewUser,
            IsCallback = true,
            CallbackData = "lang:zh",
            Language = "en",
        }, CancellationToken.None);

        Assert.True(store.Has(NewUser));
        Assert.Equal("zh", store.Get(NewUser));
        // 回执用新语言（中文）渲染
        Assert.Contains(client.Messages, m => m.Text.Contains("界面语言已设置为", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LanguageCallback_NoPrompt_Ignored()
    {
        var client = new FakeTelegramClient();
        var store = new UserLanguageStore(_dir);
        var router = Build(client, store);

        // 未弹窗直接点击 → 忽略（防止伪造回调）
        await router.HandleAsync(new InboundMessage
        {
            ChatId = NewUser,
            IsPrivate = true,
            SenderUserId = NewUser,
            IsCallback = true,
            CallbackData = "lang:zh",
            Language = "en",
        }, CancellationToken.None);

        Assert.False(store.Has(NewUser));
        Assert.Empty(client.Messages);
    }

    [Fact]
    public async Task LanguageCallback_InvalidCode_Ignored()
    {
        var client = new FakeTelegramClient();
        var store = new UserLanguageStore(_dir);
        var router = Build(client, store);

        await router.HandleAsync(new InboundMessage
        {
            ChatId = NewUser,
            IsPrivate = true,
            SenderUserId = NewUser,
            Text = "hi",
            TriggerMessageId = 1,
            Language = "en",
        }, CancellationToken.None);

        await router.HandleAsync(new InboundMessage
        {
            ChatId = NewUser,
            IsPrivate = true,
            SenderUserId = NewUser,
            IsCallback = true,
            CallbackData = "lang:fr",
            Language = "en",
        }, CancellationToken.None);

        Assert.False(store.Has(NewUser));
    }

    [Fact]
    public async Task LanguageCallback_WrongChat_Ignored()
    {
        var client = new FakeTelegramClient();
        var store = new UserLanguageStore(_dir);
        var router = Build(client, store);

        await router.HandleAsync(new InboundMessage
        {
            ChatId = NewUser,
            IsPrivate = true,
            SenderUserId = NewUser,
            Text = "hi",
            TriggerMessageId = 1,
            Language = "en",
        }, CancellationToken.None);

        // 回调点击者与会话不一致（仿冒他人会话）→ 忽略
        await router.HandleAsync(new InboundMessage
        {
            ChatId = 9999,
            IsPrivate = true,
            SenderUserId = NewUser,
            IsCallback = true,
            CallbackData = "lang:zh",
            Language = "en",
        }, CancellationToken.None);

        Assert.False(store.Has(NewUser));
    }

    [Fact]
    public async Task LanguageSelected_NextMessagesUseChosenLanguage()
    {
        var client = new FakeTelegramClient();
        var store = new UserLanguageStore(_dir);
        var router = MessageRouterTests.Build(client, new FakeDownloader(), out _, languageStore: store);

        // 未授权用户 4000 先显式选中文（弹窗仅在授权用户触发，此处直接写 store 模拟已选择）
        store.Set(4000, "zh");

        // 显式选择覆盖 language_code：拒绝提示用中文渲染（解析链验证）
        await router.HandleAsync(new InboundMessage
        {
            ChatId = 4000,
            IsPrivate = true,
            SenderUserId = 4000,
            Text = "https://example.com/v",
            TriggerMessageId = 1,
            Language = "en",
            LanguageCode = "en",
        }, CancellationToken.None);

        // 拒绝消息按显式选择的中文渲染（无权用户 → 名单拒绝文案）
        Assert.Contains(client.Messages, m => m.Text.Contains("名单", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LanguageCommand_WithEnArg_SetsLanguageAndRepliesInEnglish()
    {
        var client = new FakeTelegramClient();
        var store = new UserLanguageStore(_dir);
        var router = Build(client, store);

        // 白名单用户（1000）直接 /language en：脚本化设置，回执按所选语言（en）渲染
        await router.HandleAsync(new InboundMessage
        {
            ChatId = 1000,
            IsPrivate = true,
            SenderUserId = 1000,
            Text = "/language en",
            TriggerMessageId = 5,
            Language = "zh",
        }, CancellationToken.None);

        Assert.Equal("en", store.Get(1000));
        Assert.Contains(
            client.Messages,
            m => m.Text == TestI18n.Instance.Get("en", UserTexts.LanguageSaved, TestI18n.Instance.Get("en", UserTexts.LanguageNameEn)));
        // 不弹键盘（带参路径）
        Assert.DoesNotContain(client.Messages, m => m.Text.Contains("Choose your language", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LanguageCommand_WithZhArg_RepliesInChinese()
    {
        var client = new FakeTelegramClient();
        var store = new UserLanguageStore(_dir);
        var router = Build(client, store);
        store.Set(1000, "en");

        await router.HandleAsync(new InboundMessage
        {
            ChatId = 1000,
            IsPrivate = true,
            SenderUserId = 1000,
            Text = "/language zh",
            TriggerMessageId = 5,
            Language = "en",
        }, CancellationToken.None);

        Assert.Equal("zh", store.Get(1000));
        Assert.Contains(
            client.Messages,
            m => m.Text == TestI18n.Instance.Get("zh", UserTexts.LanguageSaved, TestI18n.Instance.Get("zh", UserTexts.LanguageNameZh)));
    }

    [Fact]
    public async Task LanguageCommand_WithoutArg_PromptsKeyboard()
    {
        var client = new FakeTelegramClient();
        var store = new UserLanguageStore(_dir);
        var router = Build(client, store);
        store.Set(1000, "zh"); // 已显式选择 → 入口不再触发首次弹窗，聚焦 /language 本身

        await router.HandleAsync(new InboundMessage
        {
            ChatId = 1000,
            IsPrivate = true,
            SenderUserId = 1000,
            Text = "/language",
            TriggerMessageId = 5,
            Language = "en",
        }, CancellationToken.None);

        // 弹语言选择键盘（lang:zh / lang:en 回调）；用户 1000 显式 zh → 解析链渲染中文文案
        Assert.Contains(client.Messages, m => m.Text == TestI18n.Zh("LanguagePrompt"));
        Assert.Contains(client.PromptButtons.SelectMany(b => b), b => b.CallbackData == "lang:zh");
        Assert.Contains(client.PromptButtons.SelectMany(b => b), b => b.CallbackData == "lang:en");
        // 未改动既有选择
        Assert.Equal("zh", store.Get(1000));
    }

    [Fact]
    public async Task LanguageCommand_WithoutArg_RepeatedTrigger_NoDuplicatePrompt()
    {
        // P2-2 验收：/language 无参重复触发与首次弹窗共用内存去重（2 分钟内仅弹一次键盘）
        var client = new FakeTelegramClient();
        var store = new UserLanguageStore(_dir);
        var router = Build(client, store);
        store.Set(1000, "zh"); // 隔离首次弹窗，聚焦 /language 本身

        await router.HandleAsync(new InboundMessage
        {
            ChatId = 1000,
            IsPrivate = true,
            SenderUserId = 1000,
            Text = "/language",
            TriggerMessageId = 5,
            Language = "en",
        }, CancellationToken.None);
        Assert.Single(client.PromptButtons);

        await router.HandleAsync(new InboundMessage
        {
            ChatId = 1000,
            IsPrivate = true,
            SenderUserId = 1000,
            Text = "/language",
            TriggerMessageId = 6,
            Language = "en",
        }, CancellationToken.None);

        // 第二次触发不重复弹窗（与 MessageRouter 首次弹窗同一登记表）
        Assert.Single(client.PromptButtons);
        Assert.Single(client.Messages, m => m.Text == TestI18n.Zh("LanguagePrompt"));
    }

    [Fact]
    public async Task LanguageCommand_InvalidArg_FallsBackToPrompt()
    {
        var client = new FakeTelegramClient();
        var store = new UserLanguageStore(_dir);
        var router = Build(client, store);
        store.Set(1000, "zh"); // 同上：隔离首次弹窗

        // 非法语言参数 → 视同无参数：弹键盘而非报错（中文渲染，见上）
        await router.HandleAsync(new InboundMessage
        {
            ChatId = 1000,
            IsPrivate = true,
            SenderUserId = 1000,
            Text = "/language fr",
            TriggerMessageId = 5,
            Language = "en",
        }, CancellationToken.None);

        Assert.Contains(client.Messages, m => m.Text == TestI18n.Zh("LanguagePrompt"));
        Assert.Equal("zh", store.Get(1000));
    }
}

/// <summary>
/// 语言弹窗内存登记的生命周期测试（过期项清理，防字典无界增长）。
/// </summary>
public class LanguagePromptRegistryTests
{
    private static CommandHandler NewHandler() => new(
        null!, null!, null!, null!, null!, null!, null!, null!, null!,
        TestI18n.Instance,
        null!, null!, null!, () => { });

    [Fact]
    public void Register_Once_ValidUntilTimeout()
    {
        var handler = NewHandler();
        Assert.True(handler.RegisterLanguagePrompt(1000));
        Assert.False(handler.RegisterLanguagePrompt(1000)); // 去重
        Assert.True(handler.IsLanguagePromptValid(1000));
    }

    [Fact]
    public void ExpiredEntry_RemovedOnValidation()
    {
        var handler = NewHandler();
        Assert.True(handler.RegisterLanguagePrompt(1000));

        // 无时间注入，反射将条目置为过期（模拟超时后回调到达）
        var field = typeof(CommandHandler).GetField("_languagePrompts", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var dict = (ConcurrentDictionary<long, DateTime>)field.GetValue(handler)!;
        dict[1000] = DateTime.UtcNow.AddMinutes(-1);

        Assert.False(handler.IsLanguagePromptValid(1000));
        Assert.False(dict.ContainsKey(1000)); // 过期项已移除，字典不增长
    }

    [Fact]
    public void NeverRegistered_Invalid()
    {
        var handler = NewHandler();
        Assert.False(handler.IsLanguagePromptValid(777));
    }
}
