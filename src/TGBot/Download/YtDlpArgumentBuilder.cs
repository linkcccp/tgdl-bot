// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

namespace TGBot.Download;

/// <summary>
/// 构建 yt-dlp 命令行参数（纯函数，便于单元测试）。
/// <para>所有参数通过 <see cref="System.Diagnostics.ProcessStartInfo.ArgumentList"/> 传递，
/// 不经过 shell，从根本上杜绝命令注入。</para>
/// </summary>
public static class YtDlpArgumentBuilder
{
    /// <summary>
    /// 构建 yt-dlp 参数列表。
    /// </summary>
    /// <param name="options">下载参数。</param>
    /// <returns>参数列表。</returns>
    public static IReadOnlyList<string> Build(DownloadOptions options)
    {
        var args = new List<string>
        {
            "--newline",
            "--no-warnings",
            "--force-overwrites",
            "--trim-filenames",
            "120",
            "--retries",
            "3",
            "--fragment-retries",
            "3",
            "--socket-timeout",
            "30",
            "-o",
            "media.%(ext)s",
            "--merge-output-format",
            options.MergeFormat,
        };

        if (!options.AllowPlaylists)
        {
            args.Add("--no-playlist");
        }

        if (options.ExtractAudio)
        {
            args.Add("--extract-audio");
            args.Add("--audio-format");
            args.Add("mp3");
            args.Add("--audio-quality");
            args.Add("0");
        }

        if (!string.IsNullOrEmpty(options.FfmpegDir))
        {
            args.Add("--ffmpeg-location");
            args.Add(options.FfmpegDir);
        }

        if (!string.IsNullOrEmpty(options.CookiesFile))
        {
            args.Add("--cookies");
            args.Add(options.CookiesFile);
        }

        if (!string.IsNullOrEmpty(options.Proxy))
        {
            args.Add("--proxy");
            args.Add(options.Proxy);
        }

        if (!string.IsNullOrEmpty(options.FormatExpression))
        {
            args.Add("-f");
            args.Add(options.FormatExpression);
        }

        if (!string.IsNullOrEmpty(options.YoutubePlayerClients) && IsYoutubeHost(options.Url))
        {
            args.Add("--extractor-args");
            args.Add($"youtube:player_client={options.YoutubePlayerClients}");
        }

        if (options.ExtraArgs is { Count: > 0 })
        {
            args.AddRange(options.ExtraArgs);
        }

        args.Add("--print");
        args.Add("META\u001f%(id)s\u001f%(title)s\u001f%(ext)s\u001f%(duration)s\u001f%(filesize_approx)s\u001f%(filesize)s");

        args.Add("--print");
        args.Add("after_move:FILE\u001f%(filepath)s");

        args.Add("--progress-template");
        args.Add("download:DLP %(progress._percent_str)s|%(progress._speed_str)s");

        args.Add(options.Url);

        return args;
    }

    private static bool IsYoutubeHost(string url)
    {
        try
        {
            return IsYoutubeUrl(url);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 判断 URL 是否属于 YouTube 域名。
    /// </summary>
    /// <param name="url">URL。</param>
    /// <returns>是 YouTube 域名返回 <see langword="true"/>。</returns>
    public static bool IsYoutubeUrl(string url)
    {
        var host = new Uri(url).Host.ToLowerInvariant();
        return host is "youtube.com" or "youtu.be" or "m.youtube.com" or "music.youtube.com" or "www.youtube.com";
    }
}
