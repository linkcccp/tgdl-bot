// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

using System.Net;
using System.Runtime.InteropServices;
using TGBot.Update;
using Xunit;

namespace TGBot.Tests;

/// <summary>
/// <see cref="ToolArch"/> 架构感知 URL 选择测试。
/// <para>注意：arm64 分支必须通过参数注入而非真实进程架构覆盖（本机为 x64 时真实进程架构永远走不到 arm64 分支）。</para>
/// </summary>
public class ToolArchTests
{
    [Theory]
    [InlineData(Architecture.X64, "https://johnvansickle.com/ffmpeg/releases/ffmpeg-release-amd64-static.tar.xz")]
    [InlineData(Architecture.Arm64, "https://johnvansickle.com/ffmpeg/releases/ffmpeg-release-arm64-static.tar.xz")]
    public void FfmpegReleaseUrl_MatchesArchAsset(Architecture arch, string expected)
    {
        Assert.Equal(expected, ToolArch.FfmpegReleaseUrl(arch));
    }

    [Theory]
    [InlineData(Architecture.X64, "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp")]
    [InlineData(Architecture.Arm64, "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_linux_aarch64")]
    public void YtDlpReleaseUrl_MatchesArchAsset(Architecture arch, string expected)
    {
        Assert.Equal(expected, ToolArch.YtDlpReleaseUrl(arch));
    }

    [Theory]
    [InlineData(Architecture.Arm)]
    [InlineData(Architecture.X86)]
    [InlineData(Architecture.Wasm)]
    public void FfmpegReleaseUrl_UnsupportedArch_ThrowsWithArchName(Architecture arch)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ToolArch.FfmpegReleaseUrl(arch));
        Assert.Contains(arch.ToString(), ex.Message);
    }

    [Theory]
    [InlineData(Architecture.Arm)]
    [InlineData(Architecture.X86)]
    [InlineData(Architecture.Wasm)]
    public void YtDlpReleaseUrl_UnsupportedArch_ThrowsWithArchName(Architecture arch)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ToolArch.YtDlpReleaseUrl(arch));
        Assert.Contains(arch.ToString(), ex.Message);
    }

    [Fact]
    public async Task YtDlpToolSource_Arm64Injected_DownloadsArm64Asset()
    {
        using var handler = new StubHttpHandler();
        using var http = new HttpClient(handler);
        var source = new YtDlpToolSource(http, () => Architecture.Arm64);

        var tmp = Path.Combine(Path.GetTempPath(), "tgdl-arch-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tmp);
        try
        {
            var binary = await source.DownloadBinaryAsync(tmp, CancellationToken.None);
            Assert.True(File.Exists(binary));
            Assert.Equal("https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_linux_aarch64", handler.LastRequestUrl);
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    [Fact]
    public async Task FfmpegToolSource_Arm64Injected_RequestsArm64Asset()
    {
        using var handler = new StubHttpHandler();
        using var http = new HttpClient(handler);
        var source = new FfmpegToolSource(http, new FailingProcessRunner(), () => Architecture.Arm64);

        var tmp = Path.Combine(Path.GetTempPath(), "tgdl-arch-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tmp);
        try
        {
            // stub runner 解压必然失败；本测试只验证架构注入后请求的 URL 是 arm64 资产，不关心解压结果。
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => source.DownloadBinaryAsync(tmp, CancellationToken.None));
            Assert.Contains("解压", ex.Message);
            Assert.Equal("https://johnvansickle.com/ffmpeg/releases/ffmpeg-release-arm64-static.tar.xz", handler.LastRequestUrl);
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    /// <summary>
    /// 记录最后请求 URL 并返回 200 空内容的 HttpMessageHandler 桩。
    /// </summary>
    private sealed class StubHttpHandler : HttpMessageHandler
    {
        /// <summary>
        /// 最近一次请求的完整 URL。
        /// </summary>
        public string? LastRequestUrl { get; private set; }

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUrl = request.RequestUri?.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) });
        }
    }

    /// <summary>
    /// 总是返回失败退出码的进程运行器桩。
    /// </summary>
    private sealed class FailingProcessRunner : IProcessRunner
    {
        /// <inheritdoc />
        public Task<ProcessOutput> RunAsync(string file, IReadOnlyList<string> args, string? workingDir, TimeSpan timeout, CancellationToken cancellationToken)
        {
            return Task.FromResult(new ProcessOutput(1, string.Empty, "stub 解压失败"));
        }
    }
}
