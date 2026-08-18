// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

using System.Text.RegularExpressions;

namespace TGBot.Update;

/// <summary>
/// 从远端响应中解析最新版本号（纯函数，便于单元测试）。
/// </summary>
public static class UriVersionParser
{
    /// <summary>
    /// 从 GitHub releases 的 Location 重定向头解析版本，如
    /// <c>https://github.com/yt-dlp/yt-dlp/releases/download/2025.01.26/yt-dlp</c>。
    /// </summary>
    /// <param name="location">Location 头值。</param>
    /// <returns>版本号；解析失败返回 <see langword="null"/>。</returns>
    public static ToolVersion? ParseGitHubRedirectLocation(string? location)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            return null;
        }

        var match = Regex.Match(location, @"releases/download/([^/]+)/", RegexOptions.IgnoreCase);
        return match.Success && ToolVersion.TryParse(match.Groups[1].Value, out var v) ? v : null;
    }

    /// <summary>
    /// 从 GitHub API 的 release 响应 JSON 中解析 <c>published_at</c> 字段，
    /// 如 <c>"published_at": "2026-08-17T13:29:26Z"</c> → <c>2026.08.17.13.29.26</c>。
    /// <para>用于 BtbN/FFmpeg-Builds 的滚动 <c>latest</c> release：其 <c>tag_name</c> 恒为
    /// <c>latest</c> 不含日期，版本标识只能取 ISO 8601 UTC 的 <c>published_at</c>（单调递增）。</para>
    /// </summary>
    /// <param name="json">GitHub API 响应 JSON。</param>
    /// <returns>归一化后的 autobuild 版本；解析失败返回 <see langword="null"/>。</returns>
    public static ToolVersion? ParseGitHubApiPublishedAt(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        var match = Regex.Match(json, @"""published_at""\s*:\s*""(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(?:Z)?""");
        if (!match.Success)
        {
            return null;
        }

        var normalized = match.Groups[1].Value.Replace('-', '.').Replace('T', '.').Replace(':', '.');
        return ToolVersion.TryParse(normalized, out var v) ? v : null;
    }
}
