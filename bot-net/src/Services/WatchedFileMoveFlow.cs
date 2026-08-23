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

    /// Backend-reporting part, with no Telegram message to edit — reused by the web poller.
    public static async Task<MoveOutcome> MoveAndReportAsync(
        ApiClient apiClient, int watchedFileId, WatchedFileResolution? resolution)
    {
        if (resolution is null)
        {
            return new MoveOutcome(false,
                "❌ Could not resolve the identity for this file (TMDB lookup or backend request failed).");
        }

        var sourcePath = WatchedFileReconciliation.ToFullPath(MediaLibrary.DownloadsDir, resolution.Path);

        if (!System.IO.File.Exists(sourcePath))
        {
            await apiClient.MarkWatchedFileMissingAsync(resolution.Path);
            return new MoveOutcome(false, WatchedFileMessages.BuildMissingText(resolution.Filename));
        }

        var extension = Path.GetExtension(resolution.Filename);
        var destPath = WatchedFileNaming.BuildDestinationPath(
            MediaLibrary.MoviesDir, MediaLibrary.ShowsDir, resolution.MediaType, resolution.Title,
            resolution.TmdbId, resolution.Season, resolution.Episode, extension);
        destPath = MediaNaming.ResolveFreePath(destPath, null);

        var (ok, error) = await SafeFileMover.MoveAsync(sourcePath, destPath);

        if (ok)
        {
            await apiClient.PatchWatchedFileStatusAsync(watchedFileId, "moved", movedPath: destPath);
            return new MoveOutcome(true, WatchedFileMessages.BuildMovedText(resolution.Filename, destPath));
        }

        await apiClient.PatchWatchedFileStatusAsync(watchedFileId, "error", errorMessage: error);
        return new MoveOutcome(false, WatchedFileMessages.BuildErrorText(resolution.Filename, error));
    }

    public static async Task ExecuteAsync(
        WTelegram.Bot bot, ApiClient apiClient, WatchedFileMessageRegistry registry,
        Message message, int watchedFileId, WatchedFileResolution? resolution)
    {
        registry.TryUntrack(watchedFileId, out _);

        var outcome = await MoveAndReportAsync(apiClient, watchedFileId, resolution);
        await bot.EditMessageText(message.Chat.Id, message.MessageId, outcome.Text);
    }
}
