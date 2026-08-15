namespace TGBot.Cookie;

/// <summary>
/// 站点注册表：把规范化主机名 / 站点键映射到 <see cref="CookieSite"/>。
/// </summary>
public sealed class SiteCookieRegistry
{
    private readonly List<CookieSite> _sites;
    private readonly Dictionary<string, CookieSite> _byHost;
    private readonly Dictionary<string, CookieSite> _byKey;

    /// <summary>
    /// 初始化 <see cref="SiteCookieRegistry"/>。
    /// </summary>
    /// <param name="sites">已注册的站点集合。</param>
    public SiteCookieRegistry(IEnumerable<CookieSite> sites)
    {
        _sites = sites.ToList();
        _byHost = new Dictionary<string, CookieSite>(StringComparer.Ordinal);
        _byKey = new Dictionary<string, CookieSite>(StringComparer.Ordinal);
        foreach (var site in _sites)
        {
            _byKey[site.Key] = site;
            foreach (var host in site.MatchedHosts)
            {
                _byHost[CookieSiteUtil.NormalizeHost(host)] = site;
            }
        }
    }

    /// <summary>
    /// 全部已注册站点。
    /// </summary>
    public IReadOnlyList<CookieSite> Sites => _sites;

    /// <summary>
    /// 按主机名解析站点（自动规范化）。
    /// </summary>
    /// <param name="host">原始主机名。</param>
    /// <returns>匹配的站点；无匹配返回 <see langword="null"/>。</returns>
    public CookieSite? ResolveHost(string host)
        => _byHost.GetValueOrDefault(CookieSiteUtil.NormalizeHost(host));

    /// <summary>
    /// 按站点键解析站点（忽略大小写）。
    /// </summary>
    /// <param name="key">站点键。</param>
    /// <returns>站点；未知键返回 <see langword="null"/>。</returns>
    public CookieSite? ResolveKey(string key)
        => !string.IsNullOrWhiteSpace(key) && _byKey.TryGetValue(key.Trim().ToLowerInvariant(), out var site)
            ? site
            : null;
}
