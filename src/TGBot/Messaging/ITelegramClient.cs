namespace TGBot.Messaging;

/// <summary>
/// 入站消息模型（与具体 Bot 库解耦，便于单元测试）。
/// </summary>
public sealed class InboundMessage
{
    /// <summary>
    /// 会话 ID。
    /// </summary>
    public required long ChatId { get; init; }

    /// <summary>
    /// 是否为私聊。
    /// </summary>
    public required bool IsPrivate { get; init; }

    /// <summary>
    /// 发送者用户 ID（可能为空）。
    /// </summary>
    public long? SenderUserId { get; init; }

    /// <summary>
    /// 消息文本。
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// 媒体说明（caption）。
    /// </summary>
    public string? Caption { get; init; }

    /// <summary>
    /// 触发消息 ID（用于回复），0 表示未知。
    /// </summary>
    public int TriggerMessageId { get; init; }

    /// <summary>
    /// 用于提取 URL 的完整文本（文本 + 说明）。
    /// </summary>
    public string UrlSearchText => string.IsNullOrEmpty(Text)
        ? (Caption ?? string.Empty)
        : string.IsNullOrEmpty(Caption)
            ? Text
            : Text + "\n" + Caption;
}

/// <summary>
/// Bot 聊天动作（发送中状态）。
/// </summary>
public enum BotChatAction
{
    /// <summary>正在输入。</summary>
    Typing,

    /// <summary>正在上传视频。</summary>
    UploadVideo,

    /// <summary>正在上传音频。</summary>
    UploadAudio,

    /// <summary>正在上传文档。</summary>
    UploadDocument,
}

/// <summary>
/// Telegram 客户端抽象：所有与 Telegram 的交互均通过本接口，便于替换与单测。
/// </summary>
public interface ITelegramClient
{
    /// <summary>
    /// 获取 Bot 用户名。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>Bot 用户名（不含 @）。</returns>
    Task<string> GetBotUsernameAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 发送文本消息。
    /// </summary>
    /// <param name="chatId">目标会话 ID。</param>
    /// <param name="text">文本。</param>
    /// <param name="replyToMessageId">回复的消息 ID，0 表示不回复。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task SendMessageAsync(long chatId, string text, int replyToMessageId, CancellationToken cancellationToken);

    /// <summary>
    /// 发送聊天动作。
    /// </summary>
    /// <param name="chatId">目标会话 ID。</param>
    /// <param name="action">动作类型。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task SendChatActionAsync(long chatId, BotChatAction action, CancellationToken cancellationToken);

    /// <summary>
    /// 发送视频。
    /// </summary>
    /// <param name="chatId">目标会话 ID。</param>
    /// <param name="filePath">本地文件路径。</param>
    /// <param name="fileName">文件名。</param>
    /// <param name="caption">说明（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task SendVideoAsync(long chatId, string filePath, string fileName, string? caption, CancellationToken cancellationToken);

    /// <summary>
    /// 发送音频。
    /// </summary>
    /// <param name="chatId">目标会话 ID。</param>
    /// <param name="filePath">本地文件路径。</param>
    /// <param name="fileName">文件名。</param>
    /// <param name="caption">说明（可空）。</param>
    /// <param name="performer">表演者（可空）。</param>
    /// <param name="title">标题（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task SendAudioAsync(long chatId, string filePath, string fileName, string? caption, string? performer, string? title, CancellationToken cancellationToken);

    /// <summary>
    /// 发送文档（非视频/音频格式的兜底）。
    /// </summary>
    /// <param name="chatId">目标会话 ID。</param>
    /// <param name="filePath">本地文件路径。</param>
    /// <param name="fileName">文件名。</param>
    /// <param name="caption">说明（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task SendDocumentAsync(long chatId, string filePath, string fileName, string? caption, CancellationToken cancellationToken);

    /// <summary>
    /// 设置机器人指令菜单。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task SetCommandsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 丢弃挂起的旧更新。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task DropPendingUpdatesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 启动长轮询并阻塞，直到被取消。
    /// </summary>
    /// <param name="onUpdate">处理单条入站消息。</param>
    /// <param name="onPollError">轮询错误的回调（不抛出则继续轮询）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>轮询正常退出时完成。</returns>
    Task RunLongPollingAsync(
        Func<InboundMessage, CancellationToken, Task> onUpdate,
        Func<Exception, CancellationToken, Task> onPollError,
        CancellationToken cancellationToken);
}
