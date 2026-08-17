// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

using System.Collections.Concurrent;
using TGBot.Logging;
using TGBot.Messaging;
using TGBot.Texts;
using TGBot.Texts.I18n;

namespace TGBot.Cookie;

/// <summary>
/// cookie 上传结果。
/// </summary>
/// <param name="Success">是否成功。</param>
/// <param name="Message">面向用户的提示（已按消息语言渲染）。</param>
public sealed record CookieUploadResult(bool Success, string Message);

/// <summary>
/// 站点 cookie 状态（用于列表展示）。
/// </summary>
/// <param name="Key">站点键。</param>
/// <param name="DisplayName">显示名。</param>
/// <param name="Has">是否已保存 cookie。</param>
public sealed record CookieStatus(string Key, string DisplayName, bool Has);

/// <summary>
/// cookies 服务：待上传状态管理、从 Telegram 下载并校验落盘、按 URL 域名解析 cookie 文件。
/// </summary>
public sealed class CookieService
{
    private static readonly TimeSpan PendingTimeout = TimeSpan.FromMinutes(5);
    private const long MaxCookieBytes = 1_000_000;

    private readonly SiteCookieRegistry _registry;
    private readonly CookieStore _store;
    private readonly ITelegramClient _client;
    private readonly II18n _i18n;
    private readonly IAppLogger _logger;
    private readonly ConcurrentDictionary<long, PendingCookie> _pending = new();

    private sealed record PendingCookie(string SiteKey, DateTime Expiry);

    /// <summary>
    /// 初始化 <see cref="CookieService"/>。
    /// </summary>
    /// <param name="registry">站点注册表。</param>
    /// <param name="store">cookie 存储。</param>
    /// <param name="client">Telegram 客户端（用于下载上传的文件）。</param>
    /// <param name="logger">日志器。</param>
    /// <param name="i18n">国际化服务（结果消息渲染）。</param>
    public CookieService(SiteCookieRegistry registry, CookieStore store, ITelegramClient client, IAppLogger logger, II18n i18n)
    {
        _registry = registry;
        _store = store;
        _client = client;
        _i18n = i18n;
        _logger = logger;
    }

    /// <summary>
    /// 全部已注册站点。
    /// </summary>
    public IReadOnlyList<CookieSite> Sites => _registry.Sites;

    /// <summary>
    /// 按站点键解析站点。
    /// </summary>
    /// <param name="key">站点键。</param>
    /// <returns>站点；未知返回 <see langword="null"/>。</returns>
    public CookieSite? ResolveSite(string key) => _registry.ResolveKey(key);

