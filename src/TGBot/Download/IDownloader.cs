namespace TGBot.Download;

/// <summary>
/// 下载进度信息。
/// </summary>
/// <param name="Percent">下载百分比（0-100），未知为 <see langword="null"/>。</param>
/// <param name="SpeedText">速度文本，可为空。</param>
public sealed record DownloadProgress(double? Percent, string? SpeedText);

/// <summary>
/// 下载器抽象，负责调用外部 yt-dlp 进程完成下载。
/// </summary>
public interface IDownloader
{
    /// <summary>
    /// 执行一次下载。
    /// </summary>
    /// <param name="options">下载参数。</param>
    /// <param name="progress">进度回调，可为 <see langword="null"/>。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>下载结果。</returns>
    /// <exception cref="DownloadException">下载失败时抛出，含用户可读的失败原因。</exception>
    Task<DownloadedMedia> DownloadAsync(
        DownloadOptions options,
        Action<DownloadProgress>? progress,
        CancellationToken cancellationToken);
}
