// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

using System.Globalization;
using System.Text.RegularExpressions;
using TGBot.Logging;

namespace TGBot.Config;

/// <summary>
/// 解析结果，包含配置对象、解析警告与配置文件原始键值（供 /config list 标注来源）。
/// </summary>
/// <param name="Config">解析得到的配置。</param>
/// <param name="Warnings">解析警告（如未知配置项）。</param>
/// <param name="RawValues">配置文件中出现的规范键 → 原始值（不含未写入文件的默认值）。</param>
public sealed record ConfigParseResult(
    AppConfig Config,
    IReadOnlyList<string> Warnings,
    IReadOnlyDictionary<string, string> RawValues);

/// <summary>
/// config.conf 解析器。
/// <para>格式：每行 <c>键 = 值</c>，支持 <c>#</c>/<c>;</c> 注释、空行与 <c>[section]</c> 段头。
/// 解析失败抛出 <see cref="ConfigParseException"/>，消息为**中英双行并列**（启动期无用户上下文，双行最稳妥）。</para>
/// <para>值校验单点：<see cref="ValidateValue"/>，/config set 与启动解析共用，禁止在其他模块重复实现规则。</para>
/// </summary>
public static class ConfigParser
{
    private static readonly Regex KeyRegex = new("^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.Compiled);

    private static readonly Dictionary<string, string> Alias = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AllowedUserIds"] = "AllowedUserIds",
        ["AllowedUsers"] = "AllowedUserIds",
        ["TargetChannelIds"] = "TargetChannelIds",
        ["TargetChannels"] = "TargetChannelIds",
        ["BotToken"] = "BotToken",
        ["Token"] = "BotToken",
        ["LocalApiBaseUrl"] = "LocalApiBaseUrl",
        ["LocalApiUrl"] = "LocalApiBaseUrl",
        ["DownloadTempDir"] = "DownloadTempDir",
        ["TempDir"] = "DownloadTempDir",
        ["YtDlpPath"] = "YtDlpPath",
        ["FfmpegPath"] = "FfmpegPath",
        ["CookieStoreDir"] = "CookieStoreDir",
        ["YtDlpProxy"] = "YtDlpProxy",
        ["YtDlpExtraArgs"] = "YtDlpExtraArgs",
        ["YtDlpYoutubePlayerClients"] = "YtDlpYoutubePlayerClients",
        ["TgdlDefaultMode"] = "TgdlDefaultMode",
        ["DefaultMode"] = "TgdlDefaultMode",
        ["TgdlLanguage"] = "TgdlLanguage",
        ["Language"] = "TgdlLanguage",
        ["StateDir"] = "StateDir",
        ["MaxConcurrentDownloads"] = "MaxConcurrentDownloads",
        ["Concurrency"] = "MaxConcurrentDownloads",
        ["LogLevel"] = "LogLevel",
        ["LogFile"] = "LogFile",
        ["DownloadRetries"] = "DownloadRetries",
        ["DownloadTimeoutSeconds"] = "DownloadTimeoutSeconds",
        ["UploadRetries"] = "UploadRetries",
        ["ExtractAudio"] = "ExtractAudio",
        ["AlsoSendMediaToRequester"] = "AlsoSendMediaToRequester",
        ["AllowPrivateUrls"] = "AllowPrivateUrls",
        ["MaxMediaSizeBytes"] = "MaxMediaSizeBytes",
        ["AllowPlaylists"] = "AllowPlaylists",
        ["MergeFormat"] = "MergeFormat",
        ["UpdateYtDlp"] = "UpdateYtDlp",
        ["UpdateFfmpeg"] = "UpdateFfmpeg",
    };

    private static readonly string[] RequiredKeys =
    {
        "BotToken", "LocalApiBaseUrl", "TargetChannelIds", "AllowedUserIds", "DownloadTempDir",
    };

    /// <summary>
    /// 安装配置锁键：不允许经 /config 或 overlay 修改。
    /// <para>含 StateDir：状态目录（languages/overlay/pending-notify）不应由 bot 远程改动，
    /// 避免配置与状态目录分裂（overlay 自身始终以 config.conf 推导的目录读取）。</para>
    /// </summary>
    public static readonly IReadOnlyCollection<string> LockedKeys = new[] { "BotToken", "AllowedUserIds", "TargetChannelIds", "StateDir" };

