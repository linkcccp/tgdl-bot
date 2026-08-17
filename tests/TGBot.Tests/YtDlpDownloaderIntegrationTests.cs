// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

using System.Net;
using TGBot.Download;
using TGBot.Logging;
using Xunit;

namespace TGBot.Tests;

/// <summary>
/// <see cref="YtDlpDownloader"/> 集成测试（依赖本机 yt-dlp 与网络）。
/// 网络或 yt-dlp 不可用时静默跳过。
/// </summary>
public class YtDlpDownloaderIntegrationTests
{
    private const string TestVideo =
        "https://raw.githubusercontent.com/mediaelement/mediaelement-files/master/big_buck_bunny.mp4";

    [Fact]
    public async Task Download_RealUrl_ReturnsMedia()
    {
        var ytDlp = FindYtDlp();
        if (ytDlp is null || !await IsReachableAsync(TestVideo))
        {
            return;
        }

        var jobDir = Path.Combine(Path.GetTempPath(), "tgdl-it-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(jobDir);
        try
        {
            var opts = new DownloadOptions(
                TestVideo, jobDir, ytDlp, null, "mp4", false, false, 1_900_000_000, TimeSpan.FromSeconds(120));

            var downloader = new YtDlpDownloader(NullLogger.Instance);
            var media = await downloader.DownloadAsync(opts, null, CancellationToken.None);

            Assert.NotNull(media.FilePath);
            Assert.True(File.Exists(media.FilePath), "下载产物文件应存在");
            Assert.Equal("mp4", media.Extension);
            Assert.True(media.SizeBytes > 0);
            Assert.False(media.IsAudio);
            Assert.Contains(media.Title, media.RawTitle ?? string.Empty, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(jobDir, true);
        }
    }

    [Fact]
    public async Task Download_ExceedingMaxSize_ThrowsTooLarge()
    {
        var ytDlp = FindYtDlp();
        if (ytDlp is null || !await IsReachableAsync(TestVideo))
        {
            return;
        }

        var jobDir = Path.Combine(Path.GetTempPath(), "tgdl-it-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(jobDir);
        try
        {
            var opts = new DownloadOptions(
                TestVideo, jobDir, ytDlp, null, "mp4", false, false, 1024, TimeSpan.FromSeconds(120));

            var downloader = new YtDlpDownloader(NullLogger.Instance);
            var ex = await Assert.ThrowsAsync<DownloadException>(
                () => downloader.DownloadAsync(opts, null, CancellationToken.None));

            Assert.Equal(DownloadFailureReason.TooLarge, ex.Reason);
        }
        finally
        {
            Directory.Delete(jobDir, true);
        }
    }

    private static async Task<bool> IsReachableAsync(string url)
    {
        try
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = true };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch
        {
            return false;
        }
    }

    private static string? FindYtDlp()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(':'))
        {
            var candidate = Path.Combine(dir, "yt-dlp");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
