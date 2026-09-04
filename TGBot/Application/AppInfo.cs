// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

using System.Reflection;

namespace TGBot.Application;

/// <summary>
/// 应用自身版本信息的稳定读取入口（来源：<c>AssemblyInformationalVersion</c>，自动跟随 csproj
/// <c>Version</c> 属性）。
/// <para>发布版本由 CI 从 git tag（<c>vX.Y.Z</c>）剥离 <c>v</c> 后经
/// <c>dotnet publish -p:Version=X.Y.Z</c> 注入；本地构建使用 csproj 默认值 2.4.0。</para>
/// </summary>
public static class AppInfo
{
    /// <summary>版本信息缺失或无法解析时的兜底值（语义为"未设置版本号"）。</summary>
    public const string FallbackVersion = "0.0.0";

    /// <summary>
    /// 规范化后的应用版本（如 <c>2.4.0</c>）。
    /// <para>截断 <c>+commit</c> 后缀（SourceLink/确定性构建注入的 commit 信息）与可选 <c>v</c>
    /// 前缀，仅保留主版本；异常输入回退 <see cref="FallbackVersion"/>，绝不抛异常。</para>
    /// </summary>
    public static string Version { get; } = Normalize(ReadInformationalVersion());

    /// <summary>
    /// 将 <c>AssemblyInformationalVersion</c> 原始值规范化为展示版本号。
    /// </summary>
    /// <param name="informationalVersion">原始信息性版本（可为 <see langword="null"/>）。</param>
    /// <returns>截断 <c>+</c> 后缀、剥离 <c>v</c> 前缀并去除首尾空白后的版本；
    /// 空值/全后缀等异常输入返回 <see cref="FallbackVersion"/>。</returns>
    public static string Normalize(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return FallbackVersion;
        }

        var plus = informationalVersion.IndexOf('+');
        var version = (plus >= 0 ? informationalVersion[..plus] : informationalVersion).Trim();
        if (version.Length > 1 && (version[0] == 'v' || version[0] == 'V') && char.IsAsciiDigit(version[1]))
        {
            version = version[1..];
        }

        // 无任何数字内容的版本（空串、裸 v/V 前缀等）视为无效 → 兜底
        return version.Any(char.IsAsciiDigit) ? version : FallbackVersion;
    }

    private static string? ReadInformationalVersion()
        => typeof(AppInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
}