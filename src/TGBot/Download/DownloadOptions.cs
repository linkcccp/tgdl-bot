// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

namespace TGBot.Download;

/// <summary>
/// 单个下载任务的运行参数快照。
/// </summary>
/// <param name="Url">待下载的已校验 URL。</param>
/// <param name="JobDir">任务专用临时目录（绝对路径）。</param>
/// <param name="YtDlpPath">yt-dlp 二进制绝对路径。</param>
/// <param name="FfmpegDir">ffmpeg/ffprobe 所在目录（可为空）。</param>
/// <param name="MergeFormat">合并/转封装容器格式。</param>
/// <param name="ExtractAudio">是否提取音频。</param>
/// <param name="AllowPlaylists">是否允许播放列表。</param>
/// <param name="MaxSizeBytes">允许的最大文件字节数。</param>
/// <param name="Timeout">下载超时时间。</param>
public sealed record DownloadOptions(
    string Url,
    string JobDir,
    string YtDlpPath,
    string? FfmpegDir,
    string MergeFormat,
    bool ExtractAudio,
    bool AllowPlaylists,
    long MaxSizeBytes,
    TimeSpan Timeout)
{
    /// <summary>站点 cookies 文件路径（可为空）。</summary>
    public string? CookiesFile { get; init; }

    /// <summary>HTTP(S) 代理地址（可为空）。</summary>
    public string? Proxy { get; init; }

    /// <summary>附加 yt-dlp 参数（可为空）。</summary>
    public IReadOnlyList<string>? ExtraArgs { get; init; }

    /// <summary>显式 <c>-f</c> 格式表达式（可为空，如 <c>137+140</c>）。</summary>
    public string? FormatExpression { get; init; }

    /// <summary>YouTube player_client 列表（可为空，仅 YouTube 域名生效）。</summary>
    public string? YoutubePlayerClients { get; init; }

    /// <summary>ffmpeg 二进制绝对路径（音频转码用，可为空）。</summary>
    public string? FfmpegPath { get; init; }
}
