using System.Text.RegularExpressions;

namespace TGBot.Update;

/// <summary>
/// 从二进制输出文本中解析版本号（纯函数，便于单元测试）。
/// </summary>
public static class BinaryVersionParser
{
    private static readonly Regex FfmpegVersionRegex = new(@"version\s+(?:n)?(\d+(?:\.\d+)+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// 解析 yt-dlp 的版本输出，如 <c>2025.01.26</c>。
    /// </summary>
    /// <param name="output">yt-dlp --version 的输出。</param>
    /// <returns>版本号；解析失败返回 <see langword="null"/>。</returns>
    public static ToolVersion? ParseYtDlp(string? output)
    {
        return ToolVersion.TryParse(output, out var v) ? v : null;
    }

    /// <summary>
    /// 解析 ffmpeg 的版本输出，如 <c>ffmpeg version n9.0.1 Copyright ...</c>。
    /// </summary>
    /// <param name="output">ffmpeg -version 的输出（首行即可）。</param>
    /// <returns>版本号；解析失败返回 <see langword="null"/>。</returns>
    public static ToolVersion? ParseFfmpeg(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var match = FfmpegVersionRegex.Match(output);
        if (match.Success && ToolVersion.TryParse(match.Groups[1].Value, out var v))
        {
            return v;
        }

        return ToolVersion.TryParse(output, out v) ? v : null;
    }
}
