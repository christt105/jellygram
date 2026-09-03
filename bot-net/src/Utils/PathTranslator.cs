namespace Bot.Utils;

public readonly record struct PathMapping(string Prefix, string Target);

/// <summary>
/// Rewrites a path as reported by Jellyfin into the equivalent path inside this container.
/// Jellyfin reports paths as its own process sees them, which only match the host paths under
/// MEDIA_ROOT when it runs outside Docker or shares the same mount points; JELLYFIN_PATH_MAP
/// covers every other deployment and is consulted first.
/// </summary>
public static class PathTranslator
{
    public static string Translate(string reportedPath) =>
        Translate(reportedPath, ConfiguredMappings());

    public static string Translate(string reportedPath, IEnumerable<PathMapping> mappings)
    {
        TryTranslate(reportedPath, mappings, out var localPath);
        return localPath;
    }

    public static bool TryTranslate(string reportedPath, out string localPath) =>
        TryTranslate(reportedPath, ConfiguredMappings(), out localPath);

    /// <summary>
    /// Translates a reported path and tells the caller whether a mapping actually matched it.
    /// <paramref name="localPath"/> is the reported path unchanged when none did, so an unmapped
    /// path that happens to exist inside this container still resolves.
    /// </summary>
    public static bool TryTranslate(string reportedPath, IEnumerable<PathMapping> mappings, out string localPath)
    {
        foreach (var (prefix, target) in mappings)
        {
            if (!reportedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var relative = reportedPath[prefix.Length..].TrimStart('/');
            localPath = relative.Length == 0 ? target : $"{target}/{relative}";
            return true;
        }

        localPath = reportedPath;
        return false;
    }

    /// <summary>
    /// Parses the "jellyfin_path:container_path,..." form of JELLYFIN_PATH_MAP.
    /// Malformed entries are reported and skipped rather than failing the whole mapping.
    /// </summary>
    public static IEnumerable<PathMapping> ParseMap(string? map)
    {
        if (string.IsNullOrWhiteSpace(map))
            yield break;

        foreach (var entry in map.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = entry.Split(':', 2);

            if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace))
            {
                Log.Warning($"[PathTranslator] Ignoring malformed JELLYFIN_PATH_MAP entry: {entry}");
                continue;
            }

            yield return new PathMapping(parts[0].TrimEnd('/'), parts[1].TrimEnd('/'));
        }
    }

    /// <summary>
    /// The mappings in effect for this deployment: JELLYFIN_PATH_MAP first, then the implicit
    /// mapping from the host library paths under MEDIA_ROOT onto the mounted library directories.
    /// </summary>
    public static IEnumerable<PathMapping> ConfiguredMappings()
    {
        foreach (var mapping in ParseMap(Environment.GetEnvironmentVariable("JELLYFIN_PATH_MAP")))
            yield return mapping;

        var mediaRoot = Environment.GetEnvironmentVariable("MEDIA_ROOT");
        if (string.IsNullOrWhiteSpace(mediaRoot)) yield break;

        mediaRoot = mediaRoot.TrimEnd('/');
        var moviesSubdir = Environment.GetEnvironmentVariable("MOVIES_SUBDIR") ?? "movies";
        var showsSubdir = Environment.GetEnvironmentVariable("SHOWS_SUBDIR") ?? "shows";

        yield return new PathMapping($"{mediaRoot}/{moviesSubdir}", MediaLibrary.MoviesDir);
        yield return new PathMapping($"{mediaRoot}/{showsSubdir}", MediaLibrary.ShowsDir);
    }
}
