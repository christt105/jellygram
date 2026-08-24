using System.Collections.Concurrent;

namespace Bot.Services;

public readonly record struct WatchedFileMessageRef(long ChatId, int MessageId);

/// <summary>
/// Tracks the Telegram message sent for each watched-file row that is still awaiting a
/// decision (its Confirm/Correct buttons are still live). <see cref="WatchNotificationService"/>
/// uses it to find and edit a still-live message when the row is later reported removed from
/// disk before it was confirmed; the confirm/correct callbacks untrack a row once they have
/// resolved it, successfully or not.
/// </summary>
public class WatchedFileMessageRegistry
{
    private readonly ConcurrentDictionary<int, WatchedFileMessageRef> _live = new();

    public void Track(int watchedFileId, long chatId, int messageId) =>
        _live[watchedFileId] = new WatchedFileMessageRef(chatId, messageId);

    public bool TryUntrack(int watchedFileId, out WatchedFileMessageRef reference) =>
        _live.TryRemove(watchedFileId, out reference);

    public IReadOnlyDictionary<int, WatchedFileMessageRef> Snapshot() =>
        new Dictionary<int, WatchedFileMessageRef>(_live);
}
