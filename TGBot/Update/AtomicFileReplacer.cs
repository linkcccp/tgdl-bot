// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

using System.Security.Cryptography;

namespace TGBot.Update;

/// <summary>
/// 原子文件替换器：先写临时文件再原子替换目标，失败时回滚旧版本。
/// </summary>
public static class AtomicFileReplacer
{
    /// <summary>
    /// 用新文件原子替换目标文件。
    /// <para>流程：备份旧文件 → 移动新文件到目标 → 设置权限 → 校验 → 成功删除备份 / 失败恢复备份。</para>
    /// </summary>
    /// <param name="targetPath">目标文件路径。</param>
    /// <param name="newFilePath">新文件路径（须与目标同一文件系统）。</param>
    /// <returns>替换后的目标路径。</returns>
    /// <exception cref="IOException">替换或校验失败时抛出。</exception>
    public static string Replace(string targetPath, string newFilePath)
    {
        var targetFull = Path.GetFullPath(targetPath);
        var dir = Path.GetDirectoryName(targetFull)!;
        Directory.CreateDirectory(dir);

        var backup = targetFull + ".old";
        var newFull = Path.GetFullPath(newFilePath);

        File.Delete(backup);
        if (File.Exists(targetFull))
        {
            File.Move(targetFull, backup);
        }

        try
        {
            File.Move(newFull, targetFull, overwrite: true);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(targetFull, (UnixFileMode)((int)UnixFileMode.UserRead | (int)UnixFileMode.UserWrite | (int)UnixFileMode.UserExecute | (int)UnixFileMode.GroupRead | (int)UnixFileMode.GroupExecute));
            }

            return targetFull;
        }
        catch
        {
            // 回滚：恢复备份
            if (File.Exists(backup))
            {
                try
                {
                    File.Move(backup, targetFull);
                }
                catch
                {
                    // ignore rollback failure
                }
            }

            throw;
        }
        finally
        {
            try
            {
                File.Delete(backup);
            }
            catch
            {
                // ignore
            }
        }
    }

    /// <summary>
    /// 计算文件 SHA-256，用于更新前完整性校验。
    /// </summary>
    /// <param name="path">文件路径。</param>
    /// <returns>十六进制 SHA-256。</returns>
    public static string Sha256(string path)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(path);
        var hash = sha.ComputeHash(fs);
        return Convert.ToHexStringLower(hash);
    }
}