    /// <summary>
    /// 按 URL 解析该域名对应站点已保存的 cookie 文件路径。
    /// </summary>
    /// <param name="url">下载 URL。</param>
    /// <returns>cookie 文件路径；无则返回 <see langword="null"/>。</returns>
    public string? ResolveCookieFile(string url)
    {
        try
        {
            var host = new Uri(url).Host;
            var site = _registry.ResolveHost(host);
            return site is null ? null : _store.GetFile(site.Key);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 开始一次待上传（记录私聊会话的目标站点，带超时）。
    /// </summary>
    /// <param name="chatId">私聊会话 ID。</param>
    /// <param name="siteKey">站点键。</param>
    /// <returns>站点；未知键返回 <see langword="null"/>。</returns>
    public CookieSite? BeginPendingUpload(long chatId, string siteKey)
    {
        var site = _registry.ResolveKey(siteKey);
        if (site is null)
        {
            return null;
        }

        _pending[chatId] = new PendingCookie(site.Key, DateTime.UtcNow + PendingTimeout);
        return site;
    }

    /// <summary>
    /// 消费待上传：收到文件消息时调用，下载、校验并保存为该站点 cookie。
    /// </summary>
    /// <param name="chatId">私聊会话 ID。</param>
    /// <param name="fileId">Telegram 文件 ID。</param>
    /// <param name="sizeBytes">文件大小（可为空）。</param>
    /// <param name="lang">消息语言（结果消息渲染）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>处理结果；该会话无待上传请求时返回 <see langword="null"/>。</returns>
    public async Task<CookieUploadResult?> ConsumePendingAsync(
        long chatId,
        string fileId,
        long? sizeBytes,
        string lang,
        CancellationToken cancellationToken)
    {
        if (!_pending.TryRemove(chatId, out var pending))
        {
            return null;
        }

        if (DateTime.UtcNow > pending.Expiry)
        {
            return new CookieUploadResult(false, _i18n.Get(lang, UserTexts.CookieExpired));
        }

        if (sizeBytes is > MaxCookieBytes)
        {
            return new CookieUploadResult(false, _i18n.Get(lang, UserTexts.CookieFileTooLarge));
        }

        var site = _registry.ResolveKey(pending.SiteKey);
        if (site is null)
        {
            return new CookieUploadResult(false, _i18n.Get(lang, UserTexts.CookieUnknownSite, pending.SiteKey, SiteListText()));
        }

        var tmp = Path.Combine(_store.RootDir, $".upload-{Guid.NewGuid():N}.tmp");
        try
        {
            await _client.DownloadFileAsync(fileId, tmp, cancellationToken).ConfigureAwait(false);
            var suspicious = ValidateFile(tmp);
            if (!_store.Save(site.Key, tmp))
            {
                return new CookieUploadResult(false, _i18n.Get(lang, UserTexts.CookieSaveFailed));
            }

            _logger.Info($"已保存 {site.Key} cookies（{new FileInfo(tmp).Length} 字节）");
            var key = suspicious ? UserTexts.CookieSavedSuspicious : UserTexts.CookieSaved;
            return new CookieUploadResult(true, _i18n.Get(lang, key, site.DisplayName, site.Key));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error("下载 cookies 文件失败", ex);
            return new CookieUploadResult(false, _i18n.Get(lang, UserTexts.CookieInvalidFile));
        }
        finally
        {
            try
            {
                File.Delete(tmp);
            }
            catch
            {
                // ignore
            }
        }
    }

    /// <summary>
    /// 列出各站点 cookie 状态。
    /// </summary>
    /// <returns>状态列表。</returns>
    public IReadOnlyList<CookieStatus> List()
    {
        var have = _store.List();
        return _registry.Sites
            .Select(s => new CookieStatus(s.Key, s.DisplayName, have.Contains(s.Key)))
            .ToList();
    }

    /// <summary>
    /// 删除站点 cookie。
    /// </summary>
    /// <param name="siteKey">站点键。</param>
    /// <returns>站点存在且删除成功返回 <see langword="true"/>。</returns>
    public bool Clear(string siteKey)
    {
        var site = _registry.ResolveKey(siteKey);
        return site is not null && _store.Delete(site.Key);
    }

    /// <summary>
    /// 全部站点键文本（用于提示）。
    /// </summary>
    /// <returns>逗号分隔的站点键。</returns>
    public string SiteListText() => string.Join("、", _registry.Sites.Select(s => s.Key));

    /// <summary>
    /// 宽松校验：非二进制且形似 Netscape 格式则视为正常，否则标记可疑（仍保存）。
    /// </summary>
    /// <param name="path">文件路径。</param>
    /// <returns>可疑返回 <see langword="true"/>。</returns>
    private static bool ValidateFile(string path)
    {
        try
        {
            var head = new byte[512];
            int n;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                n = fs.Read(head, 0, head.Length);
            }

            var nul = 0;
            for (var i = 0; i < n; i++)
            {
                if (head[i] == 0)
                {
                    nul++;
                }
            }

            if (n > 0 && nul > n / 8)
            {
                return true; // 二进制
            }

            var text = System.Text.Encoding.UTF8.GetString(head, 0, n);
            if (text.StartsWith("# Netscape", StringComparison.OrdinalIgnoreCase) || text.Contains('\t'))
            {
                return false;
            }

            return true; // 不像 Netscape 格式
        }
        catch
        {
            return true;
        }
    }
}
