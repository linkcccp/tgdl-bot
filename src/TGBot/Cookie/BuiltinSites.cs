// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

namespace TGBot.Cookie;

/// <summary>YouTube。</summary>
public sealed class YoutubeCookieSite : CookieSite
{
    /// <inheritdoc />
    public override string Key => "youtube";

    /// <inheritdoc />
    public override string DisplayName => "YouTube";

    /// <inheritdoc />
    protected override IReadOnlyList<string> Hosts { get; } =
        new[] { "youtube.com", "youtu.be", "m.youtube.com", "music.youtube.com" };
}

/// <summary>X（推特）。</summary>
public sealed class TwitterCookieSite : CookieSite
{
    /// <inheritdoc />
    public override string Key => "twitter";

    /// <inheritdoc />
    public override string DisplayName => "X（推特）";

    /// <inheritdoc />
    protected override IReadOnlyList<string> Hosts { get; } =
        new[] { "twitter.com", "x.com" };
}

/// <summary>Instagram。</summary>
public sealed class InstagramCookieSite : CookieSite
{
    /// <inheritdoc />
    public override string Key => "instagram";

    /// <inheritdoc />
    public override string DisplayName => "Instagram";

    /// <inheritdoc />
    protected override IReadOnlyList<string> Hosts { get; } =
        new[] { "instagram.com" };
}

/// <summary>TikTok。</summary>
public sealed class TiktokCookieSite : CookieSite
{
    /// <inheritdoc />
    public override string Key => "tiktok";

    /// <inheritdoc />
    public override string DisplayName => "TikTok";

    /// <inheritdoc />
    protected override IReadOnlyList<string> Hosts { get; } =
        new[] { "tiktok.com" };
}

/// <summary>Twitch。</summary>
public sealed class TwitchCookieSite : CookieSite
{
    /// <inheritdoc />
    public override string Key => "twitch";

    /// <inheritdoc />
    public override string DisplayName => "Twitch";

    /// <inheritdoc />
    protected override IReadOnlyList<string> Hosts { get; } =
        new[] { "twitch.tv" };
}

/// <summary>Facebook。</summary>
public sealed class FacebookCookieSite : CookieSite
{
    /// <inheritdoc />
    public override string Key => "facebook";

    /// <inheritdoc />
    public override string DisplayName => "Facebook";

    /// <inheritdoc />
    protected override IReadOnlyList<string> Hosts { get; } =
        new[] { "facebook.com", "fb.watch", "m.facebook.com" };
}

/// <summary>哔哩哔哩。</summary>
public sealed class BilibiliCookieSite : CookieSite
{
    /// <inheritdoc />
    public override string Key => "bilibili";

    /// <inheritdoc />
    public override string DisplayName => "哔哩哔哩";

    /// <inheritdoc />
    protected override IReadOnlyList<string> Hosts { get; } =
        new[] { "bilibili.com", "b23.tv" };
}

/// <summary>抖音。</summary>
public sealed class DouyinCookieSite : CookieSite
{
    /// <inheritdoc />
    public override string Key => "douyin";

    /// <inheritdoc />
    public override string DisplayName => "抖音";

    /// <inheritdoc />
    protected override IReadOnlyList<string> Hosts { get; } =
        new[] { "douyin.com", "iesdouyin.com" };
}

/// <summary>小红书。</summary>
public sealed class XiaohongshuCookieSite : CookieSite
{
    /// <inheritdoc />
    public override string Key => "xiaohongshu";

    /// <inheritdoc />
    public override string DisplayName => "小红书";

    /// <inheritdoc />
    protected override IReadOnlyList<string> Hosts { get; } =
        new[] { "xiaohongshu.com" };
}

/// <summary>微博。</summary>
public sealed class WeiboCookieSite : CookieSite
{
    /// <inheritdoc />
    public override string Key => "weibo";

    /// <inheritdoc />
    public override string DisplayName => "微博";

    /// <inheritdoc />
    protected override IReadOnlyList<string> Hosts { get; } =
        new[] { "weibo.com", "weibo.cn" };
}

/// <summary>SoundCloud。</summary>
public sealed class SoundcloudCookieSite : CookieSite
{
    /// <inheritdoc />
    public override string Key => "soundcloud";

    /// <inheritdoc />
    public override string DisplayName => "SoundCloud";

    /// <inheritdoc />
    protected override IReadOnlyList<string> Hosts { get; } =
        new[] { "soundcloud.com" };
}

/// <summary>Vimeo。</summary>
public sealed class VimeoCookieSite : CookieSite
{
    /// <inheritdoc />
    public override string Key => "vimeo";

    /// <inheritdoc />
    public override string DisplayName => "Vimeo";

    /// <inheritdoc />
    protected override IReadOnlyList<string> Hosts { get; } =
        new[] { "vimeo.com" };
}

/// <summary>Dailymotion。</summary>
public sealed class DailymotionCookieSite : CookieSite
{
    /// <inheritdoc />
    public override string Key => "dailymotion";

    /// <inheritdoc />
    public override string DisplayName => "Dailymotion";

    /// <inheritdoc />
    protected override IReadOnlyList<string> Hosts { get; } =
        new[] { "dailymotion.com" };
}

/// <summary>Reddit。</summary>
public sealed class RedditCookieSite : CookieSite
{
    /// <inheritdoc />
    public override string Key => "reddit";

    /// <inheritdoc />
    public override string DisplayName => "Reddit";

    /// <inheritdoc />
    protected override IReadOnlyList<string> Hosts { get; } =
        new[] { "reddit.com", "redd.it" };
}
