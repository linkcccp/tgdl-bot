// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

namespace TGBot.Texts;

/// <summary>
/// 面向 Telegram 用户的文案**资源键**容器（中英双语，实际文案见 <c>Texts/I18n/Resources</c>）。
/// <para>i18n 迁移后本类仅承载键名常量（消除魔法字符串）；实际文案由
/// <see cref="TGBot.Texts.I18n.II18n.Get"/> 按消息语言渲染，键缺失回退 en，再缺失回退键名本身。</para>
/// <para>所有文案均不含内部实现细节（堆栈、路径、配置值等），遵循零信任原则。</para>
/// </summary>
public static class UserTexts
{
    /// <summary>私聊用户未在白名单时的拒绝提示。</summary>
    public const string UnauthorizedPrivate = "UnauthorizedPrivate";

    /// <summary>频道/群组未在白名单时的拒绝提示。</summary>
    public const string UnauthorizedGroup = "UnauthorizedGroup";

    /// <summary>消息中没有有效链接时的提示。</summary>
    public const string NoValidUrl = "NoValidUrl";

    /// <summary>链接指向私网地址时的提示。</summary>
    public const string UntrustedUrl = "UntrustedUrl";

    /// <summary>下载排队提示。参数依次为：队列位置。</summary>
    public const string Queued = "Queued";

    /// <summary>开始下载提示。</summary>
    public const string Downloading = "Downloading";

    /// <summary>下载进度提示。参数依次为：百分比、速度。</summary>
    public const string DownloadProgress = "DownloadProgress";

    /// <summary>开始上传提示。</summary>
    public const string Uploading = "Uploading";

    /// <summary>上传成功提示。参数依次为：目标会话数量。</summary>
    public const string UploadDone = "UploadDone";

    /// <summary>下载失败提示。</summary>
    public const string DownloadFailed = "DownloadFailed";

    /// <summary>文件过大提示。</summary>
    public const string FileTooLarge = "FileTooLarge";

    /// <summary>磁盘空间不足提示。</summary>
    public const string NoDiskSpace = "NoDiskSpace";

    /// <summary>未知指令提示。</summary>
    public const string UnknownCommand = "UnknownCommand";

    /// <summary>帮助文本。</summary>
    public const string Help = "Help";

    /// <summary>目标站点要求认证提示。</summary>
    public const string AuthRequired = "AuthRequired";

    /// <summary>可用格式不足提示。</summary>
    public const string FormatUnavailable = "FormatUnavailable";

    /// <summary>下载模式选择提示。</summary>
    public const string ModeChoice = "ModeChoice";

    /// <summary>视频+音频按钮。</summary>
    public const string ModeVideoButton = "ModeVideoButton";

    /// <summary>仅音频按钮。</summary>
    public const string ModeAudioButton = "ModeAudioButton";

    /// <summary>仅音频任务完成提示。参数：flac 文件名、mp3 文件名。</summary>
    public const string AudioBundleDone = "AudioBundleDone";

    /// <summary>cookie 使用说明。参数：可用站点列表。</summary>
    public const string CookieUsage = "CookieUsage";

    /// <summary>开始上传提示。参数：站点显示名。</summary>
    public const string CookiePrompt = "CookiePrompt";

    /// <summary>保存成功提示。参数：站点显示名、站点键。</summary>
    public const string CookieSaved = "CookieSaved";

    /// <summary>保存成功但格式可疑提示。参数：站点显示名、站点键。</summary>
    public const string CookieSavedSuspicious = "CookieSavedSuspicious";

    /// <summary>保存失败提示。</summary>
    public const string CookieSaveFailed = "CookieSaveFailed";

    /// <summary>删除成功提示。参数：站点显示名。</summary>
    public const string CookieDeleted = "CookieDeleted";

    /// <summary>无任何站点 cookie 提示。</summary>
    public const string CookieNone = "CookieNone";

    /// <summary>cookie 列表模板。参数：站点键/状态行。</summary>
    public const string CookieListTemplate = "CookieListTemplate";

    /// <summary>未知站点提示。参数：站点键、可用站点。</summary>
    public const string CookieUnknownSite = "CookieUnknownSite";

    /// <summary>cookies 文件过大提示。</summary>
    public const string CookieFileTooLarge = "CookieFileTooLarge";

    /// <summary>cookies 文件无效提示。</summary>
    public const string CookieInvalidFile = "CookieInvalidFile";

    /// <summary>上传超时提示。</summary>
    public const string CookieExpired = "CookieExpired";

    /// <summary>更新无需执行提示。</summary>
    public const string UpdateNotNeeded = "UpdateNotNeeded";

    /// <summary>更新失败提示。</summary>
    public const string UpdateFailed = "UpdateFailed";

