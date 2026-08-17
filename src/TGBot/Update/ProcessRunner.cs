// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

using System.Diagnostics;

namespace TGBot.Update;

/// <summary>
/// 进程运行结果。
/// </summary>
/// <param name="ExitCode">退出码。</param>
/// <param name="StdOut">标准输出。</param>
/// <param name="StdErr">标准错误输出。</param>
public sealed record ProcessOutput(int ExitCode, string StdOut, string StdErr);

/// <summary>
/// 进程运行抽象，便于单元测试注入。
/// </summary>
public interface IProcessRunner
{
    /// <summary>
    /// 运行一个进程并等待其退出。
    /// </summary>
    /// <param name="file">可执行文件路径。</param>
    /// <param name="args">参数列表。</param>
    /// <param name="workingDir">工作目录，可为空。</param>
    /// <param name="timeout">超时时间。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>运行结果。</returns>
    Task<ProcessOutput> RunAsync(string file, IReadOnlyList<string> args, string? workingDir, TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>
/// 基于 <see cref="Process"/> 的真实进程运行器。
/// </summary>
public sealed class SystemProcessRunner : IProcessRunner
{
    /// <inheritdoc />
    public async Task<ProcessOutput> RunAsync(
        string file,
        IReadOnlyList<string> args,
        string? workingDir,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = file,
                WorkingDirectory = workingDir ?? string.Empty,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        foreach (var a in args)
        {
            process.StartInfo.ArgumentList.Add(a);
        }

        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        var exitTask = process.WaitForExitAsync(cancellationToken);
        var completed = await Task.WhenAny(exitTask, Task.Delay(timeout, cancellationToken)).ConfigureAwait(false);
        if (completed != exitTask)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // ignore
            }

            throw new TimeoutException($"进程 {file} 运行超时（{timeout.TotalSeconds} 秒）");
        }

        await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        return new ProcessOutput(process.ExitCode, stdoutTask.Result, stderrTask.Result);
    }
}
