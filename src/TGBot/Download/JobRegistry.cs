// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

namespace TGBot.Download;

/// <summary>
/// 下载任务注册表：跟踪运行/排队任务数量，并对已受理的 URL 去重。
/// </summary>
public sealed class JobRegistry
{
    private readonly object _lock = new();
    private readonly HashSet<string> _reservedUrls = new(StringComparer.Ordinal);
    private int _running;
    private int _queued;

    /// <summary>
    /// 进行中的任务数。
    /// </summary>
    public int Running => Volatile.Read(ref _running);

    /// <summary>
    /// 排队的任务数。
    /// </summary>
    public int Queued => Volatile.Read(ref _queued);

    /// <summary>
    /// 尝试预约一个规范化 URL。已预约时返回 <see langword="false"/>。
    /// </summary>
    /// <param name="normalizedUrl">规范化 URL。</param>
    /// <returns>预约成功返回 <see langword="true"/>。</returns>
    public bool TryReserveUrl(string normalizedUrl)
    {
        lock (_lock)
        {
            return _reservedUrls.Add(normalizedUrl);
        }
    }

    /// <summary>
    /// 释放 URL 预约。
    /// </summary>
    /// <param name="normalizedUrl">规范化 URL。</param>
    public void ReleaseUrl(string normalizedUrl)
    {
        lock (_lock)
        {
            _reservedUrls.Remove(normalizedUrl);
        }
    }

    /// <summary>
    /// 任务入队时调用。
    /// </summary>
    public void OnEnqueue() => Interlocked.Increment(ref _queued);

    /// <summary>
    /// 任务开始执行时调用。
    /// </summary>
    public void OnStart()
    {
        Interlocked.Decrement(ref _queued);
        Interlocked.Increment(ref _running);
    }

    /// <summary>
    /// 任务结束时调用。
    /// </summary>
    public void OnFinish() => Interlocked.Decrement(ref _running);
}
