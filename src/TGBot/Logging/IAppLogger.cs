// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

namespace TGBot.Logging;

/// <summary>
/// 应用日志抽象。所有模块通过本接口输出日志，便于替换实现与单元测试。
/// </summary>
public interface IAppLogger : IDisposable
{
    /// <summary>
    /// 记录一条日志。
    /// </summary>
    /// <param name="level">日志级别。</param>
    /// <param name="message">日志消息。</param>
    /// <param name="exception">关联的异常，可为 <see langword="null"/>。</param>
    void Log(LogLevel level, string message, Exception? exception = null);

    /// <summary>
    /// 记录一条 Trace 级别日志。
    /// </summary>
    /// <param name="message">日志消息。</param>
    void Trace(string message);

    /// <summary>
    /// 记录一条 Debug 级别日志。
    /// </summary>
    /// <param name="message">日志消息。</param>
    void Debug(string message);

    /// <summary>
    /// 记录一条 Info 级别日志。
    /// </summary>
    /// <param name="message">日志消息。</param>
    void Info(string message);

    /// <summary>
    /// 记录一条 Warn 级别日志。
    /// </summary>
    /// <param name="message">日志消息。</param>
    void Warn(string message);

    /// <summary>
    /// 记录一条 Error 级别日志。
    /// </summary>
    /// <param name="message">日志消息。</param>
    /// <param name="exception">关联的异常，可为 <see langword="null"/>。</param>
    void Error(string message, Exception? exception = null);
}
