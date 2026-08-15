using System.Text.Json;

namespace TGBot.Download;

/// <summary>
/// 单个格式信息（来自 yt-dlp -J 输出）。
/// </summary>
/// <param name="FormatId">格式 ID。</param>
/// <param name="Vcodec">视频编码（无视频为 "none"）。</param>
/// <param name="Acodec">音频编码（无音频为 "none"）。</param>
/// <param name="Height">视频高度（像素，可空）。</param>
/// <param name="Tbr">总码率（kbps，可空）。</param>
/// <param name="Abr">音频码率（kbps，可空）。</param>
/// <param name="HasDrm">是否 DRM 保护。</param>
public sealed record FormatInfo(string? FormatId, string? Vcodec, string? Acodec, int? Height, double? Tbr, double? Abr, bool HasDrm);

/// <summary>
/// 从 yt-dlp <c>-J</c> JSON 中挑选「最高画质视频 + 最高音质音频」的组合表达式。
/// <para>纯解析逻辑，便于单元测试。</para>
/// </summary>
public static class YtDlpFormatPicker
{
    /// <summary>
    /// 解析 yt-dlp -J 输出的 formats 列表。
    /// </summary>
    /// <param name="json">-J 输出 JSON。</param>
    /// <returns>格式列表。</returns>
    public static IReadOnlyList<FormatInfo> ParseFormats(string json)
    {
        var result = new List<FormatInfo>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return result;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("formats", out var arr))
            {
                return result;
            }

            foreach (var el in arr.EnumerateArray())
            {
                result.Add(new FormatInfo(
                    GetString(el, "format_id"),
                    GetString(el, "vcodec"),
                    GetString(el, "acodec"),
                    GetInt(el, "height"),
                    GetDouble(el, "tbr"),
                    GetDouble(el, "abr"),
                    el.TryGetProperty("has_drm", out var drm) && drm.ValueKind == JsonValueKind.True));
            }
        }
        catch (JsonException)
        {
            return new List<FormatInfo>();
        }

        return result;
    }

    /// <summary>
    /// 挑选最高视频+音频组合，返回 <c>-f</c> 表达式（如 <c>137+140</c>）。
    /// 无纯视频/纯音频时返回 <see langword="null"/>（调用方改用 <c>best</c>）。
    /// </summary>
    /// <param name="formats">格式列表。</param>
    /// <returns>表达式或 <see langword="null"/>。</returns>
    public static string? PickBestExpression(IReadOnlyList<FormatInfo> formats)
    {
        var videoOnly = formats
            .Where(f => !string.IsNullOrEmpty(f.FormatId) && !IsNone(f.Vcodec) && IsNone(f.Acodec) && !f.HasDrm)
            .ToList();
        var audioOnly = formats
            .Where(f => !string.IsNullOrEmpty(f.FormatId) && IsNone(f.Vcodec) && !IsNone(f.Acodec) && !f.HasDrm)
            .ToList();

        if (videoOnly.Count == 0 || audioOnly.Count == 0)
        {
            return null;
        }

        var bestVideo = videoOnly
            .OrderByDescending(f => f.Height ?? 0)
            .ThenByDescending(f => f.Tbr ?? 0)
            .First();
        var bestAudio = audioOnly
            .OrderByDescending(f => f.Abr ?? 0)
            .First();

        return $"{bestVideo.FormatId}+{bestAudio.FormatId}";
    }

    /// <summary>
    /// 判断媒体是否含视频流（存在 <c>vcodec!="none"</c> 且高度 &gt; 0 的真实视频格式，排除 storyboard 等）。
    /// </summary>
    /// <param name="formats">格式列表。</param>
    /// <returns>含视频返回 <see langword="true"/>。</returns>
    public static bool HasVideo(IReadOnlyList<FormatInfo> formats)
        => formats.Any(f => !IsNone(f.Vcodec) && (f.Height ?? 0) > 0 && !f.HasDrm);

    /// <summary>
    /// 判断媒体是否仅音频（没有任何真实视频格式）。
    /// </summary>
    /// <param name="formats">格式列表。</param>
    /// <returns>仅音频返回 <see langword="true"/>。</returns>
    public static bool IsAudioOnly(IReadOnlyList<FormatInfo> formats) => !HasVideo(formats);

    private static bool IsNone(string? codec)
        => string.IsNullOrEmpty(codec) || codec.Equals("none", StringComparison.OrdinalIgnoreCase);

    private static string? GetString(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? GetInt(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

    private static double? GetDouble(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.Number && v.TryGetDouble(out var d) ? d : null;
}
