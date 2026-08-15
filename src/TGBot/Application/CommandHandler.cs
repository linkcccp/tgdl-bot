using System.Globalization;
using TGBot.Config;
using TGBot.Cookie;
using TGBot.Download;
using TGBot.Logging;
using TGBot.Messaging;
using TGBot.Security;
using TGBot.Texts;
using TGBot.Update;

namespace TGBot.Application;

/// <summary>
/// 指令处理器：/update、/status、/help、/cookie、/cookies。
/// </summary>
public sealed class CommandHandler
{
    private readonly ITelegramClient _client;
    private readonly IUpdater _updater;
    private readonly DownloadGate _gate;
    private readonly JobRegistry _registry;
    private readonly string _tempDir;
    private readonly CookieService _cookies;
    private readonly AppConfig _config;
    private readonly IProcessRunner _runner;
    private readonly IAppLogger _logger;
    private readonly DateTime _startTimeUtc;

    private (ToolVersion? Yt, ToolVersion? Ff)? _versionCache;
    private DateTime _versionCacheAt = DateTime.MinValue;

    /// <summary>
    /// 初始化 <see cref="CommandHandler"/>。
    /// </summary>
    /// <param name="client">Telegram 客户端。</param>
    /// <param name="updater">更新器。</param>
    /// <param name="gate">并发闸门。</param>
    /// <param name="registry">任务注册表。</param>
    /// <param name="tempDir">临时目录（用于磁盘空间显示）。</param>
    /// <param name="cookies">cookies 服务。</param>
    /// <param name="config">配置。</param>
    /// <param name="runner">进程运行器。</param>
    /// <param name="logger">日志器。</param>
    public CommandHandler(
        ITelegramClient client,
        IUpdater updater,
        DownloadGate gate,
        JobRegistry registry,
        string tempDir,
        CookieService cookies,
        AppConfig config,
        IProcessRunner runner,
        IAppLogger logger)
    {
        _client = client;
        _updater = updater;
        _gate = gate;
        _registry = registry;
        _tempDir = tempDir;
        _cookies = cookies;
        _config = config;
        _runner = runner;
        _logger = logger;
        _startTimeUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// 处理指令消息。
    /// </summary>
    /// <param name="msg">入站消息。</param>
    /// <param name="commandText">指令文本（以 / 开头）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task HandleAsync(InboundMessage msg, string commandText, CancellationToken cancellationToken)
    {
        var name = commandText.Split(' ')[0].ToLowerInvariant();
        switch (name)
        {
            case "/help":
                await SendToAsync(msg, UserTexts.Help, cancellationToken).ConfigureAwait(false);
                break;
            case "/status":
                await SendToAsync(msg, await BuildStatusAsync(cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
                break;
            case "/update":
                await HandleUpdateAsync(msg, cancellationToken).ConfigureAwait(false);
                break;
            case "/cookie":
                await HandleCookieCommandAsync(msg, commandText, cancellationToken).ConfigureAwait(false);
                break;
            case "/cookies":
                await HandleCookiesListAsync(msg, cancellationToken).ConfigureAwait(false);
                break;
            default:
                await SendToAsync(msg, UserTexts.UnknownCommand, cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    private async Task HandleCookieCommandAsync(InboundMessage msg, string commandText, CancellationToken cancellationToken)
    {
        var parts = commandText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            await SendToAsync(msg, string.Format(UserTexts.CookieUsage, _cookies.SiteListText()), cancellationToken).ConfigureAwait(false);
            return;
        }

        var siteKey = parts[1].ToLowerInvariant();
        var isClear = parts.Length >= 3 && parts[2].Equals("clear", StringComparison.OrdinalIgnoreCase);

        if (isClear)
        {
            var site = _cookies.ResolveSite(siteKey);
            if (site is null)
            {
                await SendToAsync(msg, string.Format(UserTexts.CookieUnknownSite, siteKey, _cookies.SiteListText()), cancellationToken).ConfigureAwait(false);
                return;
            }

            _cookies.Clear(site.Key);
            await SendToAsync(msg, string.Format(UserTexts.CookieDeleted, site.DisplayName), cancellationToken).ConfigureAwait(false);
            return;
        }

        var begin = _cookies.BeginPendingUpload(msg.ChatId, siteKey);
        if (begin is null)
        {
            await SendToAsync(msg, string.Format(UserTexts.CookieUnknownSite, siteKey, _cookies.SiteListText()), cancellationToken).ConfigureAwait(false);
            return;
        }

        await SendToAsync(msg, string.Format(UserTexts.CookiePrompt, begin.DisplayName), cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleCookiesListAsync(InboundMessage msg, CancellationToken cancellationToken)
    {
        var statuses = _cookies.List();
        if (statuses.Count == 0)
        {
            await SendToAsync(msg, UserTexts.CookieNone, cancellationToken).ConfigureAwait(false);
            return;
        }

        var lines = statuses.Select(s => $"  {s.DisplayName}（{s.Key}）：{(s.Has ? "✓ 已保存" : "无")}");
        await SendToAsync(msg, string.Format(UserTexts.CookieListTemplate, string.Join("\n", lines)), cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleUpdateAsync(InboundMessage msg, CancellationToken cancellationToken)
    {
        var requester = msg.ChatId;

        async Task Progress(string text) => await SendToAsync(msg, text, cancellationToken).ConfigureAwait(false);

        try
        {
            await using var slot = await _gate.AcquireExclusiveAsync(cancellationToken).ConfigureAwait(false);
            var report = await _updater.UpdateAsync(
                _config.UpdateYtDlp,
                _config.UpdateFfmpeg,
                _ => Progress(string.Empty).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false);
            await Progress(ComposeUpdateMessage(report)).ConfigureAwait(false);
        }
        catch (UpdateException ex)
        {
            _logger.Warn($"更新失败：{ex.Message}");
            await Progress(ex.UserMessage).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch (Exception ex)
        {
            _logger.Error("更新异常", ex);
            await Progress(UserTexts.UpdateFailed).ConfigureAwait(false);
        }
    }

    private static string ComposeUpdateMessage(UpdateReport report)
    {
        if (report.Tools.Count == 0)
        {
            return UserTexts.UpdateNotNeeded;
        }

        var lines = new List<string>();
        var anyFailed = false;
        var anyUpdated = false;
        foreach (var t in report.Tools)
        {
            switch (t.Status)
            {
                case ToolUpdateStatus.Updated:
                    anyUpdated = true;
                    lines.Add($"  {t.Tool}：{t.LocalVersion} → {t.LatestVersion}");
                    break;
                case ToolUpdateStatus.AlreadyUpToDate:
                    lines.Add($"  {t.Tool}：已是最新（{t.LocalVersion}）");
                    break;
                case ToolUpdateStatus.NotConfigured:
                    lines.Add($"  {t.Tool}：未配置安装路径，已跳过");
                    break;
                default:
                    anyFailed = true;
                    lines.Add($"  {t.Tool}：更新失败");
                    break;
            }
        }

        if (!anyUpdated && !anyFailed)
        {
            return UserTexts.UpdateNotNeeded;
        }

        return (anyFailed ? UserTexts.UpdateFailed + "\n" : "更新完成：\n") + string.Join("\n", lines);
    }

    private async Task<string> BuildStatusAsync(CancellationToken cancellationToken)
    {
        var uptime = DateTime.UtcNow - _startTimeUtc;
        var versions = await GetVersionsAsync(cancellationToken).ConfigureAwait(false);
        var free = DiskUtil.GetFreeSpaceBytes(_tempDir);

        return string.Format(
            UserTexts.StatusTemplate,
            FormatUptime(uptime),
            _registry.Running,
            _registry.Queued,
            versions.Yt?.ToString() ?? "未知",
            versions.Ff?.ToString() ?? "未知",
            free is { } f ? FormatBytes(f) : "未知");
    }

    private async Task<(ToolVersion? Yt, ToolVersion? Ff)> GetVersionsAsync(CancellationToken cancellationToken)
    {
        if (_versionCache is { } cached && DateTime.UtcNow - _versionCacheAt < TimeSpan.FromSeconds(60))
        {
            return cached;
        }

        var yt = await QueryVersionAsync(_config.YtDlpPath, "yt-dlp", cancellationToken).ConfigureAwait(false);
        var ff = await QueryVersionAsync(_config.FfmpegPath, "ffmpeg", cancellationToken).ConfigureAwait(false);
        _versionCache = (yt, ff);
        _versionCacheAt = DateTime.UtcNow;
        return _versionCache.Value;
    }

    private async Task<ToolVersion?> QueryVersionAsync(string? path, string tool, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var output = await _runner.RunAsync(
                path,
                tool == "ffmpeg" ? new[] { "-version" } : new[] { "--version" },
                null,
                TimeSpan.FromSeconds(15),
                cancellationToken).ConfigureAwait(false);
            return tool == "ffmpeg"
                ? BinaryVersionParser.ParseFfmpeg(output.StdOut)
                : BinaryVersionParser.ParseYtDlp(output.StdOut);
        }
        catch (Exception ex)
        {
            _logger.Warn($"查询 {tool} 版本失败：{ex.Message}");
            return null;
        }
    }

    private async Task SendToAsync(InboundMessage msg, string text, CancellationToken cancellationToken)
    {
        try
        {
            await _client.SendMessageAsync(msg.ChatId, text, msg.IsPrivate ? msg.TriggerMessageId : 0, null, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warn($"指令回复发送失败：{ex.Message}");
        }
    }

    private static string FormatUptime(TimeSpan t)
    {
        var parts = new List<string>();
        if (t.TotalDays >= 1)
        {
            parts.Add($"{(int)t.TotalDays}天");
        }

        if (t.Hours > 0 || parts.Count > 0)
        {
            parts.Add($"{t.Hours}小时");
        }

        parts.Add($"{t.Minutes}分");
        return string.Join(" ", parts);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024)
        {
            return $"{bytes / 1024.0 / 1024 / 1024:0.0} GB";
        }

        return $"{bytes / 1024.0 / 1024:0.0} MB";
    }
}
