// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

using System.Diagnostics;
using System.Runtime.InteropServices;
using TGBot.Access;
using TGBot.Config;
using TGBot.Config.Overlay;
using TGBot.Cookie;
using TGBot.Download;
using TGBot.Logging;
using TGBot.Messaging;
using TGBot.Security;
using TGBot.Texts.I18n;
using TGBot.Update;

namespace TGBot.Application;

/// <summary>
/// 应用宿主：解析命令行参数、加载配置、装配各模块并启动 Bot 服务。
/// <para>装配期完成 overlay 合并（config-overlay.json 逐键覆盖 + 白名单并集），StateDir 承载
/// languages.json / overlay / pending-notify 等运行时状态。支持 <c>--smoke-test</c> 自检模式。</para>
/// </summary>
public static class AppHost
{
    /// <summary>
    /// 应用入口。
    /// </summary>
    /// <param name="args">命令行参数。</param>
    /// <returns>进程退出码。</returns>
    public static async Task<int> RunAsync(string[] args)
    {
        var (configPath, smokeSeconds) = ParseArgs(args);

        try
        {
            var result = ConfigLoader.Load(configPath);
            using var logger = new ConsoleLogger(result.Config.LogLevel, result.Config.LogFile);

            // 装配期 overlay 合并：先加载覆盖（StateDir 为安装锁键，不受 overlay 影响——overlay 自身
            // 始终以 config.conf 推导的目录读取，避免状态目录分裂），再显式逐键应用到 AppConfig；
            // 白名单合并延后到 AccessControlService 装配处。
            var overlayStore = new OverlayStore(ResolveStateDir(result.Config), logger);
            var config = OverlayApplier.Apply(result.Config, overlayStore.LoadConfig(), out var overlayWarnings);

            foreach (var warning in result.Warnings)
            {
                logger.Warn(warning);
            }

            foreach (var warning in overlayWarnings)
            {
                logger.Warn(warning);
            }

            // 最终状态目录（StateDir 为安装锁键，overlay 不可能覆盖它；overlay 文件始终在
            // config.conf 推导的目录，此处仅防御性确认）。
            var stateDir = ResolveStateDir(config);

            if (smokeSeconds > 0)
            {
                return await RunSmokeAsync(config, logger, smokeSeconds).ConfigureAwait(false);
            }

            using var cts = new CancellationTokenSource();
            RegisterShutdownHandlers(cts, logger);

            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(110) };
            var runner = new SystemProcessRunner();

            // i18n 装配：语言目录 → 用户语言存储 → 解析器（TgdlLanguage 为全局默认，auto 跟随用户）。
            var i18n = new I18nService(defaultLanguage: "en");
            var languageStore = new UserLanguageStore(stateDir, logger);
            languageStore.Load();
            var languageResolver = new UserLanguageResolver(languageStore, m => m.LanguageCode, config.TgdlLanguage);

            var menuLanguage = config.TgdlLanguage == UserLanguageResolver.Auto
                ? LanguageCatalog.FallbackLanguage
                : config.TgdlLanguage;
            var client = new TelegramClientWrapper(config.BotToken, config.LocalApiBaseUrl, http, cts.Token, i18n, menuLanguage);
            var updater = new Updater(
                runner,
                new IToolSource[]
                {
                    new YtDlpToolSource(http),
                    new FfmpegToolSource(http, runner),
                },
                config.YtDlpPath,
                config.FfmpegPath);

            var downloader = new YtDlpDownloader(logger);
            var gate = new DownloadGate(config.MaxConcurrentDownloads);
            var registry = new JobRegistry();
            var tempDir = new TempDirManager(config.DownloadTempDir, logger);
            tempDir.Initialize();

            var cookieStore = new CookieStore(
                string.IsNullOrEmpty(config.CookieStoreDir)
                    ? DefaultNativeCookieDir(config.DownloadTempDir)
                    : config.CookieStoreDir,
                logger);
            cookieStore.Initialize();

            var cookieService = new CookieService(
                new SiteCookieRegistry(new TGBot.Cookie.CookieSite[]
                {
                    new TGBot.Cookie.YoutubeCookieSite(),
                    new TGBot.Cookie.TwitterCookieSite(),
                    new TGBot.Cookie.InstagramCookieSite(),
                    new TGBot.Cookie.TiktokCookieSite(),
                    new TGBot.Cookie.TwitchCookieSite(),
                    new TGBot.Cookie.FacebookCookieSite(),
                    new TGBot.Cookie.BilibiliCookieSite(),
                    new TGBot.Cookie.DouyinCookieSite(),
                    new TGBot.Cookie.XiaohongshuCookieSite(),
                    new TGBot.Cookie.WeiboCookieSite(),
                    new TGBot.Cookie.SoundcloudCookieSite(),
                    new TGBot.Cookie.VimeoCookieSite(),
                    new TGBot.Cookie.DailymotionCookieSite(),
                    new TGBot.Cookie.RedditCookieSite(),
                }),
                cookieStore,
                client,
                logger,
                i18n);

            // 访问控制：安装配置白名单 ∪ overlay 追加列表（去重、来源标注）。
            var mergedAccess = AccessListMerge.Merge(config.AllowedUserIds, config.TargetChannelIds, overlayStore.LoadAccess());
            var access = new AccessControlService(mergedAccess.UserIds, mergedAccess.ChannelIds, i18n);
            var urlValidator = new UrlValidator(new DnsHostResolver());
            var upload = new UploadService(client, config.UploadRetries, config.AlsoSendMediaToRequester, logger, i18n);
            var coordinator = new DownloadCoordinator(downloader, gate, registry, tempDir, upload, client, cookieService, config, logger, i18n);

            var notifyStore = new PendingNotifyStore(stateDir, logger);
            var commands = new CommandHandler(
                client,
                updater,
                gate,
                registry,
                config.DownloadTempDir,
                cookieService,
                config,
                runner,
                logger,
                i18n,
                languageStore,
                overlayStore,
                notifyStore,
                () => cts.Cancel(),
                result.RawValues);
            var router = new MessageRouter(access, urlValidator, coordinator, commands, cookieService, client, config, logger, i18n, languageStore, languageResolver);
            var notifySender = new PendingNotifySender(notifyStore, client, i18n, logger);
            var bot = new BotService(client, router, logger, notifySender);

            logger.Info("正在启动（本地 Bot API Server 模式）…");
            await bot.RunAsync(cts.Token).ConfigureAwait(false);
            logger.Info("已退出。");
            return 0;
        }
        catch (ConfigLoadException ex)
        {
            // 异常消息已包含中英双行，直接输出。
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (ConfigParseException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("启动失败：\nStartup failed:\n" + ex.Message);
            return 1;
        }
    }

