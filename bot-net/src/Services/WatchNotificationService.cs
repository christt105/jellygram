using Bot.CallbackQueries.Callbacks.Watch;
using Bot.Models;
using Bot.Utils;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Bot.Services;

/// <summary>
/// Polls /watch/pending-notify and sends a Telegram message with a Confirm/Correct pair of
/// buttons for each newly detected file. Also polls /watch?status=removed to catch a row that
/// got deleted from disk after its notify message went out but before it was actioned, editing
/// that message to say so instead of leaving a dead button behind — a lower-overhead approach
/// than reacting to the FileSystemWatcher's own Deleted event directly, since that event fires
/// in bot-net's own process and only WatchedFolderService listens to it. Also polls
/// /watch?status=confirmed and /watch?status=corrected to pick up rows actioned from the web
/// instead of a Telegram button tap — those never go through WatchedFileMoveFlow otherwise,
/// since there's no Telegram callback to trigger it.
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
                await ProcessWebResolvedAsync("confirmed");
                await ProcessWebResolvedAsync("corrected");
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
            var text = BuildNotifyMessageText(row);

            var sent = await _bot.SendMessage(AuthConfig.OwnerUserId, text, ParseMode.Html,
                replyMarkup: BuildNotifyButtons(row));

            _registry.Track(row.Id, sent.Chat.Id, sent.MessageId);

            Log.Info($"[WatchNotification] Notified about {row.Filename} (row {row.Id}).");
            await _apiClient.PatchWatchedFileStatusAsync(row.Id, "notified");
        }
    }

    /// <summary>Also used by <see cref="CancelConfirmWatchedFileCallback"/> to restore the
    /// original notify message when the user backs out of the confirmation prompt.</summary>
    public static string BuildNotifyMessageText(WatchedFile row) =>
        WatchedFileMessages.BuildNotifyText(
            row.Filename, row.GuessMediaType, row.GuessTitle, row.GuessSeason, row.GuessEpisode, row.Confidence,
            row.GuessTmdbId);

    /// <summary>Also used by <see cref="CancelConfirmWatchedFileCallback"/> to restore the
    /// original notify message when the user backs out of the confirmation prompt.</summary>
    public static InlineKeyboardButton[][] BuildNotifyButtons(WatchedFile row)
    {
        var rows = new List<InlineKeyboardButton[]>();

        if (row.GuessTmdbId.HasValue)
        {
            rows.Add([
                InlineKeyboardButton.WithCallbackData(
                    "✅ Confirm",
                    AskConfirmWatchedFileCallback.Pack(row.Id, row.GuessTmdbId.Value, row.GuessSeason, row.GuessEpisode))
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

    /// <summary>
    /// A row confirmed/corrected from the web is moved here, whether or not a notify message for
    /// it ever went out: the same row can perfectly well have been announced on Telegram first and
    /// actioned on the web afterwards, and its still-live message is then edited with the outcome
    /// so it doesn't keep offering buttons for a file that is no longer in the downloads folder.
    /// Not racing the move a Telegram tap starts for that same row is <see cref="WatchedFileMoveClaims"/>'s
    /// job, inside the move flow itself — whichever path claims the row first is the one that moves
    /// it and the one that reports the outcome. The message is untracked only once there is an
    /// outcome to show it, so a move that blows up mid-way leaves the row listed for the next cycle
    /// to retry, message reference and all.
    /// </summary>
    private async Task ProcessWebResolvedAsync(string status)
    {
        var rows = await _apiClient.GetWatchedFilesAsync(status);
        if (rows is null || rows.Count == 0) return;

        foreach (var row in rows)
        {
            var resolution = WatchedFileResolution.FromWatchedFile(row);
            if (resolution is null)
            {
                Log.Error($"[WatchNotification] Row {row.Id} is {status} without a resolved guess, skipping.");
                continue;
            }

            Log.Info($"[WatchNotification] Picking up {row.Filename} (row {row.Id}, {status} from the web).");
            var outcome = await WatchedFileMoveFlow.MoveAndReportAsync(_apiClient, row.Id, resolution);
            if (outcome is null)
            {
                Log.Info($"[WatchNotification] {row.Filename} (row {row.Id}) is already being moved, leaving it.");
                continue;
            }

            Log.Info($"[WatchNotification] {row.Filename} (row {row.Id}): {outcome.Value.Text}");

            if (_registry.TryUntrack(row.Id, out var reference))
            {
                await _bot.EditMessageText(reference.ChatId, reference.MessageId, outcome.Value.Text);
            }
        }
    }
}
