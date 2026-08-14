using System.Diagnostics;
using System.Runtime.InteropServices;
using TGBot.Access;
using TGBot.Config;
using TGBot.Download;
using TGBot.Logging;
using TGBot.Messaging;
using TGBot.Security;
using TGBot.Update;

namespace TGBot.Application;

/// <summary>
/// 应用宿主：解析命令行参数、加载配置、装配各模块并启动 Bot 服务。
/// <para>支持 <c>--smoke-test</c> 自检模式（验证配置与模块装配并输出内存占用，不连接网络）。</para>
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
            var config = result.Config;
            using var logger = new ConsoleLogger(config.LogLevel, config.LogFile);

            foreach (var warning in result.Warnings)
            {
                logger.Warn(warning);
            }

            if (smokeSeconds > 0)
            {
                return await RunSmokeAsync(config, logger, smokeSeconds).ConfigureAwait(false);
            }

            using var cts = new CancellationTokenSource();
            RegisterShutdownHandlers(cts, logger);

            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(110) };
            var runner = new SystemProcessRunner();
            var client = new TelegramClientWrapper(config.BotToken, config.LocalApiBaseUrl, http, cts.Token);
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

            var access = new AccessControlService(config.AllowedUserIds, config.TargetChannelIds);
            var urlValidator = new UrlValidator(new DnsHostResolver());
            var upload = new UploadService(client, config.UploadRetries, config.AlsoSendMediaToRequester, logger);
            var coordinator = new DownloadCoordinator(downloader, gate, registry, tempDir, upload, client, config, logger);
            var commands = new CommandHandler(client, updater, gate, registry, config.DownloadTempDir, config, runner, logger);
            var router = new MessageRouter(access, urlValidator, coordinator, commands, client, config, logger);
            var bot = new BotService(client, router, logger);

            logger.Info("正在启动（本地 Bot API Server 模式）…");
            await bot.RunAsync(cts.Token).ConfigureAwait(false);
            logger.Info("已退出。");
            return 0;
        }
        catch (ConfigLoadException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (ConfigParseException ex)
        {
            Console.Error.WriteLine("配置错误：" + ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("启动失败：" + ex.Message);
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
        logger.Info($"配置来源：{config.SourcePath}");
        logger.Info($"Token：{MaskToken(config.BotToken)}");
        logger.Info($"本地 Bot API：{config.LocalApiBaseUrl}");
        logger.Info($"目标会话数：{config.TargetChannelIds.Count}，白名单用户数：{config.AllowedUserIds.Count}");
        logger.Info($"临时目录：{config.DownloadTempDir}");
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
}
