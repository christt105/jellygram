using System.Collections.Concurrent;

namespace Bot.Utils;

/// <summary>
/// Tracks the downloads-relative paths <see cref="Services.WatchedFileMoveFlow"/> is actively
/// moving out of the downloads folder. <see cref="Services.WatchedFolderService"/>'s
/// FileSystemWatcher fires a Deleted event for that same path as a side effect of the move
/// itself, indistinguishable from a real deletion by hand — checking this registry first lets it
/// ignore that expected event instead of racing the "moved" status update with a spurious
/// "removed" one.
/// </summary>
public static class InFlightWatchedFileMoves
{
    private static readonly ConcurrentDictionary<string, byte> Paths = new(StringComparer.Ordinal);

    public static void Mark(string relativePath) => Paths.TryAdd(relativePath, 0);

    public static void Unmark(string relativePath) => Paths.TryRemove(relativePath, out _);

    public static bool IsInFlight(string relativePath) => Paths.ContainsKey(relativePath);
}
