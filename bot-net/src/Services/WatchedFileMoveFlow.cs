using Bot.Models;
using Bot.Utils;
using Telegram.Bot.Types;

namespace Bot.Services;

/// <summary>
/// Shared tail end of both the Confirm and Correct callbacks, and of the poller that picks up
/// rows confirmed/corrected from the web: builds the destination path, re-checks the source is
/// still on disk, moves it and reports the outcome back to the backend.
/// </summary>
public static class WatchedFileMoveFlow
{
    public readonly record struct MoveOutcome(bool Success, string Text);

    /// <summary>
    /// The path the naming convention asks for, before <see cref="MediaNaming.ResolveFreePath"/>
    /// picks a numbered variant if it's already taken. Exposed so a caller can preview it (and
    /// warn about a collision) before actually committing to the move.
    /// </summary>
    public static string BuildProspectiveDestination(
        string mediaType, string title, int tmdbId, int? tvdbId, int? year, int? season, int? episode,
        string filename) =>
        WatchedFileNaming.BuildDestinationPath(
            MediaLibrary.MoviesDir, MediaLibrary.ShowsDir, mediaType, title, tmdbId, tvdbId, year, season, episode,
            Path.GetExtension(filename));

    /// <summary>
    /// Backend-reporting part, with no Telegram message to edit — reused by the web poller.
    /// Null when another caller is already moving this row (see <see cref="WatchedFileMoveClaims"/>);
    /// that caller reports the outcome, so this one has nothing to say about it.
    /// </summary>
    public static async Task<MoveOutcome?> MoveAndReportAsync(
        ApiClient apiClient, int watchedFileId, WatchedFileResolution? resolution)
    {
        if (resolution is null)
        {
            return new MoveOutcome(false,
                "❌ Could not resolve the identity for this file (TMDB lookup or backend request failed).");
        }

        if (!WatchedFileMoveClaims.TryClaim(watchedFileId)) return null;

        try
        {
            var sourcePath = WatchedFileReconciliation.ToFullPath(MediaLibrary.DownloadsDir, resolution.Path);

            if (!System.IO.File.Exists(sourcePath))
            {
                await apiClient.MarkWatchedFileMissingAsync(resolution.Path);
                return new MoveOutcome(false, WatchedFileMessages.BuildMissingText(resolution.Filename));
            }

            var destPath = BuildProspectiveDestination(
                resolution.MediaType, resolution.Title, resolution.TmdbId, resolution.TvdbId, resolution.Year,
                resolution.Season, resolution.Episode, resolution.Filename);
            destPath = MediaNaming.ResolveFreePath(destPath, null);

            // The move itself makes the source disappear from the downloads folder, which the
            // FileSystemWatcher sees as a Deleted event indistinguishable from a real deletion by
            // hand. Marking the path in-flight lets it recognize and ignore that expected event
            // instead of racing the "moved" patch below with a spurious "removed" one.
            InFlightWatchedFileMoves.Mark(resolution.Path);
            try
            {
                var (ok, error) = await SafeFileMover.MoveAsync(sourcePath, destPath);

                if (ok)
                {
                    await apiClient.PatchWatchedFileStatusAsync(watchedFileId, "moved", movedPath: destPath);
                    return new MoveOutcome(true, WatchedFileMessages.BuildMovedText(resolution.Filename, destPath));
                }

                await apiClient.PatchWatchedFileStatusAsync(watchedFileId, "error", errorMessage: error);
                return new MoveOutcome(false, WatchedFileMessages.BuildErrorText(resolution.Filename, error));
            }
            finally
            {
                InFlightWatchedFileMoves.Unmark(resolution.Path);
            }
        }
        finally
        {
            WatchedFileMoveClaims.Release(watchedFileId);
        }
    }

    public static async Task ExecuteAsync(
        WTelegram.Bot bot, ApiClient apiClient, WatchedFileMessageRegistry registry,
        Message message, int watchedFileId, WatchedFileResolution? resolution)
    {
        registry.TryUntrack(watchedFileId, out _);

        var outcome = await MoveAndReportAsync(apiClient, watchedFileId, resolution);
        if (outcome is null) return;

        await bot.EditMessageText(message.Chat.Id, message.MessageId, outcome.Value.Text);
    }
}
