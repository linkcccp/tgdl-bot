// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

using TGBot.Application;
using Xunit;

namespace TGBot.Tests;

/// <summary>
/// <see cref="AppInfo"/> 版本读取单元测试（AssemblyInformationalVersion 规范化）。
/// </summary>
public class AppInfoTests
{
    [Theory]
    [InlineData("2.4.0", "2.4.0")]
    [InlineData("2.4.0+abc123", "2.4.0")]
    [InlineData("2.4.0+abc123+def", "2.4.0")]
    [InlineData("v2.4.0", "2.4.0")]
    [InlineData("V2.4.0", "2.4.0")]
    [InlineData(" 2.4.0 ", "2.4.0")]
    [InlineData("10.0.0-preview1", "10.0.0-preview1")]
    public void Normalize_StripsCommitSuffixAndPrefix(string raw, string expected)
        => Assert.Equal(expected, AppInfo.Normalize(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("+abc123")]
    [InlineData("v")]
    [InlineData("V+abc")]
    public void Normalize_InvalidInput_FallsBack(string? raw)
        => Assert.Equal(AppInfo.FallbackVersion, AppInfo.Normalize(raw));

    [Fact]
    public void Version_IsNormalizedAndNeverContainsCommitSuffix()
    {
        Assert.False(string.IsNullOrWhiteSpace(AppInfo.Version));
        Assert.DoesNotContain('+', AppInfo.Version);
        // 幂等：再次规范化结果不变（版本已稳定）
        Assert.Equal(AppInfo.Version, AppInfo.Normalize(AppInfo.Version));
    }

    [Fact]
    public void Version_MatchesMajorMinorPatchPattern()
    {
        // csproj 默认 2.4.0；CI 从 git tag（vX.Y.Z）注入同形版本号
        Assert.Matches(@"^\d+\.\d+\.\d+$", AppInfo.Version);
    }
}