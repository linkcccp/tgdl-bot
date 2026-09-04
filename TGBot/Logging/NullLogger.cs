// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

namespace TGBot.Logging;

/// <summary>
/// 不输出任何内容的空日志实现，用于单元测试。
/// </summary>
public sealed class NullLogger : IAppLogger
{
    /// <summary>
    /// 共享的单例实例。
    /// </summary>
    public static readonly NullLogger Instance = new();

    private NullLogger()
    {
    }

    /// <inheritdoc />
    public void Log(LogLevel level, string message, Exception? exception = null)
    {
    }

    /// <inheritdoc />
    public void Trace(string message)
    {
    }

    /// <inheritdoc />
    public void Debug(string message)
    {
    }

    /// <inheritdoc />
    public void Info(string message)
    {
    }

    /// <inheritdoc />
    public void Warn(string message)
    {
    }

    /// <inheritdoc />
    public void Error(string message, Exception? exception = null)
    {
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
