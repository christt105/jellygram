using Bot.Handlers;
using Bot.Services;
using Telegram.Bot.Types.Enums;
using Message = WTelegram.Types.Message;

namespace Bot.Commands;

/// <summary>
/// Re-runs the TMDB guess for every unresolved watched file (e.g. after fixing the filename
/// parser, or if TMDB was down when files were first detected), then refreshes the Telegram
/// message of any row that already has one live so its Confirm button doesn't keep pointing at
/// the old guess.
/// </summary>
public class ReidentifyWatchedFilesCommand : ICommand
{
    private readonly BotDispatcher _dispatcher;

    public ReidentifyWatchedFilesCommand(BotDispatcher dispatcher) => _dispatcher = dispatcher;

    public async Task Execute(string[] args, Message msg)
    {
        var updated = await _dispatcher.ApiClient.ReidentifyWatchedFilesAsync();
        if (updated is null)
        {
            await _dispatcher.Bot.SendMessage(msg.Chat.Id, "❌ Failed to re-identify watched files.");
            return;
        }

        var live = _dispatcher.WatchedFileMessages.Snapshot();
        var refreshed = 0;

        foreach (var row in updated)
        {
            if (!live.TryGetValue(row.Id, out var reference)) continue;

            await _dispatcher.Bot.EditMessageText(
                reference.ChatId, reference.MessageId,
                WatchNotificationService.BuildNotifyMessageText(row), ParseMode.Html,
                replyMarkup: WatchNotificationService.BuildNotifyButtons(row));
            refreshed++;
        }

        await _dispatcher.Bot.SendMessage(msg.Chat.Id,
            $"🔄 Re-identified {updated.Count} file(s), refreshed {refreshed} live message(s).");
    }

    public string Key => "/reidentify";
    public string Description => "Re-run TMDB identification for every unresolved watched file.";
    public string Usage => "/reidentify";
}
