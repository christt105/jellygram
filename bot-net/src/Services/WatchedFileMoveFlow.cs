using Bot.Models;
using Bot.Utils;
using Telegram.Bot.Types;

namespace Bot.Services;

/// <summary>
/// Shared tail end of both the Confirm and Correct callbacks: resolve the identity was already
/// done by the caller (POST /watch/{id}/confirm or /correct), this takes the result, builds the
/// destination path, re-checks the source is still on disk, moves it and reports the outcome
/// back to the backend and to the Telegram message.
/// </summary>
public static class WatchedFileMoveFlow
{
    public static async Task ExecuteAsync(
        WTelegram.Bot bot, ApiClient apiClient, WatchedFileMessageRegistry registry,
        Message message, int watchedFileId, WatchedFileResolution? resolution)
    {
        registry.TryUntrack(watchedFileId, out _);

        if (resolution is null)
        {
            await bot.EditMessageText(message.Chat.Id, message.MessageId,
                "❌ Could not resolve the identity for this file (TMDB lookup or backend request failed).");
            return;
        }

        var sourcePath = WatchedFileReconciliation.ToFullPath(MediaLibrary.DownloadsDir, resolution.Path);

        if (!System.IO.File.Exists(sourcePath))
        {
            await apiClient.MarkWatchedFileMissingAsync(resolution.Path);
            await bot.EditMessageText(message.Chat.Id, message.MessageId,
                WatchedFileMessages.BuildMissingText(resolution.Filename));
            return;
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
            await bot.EditMessageText(message.Chat.Id, message.MessageId,
                WatchedFileMessages.BuildMovedText(resolution.Filename, destPath));
        }
        else
        {
            await apiClient.PatchWatchedFileStatusAsync(watchedFileId, "error", errorMessage: error);
            await bot.EditMessageText(message.Chat.Id, message.MessageId,
                WatchedFileMessages.BuildErrorText(resolution.Filename, error));
        }
    }
}
