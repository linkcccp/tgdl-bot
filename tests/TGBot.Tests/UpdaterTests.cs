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
    public void ParseJohnVanSickleReleasePage_Standard()
    {
        var v = UriVersionParser.ParseJohnVanSickleReleasePage("<th>release: 7.0.2</th>");
        Assert.NotNull(v);
        Assert.Equal(0, v!.CompareTo(ToolVersion.Parse("7.0.2")));
    }

    [Fact]
    public void ParseJohnVanSickleReleasePage_Invalid_Null()
    {
        Assert.Null(UriVersionParser.ParseJohnVanSickleReleasePage("<html>no release</html>"));
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
