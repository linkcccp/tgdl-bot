// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

namespace TGBot.Texts.I18n;

/// <summary>
/// 国际化文本服务：按语言与键渲染文案（支持 <c>{0}</c> 占位符格式化）。
/// <para>语言随消息显式传播（<c>InboundMessage.Language</c>），不依赖隐式上下文。</para>
/// </summary>
public interface II18n
{
    /// <summary>
    /// 按指定语言获取并格式化文案。
    /// </summary>
    /// <param name="lang">语言代码（如 <c>en</c>/<c>zh</c>）；缺失时回退 en，再缺失回退键名本身。</param>
    /// <param name="key">资源键。</param>
    /// <param name="args">占位符参数（可空）。</param>
    /// <returns>渲染后的文案。</returns>
    string Get(string lang, string key, params object[] args);

    /// <summary>
    /// 按默认语言获取并格式化文案（用于无消息上下文的场景，如启动期与后台任务）。
    /// </summary>
    /// <param name="key">资源键。</param>
    /// <param name="args">占位符参数（可空）。</param>
    /// <returns>渲染后的文案。</returns>
    string T(string key, params object[] args);
}