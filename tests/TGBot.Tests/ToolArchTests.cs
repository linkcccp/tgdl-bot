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
    [InlineData(Architecture.X64, "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-linux64-gpl.tar.xz")]
    [InlineData(Architecture.Arm64, "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-linuxarm64-gpl.tar.xz")]
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
            // stub 返回空内容：xz 魔数校验（下载后、解压前）先失败；本测试只验证架构注入后请求的 URL 是 arm64 资产。
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => source.DownloadBinaryAsync(tmp, CancellationToken.None));
            Assert.Contains("非 xz", ex.Message);
            Assert.Equal("https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-linuxarm64-gpl.tar.xz", handler.LastRequestUrl);
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    [Fact]
    public async Task FfmpegToolSource_Download_ValidXzMagic_ProceedsToExtract()
    {
        using var handler = new StubHttpHandler([0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00, 0x01, 0x02]);
        using var http = new HttpClient(handler);
        var source = new FfmpegToolSource(http, new FailingProcessRunner(), () => Architecture.X64);

        var tmp = Path.Combine(Path.GetTempPath(), "tgdl-arch-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tmp);
        try
        {
            // 魔数合法 → 走到解压步骤，stub runner 解压必然失败。
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => source.DownloadBinaryAsync(tmp, CancellationToken.None));
            Assert.Contains("解压", ex.Message);
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    [Fact]
    public async Task FfmpegToolSource_Download_NonXzMagic_ThrowsBeforeExtract()
    {
        using var handler = new StubHttpHandler("not-an-xz-archive"u8.ToArray());
        using var http = new HttpClient(handler);
        var source = new FfmpegToolSource(http, new FailingProcessRunner(), () => Architecture.X64);

        var tmp = Path.Combine(Path.GetTempPath(), "tgdl-arch-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tmp);
        try
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => source.DownloadBinaryAsync(tmp, CancellationToken.None));
            Assert.Contains("非 xz", ex.Message);
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    [Fact]
    public async Task FfmpegToolSource_Download_ShortTimeout_CancelsMidDownload()
    {
        // 注入 100ms 短超时 + 挂起响应流（模拟慢链路数据不到达）：
        // 验证下载步骤独立超时（链接令牌 CancelAfter）生效并抛出取消异常，且不留残留文件。
        using var handler = new HangingHttpHandler();
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(1) };
        var source = new FfmpegToolSource(http, new FailingProcessRunner(), () => Architecture.X64, TimeSpan.FromMilliseconds(100));

        var tmp = Path.Combine(Path.GetTempPath(), "tgdl-arch-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tmp);
        try
        {
            var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.DownloadBinaryAsync(tmp, CancellationToken.None));
            Assert.True(ex.CancellationToken.IsCancellationRequested);
            // 超时中断后归档临时文件被清理（finally 删除）。
            Assert.Empty(Directory.GetFiles(tmp));
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    /// <summary>
    /// 记录最后请求 URL 并返回指定内容的 HttpMessageHandler 桩。
    /// </summary>
    private sealed class StubHttpHandler : HttpMessageHandler
    {
        private readonly byte[] _content;

        /// <summary>
        /// 初始化 <see cref="StubHttpHandler"/>。
        /// </summary>
        /// <param name="content">响应体内容（默认空）。</param>
        public StubHttpHandler(byte[]? content = null)
        {
            _content = content ?? [];
        }

        /// <summary>
        /// 最近一次请求的完整 URL。
        /// </summary>
        public string? LastRequestUrl { get; private set; }

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUrl = request.RequestUri?.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_content) });
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

    /// <summary>
    /// 响应体为挂起流的 HttpMessageHandler 桩（模拟慢链路：数据永不到达，直到取消令牌触发）。
    /// </summary>
    private sealed class HangingHttpHandler : HttpMessageHandler
    {
        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new HangingStream()) });
        }
    }

    /// <summary>
    /// 读取时挂起直到取消令牌触发的流。
    /// </summary>
    private sealed class HangingStream : Stream
    {
        /// <inheritdoc />
        public override bool CanRead => true;

        /// <inheritdoc />
        public override bool CanSeek => false;

        /// <inheritdoc />
        public override bool CanWrite => false;

        /// <inheritdoc />
        public override long Length => throw new NotSupportedException();

        /// <inheritdoc />
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override void Flush()
        {
        }

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        /// <inheritdoc />
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        /// <inheritdoc />
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return 0;
        }
    }
}