    /// <summary>可通过 /config 修改的键（全部配置键去除安装锁键，按名称排序）。</summary>
    public static readonly IReadOnlyCollection<string> MutableKeys = Alias.Values
        .Distinct(StringComparer.Ordinal)
        .Where(k => !LockedKeys.Contains(k))
        .OrderBy(k => k, StringComparer.Ordinal)
        .ToArray();

    /// <summary>
    /// 判断键是否为连接/路径类（改错可能导致无法连接或启动失败，/config set 需回显风险警告）。
    /// </summary>
    /// <param name="canonicalKey">规范键名。</param>
    /// <returns>属于风险类返回 <see langword="true"/>。</returns>
    public static bool RequiresRiskWarning(string canonicalKey)
        => canonicalKey is "LocalApiBaseUrl" or "DownloadTempDir" or "YtDlpPath" or "FfmpegPath"
            or "CookieStoreDir" or "LogFile";

    /// <summary>
    /// 将键名（别名或规范名）解析为规范键名（大小写不敏感，与 subcommand 解析一致）。
    /// </summary>
    /// <param name="key">键名。</param>
    /// <param name="canonical">解析出的规范键名。</param>
    /// <returns>已知键返回 <see langword="true"/>。</returns>
    public static bool TryResolveKey(string key, out string canonical)
    {
        if (Alias.TryGetValue(key, out var resolved))
        {
            canonical = resolved;
            return true;
        }

        canonical = string.Empty;
        return false;
    }

    /// <summary>
    /// 归一化配置值：去除首尾空白与包围引号（与 config.conf 解析一致）。
    /// <para>落盘/应用前必须先归一化，否则含引号的值会导致重启后解析错乱（单一来源）。</para>
    /// </summary>
    /// <param name="value">原始值。</param>
    /// <returns>归一化后的值。</returns>
    public static string NormalizeValue(string value) => StripQuotes(value.Trim());

    /// <summary>
    /// 单点值校验：/config set 与启动解析共用。
    /// </summary>
    /// <param name="key">键名（支持别名，大小写不敏感）。</param>
    /// <param name="value">原始值（校验前经 <see cref="NormalizeValue"/> 归一化）。</param>
    /// <returns>校验通过返回 <see langword="null"/>；否则返回中英双行的错误文本。</returns>
    public static string? ValidateValue(string key, string value)
    {
        if (!TryResolveKey(key, out var canonical))
        {
            return Bi($"未知配置键：{key}", $"Unknown config key: {key}");
        }

        var v = NormalizeValue(value);
        return canonical switch
        {
            "BotToken" => v.Length is 0 or > 200
                ? Bi("配置项 BotToken 为空或长度非法", "BotToken is empty or has an invalid length")
                : null,
            "LocalApiBaseUrl" => ValidateBaseUrl(v),
            "AllowedUserIds" or "TargetChannelIds" => ValidateIdList(canonical, v),
            "DownloadTempDir" => ValidateRequiredString(canonical, v),
            "YtDlpPath" or "FfmpegPath" or "CookieStoreDir" or "LogFile" or "StateDir"
                or "YtDlpProxy" or "YtDlpExtraArgs" => ValidateOptionalString(canonical, v, 512),
            "YtDlpYoutubePlayerClients" => ValidateOptionalString(canonical, v, 200),
            "TgdlDefaultMode" => v.ToLowerInvariant() is "video" or "audio"
                ? null
                : Bi($"配置项 {canonical} 必须是 video/audio，实际值为“{v}”",
                    $"{canonical} must be video/audio, got \"{v}\""),
            "TgdlLanguage" => v.ToLowerInvariant() is "auto" or "en" or "zh"
                ? null
                : Bi($"配置项 {canonical} 必须是 auto/en/zh，实际值为“{v}”",
                    $"{canonical} must be auto/en/zh, got \"{v}\""),
            "MaxConcurrentDownloads" => ValidateIntRange(canonical, v, 1, 16),
            "DownloadRetries" => ValidateIntRange(canonical, v, 0, 10),
            "UploadRetries" => ValidateIntRange(canonical, v, 0, 10),
            "DownloadTimeoutSeconds" => ValidateIntRange(canonical, v, 60, 604800),
            "MaxMediaSizeBytes" => ValidateLongRange(canonical, v, 1024L * 1024, 2_000_000_000L),
            "LogLevel" => Enum.TryParse<LogLevel>(v, ignoreCase: true, out _)
                ? null
                : Bi($"配置项 {canonical} 必须是 Trace/Debug/Info/Warn/Error 之一，实际值为“{v}”",
                    $"{canonical} must be one of Trace/Debug/Info/Warn/Error, got \"{v}\""),
            "MergeFormat" => v.Length is 0 or > 30 || !v.All(ch => char.IsLetterOrDigit(ch) || ch == '/')
                ? Bi("配置项 MergeFormat 非法（应为 mp4/mkv 等格式列表）",
                    "MergeFormat is invalid (use a container list like mp4/mkv)")
                : null,
            "ExtractAudio" or "AlsoSendMediaToRequester" or "AllowPrivateUrls" or "AllowPlaylists"
                or "UpdateYtDlp" or "UpdateFfmpeg" => IsBool(v)
                ? null
                : Bi($"配置项 {canonical} 必须是 true/false，实际值为“{v}”",
                    $"{canonical} must be true/false, got \"{v}\""),
            _ => null,
        };
    }

