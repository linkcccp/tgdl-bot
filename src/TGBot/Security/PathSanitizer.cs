using System.Text;

namespace TGBot.Security;

/// <summary>
/// 文件名/路径净化工具，用于防止路径穿越、隐藏文件、控制字符与符号链接攻击。
/// </summary>
public static class PathSanitizer
{
    private static readonly char[] DisallowedFileNameChars = { '/', '\\', ':', '*', '?', '"', '<', '>', '|', '\0' };

    /// <summary>
    /// 将任意用户输入净化为安全的文件名（不含目录分隔符）。
    /// </summary>
    /// <param name="name">原始名称。</param>
    /// <param name="maxLength">最大长度。</param>
    /// <returns>净化后的文件名。</returns>
    public static string SanitizeFileName(string? name, int maxLength = 120)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "untitled";
        }

        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (char.IsControl(c))
            {
                continue;
            }

            if (DisallowedFileNameChars.Contains(c))
            {
                sb.Append('_');
            }
            else
            {
                sb.Append(c);
            }
        }

        var result = sb.ToString().Trim().TrimStart('.', ' ');
        if (result.Length == 0 || result == "." || result == "..")
        {
            return "untitled";
        }

        if (result.Length > maxLength)
        {
            result = result[..maxLength];
        }

        return result;
    }

    /// <summary>
    /// 校验某个绝对路径是否严格位于指定根目录之内（防路径穿越）。
    /// </summary>
    /// <param name="rootDir">根目录（绝对路径）。</param>
    /// <param name="candidate">待校验路径（绝对路径）。</param>
    /// <returns>在根目录内返回 <see langword="true"/>。</returns>
    public static bool IsWithinDirectory(string rootDir, string candidate)
    {
        var root = Path.GetFullPath(rootDir);
        var full = Path.GetFullPath(candidate);
        if (!full.StartsWith(root, StringComparison.Ordinal))
        {
            return false;
        }

        if (full.Length > root.Length && full[root.Length] != Path.DirectorySeparatorChar)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 判断路径是否为符号链接。
    /// </summary>
    /// <param name="path">文件路径。</param>
    /// <returns>是符号链接返回 <see langword="true"/>。</returns>
    public static bool IsSymbolicLink(string path)
    {
        try
        {
            return new FileInfo(path).LinkTarget is not null;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }
}
