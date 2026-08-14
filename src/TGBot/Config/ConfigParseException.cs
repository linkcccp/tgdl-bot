namespace TGBot.Config;

/// <summary>
/// 配置解析错误。消息为面向用户的中文提示，不含内部实现细节。
/// </summary>
public sealed class ConfigParseException : Exception
{
    /// <summary>
    /// 初始化 <see cref="ConfigParseException"/>。
    /// </summary>
    /// <param name="message">中文错误提示。</param>
    public ConfigParseException(string message)
        : base(message)
    {
    }
}
