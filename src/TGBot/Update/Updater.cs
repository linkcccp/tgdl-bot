// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

namespace TGBot.Update;

/// <summary>
/// 更新器抽象。
/// </summary>
public interface IUpdater
{
    /// <summary>
    /// 检查并更新指定的工具。
    /// </summary>
    /// <param name="includeYtDlp">是否更新 yt-dlp。</param>
    /// <param name="includeFfmpeg">是否更新 ffmpeg。</param>
    /// <param name="progress">进度回调（面向用户的中文提示）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>更新报告。</returns>
    Task<UpdateReport> UpdateAsync(
        bool includeYtDlp,
        bool includeFfmpeg,
        Action<string>? progress,
        CancellationToken cancellationToken);
}

/// <summary>
/// 默认更新器：对比本地/最新版本，必要时下载新二进制并原子替换，失败自动回滚。
/// </summary>
public sealed class Updater : IUpdater
{
    private readonly IProcessRunner _runner;
    private readonly IReadOnlyDictionary<string, IToolSource> _sources;
    private readonly string? _ytDlpPath;
    private readonly string? _ffmpegPath;
    private readonly TimeSpan _toolTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// 初始化 <see cref="Updater"/>。
    /// </summary>
    /// <param name="runner">进程运行器。</param>
    /// <param name="sources">工具源集合（按名称索引）。</param>
    /// <param name="ytDlpPath">yt-dlp 安装路径，可为空。</param>
    /// <param name="ffmpegPath">ffmpeg 安装路径，可为空。</param>
    public Updater(IProcessRunner runner, IEnumerable<IToolSource> sources, string? ytDlpPath, string? ffmpegPath)
    {
        _runner = runner;
        _sources = sources.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);
        _ytDlpPath = string.IsNullOrWhiteSpace(ytDlpPath) ? null : Path.GetFullPath(ytDlpPath);
        _ffmpegPath = string.IsNullOrWhiteSpace(ffmpegPath) ? null : Path.GetFullPath(ffmpegPath);
    }

    /// <inheritdoc />
    public async Task<UpdateReport> UpdateAsync(
        bool includeYtDlp,
        bool includeFfmpeg,
        Action<string>? progress,
        CancellationToken cancellationToken)
    {
        var results = new List<ToolUpdateResult>();
        var tempRoot = Path.Combine(Path.GetTempPath(), "tgdl-update-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempRoot);

        try
        {
            if (includeYtDlp)
            {
                results.Add(await UpdateToolAsync("yt-dlp", _ytDlpPath, _sources, tempRoot, progress, cancellationToken).ConfigureAwait(false));
            }

            if (includeFfmpeg)
            {
                results.Add(await UpdateToolAsync("ffmpeg", _ffmpegPath, _sources, tempRoot, progress, cancellationToken).ConfigureAwait(false));
            }
        }
        finally
        {
            try
            {
                Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
                // ignore cleanup failure
            }
        }

        return new UpdateReport(results);
    }

    private async Task<ToolUpdateResult> UpdateToolAsync(
        string name,
        string? installPath,
        IReadOnlyDictionary<string, IToolSource> sources,
        string tempRoot,
        Action<string>? progress,
        CancellationToken cancellationToken)
    {
        if (installPath is null || !sources.TryGetValue(name, out var source))
        {
            return new ToolUpdateResult(name, null, null, ToolUpdateStatus.NotConfigured);
        }

        ToolVersion? localVersion = null;
        try
        {
            localVersion = await GetLocalVersionAsync(name, installPath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new UpdateException(UpdateFailureReason.LocalVersionUnavailable, $"{name} 本地版本查询失败：{ex.Message}");
        }

        ToolVersion? latest;
        try
        {
            progress?.Invoke($"正在检查 {name} 最新版本…");
            latest = await source.GetLatestVersionAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new UpdateException(UpdateFailureReason.LatestVersionUnavailable, $"{name} 最新版本查询失败：{ex.Message}");
        }

        if (latest is null)
        {
            throw new UpdateException(UpdateFailureReason.LatestVersionUnavailable, $"{name} 返回空版本");
        }

        // 短路需标度一致：本地版本与远端同为日期标度（如 autobuild 年份）时才允许数值比较；
        // 标度不一致（如本地 git 提交计数 118503 与远端日期 2026…）直接比较必然误判，不得短路。
        if (localVersion is not null && localVersion.CompareTo(latest) >= 0 && localVersion.IsDateLike == latest.IsDateLike)
        {
            progress?.Invoke($"{name} 已是最新版本（{localVersion}），无需更新。");
            return new ToolUpdateResult(name, localVersion, latest, ToolUpdateStatus.AlreadyUpToDate);
        }

        progress?.Invoke($"{name} 有更新：{localVersion?.ToString() ?? "未知"} → {latest}，正在下载…");
        string downloaded;
        try
        {
            downloaded = await source.DownloadBinaryAsync(tempRoot, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new UpdateException(UpdateFailureReason.DownloadFailed, $"{name} 下载失败：{ex.Message}");
        }

        try
        {
            var verified = await VerifyBinaryAsync(name, downloaded, cancellationToken).ConfigureAwait(false);
            progress?.Invoke($"验证通过（{verified}），正在原子替换…");

            AtomicFileReplacer.Replace(installPath, downloaded);

            if (name == "ffmpeg")
            {
                try
                {
                    // 记录本次安装的 autobuild 时间，使下次 /update 能同标度比较短路；写失败忽略
                    //（下次 /update 会重新下载，无害，不阻塞更新成功）。
                    FfmpegVersionMarker.Write(installPath, latest);
                }
                catch
                {
                    // ignore
                }
            }

            var installedVersion = await GetLocalVersionAsync(name, installPath, cancellationToken).ConfigureAwait(false);
            progress?.Invoke($"{name} 更新完成：{installedVersion?.ToString() ?? "未知"}");
            return new ToolUpdateResult(name, localVersion, installedVersion, ToolUpdateStatus.Updated);
        }
        catch (UpdateException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new UpdateException(UpdateFailureReason.ReplaceFailed, $"{name} 原子替换失败：{ex.Message}");
        }
    }

    private async Task<ToolVersion?> GetLocalVersionAsync(string name, string installPath, CancellationToken cancellationToken)
    {
        // ffmpeg 优先读 marker（上次安装的 autobuild 日期，与远端同标度）；无 marker 回退二进制解析
        //（git 提交计数，仅作展示不参与短路）。
        // 须校验二进制存在：marker 残留（二进制被删/手动替换）时不得短路为"已是最新"，
        // 否则永久失去自愈能力；回退二进制解析路径（二进制缺失 → 进程启动失败 → LocalVersionUnavailable）。
        if (name == "ffmpeg" && File.Exists(installPath) && FfmpegVersionMarker.TryRead(installPath, out var marked))
        {
            return marked;
        }

        var args = name == "ffmpeg" ? new[] { "-version" } : new[] { "--version" };
        var output = await _runner.RunAsync(installPath, args, null, _toolTimeout, cancellationToken).ConfigureAwait(false);
        if (output.ExitCode != 0)
        {
            throw new InvalidOperationException($"版本命令退出码 {output.ExitCode}");
        }

        var version = name == "ffmpeg"
            ? BinaryVersionParser.ParseFfmpeg(output.StdOut)
            : BinaryVersionParser.ParseYtDlp(output.StdOut);
        return version;
    }

    private async Task<ToolVersion> VerifyBinaryAsync(string name, string binaryPath, CancellationToken cancellationToken)
    {
        var version = await GetLocalVersionAsync(name, binaryPath, cancellationToken).ConfigureAwait(false);
        if (version is null)
        {
            throw new InvalidOperationException("下载的二进制无法运行或无法解析版本");
        }

        return version;
    }
}
