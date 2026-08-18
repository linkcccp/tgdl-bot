// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

namespace TGBot.Update;

/// <summary>
/// ffmpeg autobuild 版本 marker 文件读写。
/// <para>BtbN master 二进制的 <c>ffmpeg -version</c> 输出 git 提交计数（如 <c>N-118503</c>），
/// 与远端的 autobuild 日期版本（如 <c>2026.08.17.13.29.26</c>）标度不同，无法直接比较；
/// 更新成功后把安装的 autobuild 时间写入 <c>&lt;installPath&gt;.autobuild</c>，
/// 比较时优先读 marker 保持同标度，"已是最新"短路语义不变。</para>
/// </summary>
public static class FfmpegVersionMarker
{
    /// <summary>
    /// marker 文件路径：安装路径后追加 <c>.autobuild</c> 后缀。
    /// </summary>
    /// <param name="installPath">ffmpeg 安装路径。</param>
    /// <returns>marker 文件完整路径。</returns>
    public static string MarkerPath(string installPath) => Path.GetFullPath(installPath) + ".autobuild";

    /// <summary>
    /// 读取上次安装的 autobuild 版本。
    /// </summary>
    /// <param name="installPath">ffmpeg 安装路径。</param>
    /// <param name="version">读取到的版本；失败时为 <see langword="null"/>。</param>
    /// <returns>读取成功返回 <see langword="true"/>；文件缺失或内容损坏返回 <see langword="false"/>。</returns>
    public static bool TryRead(string installPath, out ToolVersion? version)
    {
        version = null;
        try
        {
            var content = File.ReadAllText(MarkerPath(installPath));
            return ToolVersion.TryParse(content, out version);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// 写入上次安装的 autobuild 版本（先写临时文件再原子移动，避免写一半）。
    /// </summary>
    /// <param name="installPath">ffmpeg 安装路径。</param>
    /// <param name="version">要记录的版本（如 <c>latest.ToString()</c>）。</param>
    public static void Write(string installPath, ToolVersion version)
    {
        var path = MarkerPath(installPath);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, version.ToString());
        File.Move(tmp, path, overwrite: true);
    }
}