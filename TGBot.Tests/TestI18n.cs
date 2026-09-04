// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

using TGBot.Texts.I18n;

namespace TGBot.Tests;

/// <summary>
/// 测试共享的 <see cref="I18nService"/> 实例（只读，xunit 并行安全）。
/// </summary>
public static class TestI18n
{
    /// <summary>
    /// 默认语言为 en 的共享实例。
    /// </summary>
    public static readonly I18nService Instance = new();

    /// <summary>
    /// 以 zh 语言渲染指定键（测试断言中文文案用）。
    /// </summary>
    /// <param name="key">资源键。</param>
    /// <param name="args">占位符参数。</param>
    /// <returns>渲染后的中文文案。</returns>
    public static string Zh(string key, params object[] args) => Instance.Get("zh", key, args);
}
