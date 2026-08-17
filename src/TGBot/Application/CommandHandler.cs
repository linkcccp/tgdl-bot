// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

using System.Collections.Concurrent;
using System.Globalization;
using TGBot.Config;
using TGBot.Config.Overlay;
using TGBot.Cookie;
using TGBot.Download;
using TGBot.Logging;
using TGBot.Messaging;
using TGBot.Security;
using TGBot.Texts;
using TGBot.Texts.I18n;
using TGBot.Update;

namespace TGBot.Application;

/// <summary>
/// 指令处理器：/update、/status、/help、/cookie、/cookies、/language、/config、/access。
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
    private readonly II18n _i18n;
    private readonly UserLanguageStore _languageStore;
    private readonly OverlayStore _overlayStore;
    private readonly PendingNotifyStore _notifyStore;
    private readonly Action _restartTrigger;
    private readonly IReadOnlyDictionary<string, string>? _configRawValues;
    private readonly ConcurrentDictionary<long, DateTime> _languagePrompts = new();
    private readonly IAppLogger _logger;
    private readonly DateTime _startTimeUtc;

    /// <summary>配置变更触发重启的节流窗口：窗口内多次变更合并为一次重启（防白名单用户 DoS）。</summary>
    public static readonly TimeSpan DefaultRestartThrottleWindow = TimeSpan.FromSeconds(60);

    private readonly TimeSpan _restartThrottleWindow;
    private readonly object _restartLock = new();
    private DateTime _lastRestartTriggerUtc = DateTime.MinValue;

    /// <summary>语言选择键盘的有效期（超时未点仅丢弃回调，不打扰）。</summary>
    public static readonly TimeSpan LanguagePromptTimeout = TimeSpan.FromMinutes(2);

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
    /// <param name="config">生效配置（已应用 overlay）。</param>
    /// <param name="runner">进程运行器。</param>
    /// <param name="logger">日志器。</param>
    /// <param name="i18n">国际化服务。</param>
    /// <param name="languageStore">用户语言存储（/language 命令）。</param>
    /// <param name="overlayStore">overlay 存储（/config、/access 读写）。</param>
    /// <param name="notifyStore">重启通知存储（变更后先同步写盘再触发重启）。</param>
    /// <param name="restartTrigger">重启触发器（优雅退出，容器 unless-stopped 自动拉起）。</param>
    /// <param name="configRawValues">config.conf 原始键值（/config list 标注来源；可为空）。</param>
    /// <param name="restartThrottleWindow">重启节流窗口（默认 <see cref="DefaultRestartThrottleWindow"/>；测试可注入短窗口）。</param>
    public CommandHandler(
        ITelegramClient client,
        IUpdater updater,
        DownloadGate gate,
        JobRegistry registry,
        string tempDir,
        CookieService cookies,
        AppConfig config,
        IProcessRunner runner,
        IAppLogger logger,
        II18n i18n,
        UserLanguageStore languageStore,
        OverlayStore overlayStore,
        PendingNotifyStore notifyStore,
        Action restartTrigger,
        IReadOnlyDictionary<string, string>? configRawValues = null,
        TimeSpan? restartThrottleWindow = null)
    {
        _client = client;
        _updater = updater;
        _gate = gate;
        _registry = registry;
        _tempDir = tempDir;
        _cookies = cookies;
        _config = config;
        _runner = runner;
        _i18n = i18n;
        _languageStore = languageStore;
        _overlayStore = overlayStore;
        _notifyStore = notifyStore;
        _restartTrigger = restartTrigger;
        _configRawValues = configRawValues;
        _logger = logger;
        _startTimeUtc = DateTime.UtcNow;
        _restartThrottleWindow = restartThrottleWindow ?? DefaultRestartThrottleWindow;
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
                await SendToAsync(msg, _i18n.Get(msg.Language, UserTexts.Help), cancellationToken).ConfigureAwait(false);
                break;
            case "/status":
                await SendToAsync(msg, await BuildStatusAsync(msg.Language, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
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
            case "/language":
                await HandleLanguageCommandAsync(msg, commandText, cancellationToken).ConfigureAwait(false);
                break;
            case "/config":
                await HandleConfigCommandAsync(msg, commandText, cancellationToken).ConfigureAwait(false);
                break;
            case "/access":
                await HandleAccessCommandAsync(msg, commandText, cancellationToken).ConfigureAwait(false);
                break;
            default:
                await SendToAsync(msg, _i18n.Get(msg.Language, UserTexts.UnknownCommand), cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    private async Task HandleCookieCommandAsync(InboundMessage msg, string commandText, CancellationToken cancellationToken)
    {
        var parts = commandText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            await SendToAsync(msg, _i18n.Get(msg.Language, UserTexts.CookieUsage, _cookies.SiteListText()), cancellationToken).ConfigureAwait(false);
            return;
        }

        var siteKey = parts[1].ToLowerInvariant();
        var isClear = parts.Length >= 3 && parts[2].Equals("clear", StringComparison.OrdinalIgnoreCase);

        if (isClear)
        {
            var site = _cookies.ResolveSite(siteKey);
            if (site is null)
            {
                await SendToAsync(msg, _i18n.Get(msg.Language, UserTexts.CookieUnknownSite, siteKey, _cookies.SiteListText()), cancellationToken).ConfigureAwait(false);
                return;
            }

            _cookies.Clear(site.Key);
            await SendToAsync(msg, _i18n.Get(msg.Language, UserTexts.CookieDeleted, site.DisplayName), cancellationToken).ConfigureAwait(false);
            return;
        }

        var begin = _cookies.BeginPendingUpload(msg.ChatId, siteKey);
        if (begin is null)
        {
            await SendToAsync(msg, _i18n.Get(msg.Language, UserTexts.CookieUnknownSite, siteKey, _cookies.SiteListText()), cancellationToken).ConfigureAwait(false);
            return;
        }

        await SendToAsync(msg, _i18n.Get(msg.Language, UserTexts.CookiePrompt, begin.DisplayName), cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleCookiesListAsync(InboundMessage msg, CancellationToken cancellationToken)
    {
        var statuses = _cookies.List();
        if (statuses.Count == 0)
        {
            await SendToAsync(msg, _i18n.Get(msg.Language, UserTexts.CookieNone), cancellationToken).ConfigureAwait(false);
            return;
        }

        var saved = _i18n.Get(msg.Language, UserTexts.CookieStateSaved);
        var none = _i18n.Get(msg.Language, UserTexts.CookieStateNone);
        var lines = statuses.Select(s => _i18n.Get(msg.Language, UserTexts.CookieListLine, s.DisplayName, s.Key, s.Has ? saved : none));
        await SendToAsync(msg, _i18n.Get(msg.Language, UserTexts.CookieListTemplate, string.Join("\n", lines)), cancellationToken).ConfigureAwait(false);
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
            await Progress(ComposeUpdateMessage(report, msg.Language)).ConfigureAwait(false);
        }
        catch (UpdateException ex)
        {
            _logger.Warn($"更新失败：{ex.Message}");
            await Progress(UpdateReasonText(ex.Reason, msg.Language)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch (Exception ex)
        {
            _logger.Error("更新异常", ex);
            await Progress(_i18n.Get(msg.Language, UserTexts.UpdateFailed)).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 按更新失败原因渲染用户提示（<see cref="UpdateException"/> 的 detail 仅作内部日志，不直接发送）。
    /// </summary>
    /// <param name="reason">失败原因。</param>
    /// <param name="lang">消息语言。</param>
    /// <returns>用户提示。</returns>
    private string UpdateReasonText(UpdateFailureReason reason, string lang)
        => reason switch
        {
            UpdateFailureReason.LocalVersionUnavailable => _i18n.Get(lang, UserTexts.UpdateFailedLocalVersion),
            UpdateFailureReason.LatestVersionUnavailable => _i18n.Get(lang, UserTexts.UpdateFailedLatestVersion),
            UpdateFailureReason.DownloadFailed => _i18n.Get(lang, UserTexts.UpdateFailedDownload),
            UpdateFailureReason.ReplaceFailed => _i18n.Get(lang, UserTexts.UpdateFailedReplace),
            _ => _i18n.Get(lang, UserTexts.UpdateFailed),
        };

    private string ComposeUpdateMessage(UpdateReport report, string lang)
    {
        if (report.Tools.Count == 0)
        {
            return _i18n.Get(lang, UserTexts.UpdateNotNeeded);
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
                    lines.Add(_i18n.Get(lang, UserTexts.UpdateLineUpdated, t.Tool, t.LocalVersion?.ToString() ?? _i18n.Get(lang, UserTexts.Unknown), t.LatestVersion?.ToString() ?? _i18n.Get(lang, UserTexts.Unknown)));
                    break;
                case ToolUpdateStatus.AlreadyUpToDate:
                    lines.Add(_i18n.Get(lang, UserTexts.UpdateLineUpToDate, t.Tool, t.LocalVersion?.ToString() ?? _i18n.Get(lang, UserTexts.Unknown)));
                    break;
                case ToolUpdateStatus.NotConfigured:
                    lines.Add(_i18n.Get(lang, UserTexts.UpdateLineNotConfigured, t.Tool));
                    break;
                default:
                    anyFailed = true;
                    lines.Add(_i18n.Get(lang, UserTexts.UpdateLineFailed, t.Tool));
                    break;
            }
        }

        if (!anyUpdated && !anyFailed)
        {
            return _i18n.Get(lang, UserTexts.UpdateNotNeeded);
        }

        return (anyFailed ? _i18n.Get(lang, UserTexts.UpdateFailed) + "\n" : _i18n.Get(lang, UserTexts.UpdateDoneHeader)) + string.Join("\n", lines);
    }

    private async Task<string> BuildStatusAsync(string lang, CancellationToken cancellationToken)
    {
        var uptime = DateTime.UtcNow - _startTimeUtc;
        var versions = await GetVersionsAsync(cancellationToken).ConfigureAwait(false);
        var free = DiskUtil.GetFreeSpaceBytes(_tempDir);
        var unknown = _i18n.Get(lang, UserTexts.Unknown);

        return _i18n.Get(lang, UserTexts.StatusBotVersion, AppInfo.Version) + "\n" + _i18n.Get(
            lang,
            UserTexts.StatusTemplate,
            FormatUptime(uptime, lang),
            _registry.Running,
            _registry.Queued,
            versions.Yt?.ToString() ?? unknown,
            versions.Ff?.ToString() ?? unknown,
            free is { } f ? FormatBytes(f) : unknown);
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

    /// <summary>
    /// 处理 /language 命令：带参数直接设置（en/zh），无参数弹语言选择键盘。
    /// </summary>
    /// <param name="msg">入站消息。</param>
    /// <param name="commandText">指令文本。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task HandleLanguageCommandAsync(InboundMessage msg, string commandText, CancellationToken cancellationToken)
    {
        var parts = commandText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 2 &&
            BotLanguageExtensions.TryParseCode(parts[1], out var requested))
        {
            var code = requested.Code();
            if (msg.SenderUserId is { } uid)
            {
                _languageStore.Set(uid, code);
            }

            var name = _i18n.Get(code, code == "zh" ? UserTexts.LanguageNameZh : UserTexts.LanguageNameEn);
            await SendToAsync(msg, _i18n.Get(code, UserTexts.LanguageSaved, name), cancellationToken).ConfigureAwait(false);
            return;
        }

        // 无参数 → 弹语言选择键盘（内存去重防并发重复弹窗，超时仅丢弃回调）。
        // 与 MessageRouter 首次弹窗共用同一登记：已弹过（2 分钟内）则静默忽略本次触发。
        if (msg.SenderUserId is { } promptUid)
        {
            if (!RegisterLanguagePrompt(promptUid))
            {
                return;
            }

            await PromptLanguageAsync(msg, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 登记一次语言选择弹窗（内存去重：同一用户重复触发仅首次生效）。
    /// </summary>
    /// <param name="userId">用户 ID。</param>
    /// <returns>首次登记返回 <see langword="true"/>；已弹过返回 <see langword="false"/>。</returns>
    public bool RegisterLanguagePrompt(long userId)
        => _languagePrompts.TryAdd(userId, DateTime.UtcNow + LanguagePromptTimeout);

    /// <summary>
    /// 向用户发送语言选择键盘（首次弹窗与 /language 共用）。
    /// </summary>
    /// <param name="msg">目标消息（其会话与语言决定文案）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>完成异步操作。</returns>
    public async Task PromptLanguageAsync(InboundMessage msg, CancellationToken cancellationToken)
    {
        var keyboard = new[]
        {
            new InlineButton(_i18n.Get(msg.Language, UserTexts.LanguageNameZh), "lang:zh"),
            new InlineButton(_i18n.Get(msg.Language, UserTexts.LanguageNameEn), "lang:en"),
        };
        try
        {
            await _client.SendMessageAsync(msg.ChatId, _i18n.Get(msg.Language, UserTexts.LanguagePrompt), 0, keyboard, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warn($"语言选择发送失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 处理 <c>lang:</c> 回调：仅点击者本人生效、弹窗未超时则写入语言存储并回执确认。
    /// </summary>
    /// <param name="msg">回调消息（<c>CallbackData</c> 形如 <c>lang:zh</c>）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>完成异步操作。</returns>
    public async Task HandleLanguageCallbackAsync(InboundMessage msg, CancellationToken cancellationToken)
    {
        var data = msg.CallbackData;
        if (string.IsNullOrEmpty(data) || !data.StartsWith("lang:", StringComparison.Ordinal))
        {
            return;
        }

        var parts = data.Split(':');
        if (parts.Length != 2 || !BotLanguageExtensions.TryParseCode(parts[1], out var language))
        {
            return;
        }

        var code = language.Code();

        // 校验：仅点击者本人（私聊会话归属）且弹窗未超时
        if (msg.SenderUserId is not { } uid || msg.ChatId != uid || !IsLanguagePromptValid(uid))
        {
            return;
        }

        _languageStore.Set(uid, code);
        var name = _i18n.Get(code, code == "zh" ? UserTexts.LanguageNameZh : UserTexts.LanguageNameEn);
        await SendToAsync(msg, _i18n.Get(code, UserTexts.LanguageSaved, name), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 校验语言回调是否仍然有效（本进程内弹过窗且未超时）；过期项顺带移除，防字典无界增长。
    /// </summary>
    /// <param name="userId">点击者用户 ID。</param>
    /// <returns>有效返回 <see langword="true"/>。</returns>
    public bool IsLanguagePromptValid(long userId)
    {
        if (_languagePrompts.TryGetValue(userId, out var expiry) && DateTime.UtcNow <= expiry)
        {
            return true;
        }

        _languagePrompts.TryRemove(userId, out _);
        return false;
    }

    private string FormatUptime(TimeSpan t, string lang)
    {
        var parts = new List<string>();
        if (t.TotalDays >= 1)
        {
            parts.Add(_i18n.Get(lang, UserTexts.UptimeDays, (int)t.TotalDays));
        }

        if (t.Hours > 0 || parts.Count > 0)
        {
            parts.Add(_i18n.Get(lang, UserTexts.UptimeHours, t.Hours));
        }

        parts.Add(_i18n.Get(lang, UserTexts.UptimeMinutes, t.Minutes));
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

    // —— /config ——

    private static string[] SplitArgs(string commandText)
        => commandText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// 处理 /config 命令：list / set / reset / reset-all。
    /// </summary>
    /// <param name="msg">入站消息。</param>
    /// <param name="commandText">指令文本。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>完成异步操作。</returns>
    private async Task HandleConfigCommandAsync(InboundMessage msg, string commandText, CancellationToken cancellationToken)
    {
        var parts = SplitArgs(commandText);
        if (parts.Length < 2)
        {
            await SendToAsync(msg, _i18n.Get(msg.Language, UserTexts.ConfigUsage), cancellationToken).ConfigureAwait(false);
            return;
        }

        switch (parts[1].ToLowerInvariant())
        {
            case "list":
                await ConfigListAsync(msg, cancellationToken).ConfigureAwait(false);
                break;
            case "set" when parts.Length >= 4:
                await ConfigSetAsync(msg, parts[2], string.Join(' ', parts.Skip(3)), cancellationToken).ConfigureAwait(false);
                break;
            case "reset" when parts.Length >= 3:
                await ConfigResetAsync(msg, parts[2], cancellationToken).ConfigureAwait(false);
                break;
            case "reset-all":
                await ConfigResetAllAsync(msg, cancellationToken).ConfigureAwait(false);
                break;
            default:
                await SendToAsync(msg, _i18n.Get(msg.Language, UserTexts.ConfigUsage), cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    /// <summary>
    /// /config list：列出全部可改键、当前生效值与来源（overlay / config.conf / 默认）。
    /// </summary>
    private async Task ConfigListAsync(InboundMessage msg, CancellationToken cancellationToken)
    {
        var overlay = _overlayStore.LoadConfig();
        var lines = ConfigParser.MutableKeys.Select(key =>
        {
            var effective = ConfigParser.DisplayValue(_config, key);
            var display = string.IsNullOrEmpty(effective) ? _i18n.Get(msg.Language, UserTexts.ValueEmpty) : effective;
            var sourceKey = overlay.ContainsKey(key)
                ? UserTexts.ConfigSourceOverlay
                : _configRawValues?.ContainsKey(key) == true
                    ? UserTexts.ConfigSourceConfig
                    : UserTexts.ConfigSourceDefault;
            return _i18n.Get(msg.Language, UserTexts.ConfigListLine, key, display, _i18n.Get(msg.Language, sourceKey));
        });

        await SendToAsync(msg, _i18n.Get(msg.Language, UserTexts.ConfigListTemplate, string.Join("\n", lines)), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// /config set &lt;Key&gt; &lt;value&gt;：复用 <see cref="ConfigParser.ValidateValue"/> 单点校验，
    /// 通过后写 overlay → 写 pending-notify → 触发重启。
    /// </summary>
    private async Task ConfigSetAsync(InboundMessage msg, string key, string value, CancellationToken cancellationToken)
    {
        if (!ConfigParser.TryResolveKey(key, out var canonical))
        {
            await SendToAsync(msg, _i18n.Get(msg.Language, UserTexts.ConfigUnknownKey, key), cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!ConfigParser.MutableKeys.Contains(canonical))
        {
            await SendToAsync(msg, _i18n.Get(msg.Language, UserTexts.ConfigLockedKey, canonical), cancellationToken).ConfigureAwait(false);
            return;
        }

        var error = ConfigParser.ValidateValue(key, value);
        if (error is not null)
        {
            await SendToAsync(msg, _i18n.Get(msg.Language, UserTexts.ConfigRejected, error), cancellationToken).ConfigureAwait(false);
            return;
        }

        // 落盘前归一化（去引号+去首尾空白），与 ValidateValue/config.conf 解析同一来源，保证重启后值一致。
        var normalized = ConfigParser.NormalizeValue(value);

        // 同值 set：与当前生效值相同（overlay 现值 → config.conf 原始值 → 默认值）→ 已生效，
        // 无需重复写 overlay / 重启（防刷重启）。布尔键做语义比较（true/yes/on/1 等价）。
        if (IsSameEffectiveValue(canonical, normalized))
        {
            await SendToAsync(msg, _i18n.Get(msg.Language, UserTexts.ConfigNoChange, canonical), cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!_overlayStore.SetConfigValue(canonical, normalized))
        {
            await SendToAsync(msg, _i18n.Get(msg.Language, UserTexts.ConfigSaveFailed), cancellationToken).ConfigureAwait(false);
            return;
        }

        var ack = _i18n.Get(msg.Language, UserTexts.ConfigSetApplied, canonical);
        if (ConfigParser.RequiresRiskWarning(canonical))
        {
            ack += "\n" + _i18n.Get(msg.Language, UserTexts.ConfigRiskWarning);
        }

        await RestartAndNotifyAsync(msg, ack, UserTexts.ConfigApplied, new[] { canonical }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 判断 /config set 的目标值是否与当前生效值相同（来源链：overlay → config.conf → 默认值）。
    /// <para>布尔键按语义比较（true/yes/on/1 视作等价，与 <see cref="ConfigParser.ParseBoolValue"/> 一致）；
    /// 其余键字符串比较（调用方已归一化，config.conf 原始值亦经同一归一化规则）。</para>
    /// </summary>
    /// <param name="canonical">规范键名（已通过校验）。</param>
    /// <param name="normalized">归一化后的目标值。</param>
    /// <returns>与生效值相同返回 <see langword="true"/>。</returns>
    private bool IsSameEffectiveValue(string canonical, string normalized)
    {
        if (_overlayStore.LoadConfig().TryGetValue(canonical, out var overlayValue))
        {
            return SemanticallyEqual(canonical, overlayValue, normalized);
        }

        if (_configRawValues?.TryGetValue(canonical, out var rawValue) == true)
        {
            return SemanticallyEqual(canonical, rawValue, normalized);
        }

        return SemanticallyEqual(canonical, ConfigParser.DisplayValue(_config, canonical), normalized);
    }

    private static bool SemanticallyEqual(string canonical, string current, string target)
        => canonical is "ExtractAudio" or "AlsoSendMediaToRequester" or "AllowPrivateUrls"
            or "AllowPlaylists" or "UpdateYtDlp" or "UpdateFfmpeg"
            ? ConfigParser.ParseBoolValue(current) == ConfigParser.ParseBoolValue(target)
            : string.Equals(current, target, StringComparison.Ordinal);

    /// <summary>
    /// /config reset &lt;Key&gt;：删除 overlay 覆盖项（恢复安装配置/默认值）→ 重启。
    /// </summary>
    private async Task ConfigResetAsync(InboundMessage msg, string key, CancellationToken cancellationToken)
    {
        if (!ConfigParser.TryResolveKey(key, out var canonical))
        {
            await SendToAsync(msg, _i18n.Get(msg.Language, UserTexts.ConfigUnknownKey, key), cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!ConfigParser.MutableKeys.Contains(canonical))
        {
            await SendToAsync(msg, _i18n.Get(msg.Language, UserTexts.ConfigLockedKey, canonical), cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!_overlayStore.RemoveConfigKey(canonical))
        {
            await SendToAsync(msg, _i18n.Get(msg.Language, UserTexts.ConfigNotOverridden, canonical), cancellationToken).ConfigureAwait(false);
            return;
        }

        await RestartAndNotifyAsync(
            msg,
            _i18n.Get(msg.Language, UserTexts.ConfigResetApplied, canonical),
            UserTexts.ConfigApplied,
            new[] { canonical },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// /config reset-all：清空整个 config-overlay.json → 重启。
    /// </summary>
    private async Task ConfigResetAllAsync(InboundMessage msg, CancellationToken cancellationToken)
    {
        if (!_overlayStore.ClearConfig())
        {
            await SendToAsync(msg, _i18n.Get(msg.Language, UserTexts.ConfigNotOverridden, "reset-all"), cancellationToken).ConfigureAwait(false);
            return;
        }

        await RestartAndNotifyAsync(
            msg,
            _i18n.Get(msg.Language, UserTexts.ConfigResetAllApplied),
            UserTexts.ConfigApplied,
            new[] { "reset-all" },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 重启生效链路（设计遗留问题 5 的时序约束）：
    /// <list type="number">
    /// <item>同步写 pending-notify（必须在进程退出前落盘）。</item>
    /// <item>发送变更回执。</item>
    /// <item>节流触发优雅退出 → 容器 unless-stopped 自动拉起 → 新进程消费通知；
    /// 节流窗口（<see cref="DefaultRestartThrottleWindow"/>）内的后续变更只覆盖 overlay 与 pending-notify，合并为一次重启。</item>
    /// </list>
    /// </summary>
    /// <param name="msg">入站消息（回执发送目标，语言即通知语言）。</param>
    /// <param name="ackText">立即发送的回执文本。</param>
    /// <param name="notifyKey">重启后通知的文案键。</param>
    /// <param name="notifyArgs">通知占位符参数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>完成异步操作。</returns>
    private async Task RestartAndNotifyAsync(
        InboundMessage msg,
        string ackText,
        string notifyKey,
        string[] notifyArgs,
        CancellationToken cancellationToken)
    {
        var notify = new PendingNotify(
            ChatId: msg.ChatId,
            TextKey: notifyKey,
            Args: notifyArgs,
            Lang: msg.Language,
            CreatedAt: DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            Attempts: 0);

        // 1. 同步写盘（阻塞完成后再继续，保证退出前已持久化）。
        if (!_notifyStore.Save(notify))
        {
            _logger.Error("pending-notify 写入失败，重启已中止");
            await SendToAsync(msg, _i18n.Get(msg.Language, UserTexts.ConfigSaveFailed), cancellationToken).ConfigureAwait(false);
            return;
        }

        // 2. 回执。
        await SendToAsync(msg, ackText, cancellationToken).ConfigureAwait(false);

        // 3. 触发优雅退出 → 容器 restart: unless-stopped 拉起。
        //    节流：窗口内已触发过则合并——后续变更继续覆盖 overlay + pending-notify，
        //    随已安排的重启一次性生效，避免连续变更反复重启（DoS 防护）。
        var shouldTrigger = false;
        lock (_restartLock)
        {
            if (DateTime.UtcNow - _lastRestartTriggerUtc >= _restartThrottleWindow)
            {
                _lastRestartTriggerUtc = DateTime.UtcNow;
                shouldTrigger = true;
            }
        }

        if (shouldTrigger)
        {
            _logger.Info($"配置变更已持久化，触发自动重启（notify={notifyKey}）");
            _restartTrigger();
        }
        else
        {
            _logger.Info($"节流窗口内已有重启安排，本次变更合并生效（notify={notifyKey}）");
        }
    }

    // —— /access ——

    /// <summary>
    /// 处理 /access 命令：add / del / list（bot 独立白名单，与安装配置分开存储、合并生效）。
    /// </summary>
    /// <param name="msg">入站消息。</param>
    /// <param name="commandText">指令文本。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>完成异步操作。</returns>
    private async Task HandleAccessCommandAsync(InboundMessage msg, string commandText, CancellationToken cancellationToken)
    {
        var parts = SplitArgs(commandText);
        if (parts.Length < 2)
        {
            await SendToAsync(msg, _i18n.Get(msg.Language, UserTexts.AccessUsage), cancellationToken).ConfigureAwait(false);
            return;
        }

        switch (parts[1].ToLowerInvariant())
        {
            case "add" when parts.Length >= 4:
                await AccessAddAsync(msg, parts[2], parts[3], cancellationToken).ConfigureAwait(false);
                break;
            case "del" when parts.Length >= 4:
                await AccessRemoveAsync(msg, parts[2], parts[3], cancellationToken).ConfigureAwait(false);
                break;
            case "list":
                await AccessListAsync(msg, cancellationToken).ConfigureAwait(false);
                break;
            default:
                await SendToAsync(msg, _i18n.Get(msg.Language, UserTexts.AccessUsage), cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    /// <summary>
    /// /access add &lt;user|channel&gt; &lt;id&gt;：追加到 overlay（去重）→ 重启。
    /// </summary>
    private async Task AccessAddAsync(InboundMessage msg, string type, string rawId, CancellationToken cancellationToken)
    {
        if (!TryParseAccessId(type, rawId, out var id))
        {
            await SendToAsync(msg, _i18n.Get(msg.Language, UserTexts.AccessInvalidId, rawId), cancellationToken).ConfigureAwait(false);
            return;
        }

        var isUser = IsUserType(type);
        var added = isUser ? _overlayStore.AddAccessUser(id) : _overlayStore.AddAccessChannel(id);
        if (!added)
        {
            await SendToAsync(msg, _i18n.Get(msg.Language, UserTexts.AccessAlreadyAdded), cancellationToken).ConfigureAwait(false);
            return;
        }

        var typeLabel = _i18n.Get(msg.Language, isUser ? UserTexts.AccessTypeUser : UserTexts.AccessTypeChannel);
        var idText = id.ToString(CultureInfo.InvariantCulture);
        await RestartAndNotifyAsync(
            msg,
            _i18n.Get(msg.Language, UserTexts.AccessAdded, typeLabel, idText),
            UserTexts.AccessAdded,
            new[] { typeLabel, idText },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// /access del &lt;user|channel&gt; &lt;id&gt;：从 overlay 删除 → 重启；
    /// 安装配置来源的成员不可删除（设计如此），仅可删除 bot 添加项。
    /// </summary>
    private async Task AccessRemoveAsync(InboundMessage msg, string type, string rawId, CancellationToken cancellationToken)
    {
        if (!TryParseAccessId(type, rawId, out var id))
        {
            await SendToAsync(msg, _i18n.Get(msg.Language, UserTexts.AccessInvalidId, rawId), cancellationToken).ConfigureAwait(false);
            return;
        }

        var isUser = IsUserType(type);
        var removed = isUser ? _overlayStore.RemoveAccessUser(id) : _overlayStore.RemoveAccessChannel(id);
        if (!removed)
        {
            var inConfig = isUser ? _config.AllowedUserIds.Contains(id) : _config.TargetChannelIds.Contains(id);
            await SendToAsync(
                msg,
                _i18n.Get(msg.Language, inConfig ? UserTexts.AccessRemovedFromConfig : UserTexts.AccessNotFound),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var typeLabel = _i18n.Get(msg.Language, isUser ? UserTexts.AccessTypeUser : UserTexts.AccessTypeChannel);
        var idText = id.ToString(CultureInfo.InvariantCulture);
        await RestartAndNotifyAsync(
            msg,
            _i18n.Get(msg.Language, UserTexts.AccessRemoved, typeLabel, idText),
            UserTexts.AccessRemoved,
            new[] { typeLabel, idText },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// /access list：安装配置 ∪ overlay 的合并列表，逐条标注来源。
    /// </summary>
    private async Task AccessListAsync(InboundMessage msg, CancellationToken cancellationToken)
    {
        var merged = AccessListMerge.Merge(_config.AllowedUserIds, _config.TargetChannelIds, _overlayStore.LoadAccess());
        var lines = new List<string>();
        foreach (var entry in merged.Users)
        {
            lines.Add(BuildAccessLine(msg.Language, UserTexts.AccessTypeUser, entry));
        }

        foreach (var entry in merged.Channels)
        {
            lines.Add(BuildAccessLine(msg.Language, UserTexts.AccessTypeChannel, entry));
        }

        await SendToAsync(msg, _i18n.Get(msg.Language, UserTexts.AccessListTemplate, string.Join("\n", lines)), cancellationToken).ConfigureAwait(false);
    }

    private string BuildAccessLine(string lang, string typeKey, AccessEntry entry)
    {
        var type = _i18n.Get(lang, typeKey);
        var sourceKey = entry.Source == AccessEntrySource.Config ? UserTexts.AccessSourceConfig : UserTexts.AccessSourceOverlay;
        return _i18n.Get(lang, UserTexts.AccessListLine, type, entry.Id.ToString(CultureInfo.InvariantCulture), _i18n.Get(lang, sourceKey));
    }

    private static bool IsUserType(string type)
        => type.Equals("user", StringComparison.OrdinalIgnoreCase);

    private static bool TryParseAccessId(string type, string rawId, out long id)
    {
        id = 0;
        if (!long.TryParse(rawId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        // 用户 ID 必须为正整数（与安装向导规则一致）；频道/群组允许负数（如 -100xxx），0 均非法。
        if (parsed == 0 || (IsUserType(type) && parsed < 0))
        {
            return false;
        }

        id = parsed;
        return true;
    }
}
