namespace TGBot.Download;

/// <summary>
/// 下载成功的媒体文件信息。
/// </summary>
public sealed class DownloadedMedia
{
    /// <summary>
    /// 下载产物的绝对路径。
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// 标题（已净化，可直接用于上传文件名与字幕）。
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// 原始标题（用于展示）。
    /// </summary>
    public string? RawTitle { get; init; }

    /// <summary>
    /// 文件扩展名（不含点，小写）。
    /// </summary>
    public required string Extension { get; init; }

    /// <summary>
    /// 文件字节数。
    /// </summary>
    public required long SizeBytes { get; init; }

    /// <summary>
    /// 视频/音频时长（秒），未知为 <see langword="null"/>。
    /// </summary>
    public int? DurationSeconds { get; init; }

    /// <summary>
    /// 是否音频内容（由扩展名判定）。
    /// </summary>
    public required bool IsAudio { get; init; }

    /// <summary>
    /// 源 URL。
    /// </summary>
    public required string SourceUrl { get; init; }
}
