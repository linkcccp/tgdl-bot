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
    /// 从 johnvansickle.com 首页 HTML 解析 release 版本，如 <c>release: 7.0.2</c>。
    /// </summary>
    /// <param name="page">首页 HTML 内容。</param>
    /// <returns>版本号；解析失败返回 <see langword="null"/>。</returns>
    public static ToolVersion? ParseJohnVanSickleReleasePage(string? page)
    {
        if (string.IsNullOrWhiteSpace(page))
        {
            return null;
        }

        var match = Regex.Match(page, @"release\s*:\s*(\d+(?:\.\d+)+)", RegexOptions.IgnoreCase);
        return match.Success && ToolVersion.TryParse(match.Groups[1].Value, out var v) ? v : null;
    }
}
