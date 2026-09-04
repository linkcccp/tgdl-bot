// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

using TGBot.Texts;
using TGBot.Texts.I18n;

namespace TGBot.Access;

/// <summary>
/// 触发区域：私聊，或频道/群组。
/// </summary>
public enum TriggerArea
{
    /// <summary>私聊（DM）。</summary>
    Private,

    /// <summary>群组或频道。</summary>
    GroupOrChannel,
}

/// <summary>
/// 访问控制判定结果。
/// </summary>
/// <param name="Allowed">是否允许。</param>
/// <param name="Reason">拒绝时的用户提示（允许时为 <see langword="null"/>）。</param>
public sealed record AccessDecision(bool Allowed, string? Reason)
{
    /// <summary>
    /// 允许的判定。
    /// </summary>
    public static readonly AccessDecision Allow = new(true, null);

    /// <summary>
    /// 拒绝的判定。
    /// </summary>
    /// <param name="reason">拒绝提示。</param>
    public static AccessDecision Deny(string reason) => new(false, reason);
}

/// <summary>
/// 双重白名单访问控制：
/// <list type="bullet">
/// <item>私聊：仅 <c>AllowedUserIds</c> 中的用户可触发。</item>
/// <item>频道/群组：仅 <c>TargetChannelIds</c> 中的会话可触发。</item>
/// </list>
/// </summary>
public sealed class AccessControlService
{
    private readonly HashSet<long> _allowedUserIds;
    private readonly HashSet<long> _targetChannelIds;
    private readonly II18n _i18n;

    /// <summary>
    /// 初始化 <see cref="AccessControlService"/>。
    /// </summary>
    /// <param name="allowedUserIds">允许的用户 ID 集合。</param>
    /// <param name="targetChannelIds">允许的频道/群组 ID 集合。</param>
    /// <param name="i18n">国际化服务（拒绝文案渲染）。</param>
    public AccessControlService(IEnumerable<long> allowedUserIds, IEnumerable<long> targetChannelIds, II18n i18n)
    {
        _allowedUserIds = new HashSet<long>(allowedUserIds);
        _targetChannelIds = new HashSet<long>(targetChannelIds);
        _i18n = i18n;
    }

    /// <summary>
    /// 判定是否允许触发。
    /// </summary>
    /// <param name="area">触发区域。</param>
    /// <param name="userId">发送者用户 ID（私聊必填，频道发帖可为空）。</param>
    /// <param name="chatId">会话 ID。</param>
    /// <param name="lang">调用方语言（拒绝文案渲染）。</param>
    /// <returns>判定结果。</returns>
    public AccessDecision Evaluate(TriggerArea area, long? userId, long chatId, string lang)
    {
        if (area == TriggerArea.Private)
        {
            return userId.HasValue && _allowedUserIds.Contains(userId.Value)
                ? AccessDecision.Allow
                : AccessDecision.Deny(_i18n.Get(lang, UserTexts.UnauthorizedPrivate));
        }

        return _targetChannelIds.Contains(chatId)
            ? AccessDecision.Allow
            : AccessDecision.Deny(_i18n.Get(lang, UserTexts.UnauthorizedGroup));
    }
}
