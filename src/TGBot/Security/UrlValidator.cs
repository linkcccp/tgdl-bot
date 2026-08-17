// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

using System.Net;
using System.Text.RegularExpressions;
using TGBot.Texts;

namespace TGBot.Security;

/// <summary>
/// URL 校验结果。
/// </summary>
/// <param name="IsValid">是否通过校验。</param>
/// <param name="NormalizedUrl">规范化后的 URL（仅校验通过时有意义）。</param>
/// <param name="ErrorKey">面向用户的错误**资源键**（见 <c>Texts/I18n/Resources</c>，由调用方按消息语言渲染；校验失败时非空）。</param>
public sealed record UrlValidationResult(bool IsValid, string NormalizedUrl, string ErrorKey)
{
    /// <summary>
    /// 校验失败的结果。
    /// </summary>
    /// <param name="errorKey">错误资源键。</param>
    public static UrlValidationResult Fail(string errorKey) => new(false, string.Empty, errorKey);
}

/// <summary>
/// 零信任 URL 校验器。
/// <para>校验规则：仅 http/https、无内嵌凭据、长度受限、主机可解析且非私网/回环地址（SSRF 防护）。</para>
/// </summary>
public sealed class UrlValidator
{
    private const int MaxUrlLength = 2048;

    private static readonly Regex UrlRegex = new(@"https?://[^\s<>'""]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly char[] TrailingPunctuation = { '.', ',', ';', ':', '!', '?', ')', ']', '}', '>', '。', '，', '；', '：', '！', '？', '、', '」', '』', '》', '）', '〉', '」' };

    private readonly IHostResolver _resolver;

    /// <summary>
    /// 初始化 <see cref="UrlValidator"/>。
    /// </summary>
    /// <param name="resolver">主机名解析器。</param>
    public UrlValidator(IHostResolver resolver)
    {
        _resolver = resolver;
    }

    /// <summary>
    /// 从消息文本中提取所有候选 URL（http/https），并去除尾部标点。
    /// </summary>
    /// <param name="text">消息文本。</param>
    /// <returns>候选 URL 列表。</returns>
    public static IReadOnlyList<string> ExtractCandidates(string text)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text) || text.Length > 4096)
        {
            return result;
        }

        foreach (Match match in UrlRegex.Matches(text))
        {
            result.Add(StripTrailing(match.Value));
        }

        return result;
    }

    /// <summary>
    /// 校验单个 URL。
    /// </summary>
    /// <param name="raw">原始 URL 字符串。</param>
    /// <param name="allowPrivateUrls">是否允许私网/回环地址（SSRF 防护，默认关闭）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>校验结果。</returns>
    public async Task<UrlValidationResult> ValidateAsync(string raw, bool allowPrivateUrls, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return UrlValidationResult.Fail(UserTexts.UrlEmpty);
        }

        if (raw.Length > MaxUrlLength)
        {
            return UrlValidationResult.Fail(UserTexts.UrlTooLong);
        }

        if (raw.Any(c => char.IsControl(c)))
        {
            return UrlValidationResult.Fail(UserTexts.UrlInvalidChar);
        }

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        {
            return UrlValidationResult.Fail(UserTexts.UrlInvalidFormat);
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return UrlValidationResult.Fail(UserTexts.UrlSchemeNotAllowed);
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return UrlValidationResult.Fail(UserTexts.UrlUserInfo);
        }

        if (string.IsNullOrEmpty(uri.Host) || uri.Host.Length > 253)
        {
            return UrlValidationResult.Fail(UserTexts.UrlInvalidHost);
        }

        if (uri.Host.Any(c => c is '\\' or '/' || char.IsWhiteSpace(c) || char.IsControl(c)))
        {
            return UrlValidationResult.Fail(UserTexts.UrlInvalidHostChar);
        }

        if (!allowPrivateUrls)
        {
            var blocked = await IsHostBlockedAsync(uri.Host, cancellationToken).ConfigureAwait(false);
            if (blocked)
            {
                return UrlValidationResult.Fail(UserTexts.UntrustedUrl);
            }
        }

        var normalized = BuildNormalized(uri);
        return new UrlValidationResult(true, normalized, string.Empty);
    }

    private async Task<bool> IsHostBlockedAsync(string host, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var literal))
        {
            return IpAddressPolicy.IsPrivateOrLoopback(literal);
        }

        var addresses = await _resolver.ResolveAsync(host, cancellationToken).ConfigureAwait(false);
        if (addresses.Count == 0)
        {
            return true;
        }

        return addresses.Any(IpAddressPolicy.IsPrivateOrLoopback);
    }

    private static string BuildNormalized(Uri uri)
    {
        var host = uri.Host.ToLowerInvariant();
        var builder = new UriBuilder(uri.Scheme.ToLowerInvariant(), host)
        {
            Path = uri.AbsolutePath,
            Query = uri.Query,
        };
        return builder.Uri.ToString();
    }

    private static string StripTrailing(string url)
    {
        var end = url.Length;
        while (end > 0 && TrailingPunctuation.Contains(url[end - 1]))
        {
            end--;
        }

        return end == 0 ? url : url[..end];
    }
}
