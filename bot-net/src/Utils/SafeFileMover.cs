namespace Bot.Utils;

/// <summary>
/// Moves a file or directory, preferring an atomic rename and only falling back to a
/// copy-then-delete when the rename fails (e.g. EXDEV, when source and destination live on
/// different filesystems). Callers should not assume the deployment mounts a single volume;
/// this is the safety net for when it doesn't.
/// </summary>
public static class SafeFileMover
{
    private static void DefaultRename(string sourcePath, string destPath, bool isDirectory)
    {
        if (isDirectory)
            Directory.Move(sourcePath, destPath);
        else
            File.Move(sourcePath, destPath);
    }

    /// <param name="rename">
    /// Overrides the atomic-rename attempt. Exposed so tests can force the EXDEV fallback path
    /// by throwing an IOException without needing an actual cross-device mount; defaults to
    /// File.Move/Directory.Move.
    /// </param>
    public static async Task<(bool ok, string error)> MoveAsync(
        string sourcePath, string destPath, Action<string, string, bool>? rename = null)
    {
        rename ??= DefaultRename;

        try
        {
            var isDirectory = Directory.Exists(sourcePath);
            if (!isDirectory && !File.Exists(sourcePath))
                return (false, $"Source not found: {sourcePath}");

            var destDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destDir))
                Directory.CreateDirectory(destDir);

            try
            {
                rename(sourcePath, destPath, isDirectory);

                Log.Info($"[SafeFileMover] Moved {sourcePath} -> {destPath}");
                return (true, "");
            }
            catch (IOException)
            {
                Log.Info($"[SafeFileMover] Rename failed for {sourcePath} -> {destPath}, falling back to copy+delete");

                if (isDirectory)
                    await CopyDirectoryAsync(sourcePath, destPath);
                else
                    await CopyFileAsync(sourcePath, destPath);

                if (isDirectory)
                    Directory.Delete(sourcePath, recursive: true);
                else
                    File.Delete(sourcePath);

                Log.Info($"[SafeFileMover] Moved {sourcePath} -> {destPath} via copy+delete");
                return (true, "");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[SafeFileMover] Failed to move {sourcePath} -> {destPath}", ex);
            return (false, ex.Message);
        }
    }

    private static async Task CopyFileAsync(string sourcePath, string destPath)
    {
        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read);
        await using var dest = new FileStream(destPath, FileMode.CreateNew, FileAccess.Write);
        await source.CopyToAsync(dest);
    }

    private static async Task CopyDirectoryAsync(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var dir in Directory.EnumerateDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, dir);
            Directory.CreateDirectory(Path.Combine(destDir, relative));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file);
            await CopyFileAsync(file, Path.Combine(destDir, relative));
        }
    }
}
