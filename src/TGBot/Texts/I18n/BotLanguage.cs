// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

namespace TGBot.Texts.I18n;

/// <summary>
/// Bot 支持的语言枚举（内置语言集；注册语言不经此枚举）。
/// </summary>
public enum BotLanguage
{
    /// <summary>英语（<c>en</c>），也是缺键回退语言。</summary>
    En,

    /// <summary>简体中文（<c>zh</c>）。</summary>
    Zh,
}

/// <summary>
/// <see cref="BotLanguage"/> 与语言代码字符串之间的映射辅助。
/// </summary>
public static class BotLanguageExtensions
{
    /// <summary>
    /// 将枚举转换为语言代码字符串。
    /// </summary>
    /// <param name="lang">语言枚举。</param>
    /// <returns>语言代码（<c>en</c>/<c>zh</c>）。</returns>
    public static string Code(this BotLanguage lang)
        => lang == BotLanguage.Zh ? "zh" : "en";

    /// <summary>
    /// 尝试将语言代码解析为枚举（非法值返回 <see langword="false"/>，用于配置校验）。
    /// </summary>
    /// <param name="code">语言代码。</param>
    /// <param name="lang">解析结果。</param>
    /// <returns>解析成功返回 <see langword="true"/>。</returns>
    public static bool TryParseCode(string? code, out BotLanguage lang)
    {
        if (string.Equals(code, "zh", StringComparison.OrdinalIgnoreCase))
        {
            lang = BotLanguage.Zh;
            return true;
        }

        if (string.Equals(code, "en", StringComparison.OrdinalIgnoreCase))
        {
            lang = BotLanguage.En;
            return true;
        }

        lang = BotLanguage.En;
        return false;
    }
}