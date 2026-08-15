using TGBot.Logging;

namespace TGBot.Config;

/// <summary>
/// 应用配置模型，由 <see cref="ConfigParser"/> 从 config.conf 解析得到。
/// <para>所有路径均为绝对路径（在加载阶段展开）。</para>
/// </summary>
public sealed class AppConfig
{
    /// <summary>Bot Token，来自 @BotFather。</summary>
    public string BotToken { get; init; } = string.Empty;

    /// <summary>本地 Telegram Bot API Server 地址（--local 模式）。</summary>
    public string LocalApiBaseUrl { get; init; } = string.Empty;

    /// <summary>目标频道/群组 ID 白名单，下载结果推送到这些会话。</summary>
    public IReadOnlyList<long> TargetChannelIds { get; init; } = Array.Empty<long>();

    /// <summary>允许在私聊中触发下载的用户 ID 白名单。</summary>
    public IReadOnlyList<long> AllowedUserIds { get; init; } = Array.Empty<long>();

    /// <summary>下载临时目录（绝对路径）。</summary>
    public string DownloadTempDir { get; init; } = string.Empty;

    /// <summary>yt-dlp 二进制绝对路径。</summary>
    public string YtDlpPath { get; init; } = string.Empty;

    /// <summary>ffmpeg 二进制绝对路径。</summary>
    public string FfmpegPath { get; init; } = string.Empty;

    /// <summary>cookies 存储目录（各站点 cookie 文件，按域名自动选用）。</summary>
    public string CookieStoreDir { get; init; } = string.Empty;

    /// <summary>yt-dlp 使用的 HTTP(S) 代理地址，为空表示不使用。</summary>
    public string YtDlpProxy { get; init; } = string.Empty;

    /// <summary>附加 yt-dlp 参数（按空白拆分），如 <c>--extractor-args youtube:player_client=android</c>。</summary>
    public string YtDlpExtraArgs { get; init; } = string.Empty;

    /// <summary>YouTube 多 player_client 列表（仅 YouTube 域名生效，逗号分隔；为空则禁用）。</summary>
    public string YtDlpYoutubePlayerClients { get; init; } = "android,ios,web_embedded,tv";

    /// <summary>最大并发下载任务数。</summary>
    public int MaxConcurrentDownloads { get; init; } = 2;

    /// <summary>日志级别。</summary>
    public LogLevel LogLevel { get; init; } = LogLevel.Info;

    /// <summary>日志文件路径，为空表示不写文件。</summary>
    public string? LogFile { get; init; }

    /// <summary>下载失败后的任务级重试次数（不含首次尝试）。</summary>
    public int DownloadRetries { get; init; } = 3;

    /// <summary>单个下载任务的超时秒数。</summary>
    public int DownloadTimeoutSeconds { get; init; } = 3600;

    /// <summary>上传失败的自动重试次数（不含首次尝试）。</summary>
    public int UploadRetries { get; init; } = 2;

    /// <summary>是否提取音频（-x --audio-format mp3）。</summary>
    public bool ExtractAudio { get; init; }

    /// <summary>私聊请求者是否也接收下载后的媒体文件。</summary>
    public bool AlsoSendMediaToRequester { get; init; }

    /// <summary>是否允许下载指向私网/回环地址的 URL（SSRF 防护，默认拒绝）。</summary>
    public bool AllowPrivateUrls { get; init; }

    /// <summary>可上传的最大字节数（默认留安全余量，接近 Bot API 本地服务器 2GB 上限）。</summary>
    public long MaxMediaSizeBytes { get; init; } = 1_900_000_000;

    /// <summary>是否允许下载播放列表（false 时添加 --no-playlist）。</summary>
    public bool AllowPlaylists { get; init; }

    /// <summary>ffmpeg 合并/转封装容器格式（可用 <c>/</c> 分隔的候选列表，如 <c>mp4/mkv</c>）。</summary>
    public string MergeFormat { get; init; } = "mp4/mkv";

    /// <summary>是否在 /update 时更新 yt-dlp。</summary>
    public bool UpdateYtDlp { get; init; } = true;

    /// <summary>是否在 /update 时更新 ffmpeg。</summary>
    public bool UpdateFfmpeg { get; init; } = true;

    /// <summary>配置文件的完整路径（加载后回填，用于日志与错误提示）。</summary>
    public string SourcePath { get; init; } = string.Empty;
}