    /// <summary>
    /// 取配置生效值的可读字符串（/config list 展示用；空值返回空串，由调用方决定占位显示）。
    /// <para>敏感键脱敏：<c>LocalApiBaseUrl</c> 的 userinfo 与查询串凭据、<c>YtDlpProxy</c> 的 userinfo
    /// 均以 <c>***</c> 遮蔽，避免回显泄露（对齐 AppHost.MaskToken 先例）。</para>
    /// </summary>
    /// <param name="config">生效配置（已应用 overlay）。</param>
    /// <param name="canonicalKey">规范键名。</param>
    /// <returns>值的字符串形式（敏感部分已脱敏）。</returns>
    public static string DisplayValue(AppConfig config, string canonicalKey) => canonicalKey switch
    {
        "LocalApiBaseUrl" => MaskUserInfo(MaskQueryCredential(config.LocalApiBaseUrl)),
        "DownloadTempDir" => config.DownloadTempDir,
        "YtDlpPath" => config.YtDlpPath,
        "FfmpegPath" => config.FfmpegPath,
        "CookieStoreDir" => config.CookieStoreDir,
        "YtDlpProxy" => MaskUserInfo(config.YtDlpProxy),
        "YtDlpExtraArgs" => config.YtDlpExtraArgs,
        "YtDlpYoutubePlayerClients" => config.YtDlpYoutubePlayerClients,
        "TgdlDefaultMode" => config.TgdlDefaultMode,
        "TgdlLanguage" => config.TgdlLanguage,
        "StateDir" => config.StateDir,
        "LogFile" => config.LogFile ?? string.Empty,
        "MaxConcurrentDownloads" => config.MaxConcurrentDownloads.ToString(CultureInfo.InvariantCulture),
        "DownloadRetries" => config.DownloadRetries.ToString(CultureInfo.InvariantCulture),
        "UploadRetries" => config.UploadRetries.ToString(CultureInfo.InvariantCulture),
        "DownloadTimeoutSeconds" => config.DownloadTimeoutSeconds.ToString(CultureInfo.InvariantCulture),
        "MaxMediaSizeBytes" => config.MaxMediaSizeBytes.ToString(CultureInfo.InvariantCulture),
        "LogLevel" => config.LogLevel.ToString(),
        "MergeFormat" => config.MergeFormat,
        "ExtractAudio" => config.ExtractAudio ? "true" : "false",
        "AlsoSendMediaToRequester" => config.AlsoSendMediaToRequester ? "true" : "false",
        "AllowPrivateUrls" => config.AllowPrivateUrls ? "true" : "false",
        "AllowPlaylists" => config.AllowPlaylists ? "true" : "false",
        "UpdateYtDlp" => config.UpdateYtDlp ? "true" : "false",
        "UpdateFfmpeg" => config.UpdateFfmpeg ? "true" : "false",
        _ => string.Empty,
    };

