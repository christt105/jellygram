namespace Bot.Utils;

public enum RenameAction
{
    Ignore,
    Rename,
    TrackNew,
    MarkMissing
}

/// <summary>
/// Pure helpers for WatchedFolderService: turning filesystem paths into the relative form
/// reported to the backend, diffing the on-disk set of files against the backend's active
/// rows for the startup reconciliation sweep, and deciding what a Renamed event means when
/// only one side of the rename is a video file (e.g. a sample clip renamed into the real one).
/// </summary>
public static class WatchedFileReconciliation
{
    public static string ToRelativePath(string root, string fullPath)
    {
        var relative = Path.GetRelativePath(root, fullPath);
        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    public static string ToFullPath(string root, string relativePath) =>
        Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    public static RenameAction DecideRenameAction(bool oldWasVideo, bool newIsVideo)
    {
        if (!oldWasVideo && !newIsVideo) return RenameAction.Ignore;
        if (oldWasVideo && newIsVideo) return RenameAction.Rename;
        return newIsVideo ? RenameAction.TrackNew : RenameAction.MarkMissing;
    }

    public record ReconciliationDiff(IReadOnlyList<string> New, IReadOnlyList<string> Missing);

    /// <summary>
    /// Compares the relative paths found on disk against the relative paths of the backend's
    /// currently active (pending/notified) rows. Anything on disk without a matching row is a
    /// new discovery; anything active without a matching file is gone.
    /// </summary>
    public static ReconciliationDiff Compute(
        IEnumerable<string> onDiskRelativePaths,
        IEnumerable<string> backendActiveRelativePaths)
    {
        var onDisk = onDiskRelativePaths.ToHashSet(StringComparer.Ordinal);
        var backendActive = backendActiveRelativePaths.ToHashSet(StringComparer.Ordinal);

        var newFiles = onDisk.Where(p => !backendActive.Contains(p)).ToList();
        var missingFiles = backendActive.Where(p => !onDisk.Contains(p)).ToList();

        return new ReconciliationDiff(newFiles, missingFiles);
    }
}
