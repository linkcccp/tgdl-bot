// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

namespace TGBot.Update;

/// <summary>
/// 单个工具更新的状态。
/// </summary>
public enum ToolUpdateStatus
{
    /// <summary>未配置安装路径，跳过更新。</summary>
    NotConfigured,

    /// <summary>本地已是最新版本，无需更新。</summary>
    AlreadyUpToDate,

    /// <summary>更新成功。</summary>
    Updated,

    /// <summary>更新失败。</summary>
    Failed,
}

/// <summary>
/// 单个工具的更新结果。
/// </summary>
/// <param name="Tool">工具名称。</param>
/// <param name="LocalVersion">本地版本（未知为 <see langword="null"/>）。</param>
/// <param name="LatestVersion">最新版本（未知为 <see langword="null"/>）。</param>
/// <param name="Status">更新状态。</param>
public sealed record ToolUpdateResult(
    string Tool,
    ToolVersion? LocalVersion,
    ToolVersion? LatestVersion,
    ToolUpdateStatus Status);

/// <summary>
/// 整体更新报告。
/// </summary>
/// <param name="Tools">各工具的更新结果。</param>
public sealed record UpdateReport(IReadOnlyList<ToolUpdateResult> Tools)
{
    /// <summary>
    /// 是否存在失败的更新。
    /// </summary>
    public bool HasFailures => Tools.Any(t => t.Status == ToolUpdateStatus.Failed);
}

/// <summary>
/// 更新失败原因分类，用于向用户给出准确提示而不泄露内部细节。
/// </summary>
public enum UpdateFailureReason
{
    /// <summary>一般性失败。</summary>
    Failed,

    /// <summary>无法读取本地工具版本（安装路径异常或二进制不可执行）。</summary>
    LocalVersionUnavailable,

    /// <summary>无法获取最新版本信息（网络或版本源异常）。</summary>
    LatestVersionUnavailable,

    /// <summary>新版本二进制下载失败。</summary>
    DownloadFailed,

    /// <summary>二进制替换失败（已回滚至原版本）。</summary>
    ReplaceFailed,
}

/// <summary>
/// 更新失败异常。
/// </summary>
public sealed class UpdateException : Exception
{
    /// <summary>
    /// 失败原因分类（调用方按分类渲染用户提示）。
    /// </summary>
    public UpdateFailureReason Reason { get; }

    /// <summary>
    /// 初始化 <see cref="UpdateException"/>。
    /// </summary>
    /// <param name="reason">失败原因分类。</param>
    /// <param name="detail">内部详细日志（仅记录，不发送给用户；消息由调用方按分类 i18n 渲染）。</param>
    public UpdateException(UpdateFailureReason reason, string detail)
        : base(detail)
    {
        Reason = reason;
    }
}