    /// <summary>
    /// 遮蔽 URL userinfo 凭据（如 <c>http://user:pass@host</c> → <c>http://***@host</c>）。
    /// 无凭据或非 URL 时原样返回。
    /// </summary>
    /// <param name="value">原始 URL。</param>
    /// <returns>脱敏后的 URL。</returns>
    private static string MaskUserInfo(string value)
    {
        if (value.Length == 0 || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.UserInfo.Length == 0)
        {
            return value;
        }

        var schemeEnd = value.IndexOf("://", StringComparison.Ordinal);
        var at = value.IndexOf('@', schemeEnd >= 0 ? schemeEnd : 0);
        return at >= 0 ? value[..(schemeEnd + 3)] + "***@" + value[(at + 1)..] : value;
    }

    /// <summary>
    /// 遮蔽 URL 查询串凭据（如 <c>http://host?api_id=1&amp;api_hash=x</c> → <c>http://host?***</c>）。
    /// 无查询串时原样返回。
    /// </summary>
    /// <param name="value">原始 URL（LocalApiBaseUrl 已归一化）。</param>
    /// <returns>脱敏后的 URL。</returns>
    private static string MaskQueryCredential(string value)
    {
        if (value.Length == 0 || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Query.Length == 0)
        {
            return value;
        }

        var q = value.IndexOf('?');
        return q >= 0 ? value[..q] + "?***" : value;
    }

