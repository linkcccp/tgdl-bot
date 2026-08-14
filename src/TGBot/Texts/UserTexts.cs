namespace TGBot.Texts;

/// <summary>
/// 面向 Telegram 用户的中文提示文案。
/// <para>所有文案均不含内部实现细节（堆栈、路径、配置值等），遵循零信任原则。</para>
/// </summary>
public static class UserTexts
{
    /// <summary>私聊用户未在白名单时的拒绝提示。</summary>
    public const string UnauthorizedPrivate =
        "抱歉，您不在允许使用本服务的名单中，无法处理您发送的内容。";

    /// <summary>频道/群组未在白名单时的拒绝提示。</summary>
    public const string UnauthorizedGroup =
        "抱歉，本频道/群组未获得授权，无法在此处理链接。";

    /// <summary>消息中没有有效链接时的提示。</summary>
    public const string NoValidUrl =
        "消息中未找到有效的视频/音乐链接，请发送 http/https 链接。";

    /// <summary>链接指向私网地址时的提示。</summary>
    public const string UntrustedUrl =
        "链接指向的地址不受信任，已拒绝下载。";

    /// <summary>下载排队提示。参数依次为：队列位置。</summary>
    public const string Queued =
        "已收到，正在排队处理（队列位置：{0}）。";

    /// <summary>开始下载提示。</summary>
    public const string Downloading =
        "开始下载…";

    /// <summary>下载进度提示。参数依次为：百分比、速度。</summary>
    public const string DownloadProgress =
        "下载中：{0}%（{1}）";

    /// <summary>开始上传提示。</summary>
    public const string Uploading =
        "下载完成，正在上传到目标频道/群组…";

    /// <summary>上传成功提示。参数依次为：目标会话数量。</summary>
    public const string UploadDone =
        "已完成，已推送至 {0} 个目标会话。";

    /// <summary>下载失败提示。</summary>
    public const string DownloadFailed =
        "下载失败，请稍后重试，或检查链接是否有效。";

    /// <summary>上传失败提示。</summary>
    public const string UploadFailed =
        "上传失败，请稍后重试。";

    /// <summary>文件过大提示。</summary>
    public const string FileTooLarge =
        "文件过大，超出上传限制（约 2GB），已取消该任务。";

    /// <summary>磁盘空间不足提示。</summary>
    public const string NoDiskSpace =
        "服务器磁盘空间不足，已取消该任务，请稍后重试。";

    /// <summary>未知指令提示。</summary>
    public const string UnknownCommand =
        "未知指令。发送 /help 查看可用指令。";

    /// <summary>帮助文本。</summary>
    public const string Help =
        "可用指令：\n"
        + "/update - 检查并更新 ffmpeg 与 yt-dlp\n"
        + "/status - 查看运行状态与版本\n"
        + "/help   - 显示本帮助\n\n"
        + "直接发送视频/音乐链接即可触发下载。";

    /// <summary>更新无需执行提示。</summary>
    public const string UpdateNotNeeded =
        "yt-dlp 与 ffmpeg 均为最新版本，无需更新。";

    /// <summary>更新完成提示。参数依次为：yt-dlp 旧版本、新版本、ffmpeg 旧版本、新版本。</summary>
    public const string UpdateDone =
        "更新完成：\nyt-dlp：{0} → {1}\nffmpeg：{2} → {3}";

    /// <summary>更新部分完成提示（仅某个组件更新）。</summary>
    public const string UpdatePartialDone =
        "更新完成：\n{0}";

    /// <summary>更新失败提示。</summary>
    public const string UpdateFailed =
        "更新失败，已回滚至原版本，请稍后重试。";

    /// <summary>更新被拒绝提示（非白名单用户）。</summary>
    public const string UpdateDenied =
        "您没有权限执行此操作。";

    /// <summary>状态信息模板。参数依次为：运行时间、队列、进行中、yt-dlp 版本、ffmpeg 版本、可用磁盘空间。</summary>
    public const string StatusTemplate =
        "运行状态：\n"
        + "运行时间：{0}\n"
        + "进行中任务：{1}\n"
        + "排队任务：{2}\n"
        + "yt-dlp：{3}\n"
        + "ffmpeg：{4}\n"
        + "可用磁盘空间：{5}";
}
