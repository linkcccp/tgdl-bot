using System.Diagnostics;
using System.Globalization;
using TGBot.Logging;
using TGBot.Security;
using TGBot.Texts;

namespace TGBot.Download;

/// <summary>
/// 通过 <see cref="System.Diagnostics.Process.Start(ProcessStartInfo)"/> 调用系统安装的 yt-dlp 二进制实现下载。
/// <para>不内嵌 yt-dlp/ffmpeg，使用 <c>ArgumentList</c> 避免 shell 注入。</para>
/// </summary>
public sealed class YtDlpDownloader : IDownloader
{
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "mp3", "m4a", "opus", "ogg", "oga", "wav", "flac", "aac", "m4b", "mp2",
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "mp4", "mkv", "webm", "mov", "avi", "flv", "m4v", "3gp", "mpeg", "mpg", "ts",
    };

    private readonly IAppLogger _logger;

    /// <summary>
    /// 初始化 <see cref="YtDlpDownloader"/>。
    /// </summary>
    /// <param name="logger">日志器。</param>
    public YtDlpDownloader(IAppLogger logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<DownloadedMedia> DownloadAsync(
        DownloadOptions options,
        Action<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var free = DiskUtil.GetFreeSpaceBytes(options.JobDir);
        if (free is not null && free < 200L * 1024 * 1024)
        {
            throw new DownloadException(
                DownloadFailureReason.NoDiskSpace,
                UserTexts.NoDiskSpace,
                $"临时目录可用空间不足：{free} 字节");
        }

        var args = YtDlpArgumentBuilder.Build(options);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = options.YtDlpPath,
                WorkingDirectory = options.JobDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        foreach (var a in args)
        {
            process.StartInfo.ArgumentList.Add(a);
        }

        _logger.Info($"开始下载：{MaskUrl(options.Url)}");

        var meta = new MetaBuilder();
        var stderrTail = new RingBuffer(12);
        var tooLargeDetected = false;
        string? filePath = null;

        try
        {
            if (!process.Start())
            {
                throw new DownloadException(
                    DownloadFailureReason.Failed,
                    UserTexts.DownloadFailed,
                    "无法启动 yt-dlp 进程");
            }
        }
        catch (Exception ex) when (ex is not DownloadException)
        {
            throw new DownloadException(
                DownloadFailureReason.Failed,
                UserTexts.DownloadFailed,
                $"启动 yt-dlp 失败：{ex.Message}");
        }

        var stdoutTask = ReadLinesAsync(process.StandardOutput, line =>
        {
            if (YtDlpOutputParser.IsTooLargeMessage(line))
            {
                tooLargeDetected = true;
            }

            if (line.StartsWith(YtDlpOutputParser.MetaMarker, StringComparison.Ordinal))
            {
                var parsed = YtDlpOutputParser.ParseMeta(line);
                if (parsed is { } p)
                {
                    meta.Apply(p);
                }
            }
            else if (line.StartsWith(YtDlpOutputParser.FileMarker, StringComparison.Ordinal))
            {
                filePath = line.Split('\u001f', 2)[^1];
            }
        }, cancellationToken);

        var stderrTask = ReadLinesAsync(process.StandardError, line =>
        {
            stderrTail.Add(line);
            if (YtDlpOutputParser.IsTooLargeMessage(line))
            {
                tooLargeDetected = true;
            }

            var p = YtDlpOutputParser.ParseProgress(line);
            if (p is not null)
            {
                progress?.Invoke(p);
            }
        }, cancellationToken);

        Task exitTask;
        try
        {
            exitTask = process.WaitForExitAsync(cancellationToken);
            var completed = await Task.WhenAny(
                exitTask,
                Task.Delay(options.Timeout, cancellationToken)).ConfigureAwait(false);

            if (completed != exitTask)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // ignore kill failure
                }

                throw new DownloadException(
                    DownloadFailureReason.Timeout,
                    "下载超时，请稍后重试。",
                    $"yt-dlp 超过 {options.Timeout.TotalSeconds} 秒未完成");
            }

            await stdoutTask.ConfigureAwait(false);
            await stderrTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw new DownloadException(
                DownloadFailureReason.Cancelled,
                "任务已取消。",
                "下载任务被取消");
        }
        finally
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // ignore
                }
            }
        }

        var detail = string.Join(" | ", stderrTail);
        if (tooLargeDetected)
        {
            throw new DownloadException(
                DownloadFailureReason.TooLarge,
                UserTexts.FileTooLarge,
                $"文件超过上限：{detail}");
        }

        if (process.ExitCode != 0)
        {
            _logger.Warn($"yt-dlp 退出码 {process.ExitCode}：{detail}");
            if (YtDlpOutputParser.IsAuthRequiredMessage(detail))
            {
                throw new DownloadException(
                    DownloadFailureReason.AuthRequired,
                    UserTexts.AuthRequired,
                    $"站点要求认证：{detail}");
            }

            if (YtDlpOutputParser.IsFormatUnavailableMessage(detail))
            {
                throw new DownloadException(
                    DownloadFailureReason.FormatUnavailable,
                    UserTexts.FormatUnavailable,
                    $"可用格式不足：{detail}");
            }

            throw new DownloadException(
                DownloadFailureReason.Failed,
                UserTexts.DownloadFailed,
                $"yt-dlp 退出码 {process.ExitCode}");
        }

        var media = await ResolveOutputAsync(options, meta, filePath, cancellationToken).ConfigureAwait(false);
        _logger.Info($"下载完成：{MaskUrl(options.Url)} -> {media.FilePath} ({media.SizeBytes} 字节)");
        return media;
    }

    private static async Task<DownloadedMedia> ResolveOutputAsync(
        DownloadOptions options,
        MetaBuilder meta,
        string? filePathHint,
        CancellationToken cancellationToken)
    {
        string? path = null;

        if (!string.IsNullOrEmpty(filePathHint))
        {
            var full = Path.GetFullPath(filePathHint);
            if (PathSanitizer.IsWithinDirectory(options.JobDir, full) && File.Exists(full))
            {
                path = full;
            }
        }

        if (path is null)
        {
            path = Directory.EnumerateFiles(options.JobDir, "media.*", SearchOption.AllDirectories)
                .OrderByDescending(p => new FileInfo(p).Length)
                .FirstOrDefault();
        }

        if (path is null)
        {
            if (meta.SizeBytes is { } knownSize && knownSize > options.MaxSizeBytes)
            {
                throw new DownloadException(
                    DownloadFailureReason.TooLarge,
                    UserTexts.FileTooLarge,
                    $"元数据显示文件大小 {knownSize} 超过上限 {options.MaxSizeBytes}");
            }

            throw new DownloadException(
                DownloadFailureReason.Failed,
                UserTexts.DownloadFailed,
                "未找到下载产物文件");
        }

        if (!File.Exists(path))
        {
            throw new DownloadException(
                DownloadFailureReason.Failed,
                UserTexts.DownloadFailed,
                "下载产物文件不存在");
        }

        if (PathSanitizer.IsSymbolicLink(path))
        {
            throw new DownloadException(
                DownloadFailureReason.Failed,
                UserTexts.DownloadFailed,
                "下载产物是符号链接，已拒绝");
        }

        var info = new FileInfo(path);
        var ext = (info.Extension.Length > 1 ? info.Extension[1..] : string.Empty).ToLowerInvariant();
        var isAudio = AudioExtensions.Contains(ext);
        var title = meta.Title ?? "untitled";

        var size = info.Length;
        if (size > options.MaxSizeBytes)
        {
            TryDelete(path);
            throw new DownloadException(
                DownloadFailureReason.TooLarge,
                UserTexts.FileTooLarge,
                $"文件大小 {size} 超过上限 {options.MaxSizeBytes}");
        }

        return new DownloadedMedia
        {
            FilePath = path,
            Title = PathSanitizer.SanitizeFileName(title),
            RawTitle = title,
            Extension = ext,
            SizeBytes = size,
            DurationSeconds = meta.DurationSeconds,
            IsAudio = isAudio,
            SourceUrl = options.Url,
        };
    }

    private static async Task ReadLinesAsync(
        TextReader reader,
        Action<string> onLine,
        CancellationToken cancellationToken)
    {
        try
        {
            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
            {
                onLine(line);
            }
        }
        catch (OperationCanceledException)
        {
            // ignore on cancellation
        }
        catch (IOException)
        {
            // ignore stream closing
        }
    }

    private static DownloadProgress? ParseProgress(string line)
    {
        const string marker = "DLP ";
        if (!line.StartsWith(marker, StringComparison.Ordinal))
        {
            return null;
        }

        var rest = line[marker.Length..].Trim();
        if (rest.Length == 0)
        {
            return null;
        }

        var parts = rest.Split('|');
        var percentText = parts[0].Trim().Replace("%", string.Empty);
        if (percentText is "--.-" or "N/A" or "NA" or "-")
        {
            return null;
        }

        if (!double.TryParse(percentText, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
        {
            return null;
        }

        var speed = parts.Length > 1 ? parts[1].Trim() : null;
        return new DownloadProgress(percent, string.IsNullOrEmpty(speed) ? null : speed);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // ignore
        }
    }

    private static string MaskUrl(string url)
    {
        // 仅记录主机与路径，避免泄露查询参数中的敏感信息。
        try
        {
            var uri = new Uri(url);
            return $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}";
        }
        catch
        {
            return "<无效URL>";
        }
    }

    private sealed class MetaBuilder
    {
        public string? Title { get; private set; }

        public int? DurationSeconds { get; private set; }

        public long? SizeBytes { get; private set; }

        public void Apply(YtDlpOutputParser.MetaLine meta)
        {
            Title = meta.Title;
            DurationSeconds = meta.DurationSeconds;
            SizeBytes = meta.SizeBytes;
        }
    }

    private sealed class RingBuffer
    {
        private readonly string[] _items;
        private int _count;

        public RingBuffer(int capacity)
        {
            _items = new string[capacity];
        }

        public void Add(string item)
        {
            if (_count < _items.Length)
            {
                _items[_count++] = item;
            }
            else
            {
                Array.Copy(_items, 1, _items, 0, _items.Length - 1);
                _items[^1] = item;
            }
        }

        public override string ToString() => string.Join(" | ", _items.Take(_count));
    }
}
