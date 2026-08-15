using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace TGBot.Messaging;

/// <summary>
/// 基于 Telegram.Bot 库的客户端实现（Long Polling，连接本地 Bot API Server）。
/// </summary>
public sealed class TelegramClientWrapper : ITelegramClient
{
    private const int FileBufferSize = 81920;

    private readonly ITelegramBotClient _client;
    private readonly ReceiverOptions _receiverOptions = new()
    {
        AllowedUpdates = new[] { UpdateType.Message, UpdateType.ChannelPost, UpdateType.CallbackQuery },
    };

    /// <summary>
    /// 初始化 <see cref="TelegramClientWrapper"/>。
    /// </summary>
    /// <param name="token">Bot Token。</param>
    /// <param name="baseUrl">本地 Bot API Server 地址。</param>
    /// <param name="http">共享 HttpClient。</param>
    /// <param name="cancellationToken">初始化取消令牌。</param>
    public TelegramClientWrapper(string token, string baseUrl, HttpClient http, CancellationToken cancellationToken)
    {
        var options = new TelegramBotClientOptions(token, baseUrl);
        _client = new TelegramBotClient(options, http, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<string> GetBotUsernameAsync(CancellationToken cancellationToken)
    {
        var me = await _client.GetMe(cancellationToken).ConfigureAwait(false);
        return me.Username ?? string.Empty;
    }

    /// <inheritdoc />
    public async Task SendMessageAsync(long chatId, string text, int replyToMessageId, IReadOnlyList<InlineButton>? inlineKeyboard, CancellationToken cancellationToken)
    {
        ReplyParameters? reply = replyToMessageId > 0
            ? new ReplyParameters { MessageId = replyToMessageId, AllowSendingWithoutReply = true }
            : null;
        InlineKeyboardMarkup? markup = null;
        if (inlineKeyboard is { Count: > 0 })
        {
            markup = new InlineKeyboardMarkup(inlineKeyboard
                .Select(b => new[] { InlineKeyboardButton.WithCallbackData(b.Text, b.CallbackData) }));
        }

        await _client.SendMessage(chatId, text, replyParameters: reply, replyMarkup: markup, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SendChatActionAsync(long chatId, BotChatAction action, CancellationToken cancellationToken)
    {
        var mapped = action switch
        {
            BotChatAction.Typing => ChatAction.Typing,
            BotChatAction.UploadVideo => ChatAction.UploadVideo,
            BotChatAction.UploadAudio => ChatAction.UploadVoice,
            _ => ChatAction.UploadDocument,
        };
        await _client.SendChatAction(chatId, mapped, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SendVideoAsync(long chatId, string filePath, string fileName, string? caption, CancellationToken cancellationToken)
    {
        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, FileBufferSize, useAsync: true);
        await _client.SendVideo(
            chatId,
            new InputFileStream(fs, fileName),
            caption: caption,
            supportsStreaming: true,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SendAudioAsync(long chatId, string filePath, string fileName, string? caption, string? performer, string? title, CancellationToken cancellationToken)
    {
        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, FileBufferSize, useAsync: true);
        await _client.SendAudio(
            chatId,
            new InputFileStream(fs, fileName),
            caption: caption,
            performer: performer,
            title: title,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SendDocumentAsync(long chatId, string filePath, string fileName, string? caption, CancellationToken cancellationToken)
    {
        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, FileBufferSize, useAsync: true);
        await _client.SendDocument(
            chatId,
            new InputFileStream(fs, fileName),
            caption: caption,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SetCommandsAsync(CancellationToken cancellationToken)
    {
        var commands = new[]
        {
            new BotCommand { Command = "update", Description = "检查并更新 ffmpeg 与 yt-dlp" },
            new BotCommand { Command = "cookie", Description = "上传指定站点的 cookies" },
            new BotCommand { Command = "cookies", Description = "查看各站点 cookies 状态" },
            new BotCommand { Command = "status", Description = "查看运行状态与版本" },
            new BotCommand { Command = "help", Description = "显示帮助" },
        };
        await _client.SetMyCommands(commands: commands, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DownloadFileAsync(string fileId, string destinationPath, CancellationToken cancellationToken)
    {
        var file = await _client.GetFile(fileId, cancellationToken).ConfigureAwait(false);

        // --local 模式下 getFile 直接返回服务器本地绝对路径，直接复制（零额外传输）。
        if (!string.IsNullOrEmpty(file.FilePath) && File.Exists(file.FilePath))
        {
            File.Copy(file.FilePath, destinationPath, overwrite: true);
            return;
        }

        await using var fs = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, FileBufferSize, useAsync: true);
        await _client.DownloadFile(file, fs, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DropPendingUpdatesAsync(CancellationToken cancellationToken)
    {
        await _client.DropPendingUpdates(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RunLongPollingAsync(
        Func<InboundMessage, CancellationToken, Task> onUpdate,
        Func<Exception, CancellationToken, Task> onPollError,
        CancellationToken cancellationToken)
    {
        Task Handle(ITelegramBotClient _, Telegram.Bot.Types.Update update, CancellationToken ct)
        {
            var msg = Convert(update);
            return msg is null ? Task.CompletedTask : onUpdate(msg, ct);
        }

        Task HandleError(ITelegramBotClient _, Exception ex, CancellationToken ct)
            => onPollError(ex, ct);

        await _client.ReceiveAsync(Handle, HandleError, _receiverOptions, cancellationToken).ConfigureAwait(false);
    }

    private static InboundMessage? Convert(Telegram.Bot.Types.Update update)
    {
        if (update.Message is { } m)
        {
            return new InboundMessage
            {
                ChatId = m.Chat.Id,
                IsPrivate = m.Chat.Type == ChatType.Private,
                SenderUserId = m.From?.Id,
                Text = m.Text,
                Caption = m.Caption,
                TriggerMessageId = m.MessageId,
                DocumentFileId = m.Document?.FileId,
                DocumentFileName = m.Document?.FileName,
                DocumentSizeBytes = m.Document?.FileSize,
            };
        }

        if (update.ChannelPost is { } cp)
        {
            return new InboundMessage
            {
                ChatId = cp.Chat.Id,
                IsPrivate = false,
                SenderUserId = cp.From?.Id,
                Text = cp.Text,
                Caption = cp.Caption,
                TriggerMessageId = cp.MessageId,
            };
        }

        if (update.CallbackQuery is { } cb && cb.Message is { } cbm)
        {
            return new InboundMessage
            {
                ChatId = cbm.Chat.Id,
                IsPrivate = cbm.Chat.Type == ChatType.Private,
                SenderUserId = cb.From.Id,
                TriggerMessageId = cbm.MessageId,
                IsCallback = true,
                CallbackData = cb.Data,
            };
        }

        return null;
    }
}
