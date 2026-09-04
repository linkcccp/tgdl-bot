// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

using System.Runtime.InteropServices;

namespace TGBot.Update;

/// <summary>
/// 工具二进制下载地址的架构映射（/update 运行期按进程架构选择对应架构的官方资产）。
/// <para>镜像按架构构建（seed-bin 与镜像一致），唯一在运行期下载外部二进制的是 /update，
/// 因此只有这里需要感知架构。URL/资产名均为第三方官方命名，保留原样并在注释中注明对应关系
/// （amd64 即 x64、aarch64 即 arm64）。</para>
/// </summary>
public static class ToolArch
{
    /// <summary>
    /// 按运行架构返回 BtbN/FFmpeg-Builds 最新自动构建（autobuild）下载 URL。
    /// <para>上游的 <c>latest</c> 是真实存在的滚动 release tag（资产名恒定不含日期），
    /// URL 永不变化；资产命名 <c>linux64</c>/<c>linuxarm64</c> 分别对应 x64/arm64。</para>
    /// </summary>
    /// <param name="arch">运行架构（真实进程架构或测试注入值）。</param>
    /// <returns>对应架构的 ffmpeg 静态构建 .tar.xz 下载地址。</returns>
    /// <exception cref="InvalidOperationException">架构不是 x64/arm64 时抛出（快速失败，不做静默回退）。</exception>
    public static string FfmpegReleaseUrl(Architecture arch) => arch switch
    {
        // 上游官方命名：linux64 资产对应 x64（amd64），与 CI（release.yml matrix ffmpeg-url）逐字符一致。
        Architecture.X64 => "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-linux64-gpl.tar.xz",
        // 上游官方命名：linuxarm64 资产对应 arm64，与 CI 一致。
        Architecture.Arm64 => "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-linuxarm64-gpl.tar.xz",
        // 错误架构的二进制必然在 VerifyBinaryAsync（执行 --version 校验）失败，早失败省流量且报错清晰。
        _ => throw new InvalidOperationException($"不支持的运行架构 {arch}：/update 仅支持 x64 与 arm64"),
    };

    /// <summary>
    /// 按运行架构返回 yt-dlp 官方 GitHub release 资产下载 URL。
    /// </summary>
    /// <param name="arch">运行架构（真实进程架构或测试注入值）。</param>
    /// <returns>对应架构的 yt-dlp 自包含二进制下载地址。</returns>
    /// <exception cref="InvalidOperationException">架构不是 x64/arm64 时抛出（快速失败，不做静默回退）。</exception>
    public static string YtDlpReleaseUrl(Architecture arch) => arch switch
    {
        // 上游官方命名：x64 资产为 yt-dlp（官方 x86_64 命名，即 x64），不可改。
        Architecture.X64 => "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp",
        // 上游官方命名：arm64 资产为 yt-dlp_linux_aarch64（官方 aarch64 命名，即 arm64），不可改。
        Architecture.Arm64 => "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_linux_aarch64",
        _ => throw new InvalidOperationException($"不支持的运行架构 {arch}：/update 仅支持 x64 与 arm64"),
    };
}
