using Bot.Handlers;
using Bot.Services;
using Bot.Utils;
using Telegram.Bot.Types;

namespace Bot.CallbackQueries.Callbacks.Watch;

/// <summary>
/// Arms a pending action expecting a reply of the form "tmdb &lt;id&gt;" (optionally
/// " season &lt;n&gt; episode &lt;n&gt;"), then runs the same resolve+move flow as
/// <see cref="ConfirmWatchedFileCallback"/> but through /watch/{id}/correct.
/// </summary>
[Callback(Id)]
public class CorrectWatchedFileCallback : ICallbackQuery
{
    public const string Id = "correct-watched-file";

    private readonly WTelegram.Bot _bot;
    private readonly ApiClient _apiClient;
    private readonly WatchedFileMessageRegistry _registry;
    private readonly PendingActionHandler _pendingActionHandler;
    private readonly int _watchedFileId;

    private CorrectWatchedFileCallback(
        int watchedFileId, WTelegram.Bot bot, ApiClient apiClient,
        WatchedFileMessageRegistry registry, PendingActionHandler pendingActionHandler)
    {
        _watchedFileId = watchedFileId;
        _bot = bot;
        _apiClient = apiClient;
        _registry = registry;
        _pendingActionHandler = pendingActionHandler;
    }

    public async Task ExecuteAsync(Message? message)
    {
        var filename = WatchedFileMessages.ExtractFilenameFromNotifyText(message!.Text);

        await _bot.EditMessageText(message.Chat.Id, message.MessageId,
            WatchedFileMessages.BuildCorrectionPromptText(filename));

        await ArmPendingAction(message, filename);
    }

    /// Re-armed on every invalid reply too, so a typo doesn't dead-end the conversation with no
    /// way to retry other than pressing "Correct" again from scratch.
    private Task ArmPendingAction(Message message, string filename) =>
        _pendingActionHandler.SetPendingAction(new PendingActionHandler.PendingAction(
            id: $"correct-watched-file-{_watchedFileId}",
            chatId: message.Chat.Id,
            owner: message.MessageId,
            callback: async text => await HandleReply(text, message, filename),
            cancelCallback: async () =>
            {
                await _bot.EditMessageText(message.Chat.Id, message.MessageId, "Correction cancelled.");
            }
        ));

    private async Task HandleReply(string text, Message message, string filename)
    {
        if (!WatchedFileMessages.TryParseCorrection(text, out var parsed))
        {
            await _bot.EditMessageText(message.Chat.Id, message.MessageId,
                WatchedFileMessages.BuildCorrectionInvalidText());
            await ArmPendingAction(message, filename);
            return;
        }

        var resolution = await _apiClient.CorrectWatchedFileAsync(
            _watchedFileId, parsed.TmdbId, parsed.Season, parsed.Episode);
        await WatchedFileMoveFlow.ExecuteAsync(_bot, _apiClient, _registry, message, _watchedFileId, resolution);
    }

    public static string Pack(int watchedFileId) =>
        CallbackDataPacker.Pack(Id, [watchedFileId.ToString()]);

    public static ICallbackQuery Create(string[] fields, BotDispatcher dispatcher)
    {
        return new CorrectWatchedFileCallback(
            int.Parse(fields[0]), dispatcher.Bot, dispatcher.ApiClient,
            dispatcher.WatchedFileMessages, dispatcher.PendingActionHandler);
    }
}
