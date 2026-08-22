using Bot.CallbackQueries.Callbacks.Watch;
using Bot.Models;
using Bot.Utils;
using Telegram.Bot.Types.ReplyMarkups;

namespace Bot.Services;

/// <summary>
/// Polls /watch/pending-notify and sends a Telegram message with a Confirm/Correct pair of
/// buttons for each newly detected file. Also polls /watch?status=removed to catch a row that
/// got deleted from disk after its notify message went out but before it was actioned, editing
/// that message to say so instead of leaving a dead button behind — a lower-overhead approach
/// than reacting to the FileSystemWatcher's own Deleted event directly, since that event fires
/// in bot-net's own process and only WatchedFolderService listens to it.
/// </summary>
public class WatchNotificationService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly WTelegram.Bot _bot;
    private readonly ApiClient _apiClient;
    private readonly WatchedFileMessageRegistry _registry;

    public WatchNotificationService(WTelegram.Bot bot, ApiClient apiClient, WatchedFileMessageRegistry registry)
    {
        _bot = bot;
        _apiClient = apiClient;
        _registry = registry;
    }

    public async Task PollAndProcessAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await NotifyPendingAsync();
                await ReconcileRemovedAsync();
            }
            catch (Exception ex)
            {
                Log.Error("[WatchNotification] Error in polling loop", ex);
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task NotifyPendingAsync()
    {
        var pending = await _apiClient.GetPendingNotifyWatchedFilesAsync();
        if (pending is null) return;

        foreach (var row in pending)
        {
            var text = WatchedFileMessages.BuildNotifyText(
                row.Filename, row.GuessMediaType, row.GuessTitle, row.GuessSeason, row.GuessEpisode, row.Confidence);

            var sent = await _bot.SendMessage(AuthConfig.OwnerUserId, text, replyMarkup: BuildNotifyButtons(row));

            _registry.Track(row.Id, sent.Chat.Id, sent.MessageId);

            Log.Info($"[WatchNotification] Notified about {row.Filename} (row {row.Id}).");
            await _apiClient.PatchWatchedFileStatusAsync(row.Id, "notified");
        }
    }

    private static InlineKeyboardButton[][] BuildNotifyButtons(WatchedFile row)
    {
        var rows = new List<InlineKeyboardButton[]>();

        if (row.GuessTmdbId.HasValue)
        {
            rows.Add([
                InlineKeyboardButton.WithCallbackData(
                    "✅ Confirm",
                    ConfirmWatchedFileCallback.Pack(row.Id, row.GuessTmdbId.Value, row.GuessSeason, row.GuessEpisode))
            ]);
        }

        rows.Add([InlineKeyboardButton.WithCallbackData("✏️ Correct", CorrectWatchedFileCallback.Pack(row.Id))]);

        return rows.ToArray();
    }

    private async Task ReconcileRemovedAsync()
    {
        var live = _registry.Snapshot();
        if (live.Count == 0) return;

        var removed = await _apiClient.GetWatchedFilesAsync("removed");
        if (removed is null || removed.Count == 0) return;

        foreach (var row in removed)
        {
            if (!live.ContainsKey(row.Id)) continue;
            if (!_registry.TryUntrack(row.Id, out var reference)) continue;

            Log.Info($"[WatchNotification] {row.Filename} (row {row.Id}) was removed before it was confirmed.");
            await _bot.EditMessageText(
                reference.ChatId, reference.MessageId,
                WatchedFileMessages.BuildRemovedWhileNotifiedText(row.Filename));
        }
    }
}