    /// <summary>
    /// 解析配置文件文本。
    /// </summary>
    /// <param name="content">配置文件全部内容。</param>
    /// <param name="sourcePath">配置文件路径，用于错误提示。</param>
    /// <returns>解析结果。</returns>
    /// <exception cref="ConfigParseException">配置缺失、格式错误或值非法时抛出（中英双行）。</exception>
    public static ConfigParseResult Parse(string content, string sourcePath)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var warnings = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i].TrimStart('\uFEFF').Trim();
            if (raw.Length == 0 || raw[0] == '#' || raw[0] == ';')
            {
                continue;
            }

            if (raw[0] == '[' && raw[^1] == ']')
            {
                continue;
            }

            var eq = raw.IndexOf('=');
            if (eq <= 0)
            {
                throw new ConfigParseException(Bi(
                    $"配置文件格式错误（第 {i + 1} 行）：缺少“=”，应为“键 = 值”格式。",
                    $"Config format error (line {i + 1}): missing \"=\", expected \"key = value\"."));
            }

            var key = raw[..eq].Trim();
            if (!KeyRegex.IsMatch(key))
            {
                throw new ConfigParseException(Bi(
                    $"配置文件格式错误（第 {i + 1} 行）：配置键“{key}”不合法，只能包含字母、数字与下划线。",
                    $"Config format error (line {i + 1}): key \"{key}\" is invalid, only letters, digits and underscores are allowed."));
            }

            var value = StripQuotes(raw[(eq + 1)..].Trim());

            if (!Alias.TryGetValue(key, out var canonical))
            {
                warnings.Add($"未知配置项“{key}”（第 {i + 1} 行），已忽略。");
                continue;
            }

            if (!seen.Add(canonical))
            {
                throw new ConfigParseException(Bi(
                    $"配置文件格式错误（第 {i + 1} 行）：配置项“{canonical}”重复出现。",
                    $"Config format error (line {i + 1}): key \"{canonical}\" appears more than once."));
            }

            values[canonical] = value;
        }

        var missing = RequiredKeys.Where(k => !values.ContainsKey(k)).ToList();
        if (missing.Count > 0)
        {
            throw new ConfigParseException(Bi(
                $"配置缺失必需项：{string.Join("、", missing)}。请检查 {sourcePath}。",
                $"Missing required keys: {string.Join(", ", missing)}. Check {sourcePath}."));
        }

        // 单点校验：与 /config set 完全一致（/config 已保证合法，此处防御 config.conf 手工改动）。
        foreach (var (key, value) in values)
        {
            var error = ValidateValue(key, value);
            if (error is not null)
            {
                throw new ConfigParseException(error);
            }
        }

        var config = BuildConfig(values, sourcePath);
        return new ConfigParseResult(config, warnings, values);
    }

    /// <summary>
    /// 由已校验的键值构建配置对象（overlay 装配与启动解析共用）。
    /// </summary>
    /// <param name="values">规范键 → 已校验原始值。</param>
    /// <param name="sourcePath">配置来源路径。</param>
    /// <returns>配置对象。</returns>
    internal static AppConfig BuildConfig(IReadOnlyDictionary<string, string> values, string sourcePath)
    {
        var tgdlLanguage = values.TryGetValue("TgdlLanguage", out var lang) && lang.Length > 0
            ? lang.ToLowerInvariant()
            : "auto";
        var tgdlDefaultMode = values.TryGetValue("TgdlDefaultMode", out var mode) && mode.Length > 0
            ? mode.ToLowerInvariant()
            : "video";
        var playerClients = values.TryGetValue("YtDlpYoutubePlayerClients", out var pc) && pc.Length > 0
            ? pc
            : "android,ios,web_embedded,tv";

        return new AppConfig
        {
            SourcePath = sourcePath,
            BotToken = values["BotToken"],
            LocalApiBaseUrl = NormalizeBaseUrlValue(values["LocalApiBaseUrl"]),
            TargetChannelIds = ParseIdList(values["TargetChannelIds"]),
            AllowedUserIds = ParseIdList(values["AllowedUserIds"]),
            DownloadTempDir = values["DownloadTempDir"],
            YtDlpPath = GetOptional(values, "YtDlpPath"),
            FfmpegPath = GetOptional(values, "FfmpegPath"),
            CookieStoreDir = GetOptional(values, "CookieStoreDir"),
            YtDlpProxy = GetOptional(values, "YtDlpProxy"),
            YtDlpExtraArgs = GetOptional(values, "YtDlpExtraArgs"),
            YtDlpYoutubePlayerClients = playerClients,
            TgdlDefaultMode = tgdlDefaultMode,
            TgdlLanguage = tgdlLanguage,
            StateDir = GetOptional(values, "StateDir"),
            MaxConcurrentDownloads = GetInt(values, "MaxConcurrentDownloads", 2),
            LogLevel = GetLogLevel(values),
            LogFile = GetOptional(values, "LogFile") is { Length: > 0 } logFile ? logFile : null,
            DownloadRetries = GetInt(values, "DownloadRetries", 3),
            DownloadTimeoutSeconds = GetInt(values, "DownloadTimeoutSeconds", 3600),
            UploadRetries = GetInt(values, "UploadRetries", 2),
            ExtractAudio = GetBool(values, "ExtractAudio") ?? false,
            AlsoSendMediaToRequester = GetBool(values, "AlsoSendMediaToRequester") ?? false,
            AllowPrivateUrls = GetBool(values, "AllowPrivateUrls") ?? false,
            MaxMediaSizeBytes = GetLong(values, "MaxMediaSizeBytes", 1_900_000_000L),
            AllowPlaylists = GetBool(values, "AllowPlaylists") ?? false,
            MergeFormat = GetOptional(values, "MergeFormat") is { Length: > 0 } mergeFormat ? mergeFormat : "mp4/mkv",
            UpdateYtDlp = GetBool(values, "UpdateYtDlp") ?? true,
            UpdateFfmpeg = GetBool(values, "UpdateFfmpeg") ?? true,
        };
    }

    /// <summary>
    /// 规范化 LocalApiBaseUrl（去除末尾斜杠；调用前必须已通过 <see cref="ValidateValue"/>）。
    /// </summary>
    /// <param name="value">原始值。</param>
    /// <returns>规范化后的地址。</returns>
    internal static string NormalizeBaseUrlValue(string value)
    {
        var uri = new Uri(value, UriKind.Absolute);
        return uri.ToString().TrimEnd('/');
    }

    /// <summary>
    /// 解析布尔值（true/yes/on/1；调用前必须已通过 <see cref="ValidateValue"/>）。
    /// </summary>
    /// <param name="value">原始值。</param>
    /// <returns>布尔结果。</returns>
    internal static bool ParseBoolValue(string value) => value.ToLowerInvariant() switch
    {
        "true" or "yes" or "on" or "1" => true,
        _ => false,
    };

    private static string? ValidateBaseUrl(string v)
    {
        if (v.Length == 0)
        {
            return Bi("配置项 LocalApiBaseUrl 不能为空", "LocalApiBaseUrl cannot be empty");
        }

        if (v.Length > 512)
        {
            return Bi("配置项 LocalApiBaseUrl 长度超过限制", "LocalApiBaseUrl exceeds the length limit");
        }

        return Uri.TryCreate(v, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? null
            : Bi("配置项 LocalApiBaseUrl 必须是 http:// 或 https:// 开头的地址",
                "LocalApiBaseUrl must start with http:// or https://");
    }

    private static string? ValidateRequiredString(string key, string v)
        => v.Length == 0
            ? Bi($"配置项 {key} 不能为空", $"{key} cannot be empty")
            : ValidateOptionalString(key, v, 512);

    private static string? ValidateOptionalString(string key, string v, int maxLength)
    {
        if (v.Length == 0)
        {
            return null;
        }

        return v.Length > maxLength
            ? Bi($"配置项 {key} 长度超过限制（≤{maxLength}）", $"{key} exceeds the length limit ({maxLength})")
            : null;
    }

    private static string? ValidateIdList(string key, string v)
    {
        if (v.Length == 0)
        {
            return Bi($"配置项 {key} 至少需要一个 ID，用英文逗号分隔",
                $"{key} needs at least one ID, comma-separated");
        }

        var parts = v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var ids = new List<long>(parts.Length);
        foreach (var part in parts)
        {
            if (!long.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                return Bi($"配置项 {key} 中“{part}”不是合法的整数 ID",
                    $"{key}: \"{part}\" is not a valid integer ID");
            }

            ids.Add(id);
        }

        var dup = ids.GroupBy(x => x).FirstOrDefault(g => g.Count() > 1);
        if (dup is not null)
        {
            return Bi($"配置项 {key} 中存在重复 ID：{dup.Key}",
                $"{key} contains duplicate ID: {dup.Key}");
        }

        return null;
    }

    private static string? ValidateIntRange(string key, string v, int min, int max)
    {
        if (!int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
        {
            return Bi($"配置项 {key} 必须是整数，实际值为“{v}”", $"{key} must be an integer, got \"{v}\"");
        }

        return n < min || n > max
            ? Bi($"配置项 {key} 必须在 {min} 到 {max} 之间", $"{key} must be between {min} and {max}")
            : null;
    }

    private static string? ValidateLongRange(string key, string v, long min, long max)
    {
        if (!long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
        {
            return Bi($"配置项 {key} 必须是整数，实际值为“{v}”", $"{key} must be an integer, got \"{v}\"");
        }

        return n < min || n > max
            ? Bi($"配置项 {key} 必须在 {min} 到 {max} 之间", $"{key} must be between {min} and {max}")
            : null;
    }

    private static bool IsBool(string v)
        => v.ToLowerInvariant() is "true" or "yes" or "on" or "1" or "false" or "no" or "off" or "0";

    private static string GetOptional(IReadOnlyDictionary<string, string> v, string key)
        => v.TryGetValue(key, out var val) ? val : string.Empty;

    private static int GetInt(IReadOnlyDictionary<string, string> v, string key, int def)
        => v.TryGetValue(key, out var val) && val.Length > 0 &&
           int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : def;

    private static long GetLong(IReadOnlyDictionary<string, string> v, string key, long def)
        => v.TryGetValue(key, out var val) && val.Length > 0 &&
           long.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : def;

    private static bool? GetBool(IReadOnlyDictionary<string, string> v, string key)
        => v.TryGetValue(key, out var val) && val.Length > 0 && IsBool(val)
            ? ParseBoolValue(val)
            : null;

    private static LogLevel GetLogLevel(IReadOnlyDictionary<string, string> v)
        => v.TryGetValue("LogLevel", out var val) && val.Length > 0 &&
           Enum.TryParse<LogLevel>(val, ignoreCase: true, out var level)
            ? level
            : LogLevel.Info;

    private static IReadOnlyList<long> ParseIdList(string v)
        => v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => long.Parse(p, NumberStyles.Integer, CultureInfo.InvariantCulture))
            .ToArray();

    private static string StripQuotes(string value)
    {
        if (value.Length >= 2 &&
            ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }

        return value;
    }

    private static string Bi(string zh, string en) => $"配置错误：{zh}。\nConfig error: {en}.";
}
