using System.Globalization;
using System.Text.RegularExpressions;
using TGBot.Logging;

namespace TGBot.Config;

/// <summary>
/// 解析结果，包含配置对象与解析过程中产生的警告。
/// </summary>
/// <param name="Config">解析得到的配置。</param>
/// <param name="Warnings">解析警告（如未知配置项）。</param>
public sealed record ConfigParseResult(AppConfig Config, IReadOnlyList<string> Warnings);

/// <summary>
/// config.conf 解析器。
/// <para>格式：每行 <c>键 = 值</c>，支持 <c>#</c>/<c>;</c> 注释、空行与 <c>[section]</c> 段头。
/// 解析失败抛出 <see cref="ConfigParseException"/>，消息为清晰的中文提示（含行号）。</para>
/// </summary>
public static class ConfigParser
{
    private static readonly Regex KeyRegex = new("^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.Compiled);

    private static readonly Dictionary<string, string> Alias = new(StringComparer.Ordinal)
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
    /// 解析配置文件文本。
    /// </summary>
    /// <param name="content">配置文件全部内容。</param>
    /// <param name="sourcePath">配置文件路径，用于错误提示。</param>
    /// <returns>解析结果。</returns>
    /// <exception cref="ConfigParseException">配置缺失、格式错误或值非法时抛出。</exception>
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
                throw new ConfigParseException(
                    $"配置文件格式错误（第 {i + 1} 行）：缺少“=”，应为“键 = 值”格式。");
            }

            var key = raw[..eq].Trim();
            if (!KeyRegex.IsMatch(key))
            {
                throw new ConfigParseException(
                    $"配置文件格式错误（第 {i + 1} 行）：配置键“{key}”不合法，只能包含字母、数字与下划线。");
            }

            var value = raw[(eq + 1)..].Trim();
            value = StripQuotes(value);

            if (!Alias.TryGetValue(key, out var canonical))
            {
                warnings.Add($"未知配置项“{key}”（第 {i + 1} 行），已忽略。");
                continue;
            }

            if (!seen.Add(canonical))
            {
                throw new ConfigParseException(
                    $"配置文件格式错误（第 {i + 1} 行）：配置项“{canonical}”重复出现。");
            }

