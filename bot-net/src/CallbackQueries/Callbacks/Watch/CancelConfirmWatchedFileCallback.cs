using Bot.Services;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Bot.CallbackQueries.Callbacks.Watch;

/// <summary>Backs out of the confirmation prompt, restoring the original notify message and
/// its Confirm/Correct buttons so the row can still be actioned later.</summary>
[Callback(Id)]
public class CancelConfirmWatchedFileCallback : ICallbackQuery
{
    public const string Id = "cancel-confirm-watched-file";

    private readonly WTelegram.Bot _bot;
    private readonly ApiClient _apiClient;
    private readonly int _watchedFileId;

    private CancelConfirmWatchedFileCallback(int watchedFileId, WTelegram.Bot bot, ApiClient apiClient)
    {
        _watchedFileId = watchedFileId;
        _bot = bot;
        _apiClient = apiClient;
    }

    public async Task ExecuteAsync(Message? message)
    {
        var rows = await _apiClient.GetWatchedFilesAsync();
        var row = rows?.FirstOrDefault(r => r.Id == _watchedFileId);

        if (row is null)
        {
            await _bot.EditMessageText(message!.Chat.Id, message.MessageId, "This file is no longer tracked.");
            return;
        }

        await _bot.EditMessageText(
            message!.Chat.Id, message.MessageId,
            WatchNotificationService.BuildNotifyMessageText(row), ParseMode.Html,
            replyMarkup: WatchNotificationService.BuildNotifyButtons(row));
    }

    public static string Pack(int watchedFileId) => CallbackDataPacker.Pack(Id, [watchedFileId.ToString()]);

    public static ICallbackQuery Create(string[] fields, BotDispatcher dispatcher) =>
        new CancelConfirmWatchedFileCallback(int.Parse(fields[0]), dispatcher.Bot, dispatcher.ApiClient);
}