    /// <summary>更新失败提示：无法读取本地工具版本。</summary>
    public const string UpdateFailedLocalVersion = "UpdateFailedLocalVersion";

    /// <summary>更新失败提示：无法获取最新版本信息。</summary>
    public const string UpdateFailedLatestVersion = "UpdateFailedLatestVersion";

    /// <summary>更新失败提示：新版本下载失败。</summary>
    public const string UpdateFailedDownload = "UpdateFailedDownload";

    /// <summary>更新失败提示：二进制替换失败（已回滚）。</summary>
    public const string UpdateFailedReplace = "UpdateFailedReplace";

    /// <summary>状态信息模板。参数依次为：运行时间、进行中、排队、yt-dlp 版本、ffmpeg 版本、可用磁盘空间。</summary>
    public const string StatusTemplate = "StatusTemplate";

    /// <summary>状态信息首行：bot 自身版本。参数：版本号（如 2.4.0）。</summary>
    public const string StatusBotVersion = "StatusBotVersion";

    /// <summary>链接正在处理中提示（URL 去重入队失败时）。</summary>
    public const string Busy = "Busy";

    /// <summary>指令仅限私聊提示。</summary>
    public const string CommandPrivateOnly = "CommandPrivateOnly";

    /// <summary>部分会话上传失败提示（附加在完成消息末尾）。</summary>
    public const string PartialFailures = "PartialFailures";

    /// <summary>更新完成标题行。</summary>
    public const string UpdateDoneHeader = "UpdateDoneHeader";

    /// <summary>更新结果行：工具名、旧版本、新版本。</summary>
    public const string UpdateLineUpdated = "UpdateLineUpdated";

    /// <summary>更新结果行：工具名、当前版本（已是最新）。</summary>
    public const string UpdateLineUpToDate = "UpdateLineUpToDate";

    /// <summary>更新结果行：工具名（未配置安装路径）。</summary>
    public const string UpdateLineNotConfigured = "UpdateLineNotConfigured";

    /// <summary>更新结果行：工具名（更新失败）。</summary>
    public const string UpdateLineFailed = "UpdateLineFailed";

    /// <summary>cookie 列表单行模板。参数：显示名、站点键、状态。</summary>
    public const string CookieListLine = "CookieListLine";

    /// <summary>cookie 已保存状态。</summary>
    public const string CookieStateSaved = "CookieStateSaved";

    /// <summary>cookie 无状态。</summary>
    public const string CookieStateNone = "CookieStateNone";

    /// <summary>未知值（版本、磁盘空间等）。</summary>
    public const string Unknown = "Unknown";

    /// <summary>运行时间：天数。参数：天数。</summary>
    public const string UptimeDays = "UptimeDays";

    /// <summary>运行时间：小时数。参数：小时数。</summary>
    public const string UptimeHours = "UptimeHours";

    /// <summary>运行时间：分钟数。参数：分钟数。</summary>
    public const string UptimeMinutes = "UptimeMinutes";

    /// <summary>媒体说明标题行。参数：标题。</summary>
    public const string CaptionTitle = "CaptionTitle";

    /// <summary>媒体说明来源行。参数：来源 URL。</summary>
    public const string CaptionSource = "CaptionSource";

    /// <summary>语言选择提示（首次私聊弹窗与 /language 命令）。</summary>
    public const string LanguagePrompt = "LanguagePrompt";

    /// <summary>语言设置成功回执。参数：语言显示名。</summary>
    public const string LanguageSaved = "LanguageSaved";

    /// <summary>简体中文语言显示名（按钮文本，不翻译）。</summary>
    public const string LanguageNameZh = "LanguageNameZh";

    /// <summary>English 语言显示名（按钮文本，不翻译）。</summary>
    public const string LanguageNameEn = "LanguageNameEn";

    /// <summary>/config 使用说明。</summary>
    public const string ConfigUsage = "ConfigUsage";

    /// <summary>/config list 模板。参数：键值行列表。</summary>
    public const string ConfigListTemplate = "ConfigListTemplate";

    /// <summary>/config list 单行。参数：键、生效值、来源。</summary>
    public const string ConfigListLine = "ConfigListLine";

    /// <summary>配置来源：overlay 覆盖。</summary>
    public const string ConfigSourceOverlay = "ConfigSourceOverlay";

    /// <summary>配置来源：config.conf。</summary>
    public const string ConfigSourceConfig = "ConfigSourceConfig";

    /// <summary>配置来源：内置默认值。</summary>
    public const string ConfigSourceDefault = "ConfigSourceDefault";

    /// <summary>/config set 回执。参数：键名。</summary>
    public const string ConfigSetApplied = "ConfigSetApplied";

    /// <summary>/config reset 回执。参数：键名。</summary>
    public const string ConfigResetApplied = "ConfigResetApplied";

