using System.Text;

namespace TGBot.Messaging;

/// <summary>
/// 媒体说明（caption）构建工具。
/// </summary>
public static class CaptionBuilder
{
    private const int MaxLength = 1000;

    /// <summary>
    /// 构建媒体说明：<c>标题：xxx\n\n来源：https://...</c>。
    /// </summary>
    /// <param name="title">标题。</param>
    /// <param name="sourceUrl">来源 URL。</param>
    /// <returns>净化后的说明文本。</returns>
    public static string Build(string title, string sourceUrl)
    {
        var text = $"标题：{title}\n\n来源：{sourceUrl}";
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