            values[canonical] = value;
        }

        var missing = RequiredKeys.Where(k => !values.ContainsKey(k)).ToList();
        if (missing.Count > 0)
        {
            throw new ConfigParseException(
                $"配置缺失必需项：{string.Join("、", missing)}。请检查 {sourcePath}。");
        }

        var config = new AppConfig
        {
            SourcePath = sourcePath,
            BotToken = values["BotToken"],
            LocalApiBaseUrl = NormalizeBaseUrl(GetString(values, "LocalApiBaseUrl"), "LocalApiBaseUrl"),
            TargetChannelIds = GetLongList(values, "TargetChannelIds"),
            AllowedUserIds = GetLongList(values, "AllowedUserIds"),
            DownloadTempDir = GetString(values, "DownloadTempDir"),
            YtDlpPath = GetString(values, "YtDlpPath"),
            FfmpegPath = GetString(values, "FfmpegPath"),
            CookieStoreDir = GetString(values, "CookieStoreDir"),
            YtDlpProxy = GetString(values, "YtDlpProxy"),
            YtDlpExtraArgs = GetString(values, "YtDlpExtraArgs"),
            YtDlpYoutubePlayerClients = values.TryGetValue("YtDlpYoutubePlayerClients", out var pc)
                ? pc
                : "android,ios,web_embedded,tv",
            MaxConcurrentDownloads = GetInt(values, "MaxConcurrentDownloads", 2),
            LogLevel = GetLogLevel(values, "LogLevel", Logging.LogLevel.Info),
            LogFile = string.IsNullOrEmpty(GetString(values, "LogFile")) ? null : GetString(values, "LogFile"),
            DownloadRetries = GetInt(values, "DownloadRetries", 3),
            DownloadTimeoutSeconds = GetInt(values, "DownloadTimeoutSeconds", 3600),
            UploadRetries = GetInt(values, "UploadRetries", 2),
            ExtractAudio = GetBool(values, "ExtractAudio", false),
            AlsoSendMediaToRequester = GetBool(values, "AlsoSendMediaToRequester", false),
            AllowPrivateUrls = GetBool(values, "AllowPrivateUrls", false),
            MaxMediaSizeBytes = GetLong(values, "MaxMediaSizeBytes", 1_900_000_000L),
            AllowPlaylists = GetBool(values, "AllowPlaylists", false),
            MergeFormat = GetString(values, "MergeFormat", "mp4/mkv"),
            UpdateYtDlp = GetBool(values, "UpdateYtDlp", true),
            UpdateFfmpeg = GetBool(values, "UpdateFfmpeg", true),
        };

        Validate(config);
        return new ConfigParseResult(config, warnings);
    }

    private static string NormalizeBaseUrl(string value, string key)
    {
        if (value.Length == 0)
        {
            throw new ConfigParseException($"配置项 {key} 不能为空。");
        }

        if (value.Length > 512)
        {
            throw new ConfigParseException($"配置项 {key} 长度超过限制。");
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return uri.ToString().TrimEnd('/');
        }

        throw new ConfigParseException($"配置项 {key} 必须是 http:// 或 https:// 开头的地址。");
    }

    private static void Validate(AppConfig c)
    {
        if (c.BotToken.Length == 0 || c.BotToken.Length > 200)
        {
            throw new ConfigParseException("配置项 BotToken 为空或长度非法。");
        }

        if (c.TargetChannelIds.Count == 0)
        {
            throw new ConfigParseException("配置项 TargetChannelIds 必须至少包含一个频道/群组 ID。");
        }

        if (c.AllowedUserIds.Count == 0)
        {
            throw new ConfigParseException("配置项 AllowedUserIds 必须至少包含一个用户 ID。");
        }

        if (c.MaxConcurrentDownloads < 1 || c.MaxConcurrentDownloads > 16)
        {
            throw new ConfigParseException("配置项 MaxConcurrentDownloads 必须在 1 到 16 之间。");
        }

        if (c.DownloadRetries < 0 || c.DownloadRetries > 10)
        {
            throw new ConfigParseException("配置项 DownloadRetries 必须在 0 到 10 之间。");
        }

        if (c.UploadRetries < 0 || c.UploadRetries > 10)
        {
            throw new ConfigParseException("配置项 UploadRetries 必须在 0 到 10 之间。");
        }

        if (c.DownloadTimeoutSeconds < 60 || c.DownloadTimeoutSeconds > 86_400 * 7)
        {
            throw new ConfigParseException("配置项 DownloadTimeoutSeconds 必须在 60 到 604800 之间。");
        }

        if (c.MaxMediaSizeBytes < 1024L * 1024L || c.MaxMediaSizeBytes > 2_000_000_000L)
        {
            throw new ConfigParseException("配置项 MaxMediaSizeBytes 必须在 1MB 到 2GB 之间。");
        }

        if (c.MergeFormat.Length == 0 || c.MergeFormat.Length > 30 ||
            !c.MergeFormat.All(ch => char.IsLetterOrDigit(ch) || ch == '/'))
        {
            throw new ConfigParseException("配置项 MergeFormat 非法（应为 mp4/mkv 等格式列表）。");
        }

        if (c.YtDlpExtraArgs.Length > 512)
        {
            throw new ConfigParseException("配置项 YtDlpExtraArgs 长度超过限制。");
        }

        if (c.YtDlpYoutubePlayerClients.Length > 200)
        {
            throw new ConfigParseException("配置项 YtDlpYoutubePlayerClients 长度超过限制。");
        }

        if (c.YtDlpProxy.Length > 512)
        {
            throw new ConfigParseException("配置项 YtDlpProxy 长度超过限制。");
        }

        if (c.CookieStoreDir.Length > 512)
        {
            throw new ConfigParseException("配置项 CookieStoreDir 长度超过限制。");
        }
    }

    private static string GetString(Dictionary<string, string> v, string key, string? def = null)
        => v.TryGetValue(key, out var val) && val.Length > 0 ? val : (def ?? string.Empty);

    private static long GetLong(Dictionary<string, string> v, string key)
    {
        if (!v.TryGetValue(key, out var val) ||
            !long.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
        {
            throw new ConfigParseException($"配置项 {key} 必须是整数，实际值为“{val ?? "(缺失)"}”。");
        }

        return result;
    }

    private static long GetLong(Dictionary<string, string> v, string key, long def)
    {
        if (!v.TryGetValue(key, out var val) || val.Length == 0)
        {
            return def;
        }

        if (!long.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
        {
            throw new ConfigParseException($"配置项 {key} 必须是整数，实际值为“{val}”。");
        }

        return result;
    }

    private static int GetInt(Dictionary<string, string> v, string key, int def)
    {
        if (!v.TryGetValue(key, out var val) || val.Length == 0)
        {
            return def;
        }

        if (!int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
        {
            throw new ConfigParseException($"配置项 {key} 必须是整数，实际值为“{val}”。");
        }

        return result;
    }

    private static IReadOnlyList<long> GetLongList(Dictionary<string, string> v, string key)
    {
        if (!v.TryGetValue(key, out var val))
        {
            throw new ConfigParseException($"配置项 {key} 缺失。");
        }

        var parts = val.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            throw new ConfigParseException($"配置项 {key} 至少需要一个 ID，用英文逗号分隔。");
        }

        var list = new List<long>(parts.Length);
        foreach (var part in parts)
        {
            if (!long.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                throw new ConfigParseException($"配置项 {key} 中“{part}”不是合法的整数 ID。");
            }

            list.Add(id);
        }

        var dup = list.GroupBy(x => x).FirstOrDefault(g => g.Count() > 1);
        if (dup is not null)
        {
            throw new ConfigParseException($"配置项 {key} 中存在重复 ID：{dup.Key}。");
        }

        return list;
    }

    private static bool GetBool(Dictionary<string, string> v, string key, bool def)
    {
        if (!v.TryGetValue(key, out var val) || val.Length == 0)
        {
            return def;
        }

        switch (val.ToLowerInvariant())
        {
            case "true":
            case "yes":
            case "on":
            case "1":
                return true;
            case "false":
            case "no":
            case "off":
            case "0":
                return false;
            default:
                throw new ConfigParseException($"配置项 {key} 必须是 true/false，实际值为“{val}”。");
        }
    }

    private static LogLevel GetLogLevel(Dictionary<string, string> v, string key, LogLevel def)
    {
        if (!v.TryGetValue(key, out var val) || val.Length == 0)
        {
            return def;
        }

        if (Enum.TryParse<LogLevel>(val, ignoreCase: true, out var level))
        {
            return level;
        }

        throw new ConfigParseException(
            $"配置项 {key} 必须是 Trace/Debug/Info/Warn/Error 之一，实际值为“{val}”。");
    }

    private static string StripQuotes(string value)
    {
        if (value.Length >= 2 &&
            ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }

        return value;
    }
}
