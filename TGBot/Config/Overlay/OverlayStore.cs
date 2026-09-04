// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

using System.Text.Json;
using TGBot.Logging;

namespace TGBot.Config.Overlay;

/// <summary>
/// access-overlay.json 内容：bot 独立维护的白名单追加列表（与安装配置物理隔离）。
/// </summary>
/// <param name="ExtraAllowedUsers">bot 添加的白名单用户（安装配置之外）。</param>
/// <param name="ExtraTargetChannels">bot 添加的目标频道/群组（安装配置之外）。</param>
public sealed record AccessOverlayData(
    IReadOnlyList<long> ExtraAllowedUsers,
    IReadOnlyList<long> ExtraTargetChannels)
{
    /// <summary>空列表。</summary>
    public static readonly AccessOverlayData Empty = new(Array.Empty<long>(), Array.Empty<long>());
}

/// <summary>
/// overlay 持久化存储：<c>StateDir/config-overlay.json</c>（键 → 字符串值）与
/// <c>StateDir/access-overlay.json</c>（bot 白名单追加列表）。
/// <para>与安装配置（config.conf）物理隔离、互不覆盖；原子写（临时文件 + rename）+ 锁保证并发安全；
/// 两文件分开存储避免互锁。entrypoint 不触碰这些文件，tgdl-data 卷持久，pull 重建不丢。</para>
/// </summary>
public sealed class OverlayStore
{
    /// <summary>配置覆盖文件名。</summary>
    public const string ConfigFileName = "config-overlay.json";

    /// <summary>白名单覆盖文件名。</summary>
    public const string AccessFileName = "access-overlay.json";

    private readonly string _configPath;
    private readonly string _accessPath;
    private readonly IAppLogger? _logger;
    private readonly object _configLock = new();
    private readonly object _accessLock = new();

    /// <summary>
    /// 初始化 <see cref="OverlayStore"/>。
    /// </summary>
    /// <param name="stateDir">状态目录（<c>StateDir</c> 配置键）。</param>
    /// <param name="logger">日志器（可空）。</param>
    public OverlayStore(string stateDir, IAppLogger? logger = null)
    {
        var dir = Path.GetFullPath(stateDir);
        _configPath = Path.Combine(dir, ConfigFileName);
        _accessPath = Path.Combine(dir, AccessFileName);
        _logger = logger;
    }

    /// <summary>
    /// 读取配置覆盖（每次读盘；文件缺失或损坏时按空覆盖处理并记录警告）。
    /// </summary>
    /// <returns>键 → 字符串值。</returns>
    public IReadOnlyDictionary<string, string> LoadConfig()
    {
        lock (_configLock)
        {
            return LoadConfigUnlocked();
        }
    }

