// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

using System.Text;
using TGBot.Texts;
using TGBot.Texts.I18n;

namespace TGBot.Messaging;

/// <summary>
/// 媒体说明（caption）构建工具。
/// </summary>
public static class CaptionBuilder
{
    private const int MaxLength = 1000;

    /// <summary>
    /// 构建媒体说明：<c>标题：xxx\n\n来源：https://...</c>（文案按语言渲染）。
    /// </summary>
    /// <param name="i18n">国际化服务。</param>
    /// <param name="lang">语言代码。</param>
    /// <param name="title">标题。</param>
    /// <param name="sourceUrl">来源 URL。</param>
    /// <returns>净化后的说明文本。</returns>
    public static string Build(II18n i18n, string lang, string title, string sourceUrl)
    {
        var text = $"{i18n.Get(lang, UserTexts.CaptionTitle, title)}\n\n{i18n.Get(lang, UserTexts.CaptionSource, sourceUrl)}";
        return Sanitize(text);
    }

    /// <summary>
    /// 净化说明文本：去除控制字符、压缩连续空行、限制长度。
    /// </summary>
    /// <param name="text">原始文本。</param>
    /// <returns>净化后的文本。</returns>
    public static string Sanitize(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(text.Length);
        var blankLines = 0;
        foreach (var c in text)
        {
            if (char.IsControl(c) && c != '\n')
            {
                continue;
            }

            if (c == '\n')
            {
                blankLines++;
                if (blankLines > 2)
                {
                    continue;
                }
            }
            else
            {
                blankLines = 0;
            }

            sb.Append(c);
        }

        var result = sb.ToString().Trim();
        return result.Length <= MaxLength ? result : result[..MaxLength];
    }
}
