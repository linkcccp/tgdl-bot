// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

using TGBot.Update;
using Xunit;

namespace TGBot.Tests;

/// <summary>
/// <see cref="ToolVersion"/> 单元测试。
/// </summary>
public class ToolVersionTests
{
    [Theory]
    [InlineData("2025.01.26", "2025.01.26", 0)]
    [InlineData("2025.01.26", "2025.01.25", 1)]
    [InlineData("2024.12.31", "2025.01.01", -1)]
    [InlineData("7.1.1", "7.1.0", 1)]
    [InlineData("7.1", "7.1.0", 0)]
    [InlineData("n9.0.1", "9.0.1", 0)]
    [InlineData("7.1.1-1", "7.1.1", 0)]
    [InlineData("2025.1.2", "2025.01.26", -1)]
    public void CompareTo_AsExpected(string a, string b, int expected)
    {
        Assert.True(ToolVersion.TryParse(a, out var va));
        Assert.True(ToolVersion.TryParse(b, out var vb));
        Assert.Equal(expected, Math.Sign(va.CompareTo(vb)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData(null)]
    [InlineData("   ")]
    public void TryParse_Invalid_ReturnsFalse(string? raw)
    {
        Assert.False(ToolVersion.TryParse(raw, out _));
    }

    [Fact]
    public void TryParse_ExtractsNumericFromText()
    {
        Assert.True(ToolVersion.TryParse("ffmpeg version n7.1.1 Copyright", out var v));
        Assert.True(v.CompareTo(ToolVersion.Parse("7.0")) > 0);
        Assert.Equal(0, v.CompareTo(ToolVersion.Parse("7.1.1")));
    }

    [Theory]
    [InlineData("2026.08.17.13.29.26", true)]
    [InlineData("2025.01.26", true)]
    [InlineData("2000.1.1", true)]
    [InlineData("2100.1.1", true)]
    [InlineData("7.1.1", false)]
    [InlineData("118503", false)]
    [InlineData("1999.12.31", false)]
    [InlineData("2101.1.1", false)]
    public void IsDateLike_YearInRange2000To2100(string raw, bool expected)
    {
        Assert.True(ToolVersion.TryParse(raw, out var v));
        Assert.Equal(expected, v!.IsDateLike);
    }
}

/// <summary>
/// <see cref="BinaryVersionParser"/> 单元测试。
/// </summary>
public class BinaryVersionParserTests
{
    [Fact]
    public void ParseYtDlp_Standard()
    {
        var v = BinaryVersionParser.ParseYtDlp("2025.01.26\n");
        Assert.NotNull(v);
        Assert.Equal(0, v!.CompareTo(ToolVersion.Parse("2025.01.26")));
    }

    [Fact]
    public void ParseFfmpeg_Standard()
    {
        var v = BinaryVersionParser.ParseFfmpeg("ffmpeg version 7.0.2 Copyright (c) 2000-2024 the FFmpeg developers");
        Assert.NotNull(v);
        Assert.Equal(0, v!.CompareTo(ToolVersion.Parse("7.0.2")));
    }

    [Fact]
    public void ParseFfmpeg_WithNPrefix()
    {
        var v = BinaryVersionParser.ParseFfmpeg("ffmpeg version n9.0.1 Copyright (c) 2000-2026");
        Assert.NotNull(v);
        Assert.Equal(0, v!.CompareTo(ToolVersion.Parse("9.0.1")));
    }

    [Fact]
    public void ParseFfmpeg_Garbage_Null()
    {
        Assert.Null(BinaryVersionParser.ParseFfmpeg("no version here"));
    }
}

/// <summary>
/// <see cref="UriVersionParser"/> 单元测试。
/// </summary>
public class UriVersionParserTests
{
    [Fact]
    public void ParseGitHubRedirectLocation_Standard()
    {
        var v = UriVersionParser.ParseGitHubRedirectLocation(
            "https://github.com/yt-dlp/yt-dlp/releases/download/2026.07.04/yt-dlp");
        Assert.NotNull(v);
        Assert.Equal(0, v!.CompareTo(ToolVersion.Parse("2026.07.04")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://example.com/no-release")]
    public void ParseGitHubRedirectLocation_Invalid_Null(string? loc)
    {
        Assert.Null(UriVersionParser.ParseGitHubRedirectLocation(loc));
    }

    [Fact]
    public void ParseGitHubApiPublishedAt_Standard()
    {
        var v = UriVersionParser.ParseGitHubApiPublishedAt(
            "{\"tag_name\":\"latest\",\"published_at\":\"2026-08-17T13:29:26Z\",\"name\":\"Latest Auto-Build (2026-08-17 13:05)\"}");
        Assert.NotNull(v);
        Assert.Equal(0, v!.CompareTo(ToolVersion.Parse("2026.08.17.13.29.26")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("{\"tag_name\":\"latest\"}")]
    [InlineData("{\"published_at\":\"not-a-date\"}")]
    [InlineData("{\"published_at\":\"2026-08-17T13:29\"}")]
    [InlineData("{\"published_at\":\"2026-08-17 13:29:26Z\"}")]
    [InlineData("{\"published_at\":\"26-08-17T13:29:26Z\"}")]
    public void ParseGitHubApiPublishedAt_Invalid_Null(string? json)
    {
        Assert.Null(UriVersionParser.ParseGitHubApiPublishedAt(json));
    }
}

/// <summary>
/// <see cref="AtomicFileReplacer"/> 单元测试。
/// </summary>
public class AtomicFileReplacerTests
{
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tgdl-at-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Replace_NewFile_Succeeds()
    {
        var dir = NewTempDir();
        try
        {
            var target = Path.Combine(dir, "tool");
            var newFile = Path.Combine(dir, "new");
            File.WriteAllText(newFile, "binary-data");

            AtomicFileReplacer.Replace(target, newFile);

            Assert.True(File.Exists(target));
            Assert.Equal("binary-data", File.ReadAllText(target));
            Assert.False(File.Exists(newFile));
            Assert.False(File.Exists(target + ".old"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Replace_ExistingFile_BacksUpThenReplaces()
    {
        var dir = NewTempDir();
        try
        {
            var target = Path.Combine(dir, "tool");
            var newFile = Path.Combine(dir, "new");
            File.WriteAllText(target, "old");
            File.WriteAllText(newFile, "new");

            AtomicFileReplacer.Replace(target, newFile);

            Assert.Equal("new", File.ReadAllText(target));
            Assert.False(File.Exists(target + ".old"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Sha256_Stable()
    {
        var dir = NewTempDir();
        try
        {
            var file = Path.Combine(dir, "f");
            File.WriteAllText(file, "hello");
            Assert.Equal(AtomicFileReplacer.Sha256(file), AtomicFileReplacer.Sha256(file));
            Assert.Equal(64, AtomicFileReplacer.Sha256(file).Length);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}

/// <summary>
/// <see cref="Updater"/> 标度一致短路行为测试（marker 日期 vs 远端日期）。
/// </summary>
public class UpdaterShortCircuitTests
{
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tgdl-us-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public async Task UpdateFfmpeg_MarkerNewerThanRemote_ShortCircuitsWithoutRunner()
    {
        var dir = NewTempDir();
        try
        {
            var ffmpegPath = Path.Combine(dir, "ffmpeg");
            FfmpegVersionMarker.Write(ffmpegPath, ToolVersion.Parse("2026.08.17.13.29.26"));

            var source = new StubToolSource("ffmpeg") { Latest = ToolVersion.Parse("2026.08.17.12.00.00") };
            var runner = new StubRunner();
            var updater = new Updater(runner, new[] { source }, null, ffmpegPath);

            var report = await updater.UpdateAsync(includeYtDlp: false, includeFfmpeg: true, null, CancellationToken.None);

            var result = Assert.Single(report.Tools);
            Assert.Equal(ToolUpdateStatus.AlreadyUpToDate, result.Status);
            Assert.Equal(0, source.DownloadCount);
            // marker 命中时不做二进制版本探测。
            Assert.Equal(0, runner.CallCount);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task UpdateFfmpeg_MarkerOlderThanRemote_DownloadsAndWritesNewMarker()
    {
        var dir = NewTempDir();
        try
        {
            var ffmpegPath = Path.Combine(dir, "ffmpeg");
            FfmpegVersionMarker.Write(ffmpegPath, ToolVersion.Parse("2026.08.17.10.00.00"));
            var latest = ToolVersion.Parse("2026.08.17.13.29.26");

            var source = new StubToolSource("ffmpeg") { Latest = latest };
            var runner = new StubRunner();
            var updater = new Updater(runner, new[] { source }, null, ffmpegPath);

            var report = await updater.UpdateAsync(includeYtDlp: false, includeFfmpeg: true, null, CancellationToken.None);

            var result = Assert.Single(report.Tools);
            Assert.Equal(ToolUpdateStatus.Updated, result.Status);
            Assert.Equal(1, source.DownloadCount);
            Assert.True(FfmpegVersionMarker.TryRead(ffmpegPath, out var marked));
            Assert.Equal(0, marked!.CompareTo(latest));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task UpdateFfmpeg_NoMarkerGitCountLocal_NotShortCircuited()
    {
        var dir = NewTempDir();
        try
        {
            // 无 marker：本地二进制解析出 git 提交计数 [118503]（非日期标度），
            // 数值上 118503 > 2026 但标度不一致，不得短路，必须更新。
            var ffmpegPath = Path.Combine(dir, "ffmpeg");
            var source = new StubToolSource("ffmpeg") { Latest = ToolVersion.Parse("2026.08.17.13.29.26") };
            var runner = new StubRunner("ffmpeg version N-118503-g2b46d3311f");
            var updater = new Updater(runner, new[] { source }, null, ffmpegPath);

            var report = await updater.UpdateAsync(includeYtDlp: false, includeFfmpeg: true, null, CancellationToken.None);

            var result = Assert.Single(report.Tools);
            Assert.Equal(ToolUpdateStatus.Updated, result.Status);
            Assert.Equal(1, source.DownloadCount);
            // 更新成功后写入 marker，下次 /update 可同标度短路。
            Assert.True(FfmpegVersionMarker.TryRead(ffmpegPath, out _));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>
    /// 可配置最新版本与下载计数的工具源桩。
    /// </summary>
    private sealed class StubToolSource : IToolSource
    {
        /// <summary>
        /// 初始化 <see cref="StubToolSource"/>。
        /// </summary>
        /// <param name="name">工具名称。</param>
        public StubToolSource(string name)
        {
            Name = name;
        }

        /// <inheritdoc />
        public string Name { get; }

        /// <summary>
        /// 最新版本（模拟远端版本发现结果）。
        /// </summary>
        public ToolVersion? Latest { get; set; }

        /// <summary>
        /// 下载被调用的次数。
        /// </summary>
        public int DownloadCount { get; private set; }

        /// <inheritdoc />
        public Task<ToolVersion?> GetLatestVersionAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(Latest);
        }

        /// <inheritdoc />
        public async Task<string> DownloadBinaryAsync(string destinationDir, CancellationToken cancellationToken)
        {
            DownloadCount++;
            var path = Path.Combine(destinationDir, "stub-binary");
            await File.WriteAllTextAsync(path, "stub", cancellationToken);
            return path;
        }
    }

    /// <summary>
    /// 版本命令输出可配置的进程运行器桩。
    /// </summary>
    private sealed class StubRunner : IProcessRunner
    {
        /// <summary>
        /// 初始化 <see cref="StubRunner"/>。
        /// </summary>
        /// <param name="versionOutput">版本命令输出（默认 BtbN master git 计数格式）。</param>
        public StubRunner(string? versionOutput = null)
        {
            VersionOutput = versionOutput ?? "ffmpeg version N-118503-g2b46d3311f";
        }

        /// <summary>
        /// 版本命令输出。
        /// </summary>
        public string VersionOutput { get; }

        /// <summary>
        /// 被调用的次数。
        /// </summary>
        public int CallCount { get; private set; }

        /// <inheritdoc />
        public Task<ProcessOutput> RunAsync(string file, IReadOnlyList<string> args, string? workingDir, TimeSpan timeout, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new ProcessOutput(0, VersionOutput, string.Empty));
        }
    }
}
