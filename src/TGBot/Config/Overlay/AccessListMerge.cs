// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

namespace TGBot.Config.Overlay;

/// <summary>
/// 白名单条目的来源。
/// </summary>
public enum AccessEntrySource
{
    /// <summary>安装配置（config.conf），不可经 /access 删除。</summary>
    Config,

    /// <summary>bot 维护的 overlay 追加列表。</summary>
    Overlay,
}

/// <summary>
/// 合并后的白名单条目（带来源标注）。
/// </summary>
/// <param name="Id">用户/会话 ID。</param>
/// <param name="Source">来源。</param>
public sealed record AccessEntry(long Id, AccessEntrySource Source);

/// <summary>
/// 合并结果：带来源标注的列表 + 去重后的 ID 集合（供 <c>AccessControlService</c> 使用）。
/// </summary>
/// <param name="Users">用户条目（升序，config 来源优先标注）。</param>
/// <param name="Channels">频道/群组条目（升序，config 来源优先标注）。</param>
public sealed record AccessListMergeResult(
    IReadOnlyList<AccessEntry> Users,
    IReadOnlyList<AccessEntry> Channels)
{
    /// <summary>去重后的用户 ID。</summary>
    public IReadOnlyList<long> UserIds => Users.Select(e => e.Id).ToArray();

    /// <summary>去重后的频道/群组 ID。</summary>
    public IReadOnlyList<long> ChannelIds => Channels.Select(e => e.Id).ToArray();
}

/// <summary>
/// 安装配置白名单 ∪ overlay 追加列表的合并器：去重（config 来源优先标注）、升序输出。
/// <para>仅支持追加：config 来源成员不可删除（/access del 时提示），overlay 来源可删。</para>
/// </summary>
public static class AccessListMerge
{
    /// <summary>
    /// 合并两个来源的白名单。
    /// </summary>
    /// <param name="configUsers">安装配置的用户白名单。</param>
    /// <param name="configChannels">安装配置的目标频道/群组。</param>
    /// <param name="overlay">bot 追加列表。</param>
    /// <returns>合并结果。</returns>
    public static AccessListMergeResult Merge(
        IReadOnlyList<long> configUsers,
        IReadOnlyList<long> configChannels,
        AccessOverlayData overlay)
    {
        return new AccessListMergeResult(
            MergeList(configUsers, overlay.ExtraAllowedUsers),
            MergeList(configChannels, overlay.ExtraTargetChannels));
    }

    private static IReadOnlyList<AccessEntry> MergeList(IReadOnlyList<long> config, IReadOnlyList<long> extra)
    {
        var map = new SortedDictionary<long, AccessEntrySource>();
        foreach (var id in config)
        {
            map[id] = AccessEntrySource.Config;
        }

        foreach (var id in extra)
        {
            map.TryAdd(id, AccessEntrySource.Overlay);
        }

        return map.Select(kv => new AccessEntry(kv.Key, kv.Value)).ToArray();
    }
}