    private static (string? ConfigPath, int SmokeSeconds) ParseArgs(string[] args)
    {
        string? configPath = null;
        var smokeSeconds = 0;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--config" when i + 1 < args.Length:
                    configPath = args[++i];
                    break;
                case "--smoke-test":
                    smokeSeconds = i + 1 < args.Length && int.TryParse(args[i + 1], out var s) ? s : 8;
                    break;
                case "--help":
                case "-h":
                    PrintUsage();
                    Environment.Exit(0);
                    break;
            }
        }

        return (configPath, smokeSeconds);
    }

    private static void PrintUsage()
    {
        Console.WriteLine("用法：tgdl-bot [--config <路径>] [--smoke-test [秒数]] [--help]");
        Console.WriteLine("  --config <路径>   指定配置文件路径（默认查找程序同目录或当前目录的 config.conf）");
        Console.WriteLine("  --smoke-test      自检模式：验证配置与模块装配，不连接网络");
        Console.WriteLine("  --help            显示本帮助");
    }

    private static void RegisterShutdownHandlers(CancellationTokenSource cts, IAppLogger logger)
    {
        PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx =>
        {
            ctx.Cancel = true;
            logger.Info("收到 SIGTERM，正在优雅退出…");
            cts.Cancel();
        });
        PosixSignalRegistration.Create(PosixSignal.SIGINT, ctx =>
        {
            ctx.Cancel = true;
            logger.Info("收到 SIGINT，正在优雅退出…");
            cts.Cancel();
        });
    }

    private static async Task<int> RunSmokeAsync(AppConfig config, IAppLogger logger, int seconds)
    {
        logger.Info("=== 自检模式（不连接网络）===");
        logger.Info($"tgdl-bot 版本：{AppInfo.Version}");
        logger.Info($"配置来源：{config.SourcePath}");
        logger.Info($"Token：{MaskToken(config.BotToken)}");
        logger.Info($"本地 Bot API：{config.LocalApiBaseUrl}");
        logger.Info($"目标会话数：{config.TargetChannelIds.Count}，白名单用户数：{config.AllowedUserIds.Count}");
        logger.Info($"临时目录：{config.DownloadTempDir}");
        logger.Info($"状态目录：{ResolveStateDir(config)}");
        logger.Info($"yt-dlp：{config.YtDlpPath}，ffmpeg：{config.FfmpegPath}");
        logger.Info($"并发：{config.MaxConcurrentDownloads}，超时：{config.DownloadTimeoutSeconds}s");

        var http = new HttpClient();
        var runner = new SystemProcessRunner();
        _ = new Updater(runner, new IToolSource[] { new YtDlpToolSource(http), new FfmpegToolSource(http, runner) }, config.YtDlpPath, config.FfmpegPath);

        var cts = new CancellationTokenSource();
        _ = Task.Run(() => cts.CancelAfter(TimeSpan.FromSeconds(seconds)));
        while (!cts.IsCancellationRequested)
        {
            var rssMb = Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024;
            logger.Info($"自检运行中… RSS ≈ {rssMb} MB");
            await Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None).ConfigureAwait(false);
        }

        var finalRssMb = Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024;
        logger.Info($"=== 自检完成，RSS ≈ {finalRssMb} MB ===");
        return 0;
    }

    private static string MaskToken(string token)
        => token.Length <= 8 ? "***" : token[..6] + "…" + token[^4..];

    /// <summary>
    /// 原生（非 Docker）运行时的 cookie 缺省目录：放在临时目录同级而非其内部，
    /// 避免启动时清理临时目录误删 cookie。
    /// </summary>
    /// <param name="tempDir">下载临时目录。</param>
    /// <returns>cookie 目录绝对路径。</returns>
    private static string DefaultNativeCookieDir(string tempDir)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(tempDir));
        return Path.Combine(parent ?? tempDir, "cookies");
    }

    /// <summary>
    /// 运行时状态目录：优先 <c>StateDir</c> 配置键；为空时推导为 DownloadTempDir 父目录。
    /// <para>容器内 entrypoint 会在生成 config.conf 时显式写入 <c>/opt/tgdl-bot/api-data</c>
    /// （tgdl-data 卷内，pull 重建不丢），故推导路径主要服务于原生运行场景。</para>
    /// </summary>
    /// <param name="config">生效配置。</param>
    /// <returns>状态目录绝对路径。</returns>
    private static string ResolveStateDir(AppConfig config)
    {
        if (!string.IsNullOrEmpty(config.StateDir))
        {
            return Path.GetFullPath(config.StateDir);
        }

        var parent = Path.GetDirectoryName(Path.GetFullPath(config.DownloadTempDir));
        return parent ?? config.DownloadTempDir;
    }
}
