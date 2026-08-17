// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

namespace TGBot.Logging;

/// <summary>
/// 日志级别。
/// </summary>
public enum LogLevel
{
    /// <summary>最详细的调试信息，仅在排查问题时使用。</summary>
    Trace = 0,

    /// <summary>调试信息。</summary>
    Debug = 1,

    /// <summary>常规信息。</summary>
    Info = 2,

    /// <summary>警告信息，不影响正常运行。</summary>
    Warn = 3,

    /// <summary>错误信息，通常表示某次操作失败。</summary>
    Error = 4,
}
