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
        ApiClient apiClient, int watchedFileId, WatchedFileResolution? resolution,
        JellyfinSeriesIdentifier? jellyfin = null)
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
                    QueueJellyfinIdentification(jellyfin, resolution, destPath);
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

    /// <summary>
    /// The show folder of an episode destination, two levels up from the file itself
    /// ("Show [tvdbid-x]/Season 01/Show - S01E01.mkv"). That folder is what Jellyfin turns into
    /// the Series item, so it is what an identification has to be matched against.
    /// </summary>
    public static string? SeriesFolderOf(string destPath)
    {
        var seasonDir = Path.GetDirectoryName(destPath);
        var seriesFolder = seasonDir is null ? null : Path.GetDirectoryName(seasonDir);

        return string.IsNullOrEmpty(seriesFolder) ? null : seriesFolder;
    }

    /// <summary>
    /// Movies are left out on purpose: TMDB is Jellyfin's primary provider for them and folder
    /// ids have not misidentified one, while series go through TheTVDB first.
    /// </summary>
    private static void QueueJellyfinIdentification(
        JellyfinSeriesIdentifier? jellyfin, WatchedFileResolution resolution, string destPath)
    {
        if (jellyfin is null || resolution.MediaType != "tv") return;

        var seriesFolder = SeriesFolderOf(destPath);
        if (seriesFolder is null) return;

        jellyfin.QueueIdentification(seriesFolder, resolution.TmdbId, resolution.Title);
    }

    public static async Task ExecuteAsync(
        WTelegram.Bot bot, ApiClient apiClient, WatchedFileMessageRegistry registry,
        Message message, int watchedFileId, WatchedFileResolution? resolution,
        JellyfinSeriesIdentifier? jellyfin = null)
    {
        registry.TryUntrack(watchedFileId, out _);

        var outcome = await MoveAndReportAsync(apiClient, watchedFileId, resolution, jellyfin);
        if (outcome is null) return;

        await bot.EditMessageText(message.Chat.Id, message.MessageId, outcome.Value.Text);
    }
}
