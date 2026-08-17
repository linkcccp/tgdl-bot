// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

using System.Collections.Concurrent;
using System.Text.Json;

namespace TGBot.Texts.I18n;

/// <summary>
/// 国际化服务实现：加载嵌入式 <c>en.json</c>/<c>zh.json</c> 与
/// <see cref="LanguageCatalog.Register"/> 注册的语言，提供缺键回退（en → 键名）与占位符格式化。
/// <para>数字格式化在 <c>InvariantGlobalization</c> 下固定为 invariant，占位符语义确定。</para>
/// </summary>
public sealed class I18nService : II18n
{
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> _catalogs = new(StringComparer.Ordinal);
    private readonly string _defaultLanguage;

    /// <summary>
    /// 初始化 <see cref="I18nService"/>。
    /// </summary>
    /// <param name="defaultLanguage">默认语言（<see cref="II18n.T"/> 使用）；默认 <c>en</c>。</param>
    /// <param name="extraCatalogs">额外语言目录（测试注入或运行时扩展，覆盖内置与已注册语言）。</param>
    public I18nService(
        string? defaultLanguage = null,
        IEnumerable<KeyValuePair<string, IReadOnlyDictionary<string, string>>>? extraCatalogs = null)
    {
        _defaultLanguage = LanguageCatalog.NormalizeLanguageCode(defaultLanguage) ?? LanguageCatalog.FallbackLanguage;

        foreach (var lang in LanguageCatalog.BuiltinLanguages)
        {
            _catalogs[lang] = LoadEmbedded(lang);
        }

        // 注册语言覆盖内置（开发者扩展 / 覆盖默认文案）。
        foreach (var lang in LanguageCatalog.SupportedLanguages)
        {
            if (LanguageCatalog.GetCatalog(lang) is { } registered)
            {
                _catalogs[lang] = registered;
            }
        }

        if (extraCatalogs is not null)
        {
            foreach (var (lang, entries) in extraCatalogs)
            {
                _catalogs[lang] = entries;
            }
        }
    }

    /// <inheritdoc />
    public string Get(string lang, string key, params object[] args)
    {
        var template = Resolve(lang, key);
        if (args.Length == 0)
        {
            return template;
        }

        // 注意：string.Format 只解析格式字符串（模板）中的格式项，参数值中的花括号按字面插入、
        // 不参与解析（如 YtDlpExtraArgs 含模板/JSON 片段时原样输出，不会抛异常），因此无需转义参数。
        try
        {
            return string.Format(template, args);
        }
        catch (FormatException)
        {
            // 模板本身含未配对花括号（翻译资源缺陷）时兜底返回原文案，绝不向调用方抛异常
            // （否则 /config list 等链路会静默无响应）。
            return template;
        }
    }

    /// <inheritdoc />
    public string T(string key, params object[] args) => Get(_defaultLanguage, key, args);

    /// <summary>
    /// 获取某语言的合并文案目录（内置 + 注册覆盖 + 额外注入），用于工具与测试校验完整性。
    /// </summary>
    /// <param name="lang">语言代码（如 <c>en</c>/<c>zh</c>）。</param>
    /// <returns>键 → 文案目录；无该语言时返回 <see langword="null"/>。</returns>
    public IReadOnlyDictionary<string, string>? CatalogFor(string lang)
    {
        var code = LanguageCatalog.NormalizeLanguageCode(lang) ?? lang;
        return _catalogs.TryGetValue(code, out var catalog) ? catalog : null;
    }

    private string Resolve(string lang, string key)
    {
        var code = LanguageCatalog.NormalizeLanguageCode(lang);
        var effective = code ?? lang;

        if (_catalogs.TryGetValue(effective, out var catalog) && catalog.TryGetValue(key, out var text))
        {
            return text;
        }

        if (_catalogs.TryGetValue(LanguageCatalog.FallbackLanguage, out var en) && en.TryGetValue(key, out var fallback))
        {
            return fallback;
        }

        return key;
    }

    /// <summary>
    /// 从嵌入程序集加载内置语言 JSON。
    /// </summary>
    /// <param name="lang">语言代码（en/zh）。</param>
    /// <returns>键 → 文案字典。</returns>
    /// <exception cref="InvalidOperationException">嵌入式资源缺失或 JSON 非法时抛出（装配期错误，应立即暴露）。</exception>
    private static IReadOnlyDictionary<string, string> LoadEmbedded(string lang)
    {
        var assembly = typeof(I18nService).Assembly;
        var resourceName = $"TGBot.Texts.I18n.Resources.{lang}.json";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"嵌入式语言资源缺失：{resourceName}");
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
            ?? throw new InvalidOperationException($"嵌入式语言资源解析失败：{resourceName}");
    }
}