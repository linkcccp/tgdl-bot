// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

using System.Text.RegularExpressions;
using TGBot.Logging;

namespace TGBot.Cookie;

/// <summary>
/// cookies 持久化存储：<c>&lt;storeDir&gt;/&lt;siteKey&gt;.txt</c>，文件 0600、目录 0700。
/// <para>站点键仅允许 <c>[a-z0-9_-]</c>，杜绝路径穿越；不使用用户提供的文件名。</para>
/// </summary>
public sealed class CookieStore
{
    private static readonly Regex SafeKeyRegex = new("^[a-z0-9_-]+$", RegexOptions.Compiled);

    private readonly string _rootDir;
    private readonly IAppLogger _logger;

    /// <summary>
    /// 初始化 <see cref="CookieStore"/>。
    /// </summary>
    /// <param name="rootDir">存储根目录。</param>
    /// <param name="logger">日志器。</param>
    public CookieStore(string rootDir, IAppLogger logger)
    {
        _rootDir = Path.GetFullPath(rootDir);
        _logger = logger;
    }

    /// <summary>
    /// 存储根目录。
    /// </summary>
    public string RootDir => _rootDir;

    /// <summary>
    /// 创建存储目录并设置 0700 权限。
    /// </summary>
    public void Initialize()
    {
        Directory.CreateDirectory(_rootDir);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(_rootDir, (UnixFileMode)((int)UnixFileMode.UserRead | (int)UnixFileMode.UserWrite | (int)UnixFileMode.UserExecute));
        }
    }

    /// <summary>
    /// 获取站点 cookie 文件路径（文件存在时返回，否则返回 <see langword="null"/>）。
    /// </summary>
    /// <param name="siteKey">站点键。</param>
    /// <returns>文件路径或 <see langword="null"/>。</returns>
    public string? GetFile(string siteKey)
    {
        if (!IsSafeKey(siteKey))
        {
            return null;
        }

        var path = PathFor(siteKey);
        if (!File.Exists(path))
        {
            return null;
        }

        return path;
    }

    /// <summary>
    /// 保存站点 cookie（覆盖写）。
    /// </summary>
    /// <param name="siteKey">站点键。</param>
    /// <param name="sourcePath">来源文件路径。</param>
    /// <returns>保存成功返回 <see langword="true"/>。</returns>
    public bool Save(string siteKey, string sourcePath)
    {
        if (!IsSafeKey(siteKey))
        {
            _logger.Warn($"拒绝非法站点键保存 cookie：{siteKey}");
            return false;
        }

        try
        {
            Initialize();
            var dest = PathFor(siteKey);
            var tmp = dest + ".tmp";
            File.Copy(sourcePath, tmp, overwrite: true);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(tmp, (UnixFileMode)((int)UnixFileMode.UserRead | (int)UnixFileMode.UserWrite));
            }

            File.Move(tmp, dest, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"保存 cookies 失败（{siteKey}）", ex);
            return false;
        }
    }

    /// <summary>
    /// 删除站点 cookie。
    /// </summary>
    /// <param name="siteKey">站点键。</param>
    /// <returns>删除成功返回 <see langword="true"/>。</returns>
    public bool Delete(string siteKey)
    {
        if (!IsSafeKey(siteKey))
        {
            return false;
        }

        try
        {
            var path = PathFor(siteKey);
            if (File.Exists(path))
            {
                File.Delete(path);
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"删除 cookies 失败（{siteKey}）", ex);
        }

        return false;
    }

    /// <summary>
    /// 列出已保存的站点键。
    /// </summary>
    /// <returns>站点键列表。</returns>
    public IReadOnlyList<string> List()
    {
        var result = new List<string>();
        if (!Directory.Exists(_rootDir))
        {
            return result;
        }

        foreach (var f in Directory.EnumerateFiles(_rootDir, "*.txt"))
        {
            result.Add(Path.GetFileNameWithoutExtension(f));
        }

        return result;
    }

    private string PathFor(string siteKey) => Path.Combine(_rootDir, siteKey + ".txt");

    private static bool IsSafeKey(string siteKey)
        => !string.IsNullOrWhiteSpace(siteKey) && SafeKeyRegex.IsMatch(siteKey);
}
