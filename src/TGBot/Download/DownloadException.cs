namespace TGBot.Download;

/// <summary>
/// 下载失败原因分类，用于向用户给出准确提示而不泄露内部细节。
/// </summary>
public enum DownloadFailureReason
{
    /// <summary>一般性失败（链接失效、格式错误、网络问题等）。</summary>
    Failed,

    /// <summary>文件超出大小上限。</summary>
    TooLarge,

    /// <summary>磁盘空间不足。</summary>
    NoDiskSpace,

    /// <summary>任务超时。</summary>
    Timeout,

    /// <summary>任务被取消（关机或用户中断）。</summary>
    Cancelled,
}

/// <summary>
/// 下载失败异常。
/// </summary>
public sealed class DownloadException : Exception
{
    /// <summary>
    /// 失败原因分类。
    /// </summary>
    public DownloadFailureReason Reason { get; }

    /// <summary>
    /// 面向用户的中文提示（不含内部细节）。
    /// </summary>
    public string UserMessage { get; }

    /// <summary>
    /// 初始化 <see cref="DownloadException"/>。
    /// </summary>
    /// <param name="reason">失败原因分类。</param>
    /// <param name="userMessage">面向用户的中文提示。</param>
    /// <param name="detail">内部详细日志（仅记录，不发送给用户）。</param>
    public DownloadException(DownloadFailureReason reason, string userMessage, string? detail = null)
        : base(detail ?? userMessage)
    {
        Reason = reason;
        UserMessage = userMessage;
    }
}
