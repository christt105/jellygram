using Bot.Services;
using Bot.Utils;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Bot.CallbackQueries.Callbacks.Watch;

/// <summary>
/// First tap of the "Confirm" button: previews the destination path (including whether it
/// collides with a file already there) and requires a second tap on
/// <see cref="ConfirmWatchedFileCallback"/> before anything actually moves.
/// </summary>
[Callback(Id)]
public class AskConfirmWatchedFileCallback : ICallbackQuery
{
    public const string Id = "ask-confirm-watched-file";

    private readonly WTelegram.Bot _bot;
    private readonly ApiClient _apiClient;
    private readonly int _watchedFileId;
    private readonly int _tmdbId;
    private readonly int? _season;
    private readonly int? _episode;

    private AskConfirmWatchedFileCallback(
        int watchedFileId, int tmdbId, int? season, int? episode, WTelegram.Bot bot, ApiClient apiClient)
    {
        _watchedFileId = watchedFileId;
        _tmdbId = tmdbId;
        _season = season;
        _episode = episode;
        _bot = bot;
        _apiClient = apiClient;
    }

    public async Task ExecuteAsync(Message? message)
    {
        var rows = await _apiClient.GetWatchedFilesAsync();
        var row = rows?.FirstOrDefault(r => r.Id == _watchedFileId);

        if (row?.GuessMediaType is null || row.GuessTitle is null)
        {
            await _bot.EditMessageText(message!.Chat.Id, message.MessageId, "This file is no longer tracked.");
            return;
        }

        var destPath = WatchedFileMoveFlow.BuildProspectiveDestination(
            row.GuessMediaType, row.GuessTitle, _tmdbId, _season, _episode, row.Filename);
        var collision = System.IO.File.Exists(destPath);

        var text = WatchedFileMessages.BuildConfirmPromptText(row.Filename, destPath, collision);
        var keyboard = new InlineKeyboardMarkup([
            [
                InlineKeyboardButton.WithCallbackData(
                    collision ? "⚠️ Move anyway" : "✅ Yes, move",
                    ConfirmWatchedFileCallback.Pack(_watchedFileId, _tmdbId, _season, _episode)),
                InlineKeyboardButton.WithCallbackData(
                    "✖️ Cancel", CancelConfirmWatchedFileCallback.Pack(_watchedFileId))
            ]
        ]);

        await _bot.EditMessageText(message!.Chat.Id, message.MessageId, text, replyMarkup: keyboard);
    }

    public static string Pack(int watchedFileId, int tmdbId, int? season, int? episode) =>
        CallbackDataPacker.Pack(Id,
            [watchedFileId.ToString(), tmdbId.ToString(), season?.ToString() ?? "", episode?.ToString() ?? ""]);

    public static ICallbackQuery Create(string[] fields, BotDispatcher dispatcher)
    {
        var season = string.IsNullOrEmpty(fields[2]) ? (int?)null : int.Parse(fields[2]);
        var episode = string.IsNullOrEmpty(fields[3]) ? (int?)null : int.Parse(fields[3]);

        return new AskConfirmWatchedFileCallback(
            int.Parse(fields[0]), int.Parse(fields[1]), season, episode, dispatcher.Bot, dispatcher.ApiClient);
    }
}
