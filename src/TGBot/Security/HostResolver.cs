using System.Net;

namespace TGBot.Security;

/// <summary>
/// 主机名解析抽象，便于单元测试注入伪造解析结果。
/// </summary>
public interface IHostResolver
{
    /// <summary>
    /// 解析主机名对应的全部 IP 地址。
    /// </summary>
    /// <param name="host">主机名。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>解析出的 IP 地址列表；解析失败时返回空列表。</returns>
    Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken);
}

/// <summary>
/// 基于 <see cref="Dns"/> 的真实 DNS 解析器。
/// </summary>
public sealed class DnsHostResolver : IHostResolver
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
            return addresses;
        }
        catch
        {
            return Array.Empty<IPAddress>();
        }
    }
}

/// <summary>
/// IP 地址策略：判定地址是否属于私网/回环/链路本地等不受信任网段。
/// </summary>
public static class IpAddressPolicy
{
    /// <summary>
    /// 判断地址是否为私网、回环、链路本地或未指定地址（SSRF 防护用）。
    /// </summary>
    /// <param name="address">IP 地址。</param>
    /// <returns>地址受信任时为 <see langword="false"/>。</returns>
    public static bool IsPrivateOrLoopback(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.IsIPv6Multicast)
        {
            return true;
        }

        var bytes = address.GetAddressBytes();
        if (bytes.Length == 4)
        {
            var b0 = bytes[0];
            var b1 = bytes[1];
            if (b0 == 0)
            {
                return true;
            }

            if (b0 == 10)
            {
                return true;
            }

            if (b0 == 127)
            {
                return true;
            }

            if (b0 == 169 && b1 == 254)
            {
                return true;
            }

            if (b0 == 172 && b1 is >= 16 and <= 31)
            {
                return true;
            }

            if (b0 == 192 && b1 == 168)
            {
                return true;
            }

            if (b0 == 100 && b1 is >= 64 and <= 127)
            {
                return true;
            }

            return false;
        }

        if (bytes.Length == 16)
        {
            var allZero = bytes.All(b => b == 0);
            if (allZero)
            {
                return true;
            }

            var isLoopback = bytes[^1] == 1 && bytes.Take(bytes.Length - 1).All(b => b == 0);
            if (isLoopback)
            {
                return true;
            }

            if (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80)
            {
                return true;
            }

            if ((bytes[0] & 0xfe) == 0xfc)
            {
                return true;
            }

            if (bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0 && bytes[3] == 0 &&
                bytes[4] == 0 && bytes[5] == 0 && bytes[6] == 0 && bytes[7] == 0 &&
                bytes[8] == 0 && bytes[9] == 0 && bytes[10] == 0xff && bytes[11] == 0xff)
            {
                return true;
            }

            return false;
        }

        return true;
    }
}
