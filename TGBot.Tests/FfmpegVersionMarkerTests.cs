// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

using TGBot.Update;
using Xunit;

namespace TGBot.Tests;

/// <summary>
/// <see cref="FfmpegVersionMarker"/> 单元测试。
/// </summary>
public class FfmpegVersionMarkerTests
{
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tgdl-mk-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void WriteThenRead_RoundTrips()
    {
        var dir = NewTempDir();
        try
        {
            var installPath = Path.Combine(dir, "ffmpeg");
            var version = ToolVersion.Parse("2026.08.17.13.29.26");

            FfmpegVersionMarker.Write(installPath, version);

            Assert.True(FfmpegVersionMarker.TryRead(installPath, out var read));
            Assert.Equal(0, read!.CompareTo(version));
            Assert.True(File.Exists(FfmpegVersionMarker.MarkerPath(installPath)));
            // 临时文件已被原子移动，不留残留。
            Assert.False(File.Exists(FfmpegVersionMarker.MarkerPath(installPath) + ".tmp"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void TryRead_Missing_ReturnsFalse()
    {
        var dir = NewTempDir();
        try
        {
            var installPath = Path.Combine(dir, "ffmpeg");
            Assert.False(FfmpegVersionMarker.TryRead(installPath, out _));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void TryRead_CorruptedContent_ReturnsFalse()
    {
        var dir = NewTempDir();
        try
        {
            var installPath = Path.Combine(dir, "ffmpeg");
            File.WriteAllText(FfmpegVersionMarker.MarkerPath(installPath), "not-a-version");

            Assert.False(FfmpegVersionMarker.TryRead(installPath, out _));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void WriteThenRead_PathWithSpaces_Works()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tgdl mk dir " + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var installPath = Path.Combine(dir, "my ffmpeg");
            var version = ToolVersion.Parse("2026.08.17.13.29.26");

            FfmpegVersionMarker.Write(installPath, version);

            Assert.True(FfmpegVersionMarker.TryRead(installPath, out var read));
            Assert.Equal(0, read!.CompareTo(version));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Write_OverwritesPreviousValue()
    {
        var dir = NewTempDir();
        try
        {
            var installPath = Path.Combine(dir, "ffmpeg");

            FfmpegVersionMarker.Write(installPath, ToolVersion.Parse("2026.08.17.10.00.00"));
            FfmpegVersionMarker.Write(installPath, ToolVersion.Parse("2026.08.17.13.29.26"));

            Assert.True(FfmpegVersionMarker.TryRead(installPath, out var read));
            Assert.Equal(0, read!.CompareTo(ToolVersion.Parse("2026.08.17.13.29.26")));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