    /// <summary>
    /// 设置（新增或覆盖）一个配置覆盖项并原子写盘。
    /// </summary>
    /// <param name="key">规范键名（调用方须先经 <see cref="ConfigParser.ValidateValue"/> 校验）。</param>
    /// <param name="value">字符串值（与 config.conf 值格式一致）。</param>
    /// <returns>写入成功返回 <see langword="true"/>。</returns>
    public bool SetConfigValue(string key, string value)
    {
        lock (_configLock)
        {
            var dict = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var (k, v) in LoadConfigUnlocked())
            {
                dict[k] = v;
            }

            dict[key] = value;
            return AtomicWrite(_configPath, JsonSerializer.Serialize(dict));
        }
    }

    /// <summary>
    /// 删除一个配置覆盖项（恢复安装配置/默认值）并原子写盘。
    /// </summary>
    /// <param name="key">规范键名。</param>
    /// <returns>键原本存在且已删除返回 <see langword="true"/>；未被覆盖返回 <see langword="false"/>。</returns>
    public bool RemoveConfigKey(string key)
    {
        lock (_configLock)
        {
            var dict = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var (k, v) in LoadConfigUnlocked())
            {
                dict[k] = v;
            }

            if (!dict.Remove(key))
            {
                return false;
            }

            return AtomicWrite(_configPath, JsonSerializer.Serialize(dict));
        }
    }

    /// <summary>
    /// 清空全部配置覆盖并原子写盘。
    /// </summary>
    /// <returns>原本存在覆盖且已清空返回 <see langword="true"/>；无覆盖返回 <see langword="false"/>。</returns>
    public bool ClearConfig()
    {
        lock (_configLock)
        {
            if (LoadConfigUnlocked().Count == 0)
            {
                return false;
            }

            return AtomicWrite(_configPath, "{}");
        }
    }

    /// <summary>
    /// 读取白名单覆盖（文件缺失或损坏时按空覆盖处理并记录警告）。
    /// </summary>
    /// <returns>追加列表数据。</returns>
    public AccessOverlayData LoadAccess()
    {
        lock (_accessLock)
        {
            return LoadAccessUnlocked();
        }
    }

    /// <summary>
    /// 追加一个白名单用户（去重；原子写盘）。
    /// </summary>
    /// <param name="userId">用户 ID（正整数）。</param>
    /// <returns>已新增返回 <see langword="true"/>；已存在返回 <see langword="false"/>。</returns>
    public bool AddAccessUser(long userId)
    {
        lock (_accessLock)
        {
            var data = LoadAccessUnlocked();
            if (data.ExtraAllowedUsers.Contains(userId))
            {
                return false;
            }

            return WriteAccess(new AccessOverlayData(
                data.ExtraAllowedUsers.Append(userId).Distinct().OrderBy(x => x).ToArray(),
                data.ExtraTargetChannels));
        }
    }

    /// <summary>
    /// 从追加列表移除一个白名单用户（原子写盘）。
    /// </summary>
    /// <param name="userId">用户 ID。</param>
    /// <returns>已移除返回 <see langword="true"/>；原本不在列表返回 <see langword="false"/>。</returns>
    public bool RemoveAccessUser(long userId)
    {
        lock (_accessLock)
        {
            var data = LoadAccessUnlocked();
            if (!data.ExtraAllowedUsers.Contains(userId))
            {
                return false;
            }

            return WriteAccess(new AccessOverlayData(
                data.ExtraAllowedUsers.Where(x => x != userId).ToArray(),
                data.ExtraTargetChannels));
        }
    }

    /// <summary>
    /// 追加一个目标频道/群组（去重；原子写盘）。
    /// </summary>
    /// <param name="channelId">频道/群组 ID（允许负数，如 -100xxx）。</param>
    /// <returns>已新增返回 <see langword="true"/>；已存在返回 <see langword="false"/>。</returns>
    public bool AddAccessChannel(long channelId)
    {
        lock (_accessLock)
        {
            var data = LoadAccessUnlocked();
            if (data.ExtraTargetChannels.Contains(channelId))
            {
                return false;
            }

            return WriteAccess(new AccessOverlayData(
                data.ExtraAllowedUsers,
                data.ExtraTargetChannels.Append(channelId).Distinct().OrderBy(x => x).ToArray()));
        }
    }

    /// <summary>
    /// 从追加列表移除一个目标频道/群组（原子写盘）。
    /// </summary>
    /// <param name="channelId">频道/群组 ID。</param>
    /// <returns>已移除返回 <see langword="true"/>；原本不在列表返回 <see langword="false"/>。</returns>
    public bool RemoveAccessChannel(long channelId)
    {
        lock (_accessLock)
        {
            var data = LoadAccessUnlocked();
            if (!data.ExtraTargetChannels.Contains(channelId))
            {
                return false;
            }

            return WriteAccess(new AccessOverlayData(
                data.ExtraAllowedUsers,
                data.ExtraTargetChannels.Where(x => x != channelId).ToArray()));
        }
    }

    private IReadOnlyDictionary<string, string> LoadConfigUnlocked()
    {
        try
        {
            if (!File.Exists(_configPath))
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            var json = File.ReadAllText(_configPath);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            _logger?.Warn($"读取 {ConfigFileName} 失败，按空覆盖处理：{ex.Message}");
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private AccessOverlayData LoadAccessUnlocked()
    {
        try
        {
            if (!File.Exists(_accessPath))
            {
                return AccessOverlayData.Empty;
            }

            var json = File.ReadAllText(_accessPath);
            var data = JsonSerializer.Deserialize<AccessOverlayData>(json);
            return data is null
                ? AccessOverlayData.Empty
                : new AccessOverlayData(
                    data.ExtraAllowedUsers ?? Array.Empty<long>(),
                    data.ExtraTargetChannels ?? Array.Empty<long>());
        }
        catch (Exception ex)
        {
            _logger?.Warn($"读取 {AccessFileName} 失败，按空覆盖处理：{ex.Message}");
            return AccessOverlayData.Empty;
        }
    }

    private bool WriteAccess(AccessOverlayData data)
        => AtomicWrite(_accessPath, JsonSerializer.Serialize(data));

    private bool AtomicWrite(string path, string json)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            if (!OperatingSystem.IsWindows())
            {
                // 状态文件含白名单/配置覆盖，0600 防同机其他用户读取（对齐 CookieStore 先例）。
                File.SetUnixFileMode(tmp, (UnixFileMode)((int)UnixFileMode.UserRead | (int)UnixFileMode.UserWrite));
            }

            File.Move(tmp, path, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.Warn($"写入 {Path.GetFileName(path)} 失败：{ex.Message}");
            return false;
        }
    }
}
