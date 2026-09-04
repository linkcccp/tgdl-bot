// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

using System.Net;
using TGBot.Security;
using Xunit;

namespace TGBot.Tests;

/// <summary>
/// <see cref="IpAddressPolicy"/> 单元测试。
/// </summary>
public class IpAddressPolicyTests
{
    [Theory]
    [InlineData("10.0.0.1", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("169.254.1.1", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.31.255.255", true)]
    [InlineData("172.32.0.1", false)]
    [InlineData("192.168.1.1", true)]
    [InlineData("100.64.0.1", true)]
    [InlineData("0.0.0.0", true)]
    [InlineData("8.8.8.8", false)]
    [InlineData("1.1.1.1", false)]
    [InlineData("93.184.216.34", false)]
    public void IsPrivateOrLoopback_Ipv4(string ip, bool expected)
    {
        Assert.Equal(expected, IpAddressPolicy.IsPrivateOrLoopback(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("::1", true)]
    [InlineData("::", true)]
    [InlineData("fc00::1", true)]
    [InlineData("fdff::1", true)]
    [InlineData("fe80::1", true)]
    [InlineData("2001:4860:4860::8888", false)]
    public void IsPrivateOrLoopback_Ipv6(string ip, bool expected)
    {
        Assert.Equal(expected, IpAddressPolicy.IsPrivateOrLoopback(IPAddress.Parse(ip)));
    }

    [Fact]
    public void IsPrivateOrLoopback_Ipv4MappedToIpv6_DetectsPrivate()
    {
        var mapped = IPAddress.Parse("::ffff:192.168.1.1");
        Assert.True(IpAddressPolicy.IsPrivateOrLoopback(mapped));
    }
}

/// <summary>
/// <see cref="UrlValidator"/> 单元测试（使用伪造 DNS 解析器）。
/// </summary>
public class UrlValidatorTests
{
    private sealed class FakeResolver : IHostResolver
    {
        private readonly Dictionary<string, string> _map;

        public FakeResolver(params string[] hosts)
        {
            _map = hosts.ToDictionary(h => h, h => "93.184.216.34", StringComparer.OrdinalIgnoreCase);
        }

        public void Add(string host, string ip) => _map[host] = ip;

        public Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken)
        {
            var list = new List<IPAddress>();
            if (_map.TryGetValue(host, out var ip))
            {
                list.Add(IPAddress.Parse(ip));
            }

            return Task.FromResult<IReadOnlyList<IPAddress>>(list);
        }
    }

    private static UrlValidator Create(params string[] hosts) => new(new FakeResolver(hosts));

    [Fact]
    public async Task ValidateAsync_ValidHttpsUrl_Ok()
    {
        var result = await Create("example.com").ValidateAsync("https://example.com/watch?v=1&t=2", false, CancellationToken.None);
        Assert.True(result.IsValid);
        Assert.StartsWith("https://example.com", result.NormalizedUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateAsync_HttpUrl_Ok()
    {
        var result = await Create("example.com").ValidateAsync("http://example.com/video.mp4", false, CancellationToken.None);
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("ftp://example.com/file")]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    [InlineData("not a url")]
    public async Task ValidateAsync_UnsupportedScheme_Fails(string url)
    {
        var result = await Create("example.com").ValidateAsync(url, false, CancellationToken.None);
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.ErrorKey);
    }

    [Theory]
    [InlineData("https://user:pass@example.com/")]
    [InlineData("https://user@example.com/")]
    public async Task ValidateAsync_EmbeddedCredentials_Fails(string url)
    {
        var result = await Create("example.com").ValidateAsync(url, false, CancellationToken.None);
        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("https://127.0.0.1/admin")]
    [InlineData("https://10.0.0.5/x")]
    [InlineData("https://192.168.1.1/y")]
    [InlineData("http://[::1]/x")]
    public async Task ValidateAsync_PrivateIpLiteral_FailsByDefault(string url)
    {
        var result = await Create().ValidateAsync(url, false, CancellationToken.None);
        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("https://127.0.0.1/admin")]
    [InlineData("https://10.0.0.5/x")]
    public async Task ValidateAsync_PrivateIpLiteral_AllowedWhenConfigured(string url)
    {
        var result = await Create().ValidateAsync(url, true, CancellationToken.None);
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_PublicHost_Ok()
    {
        var result = await Create("example.com").ValidateAsync("https://example.com/v", false, CancellationToken.None);
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_UnknownHost_FailsClosed()
    {
        var result = await Create("known.example.com").ValidateAsync("https://unknown.example.com/v", false, CancellationToken.None);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_PrivateResolvingHost_Fails()
    {
        var fake = new FakeResolver("internal.example.com");
        fake.Add("internal.example.com", "10.0.0.5");
        var validator = new UrlValidator(fake);
        var result = await validator.ValidateAsync("https://internal.example.com/v", false, CancellationToken.None);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_PublicResolvingHost_Ok()
    {
        var fake = new FakeResolver("public.example.com");
        fake.Add("public.example.com", "93.184.216.34");
        var validator = new UrlValidator(fake);
        var result = await validator.ValidateAsync("https://public.example.com/v", false, CancellationToken.None);
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_TooLongUrl_Fails()
    {
        var url = "https://example.com/" + new string('a', 2500);
        var result = await Create("example.com").ValidateAsync(url, false, CancellationToken.None);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_ControlChars_Fails()
    {
        var result = await Create("example.com").ValidateAsync("https://example.com/\n/path", false, CancellationToken.None);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void ExtractCandidates_ExtractsAndStripsPunctuation()
    {
        var text = "看这个 https://example.com/video.mp4 很棒。还有 http://x.io/a.html) 和 https://y.io/b.html";
        var candidates = UrlValidator.ExtractCandidates(text);
        Assert.Equal(3, candidates.Count);
        Assert.Equal("https://example.com/video.mp4", candidates[0]);
        Assert.Equal("http://x.io/a.html", candidates[1]);
        Assert.Equal("https://y.io/b.html", candidates[2]);
    }

    [Fact]
    public void ExtractCandidates_NoUrl_ReturnsEmpty()
    {
        Assert.Empty(UrlValidator.ExtractCandidates("没有任何链接的普通文本"));
    }

    [Fact]
    public void ExtractCandidates_TooLongText_ReturnsEmpty()
    {
        Assert.Empty(UrlValidator.ExtractCandidates(new string('a', 5000)));
    }

    [Fact]
    public void SanitizeFileName_RemovesPathTraversal()
    {
        Assert.DoesNotContain('/', PathSanitizer.SanitizeFileName("../../etc/passwd"));
        Assert.DoesNotContain('\\', PathSanitizer.SanitizeFileName("..\\..\\x"));
    }

    [Fact]
    public void SanitizeFileName_Empty_ReturnsUntitled()
    {
        Assert.Equal("untitled", PathSanitizer.SanitizeFileName(""));
        Assert.Equal("untitled", PathSanitizer.SanitizeFileName(null));
        Assert.Equal("untitled", PathSanitizer.SanitizeFileName("   "));
    }

    [Fact]
    public void SanitizeFileName_RemovesLeadingDotsAndControls()
    {
        Assert.Equal("normal", PathSanitizer.SanitizeFileName("..normal"));
        Assert.DoesNotContain('\0', PathSanitizer.SanitizeFileName("a\0b"));
    }

    [Fact]
    public void SanitizeFileName_CapsLength()
    {
        var result = PathSanitizer.SanitizeFileName(new string('a', 500), 50);
        Assert.Equal(50, result.Length);
    }

    [Fact]
    public void SanitizeFileName_Unicode_Kept()
    {
        Assert.Equal("中文标题", PathSanitizer.SanitizeFileName("中文标题"));
    }

    [Fact]
    public void IsWithinDirectory_RejectsTraversal()
    {
        Assert.True(PathSanitizer.IsWithinDirectory("/tmp/root", "/tmp/root/job/file"));
        Assert.True(PathSanitizer.IsWithinDirectory("/tmp/root", "/tmp/root"));
        Assert.False(PathSanitizer.IsWithinDirectory("/tmp/root", "/tmp/rooted"));
        Assert.False(PathSanitizer.IsWithinDirectory("/tmp/root", "/etc/passwd"));
        Assert.False(PathSanitizer.IsWithinDirectory("/tmp/root", "/tmp/root/../outside"));
    }
}
