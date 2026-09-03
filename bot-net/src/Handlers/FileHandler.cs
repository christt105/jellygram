using System.Text.Json;
using Bot;
using Bot.Models;
using Bot.Services;
using Bot.Utils;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Message = WTelegram.Types.Message;

namespace TelegramBot.Handlers;

public class FileHandler
{
    private readonly WTelegram.Bot _bot;
    private readonly ApiClient _apiClient;
    private readonly TaskQueue _queue;
    private readonly Bot.Handlers.PendingActionHandler _pendingActionHandler;

    public FileHandler(BotDispatcher bot)
    {
        _bot = bot.Bot;
        _queue = bot.Queue;
        _apiClient = bot.ApiClient;
        _pendingActionHandler = bot.PendingActionHandler;
    }

    public async Task Handle(Message msg, UpdateType type)
    {
        string? fileName = null;
        long fileSize = 0;
        string? mimeType = null;

        if (msg.Document != null)
        {
            fileName = msg.Document.FileName;
            fileSize = msg.Document.FileSize ?? 0;
            mimeType = msg.Document.MimeType;
        }
        else if (msg.Video != null)
        {
            fileName = msg.Video.FileName;
            fileSize = msg.Video.FileSize ?? 0;
            mimeType = msg.Video.MimeType;
        }

        if (string.IsNullOrEmpty(fileName))
        {
            fileName = $"video_{msg.MessageId}.mp4";
        }

        var forward = ForwardMetadata.Extract(msg.ForwardOrigin, msg.TLMessage);

        var uploadFile = new UploadFile
        {
            MessageId = msg.MessageId,
            FileName = fileName,
            FileSize = fileSize,
            UploadDate = msg.Date.ToString("O"),
            MimeType = mimeType ?? "video/mp4",
            DocumentId = forward.DocumentId,
            FwdFromType = forward.FromType,
            FwdFromId = forward.FromId,
            FwdFromName = forward.FromName,
            FwdFromHidden = forward.Hidden
        };

        var message = @$"📥 New file received:
Id: {uploadFile.MessageId}
Name: {uploadFile.FileName}
Date: {uploadFile.UploadDate}
File size: {Beautify.FormatSize(uploadFile.FileSize)}
File extension: {uploadFile.MimeType}

Starting to process...";

        Log.Info($"File received {message}");

        var answer = await _bot.SendMessage(
            msg.Chat.Id,
            message,
            replyParameters: new ReplyParameters { MessageId = msg.MessageId });

        await _queue.Enqueue(() => IdentifyFile(uploadFile, answer));
    }

    private async Task IdentifyFile(UploadFile file, Message answer)
    {
        var result = await _apiClient.UploadAsync(file);

        Log.Info(JsonSerializer.Serialize(result));
        
        if (result.MovieId == null && result.EpisodeId == null && result.SeasonId == null && result.CollectionId.HasValue)
        {
            var message = @$"⚠️ Could not automatically identify the file.
Id: {file.MessageId}
Name: {file.FileName}

Please reply to this message with the numeric TMDB ID to link it manually.";

            await _bot.EditMessageText(answer.Chat.Id, answer.MessageId, message);
            
            await _pendingActionHandler.SetPendingAction(new Bot.Handlers.PendingActionHandler.PendingAction(
                id: $"identify_{result.CollectionId}",
                chatId: answer.Chat.Id,
                owner: this,
                callback: async (text) =>
                {
                    if (int.TryParse(text.Trim(), out var tmdbId))
                    {
                        await _bot.SendMessage(answer.Chat.Id, $"Attempting to link with TMDB ID {tmdbId}...");
                        var success = await _apiClient.IdentifyCollectionAsync(result.CollectionId.Value, tmdbId);
                        if (success)
                        {
                            await _bot.SendMessage(answer.Chat.Id, "✅ File linked successfully.");
                        }
                        else
                        {
                            await _bot.SendMessage(answer.Chat.Id, "❌ An error occurred while linking the file (possibly an incorrect ID).");
                        }
                    }
                    else
                    {
                        await _bot.SendMessage(answer.Chat.Id, "❌ Invalid ID. Operation cancelled.");
                    }
                },
                cancelCallback: async () =>
                {
                    await _bot.SendMessage(answer.Chat.Id, "Link operation cancelled.");
                }
            ));
        }
        else
        {
            var message = @$"✅ File processed and identified successfully:
Id: {file.MessageId}
Name: {file.FileName}
Movie ID: {result.MovieId}
Season ID: {result.SeasonId}
Episode ID: {result.EpisodeId}
Collection ID: {result.CollectionId}";
            await _bot.EditMessageText(answer.Chat.Id, answer.MessageId, message);
        }
    }
}