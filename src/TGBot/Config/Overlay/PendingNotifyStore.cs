// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

using System.Text.Json;
using TGBot.Logging;

namespace TGBot.Config.Overlay;

/// <summary>
/// 重启后待发送的通知（/config、/access 变更后由新进程消费）。
/// </summary>
/// <param name="ChatId">待通知的会话（发起变更的用户 chatId）。</param>
/// <param name="TextKey">文案资源键。</param>
/// <param name="Args">占位符参数。</param>
/// <param name="Lang">渲染语言（变更时用户的语言）。</param>
/// <param name="CreatedAt">创建时间（ISO 8601）。</param>
/// <param name="Attempts">已失败发送次数。</param>
public sealed record PendingNotify(
    long ChatId,
    string TextKey,
    IReadOnlyList<string> Args,
    string Lang,
    string CreatedAt,
    int Attempts);

/// <summary>
/// 重启通知存储：<c>StateDir/pending-notify.json</c>。
/// <para>防重复：启动期先 rename 为 <c>.sending</c>（原子认领，进程崩溃也不重发）再发送，
/// 成功后删除；失败保留并递增 <see cref="MaxAttempts"/>，超过上限丢弃并记录日志。</para>
/// <para>写入为同步原子写（临时文件 + rename）——调用方必须保证在进程退出前完成（设计遗留问题 5）。</para>
/// </summary>
public sealed class PendingNotifyStore
{
    /// <summary>待通知文件名。</summary>
    public const string FileName = "pending-notify.json";

    /// <summary>发送中文件名（rename 认领后）。</summary>
    public const string SendingFileName = FileName + ".sending";

    /// <summary>发送失败上限，达到后丢弃。</summary>
    public const int MaxAttempts = 3;

    private readonly string _filePath;
    private readonly string _sendingPath;
    private readonly IAppLogger? _logger;
    private readonly object _lock = new();

    /// <summary>
    /// 初始化 <see cref="PendingNotifyStore"/>。
    /// </summary>
    /// <param name="stateDir">状态目录（<c>StateDir</c> 配置键）。</param>
    /// <param name="logger">日志器（可空）。</param>
    public PendingNotifyStore(string stateDir, IAppLogger? logger = null)
    {
        var dir = Path.GetFullPath(stateDir);
        _filePath = Path.Combine(dir, FileName);
        _sendingPath = Path.Combine(dir, SendingFileName);
        _logger = logger;
    }

    /// <summary>
    /// 同步保存待通知（覆盖旧值，Attempts 重置为 0）。
    /// <para>必须同步完成再触发进程退出：保证退出前已持久化，新进程启动即可消费。</para>
    /// </summary>
    /// <param name="notify">通知内容。</param>
    /// <returns>写入成功返回 <see langword="true"/>。</returns>
    public bool Save(PendingNotify notify)
    {
        lock (_lock)
        {
            try
            {
                var dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                return AtomicWrite(_filePath, JsonSerializer.Serialize(notify with { Attempts = 0 }));
            }
            catch (Exception ex)
            {
                _logger?.Warn($"写入 {FileName} 失败：{ex.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// 认领待发送通知：pending → .sending（原子 rename 防重复发送），随后读取内容。
    /// </summary>
    /// <returns>待发送通知；无待发送或超过失败上限时返回 <see langword="null"/>。</returns>
    public PendingNotify? Claim()
    {
        lock (_lock)
        {
            if (File.Exists(_filePath))
            {
                try
                {
                    File.Move(_filePath, _sendingPath, overwrite: true);
                }
                catch (Exception ex)
                {
                    _logger?.Warn($"认领 {FileName} 失败：{ex.Message}");
                    return null;
                }
            }

            if (!File.Exists(_sendingPath))
            {
                return null;
            }

            try
            {
                var notify = ReadNotify(_sendingPath);
                if (notify.Attempts >= MaxAttempts)
                {
                    File.Delete(_sendingPath);
                    _logger?.Warn($"{FileName} 超过 {MaxAttempts} 次未送达，已丢弃");
                    return null;
                }

                return notify;
            }
            catch (Exception ex)
            {
                _logger?.Warn($"读取 {FileName} 失败，已丢弃：{ex.Message}");
                try
                {
                    File.Delete(_sendingPath);
                }
                catch (Exception deleteEx)
                {
                    _logger?.Warn($"删除损坏的 {FileName} 失败：{deleteEx.Message}");
                }

                return null;
            }
        }
    }

    /// <summary>
    /// 发送成功：删除 .sending，本次通知完成。
    /// </summary>
    public void Succeed()
    {
        lock (_lock)
        {
            try
            {
                File.Delete(_sendingPath);
            }
            catch (Exception ex)
            {
                _logger?.Warn($"删除 {SendingFileName} 失败：{ex.Message}");
            }
        }
    }

    /// <summary>
    /// 发送失败：Attempts + 1；达到 <see cref="MaxAttempts"/> 时删除并丢弃。
    /// </summary>
    /// <returns>丢弃返回 <see langword="true"/>；保留待下次启动重试返回 <see langword="false"/>。</returns>
    public bool Fail()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(_sendingPath))
                {
                    return false;
                }

                var notify = ReadNotify(_sendingPath);
                var attempts = notify.Attempts + 1;
                if (attempts >= MaxAttempts)
                {
                    File.Delete(_sendingPath);
                    return true;
                }

                return AtomicWrite(_sendingPath, JsonSerializer.Serialize(notify with { Attempts = attempts }));
            }
            catch (Exception ex)
            {
                _logger?.Warn($"更新 {FileName} 失败计数失败：{ex.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// 原子写（临时文件 + rename），写入前设 0600 权限（状态文件含会话 ID 等，防同机读取）。
    /// </summary>
    /// <param name="path">目标路径。</param>
    /// <param name="content">文件内容。</param>
    /// <returns>写入成功返回 <see langword="true"/>。</returns>
    private bool AtomicWrite(string path, string content)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(tmp, (UnixFileMode)((int)UnixFileMode.UserRead | (int)UnixFileMode.UserWrite));
        }

        File.Move(tmp, path, overwrite: true);
        return true;
    }

    private static PendingNotify ReadNotify(string path)
    {
        var json = File.ReadAllText(path);
        var notify = JsonSerializer.Deserialize<PendingNotify>(json);
        if (notify is null)
        {
            throw new InvalidDataException("内容为空");
        }

        return notify;
    }
}