    /// <summary>/config reset-all 回执。</summary>
    public const string ConfigResetAllApplied = "ConfigResetAllApplied";

    /// <summary>重启后生效通知（pending-notify）。参数：键名。</summary>
    public const string ConfigApplied = "ConfigApplied";

    /// <summary>配置校验失败。参数：双语错误文本。</summary>
    public const string ConfigRejected = "ConfigRejected";

    /// <summary>未知配置键。参数：键名。</summary>
    public const string ConfigUnknownKey = "ConfigUnknownKey";

    /// <summary>安装锁键不可经 /config 修改。参数：键名。</summary>
    public const string ConfigLockedKey = "ConfigLockedKey";

    /// <summary>连接/路径类键的风险警告（追加在 /config set 回执后）。</summary>
    public const string ConfigRiskWarning = "ConfigRiskWarning";

    /// <summary>键未被覆盖，无需重置。参数：键名。</summary>
    public const string ConfigNotOverridden = "ConfigNotOverridden";

    /// <summary>同值 set 已生效，无需重启。参数：键名。</summary>
    public const string ConfigNoChange = "ConfigNoChange";

    /// <summary>配置/通知写入失败。</summary>
    public const string ConfigSaveFailed = "ConfigSaveFailed";

    /// <summary>空值的展示占位。</summary>
    public const string ValueEmpty = "ValueEmpty";

    /// <summary>/access 使用说明。</summary>
    public const string AccessUsage = "AccessUsage";

    /// <summary>条目类型：用户。</summary>
    public const string AccessTypeUser = "AccessTypeUser";

    /// <summary>条目类型：频道/群组。</summary>
    public const string AccessTypeChannel = "AccessTypeChannel";

    /// <summary>添加成功（回执与重启通知共用）。参数：类型、ID。</summary>
    public const string AccessAdded = "AccessAdded";

    /// <summary>移除成功（回执与重启通知共用）。参数：类型、ID。</summary>
    public const string AccessRemoved = "AccessRemoved";

    /// <summary>已在列表中，无需重复添加。</summary>
    public const string AccessAlreadyAdded = "AccessAlreadyAdded";

    /// <summary>不在列表中。</summary>
    public const string AccessNotFound = "AccessNotFound";

    /// <summary>条目来自安装配置，不可经 /access 删除。</summary>
    public const string AccessRemovedFromConfig = "AccessRemovedFromConfig";

    /// <summary>无效 ID。参数：原始输入。</summary>
    public const string AccessInvalidId = "AccessInvalidId";

    /// <summary>/access list 模板。参数：条目行列表。</summary>
    public const string AccessListTemplate = "AccessListTemplate";

    /// <summary>/access list 单行。参数：类型、ID、来源。</summary>
    public const string AccessListLine = "AccessListLine";

    /// <summary>白名单来源：安装配置。</summary>
    public const string AccessSourceConfig = "AccessSourceConfig";

    /// <summary>白名单来源：bot 添加。</summary>
    public const string AccessSourceOverlay = "AccessSourceOverlay";

    /// <summary>链接为空。</summary>
    public const string UrlEmpty = "UrlEmpty";

    /// <summary>链接过长。</summary>
    public const string UrlTooLong = "UrlTooLong";

    /// <summary>链接包含非法字符。</summary>
    public const string UrlInvalidChar = "UrlInvalidChar";

    /// <summary>链接格式无效。</summary>
    public const string UrlInvalidFormat = "UrlInvalidFormat";

    /// <summary>仅支持 http/https 链接。</summary>
    public const string UrlSchemeNotAllowed = "UrlSchemeNotAllowed";

    /// <summary>链接包含用户名信息。</summary>
    public const string UrlUserInfo = "UrlUserInfo";

    /// <summary>链接主机名无效。</summary>
    public const string UrlInvalidHost = "UrlInvalidHost";

    /// <summary>链接主机名包含非法字符。</summary>
    public const string UrlInvalidHostChar = "UrlInvalidHostChar";

    /// <summary>指令菜单：update。</summary>
    public const string MenuUpdate = "MenuUpdate";

    /// <summary>指令菜单：cookie。</summary>
    public const string MenuCookie = "MenuCookie";

    /// <summary>指令菜单：cookies。</summary>
    public const string MenuCookies = "MenuCookies";

    /// <summary>指令菜单：status。</summary>
    public const string MenuStatus = "MenuStatus";

    /// <summary>指令菜单：language。</summary>
    public const string MenuLanguage = "MenuLanguage";

    /// <summary>指令菜单：config。</summary>
    public const string MenuConfig = "MenuConfig";

    /// <summary>指令菜单：access。</summary>
    public const string MenuAccess = "MenuAccess";

    /// <summary>指令菜单：help。</summary>
    public const string MenuHelp = "MenuHelp";
}