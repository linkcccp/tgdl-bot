// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

using System.Globalization;
using TGBot.Logging;

namespace TGBot.Config.Overlay;

/// <summary>
/// 将 config-overlay.json 显式逐键覆盖到 <see cref="AppConfig"/>（with 语义，禁用反射，编译期安全）。
/// <para>仅覆盖 <see cref="ConfigParser.MutableKeys"/> 白名单键；BotToken、安装白名单两键与 StateDir 拒绝
/// （StateDir 为安装锁键：状态目录不应由 bot 远程改动，overlay 自身始终以 config.conf 推导的目录读取）；
/// 非法值/未知键跳过并产生警告（overlay 由 /config 经 <see cref="ConfigParser.ValidateValue"/> 写入，
/// 手工改动时防御）。</para>
/// </summary>
public static class OverlayApplier
{
    /// <summary>
    /// 应用配置覆盖，返回新配置实例（未覆盖项与 <paramref name="baseConfig"/> 相同）。
    /// </summary>
    /// <param name="baseConfig">基础配置（config.conf 解析结果）。</param>
    /// <param name="overlay">覆盖条目（键 → 字符串值，键可为别名）。</param>
    /// <param name="warnings">被跳过的条目说明（未知键 / 锁键 / 非法值）。</param>
    /// <returns>覆盖后的新配置。</returns>
    public static AppConfig Apply(AppConfig baseConfig, IReadOnlyDictionary<string, string> overlay, out IReadOnlyList<string> warnings)
    {
        var result = baseConfig;
        var skipped = new List<string>();
        foreach (var (key, value) in overlay)
        {
            if (!ConfigParser.TryResolveKey(key, out var canonical))
            {
                skipped.Add($"跳过未知配置键 {key}（来自 config-overlay.json）");
                continue;
            }

            if (!ConfigParser.MutableKeys.Contains(canonical))
            {
                skipped.Add($"跳过安装配置键 {canonical}（不允许经 overlay 修改）");
                continue;
            }

            var error = ConfigParser.ValidateValue(canonical, value);
            if (error is not null)
            {
                skipped.Add($"跳过非法覆盖 {canonical}：{error}");
                continue;
            }

            // 与 config.conf 解析一致：落盘/应用前归一化（去引号+去首尾空白），防止含引号值错乱。
            result = ApplyOne(result, canonical, ConfigParser.NormalizeValue(value));
        }

        warnings = skipped;
        return result;
    }

    /// <summary>
    /// 单键覆盖（调用前必须已通过 <see cref="ConfigParser.ValidateValue"/> 且值已经
    /// <see cref="ConfigParser.NormalizeValue"/> 归一化，保证格式可解析）。
    /// </summary>
    private static AppConfig ApplyOne(AppConfig c, string canonical, string value) => canonical switch
    {
        "LocalApiBaseUrl" => c with { LocalApiBaseUrl = ConfigParser.NormalizeBaseUrlValue(value) },
        "DownloadTempDir" => c with { DownloadTempDir = value },
        "YtDlpPath" => c with { YtDlpPath = value },
        "FfmpegPath" => c with { FfmpegPath = value },
        "CookieStoreDir" => c with { CookieStoreDir = value },
        "YtDlpProxy" => c with { YtDlpProxy = value },
        "YtDlpExtraArgs" => c with { YtDlpExtraArgs = value },
        "YtDlpYoutubePlayerClients" => c with { YtDlpYoutubePlayerClients = value },
        "TgdlDefaultMode" => c with { TgdlDefaultMode = value.ToLowerInvariant() },
        "TgdlLanguage" => c with { TgdlLanguage = value.ToLowerInvariant() },
        "StateDir" => c with { StateDir = value },
        "LogFile" => c with { LogFile = string.IsNullOrEmpty(value) ? null : value },
        "MaxConcurrentDownloads" => c with { MaxConcurrentDownloads = int.Parse(value, CultureInfo.InvariantCulture) },
        "DownloadRetries" => c with { DownloadRetries = int.Parse(value, CultureInfo.InvariantCulture) },
        "UploadRetries" => c with { UploadRetries = int.Parse(value, CultureInfo.InvariantCulture) },
        "DownloadTimeoutSeconds" => c with { DownloadTimeoutSeconds = int.Parse(value, CultureInfo.InvariantCulture) },
        "MaxMediaSizeBytes" => c with { MaxMediaSizeBytes = long.Parse(value, CultureInfo.InvariantCulture) },
        "LogLevel" => c with { LogLevel = Enum.Parse<LogLevel>(value, ignoreCase: true) },
        "MergeFormat" => c with { MergeFormat = value },
        "ExtractAudio" => c with { ExtractAudio = ConfigParser.ParseBoolValue(value) },
        "AlsoSendMediaToRequester" => c with { AlsoSendMediaToRequester = ConfigParser.ParseBoolValue(value) },
        "AllowPrivateUrls" => c with { AllowPrivateUrls = ConfigParser.ParseBoolValue(value) },
        "AllowPlaylists" => c with { AllowPlaylists = ConfigParser.ParseBoolValue(value) },
        "UpdateYtDlp" => c with { UpdateYtDlp = ConfigParser.ParseBoolValue(value) },
        "UpdateFfmpeg" => c with { UpdateFfmpeg = ConfigParser.ParseBoolValue(value) },
        _ => c,
    };
}
