// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

namespace TGBot.Cookie;

/// <summary>
/// 站点抽象：一个站点对应一套 cookies 文件，并声明其匹配的域名。
/// <para>新增站点 = 继承本抽象并注册到 <see cref="SiteCookieRegistry"/>，命令与存储零改动。</para>
/// </summary>
public abstract class CookieSite
{
    /// <summary>
    /// 站点键（小写、仅字母数字与下划线，用作存储文件名）。
    /// </summary>
    public abstract string Key { get; }

    /// <summary>
    /// 站点显示名（用于用户提示）。
    /// </summary>
    public abstract string DisplayName { get; }

    /// <summary>
    /// 该站点匹配的主机名集合（规范化后，不含 <c>www.</c> 前缀）。
    /// </summary>
    protected abstract IReadOnlyList<string> Hosts { get; }

    /// <summary>
    /// 对外暴露的匹配主机名集合（供注册表构建索引）。
    /// </summary>
    public IReadOnlyList<string> MatchedHosts => Hosts;

    /// <summary>
    /// 判断规范化后的主机名是否属于本站点。
    /// </summary>
    /// <param name="normalizedHost">规范化主机名（小写、去 <c>www.</c>）。</param>
    /// <returns>匹配返回 <see langword="true"/>。</returns>
    public bool Matches(string normalizedHost)
        => Hosts.Contains(normalizedHost, StringComparer.Ordinal);
}

/// <summary>
/// 通用站点辅助。
/// </summary>
public static class CookieSiteUtil
{
    /// <summary>
    /// 规范化主机名：转小写并去掉 <c>www.</c> 前缀。
    /// </summary>
    /// <param name="host">原始主机名。</param>
    /// <returns>规范化主机名。</returns>
    public static string NormalizeHost(string host)
    {
        var h = (host ?? string.Empty).Trim().ToLowerInvariant();
        if (h.StartsWith("www.", StringComparison.Ordinal))
        {
            h = h[4..];
        }

        return h;
    }
}
