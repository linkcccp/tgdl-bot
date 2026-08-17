// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

using System.Globalization;

namespace TGBot.Download;

/// <summary>
/// yt-dlp 标准输出/错误输出解析器（纯函数，便于单元测试）。
/// </summary>
public static class YtDlpOutputParser
{
    /// <summary>
    /// 进度行标记。
    /// </summary>
    public const string ProgressMarker = "DLP ";

    /// <summary>
    /// 元数据行标记。
    /// </summary>
    public const string MetaMarker = "META\u001f";

    /// <summary>
    /// 文件路径行标记。
    /// </summary>
    public const string FileMarker = "FILE\u001f";

    /// <summary>
    /// 解析进度行，如 <c>DLP  42.5%| 1.2MiB/s</c>。
    /// </summary>
    /// <param name="line">输出行。</param>
    /// <returns>进度信息；非进度行返回 <see langword="null"/>。</returns>
    public static DownloadProgress? ParseProgress(string line)
    {
        if (!line.StartsWith(ProgressMarker, StringComparison.Ordinal))
        {
            return null;
        }

        var rest = line[ProgressMarker.Length..].Trim();
        if (rest.Length == 0)
        {
            return null;
        }

        var parts = rest.Split('|');
        var percentText = parts[0].Trim().Replace("%", string.Empty);
        if (percentText is "--.-" or "N/A" or "NA" or "-")
        {
            return null;
        }

        if (!double.TryParse(percentText, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
        {
            return null;
        }

        var speed = parts.Length > 1 ? parts[1].Trim() : null;
        return new DownloadProgress(percent, string.IsNullOrEmpty(speed) ? null : speed);
    }

    /// <summary>
    /// 判断输出行是否表示文件超过大小上限。
    /// </summary>
    /// <param name="line">输出行。</param>
    /// <returns>是超限提示时返回 <see langword="true"/>。</returns>
    public static bool IsTooLargeMessage(string line)
        => line.Contains("max-filesize", StringComparison.OrdinalIgnoreCase) ||
           line.Contains("larger than", StringComparison.OrdinalIgnoreCase) ||
           line.Contains("max filesize", StringComparison.OrdinalIgnoreCase);

    private static readonly string[] AuthRequiredPatterns =
    {
        "sign in to confirm",
        "confirm you're not a bot",
        "use --cookies",
        "sign in to",
        "login required",
        "requires authentication",
        "to access this page",
        "private video",
        "incomplete login",
        "po_token",
        "not a bot",
    };

    /// <summary>
    /// 判断输出是否表示站点要求登录/认证（机器人检测等）。
    /// </summary>
    /// <param name="text">输出文本（含错误）。</param>
    /// <returns>需要认证时返回 <see langword="true"/>。</returns>
    public static bool IsAuthRequiredMessage(string text)
    {
        foreach (var p in AuthRequiredPatterns)
        {
            if (text.Contains(p, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 判断输出是否表示可用格式不足（如 Requested format is not available）。
    /// </summary>
    /// <param name="text">输出文本（含错误）。</param>
    /// <returns>格式不足时返回 <see langword="true"/>。</returns>
    public static bool IsFormatUnavailableMessage(string text)
        => text.Contains("requested format is not available", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 从元数据行提取标题、时长与文件大小。
    /// </summary>
    /// <param name="line">形如 <c>META\x1fid\x1f标题\x1fext\x1fduration\x1fsize_approx\x1fsize</c> 的行。</param>
    /// <returns>解析结果；行格式不正确时返回 <see langword="null"/>。</returns>
    public static MetaLine? ParseMeta(string line)
    {
        if (!line.StartsWith(MetaMarker, StringComparison.Ordinal))
        {
            return null;
        }

        var parts = line.Split('\u001f');
        if (parts.Length < 4)
        {
            return null;
        }

        var title = parts[2];
        int? duration = null;
        if (parts.Length >= 5 &&
            int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var d))
        {
            duration = d;
        }

        long? size = null;
        if (parts.Length >= 7 && long.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out var s))
        {
            size = s;
        }

        if (size is null && parts.Length >= 6 &&
            long.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var approx))
        {
            size = approx;
        }

        return new MetaLine(title, duration, size);
    }

    /// <summary>
    /// 元数据行解析结果。
    /// </summary>
    /// <param name="Title">标题。</param>
    /// <param name="DurationSeconds">时长（秒），未知为 <see langword="null"/>。</param>
    /// <param name="SizeBytes">文件大小（字节），未知为 <see langword="null"/>。</param>
    public sealed record MetaLine(string Title, int? DurationSeconds, long? SizeBytes);
}
