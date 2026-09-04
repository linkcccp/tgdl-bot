// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

using System.Collections.Concurrent;

namespace TGBot.Texts.I18n;

/// <summary>
/// 语言目录注册中心：内置 <c>en</c>/<c>zh</c> 嵌入式资源 + 运行时注册扩展语言。
/// <para>开发者可通过 <see cref="Register"/> 一行注册新语言（或提 PR 增加嵌入式资源）；
/// 注册在 <see cref="I18nService"/> 构造时合并生效（注册内容覆盖内置键）。</para>
/// </summary>
public static class LanguageCatalog
{
    /// <summary>内置语言代码集合。</summary>
    public static readonly IReadOnlyCollection<string> BuiltinLanguages = new[] { "en", "zh" };

    /// <summary>缺键回退语言，恒为 <c>en</c>。</summary>
    public const string FallbackLanguage = "en";

    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> Registry =
        new(StringComparer.Ordinal);

    /// <summary>
    /// 全部可用语言：内置 <c>en</c>/<c>zh</c> + 已注册语言（去重）。
    /// </summary>
    public static IReadOnlyCollection<string> SupportedLanguages
        => BuiltinLanguages.Concat(Registry.Keys).Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>
    /// 注册（覆盖或新增）一种语言的完整文案字典。
    /// </summary>
    /// <param name="lang">语言代码（如 <c>fr</c>）；覆盖内置语言（<c>en</c>/<c>zh</c>）时以注册内容为准。</param>
    /// <param name="entries">键 → 文案映射。</param>
    /// <exception cref="ArgumentException"><paramref name="lang"/> 为空或 <paramref name="entries"/> 为空时抛出。</exception>
    public static void Register(string lang, IReadOnlyDictionary<string, string> entries)
    {
        if (string.IsNullOrWhiteSpace(lang))
        {
            throw new ArgumentException("语言代码不能为空。", nameof(lang));
        }

        if (entries is null || entries.Count == 0)
        {
            throw new ArgumentException("语言条目不能为空。", nameof(entries));
        }

        Registry[lang] = new Dictionary<string, string>(entries, StringComparer.Ordinal);
    }

    /// <summary>
    /// 注销一种已注册语言（恢复内置文案；主要用于测试隔离）。
    /// </summary>
    /// <param name="lang">语言代码。</param>
    public static void Unregister(string lang)
        => Registry.TryRemove(lang, out _);

    /// <summary>
    /// 将 BCP 47 语言标签归一化为内置语言代码。
    /// </summary>
    /// <param name="bc47">BCP 47 语言标签（如 <c>zh-CN</c>/<c>zh-TW</c>/<c>zh-Hant</c>/<c>en-US</c>）。</param>
    /// <returns>归一化语言代码（<c>zh</c>/<c>en</c>）；无法映射时返回 <see langword="null"/>。</returns>
    public static string? NormalizeLanguageCode(string? bc47)
    {
        if (string.IsNullOrWhiteSpace(bc47))
        {
            return null;
        }

        var code = bc47.Trim();
        var dash = code.IndexOfAny(new[] { '-', '_' });
        var prefix = dash >= 0 ? code[..dash] : code;
        return prefix.ToLowerInvariant() switch
        {
            "zh" => "zh",
            "en" => "en",
            _ => null,
        };
    }

    /// <summary>
    /// 获取某语言的当前文案目录（内置 <c>en</c>/<c>zh</c> + 注册覆盖；用于工具与测试校验）。
    /// </summary>
    /// <param name="lang">语言代码。</param>
    /// <returns>文案目录；无该语言时返回 <see langword="null"/>。</returns>
    public static IReadOnlyDictionary<string, string>? GetCatalog(string lang)
        => Registry.TryGetValue(lang, out var entries) ? entries : null;
}