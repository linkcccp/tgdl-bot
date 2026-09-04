// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

using TGBot.Messaging;

namespace TGBot.Texts.I18n;

/// <summary>
/// 用户语言解析器：按设计文档 §2.3 的解析链（高→低）决定消息语言：
/// <list type="number">
/// <item>私聊用户显式选择（<see cref="UserLanguageStore"/>）。</item>
/// <item>Telegram <c>language_code</c> 前缀映射（经 <see cref="LanguageCatalog.NormalizeLanguageCode"/>）。</item>
/// <item>全局默认语言（<c>auto</c> 时跳过本步）。</item>
/// <item>内建 fallback：<c>en</c>。</item>
/// </list>
/// 群组/频道消息不查用户存储，直接走全局默认 → fallback。
/// </summary>
public sealed class UserLanguageResolver
{
    /// <summary>全局默认语言取 <c>auto</c> 时表示"跟随用户/回退 en"。</summary>
    public const string Auto = "auto";

    private readonly UserLanguageStore _store;
    private readonly Func<InboundMessage, string?> _languageCodeProvider;
    private readonly string _globalDefault;

    /// <summary>
    /// 初始化 <see cref="UserLanguageResolver"/>。
    /// </summary>
    /// <param name="store">用户语言存储。</param>
    /// <param name="languageCodeProvider">从入站消息提取原始 Telegram <c>language_code</c> 的委托
    /// （无信号时返回 null，通常直接返回 <c>msg.LanguageCode</c>）。</param>
    /// <param name="globalDefault">全局默认语言（<c>auto</c>/<c>en</c>/<c>zh</c>）；默认 <c>auto</c>。</param>
    public UserLanguageResolver(
        UserLanguageStore store,
        Func<InboundMessage, string?> languageCodeProvider,
        string globalDefault = Auto)
    {
        _store = store;
        _languageCodeProvider = languageCodeProvider;
        _globalDefault = LanguageCatalog.NormalizeLanguageCode(globalDefault) ?? Auto;
    }

    /// <summary>
    /// 按解析链计算消息语言（入口解析一次，随消息流动）。
    /// </summary>
    /// <param name="msg">入站消息。</param>
    /// <returns>语言代码（en/zh 或注册语言）。</returns>
    public string Resolve(InboundMessage msg)
    {
        // 1. 私聊用户显式选择（群组不查存储，避免群成员个人语言污染群内文案）。
        if (msg.IsPrivate && msg.SenderUserId is { } userId && _store.Get(userId) is { } explicitLang)
        {
            return explicitLang;
        }

        // 2. Telegram language_code 前缀映射（私聊与群组均适用）。
        if (LanguageCatalog.NormalizeLanguageCode(_languageCodeProvider(msg)) is { } mapped)
        {
            return mapped;
        }

        // 3. 全局默认（auto 时跳过）→ 4. fallback en。
        return _globalDefault == Auto
            ? LanguageCatalog.FallbackLanguage
            : _globalDefault;
    }
}