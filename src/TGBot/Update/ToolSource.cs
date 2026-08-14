using TGBot.Security;

namespace TGBot.Update;

/// <summary>
/// 工具最新版本源抽象（用于发现最新版本并下载二进制）。
/// </summary>
public interface IToolSource
{
    /// <summary>
    /// 工具名称（yt-dlp / ffmpeg）。
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 获取最新版本号。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>最新版本；无法获取时返回 <see langword="null"/>。</returns>
    Task<ToolVersion?> GetLatestVersionAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 下载最新二进制到目标目录。
    /// </summary>
    /// <param name="destinationDir">目标目录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>下载得到的可执行文件路径。</returns>
    Task<string> DownloadBinaryAsync(string destinationDir, CancellationToken cancellationToken);
}

/// <summary>
/// yt-dlp 官方最新版源（GitHub releases）。
/// </summary>
public sealed class YtDlpToolSource : IToolSource
{
    private const string LatestUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp";

    private readonly HttpClient _http;

    /// <summary>
    /// 初始化 <see cref="YtDlpToolSource"/>。
    /// </summary>
    /// <param name="http">共享 HttpClient（自动跟随重定向，用于下载）。</param>
    public YtDlpToolSource(HttpClient http)
    {
        _http = http;
    }

    /// <summary>
    /// 工具名称。
    /// </summary>
    public string Name => "yt-dlp";

    /// <inheritdoc />
    public async Task<ToolVersion?> GetLatestVersionAsync(CancellationToken cancellationToken)
    {
        // 需要读取重定向的 Location 头来获取版本，因此必须禁用自动重定向。
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var headClient = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Head, LatestUrl);
        using var response = await headClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is System.Net.HttpStatusCode.Redirect or System.Net.HttpStatusCode.RedirectKeepVerb or System.Net.HttpStatusCode.Found)
        {
            return UriVersionParser.ParseGitHubRedirectLocation(response.Headers.Location?.ToString());
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<string> DownloadBinaryAsync(string destinationDir, CancellationToken cancellationToken)
    {
        var tempPath = Path.Combine(destinationDir, $"yt-dlp-{Guid.NewGuid():N}");
        using (var response = await _http.GetAsync(LatestUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var file = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await stream.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
        }

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(tempPath, (UnixFileMode)((int)UnixFileMode.UserRead | (int)UnixFileMode.UserWrite | (int)UnixFileMode.UserExecute | (int)UnixFileMode.GroupRead | (int)UnixFileMode.GroupExecute));
        }

        return tempPath;
    }
}

/// <summary>
/// ffmpeg 官方静态构建源（johnvansickle.com）。
/// <para>选择理由：官方维护的静态构建，无需 root 权限即可安装到非 root 运行目录、
/// 支持原子替换与回滚，且版本通常领先 Debian apt 仓库；apt 需要 root 且版本较旧。</para>
/// </summary>
public sealed class FfmpegToolSource : IToolSource
{
    private const string HomePageUrl = "https://johnvansickle.com/ffmpeg/";
    private const string ReleaseUrl = "https://johnvansickle.com/ffmpeg/releases/ffmpeg-release-amd64-static.tar.xz";

    private readonly HttpClient _http;
    private readonly IProcessRunner _runner;

    /// <summary>
    /// 初始化 <see cref="FfmpegToolSource"/>。
    /// </summary>
    /// <param name="http">共享 HttpClient。</param>
    /// <param name="runner">进程运行器（用于解压）。</param>
    public FfmpegToolSource(HttpClient http, IProcessRunner runner)
    {
        _http = http;
        _runner = runner;
    }

    /// <summary>
    /// 工具名称。
    /// </summary>
    public string Name => "ffmpeg";

    /// <inheritdoc />
    public async Task<ToolVersion?> GetLatestVersionAsync(CancellationToken cancellationToken)
    {
        var page = await _http.GetStringAsync(HomePageUrl, cancellationToken).ConfigureAwait(false);
        return UriVersionParser.ParseJohnVanSickleReleasePage(page);
    }

    /// <inheritdoc />
    public async Task<string> DownloadBinaryAsync(string destinationDir, CancellationToken cancellationToken)
    {
        var archivePath = Path.Combine(destinationDir, $"ffmpeg-{Guid.NewGuid():N}.tar.xz");
        var extractDir = Path.Combine(destinationDir, $"ffmpeg-x-{Guid.NewGuid():N}");
        Directory.CreateDirectory(extractDir);

        try
        {
            using (var response = await _http.GetAsync(ReleaseUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var file = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                await stream.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
            }

            var result = await _runner.RunAsync("tar", new[] { "-xf", archivePath, "-C", extractDir }, null, TimeSpan.FromMinutes(5), cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException($"解压 ffmpeg 失败：{result.StdErr}");
            }

            var binary = Directory.GetFiles(extractDir, "ffmpeg", SearchOption.AllDirectories)
                .FirstOrDefault(p => !PathSanitizer.IsSymbolicLink(p));
            if (binary is null)
            {
                throw new InvalidOperationException("解压后未找到 ffmpeg 可执行文件");
            }

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(binary, (UnixFileMode)((int)UnixFileMode.UserRead | (int)UnixFileMode.UserWrite | (int)UnixFileMode.UserExecute | (int)UnixFileMode.GroupRead | (int)UnixFileMode.GroupExecute));
            }

            return binary;
        }
        finally
        {
            try
            {
                File.Delete(archivePath);
            }
            catch
            {
                // ignore
            }
        }
    }
}

