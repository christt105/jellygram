namespace Bot.Utils;

/// <summary>
/// Resolves the mounted media library directories and walks the video files in them.
/// </summary>
public static class MediaLibrary
{
    public static readonly string[] VideoExtensions =
        [".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm"];

    public static string MoviesDir =>
        Environment.GetEnvironmentVariable("JELLYFIN_MOVIES_DIR") ?? "/data/jellyfin/movies";

    public static string ShowsDir =>
        Environment.GetEnvironmentVariable("JELLYFIN_SHOWS_DIR") ?? "/data/jellyfin/shows";

    public static string DownloadsDir =>
        Environment.GetEnvironmentVariable("DOWNLOADS_DIR") ?? "/data/media/downloads";

    public static string[] Roots() => [MoviesDir, ShowsDir];

    public static bool IsVideo(string path) =>
        VideoExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static IEnumerable<string> EnumerateVideos(string dir)
    {
        if (!Directory.Exists(dir)) return [];

        return Directory
            .EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Where(IsVideo);
    }

    /// <summary>
    /// Resolves a path and confirms it lives inside one of the library roots. Callers use
    /// it to make sure an HTTP request cannot reach files outside the mounted directories.
    /// </summary>
    /// <param name="path">Path to check, absolute or relative.</param>
    /// <param name="fullPath">The resolved absolute path.</param>
    /// <returns>True when the resolved path is inside a library root.</returns>
    public static bool TryResolveInsideLibrary(string path, out string fullPath)
    {
        fullPath = "";
        if (string.IsNullOrWhiteSpace(path)) return false;

        fullPath = Path.GetFullPath(path);

        foreach (var root in Roots())
        {
            var fullRoot = Path.GetFullPath(root);
            if (!fullRoot.EndsWith(Path.DirectorySeparatorChar))
                fullRoot += Path.DirectorySeparatorChar;

            if (fullPath.StartsWith(fullRoot, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
