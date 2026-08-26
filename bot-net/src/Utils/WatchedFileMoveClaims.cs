using System.Collections.Concurrent;

namespace Bot.Utils;

/// <summary>
/// One claim per watched-file row while <see cref="Services.WatchedFileMoveFlow"/> is moving it.
/// A row can reach <c>confirmed</c>/<c>corrected</c> through a Telegram button tap or through the
/// web, and both the tap's callback and the poller that picks up web-resolved rows end up calling
/// the same move flow — claiming the row id makes whichever gets there first the only one that
/// moves it, instead of both passing the <c>File.Exists</c> check and racing to the same source.
/// </summary>
public static class WatchedFileMoveClaims
{
    private static readonly ConcurrentDictionary<int, byte> Claimed = new();

    public static bool TryClaim(int watchedFileId) => Claimed.TryAdd(watchedFileId, 0);

    public static void Release(int watchedFileId) => Claimed.TryRemove(watchedFileId, out _);
}
