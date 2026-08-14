namespace TGBot.Update;

/// <summary>
/// 单个工具更新的状态。
/// </summary>
public enum ToolUpdateStatus
{
    /// <summary>未配置安装路径，跳过更新。</summary>
    NotConfigured,

    /// <summary>本地已是最新版本，无需更新。</summary>
    AlreadyUpToDate,

    /// <summary>更新成功。</summary>
    Updated,

    /// <summary>更新失败。</summary>
    Failed,
}

/// <summary>
/// 单个工具的更新结果。
/// </summary>
/// <param name="Tool">工具名称。</param>
/// <param name="LocalVersion">本地版本（未知为 <see langword="null"/>）。</param>
/// <param name="LatestVersion">最新版本（未知为 <see langword="null"/>）。</param>
/// <param name="Status">更新状态。</param>
public sealed record ToolUpdateResult(
    string Tool,
    ToolVersion? LocalVersion,
    ToolVersion? LatestVersion,
    ToolUpdateStatus Status);

/// <summary>
/// 整体更新报告。
/// </summary>
/// <param name="Tools">各工具的更新结果。</param>
public sealed record UpdateReport(IReadOnlyList<ToolUpdateResult> Tools)
{
    /// <summary>
    /// 是否存在失败的更新。
    /// </summary>
    public bool HasFailures => Tools.Any(t => t.Status == ToolUpdateStatus.Failed);
}

/// <summary>
/// 更新失败异常。
/// </summary>
public sealed class UpdateException : Exception
{
    /// <summary>
    /// 面向用户的中文提示（不含内部细节）。
    /// </summary>
    public string UserMessage { get; }

    /// <summary>
    /// 初始化 <see cref="UpdateException"/>。
    /// </summary>
    /// <param name="userMessage">面向用户的中文提示。</param>
    /// <param name="detail">内部详细日志（仅记录，不发送给用户）。</param>
    public UpdateException(string userMessage, string? detail = null)
        : base(detail ?? userMessage)
    {
        UserMessage = userMessage;
    }
}
