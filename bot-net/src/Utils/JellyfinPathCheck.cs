namespace Bot.Utils;

public enum LibraryKind
{
    Movies,
    Shows
}

public enum LibraryPathStatus
{
    /// <summary>The translated path exists inside this container.</summary>
    Resolved,

    /// <summary>A mapping matched, but its target is not on disk here.</summary>
    Mapped,

    /// <summary>No mapping matched, and the reported path is not on disk here either.</summary>
    Unmapped
}

public readonly record struct LibraryLocation(string Library, LibraryKind Kind, string ReportedPath);

public readonly record struct LibraryPathResult(LibraryLocation Location, string LocalPath, LibraryPathStatus Status);

/// <summary>
/// Runs the locations Jellyfin reports for its own libraries through <see cref="PathTranslator"/>
/// and checks that each one lands on a path this container can actually open. A JELLYFIN_PATH_MAP
/// that does not match otherwise stays invisible until an upload fails days later.
/// </summary>
public static class JellyfinPathCheck
{
    public static IReadOnlyList<LibraryPathResult> Check(
        IEnumerable<LibraryLocation> locations,
        IEnumerable<PathMapping> mappings) =>
        Check(locations, mappings, path => File.Exists(path) || Directory.Exists(path));

    public static IReadOnlyList<LibraryPathResult> Check(
        IEnumerable<LibraryLocation> locations,
        IEnumerable<PathMapping> mappings,
        Func<string, bool> exists)
    {
        var configured = mappings.ToArray();
        var results = new List<LibraryPathResult>();

        foreach (var location in locations)
        {
            if (string.IsNullOrWhiteSpace(location.ReportedPath)) continue;

            var mapped = PathTranslator.TryTranslate(location.ReportedPath, configured, out var localPath);
            var status = exists(localPath)
                ? LibraryPathStatus.Resolved
                : mapped ? LibraryPathStatus.Mapped : LibraryPathStatus.Unmapped;

            results.Add(new LibraryPathResult(location, localPath, status));
        }

        return results;
    }

    public static IEnumerable<string> Warnings(IEnumerable<LibraryPathResult> results)
    {
        foreach (var result in results)
        {
            var library = result.Location.Library;
            var reported = result.Location.ReportedPath;

            switch (result.Status)
            {
                case LibraryPathStatus.Mapped:
                    yield return $"Jellyfin library \"{library}\" reports {reported}, which maps to {result.LocalPath}, " +
                                 "but nothing exists there inside bot-net. Check the right-hand side of JELLYFIN_PATH_MAP " +
                                 "and that MEDIA_ROOT is mounted where it says. Uploads from this library will fail.";
                    break;

                case LibraryPathStatus.Unmapped:
                    var target = result.Location.Kind == LibraryKind.Shows
                        ? MediaLibrary.ShowsDir
                        : MediaLibrary.MoviesDir;

                    yield return $"Jellyfin library \"{library}\" reports {reported}, which no JELLYFIN_PATH_MAP entry " +
                                 "or MEDIA_ROOT prefix matches, and that path does not exist inside bot-net either. " +
                                 $"Add {reported.TrimEnd('/')}:{target} to JELLYFIN_PATH_MAP. " +
                                 "Uploads from this library will fail.";
                    break;
            }
        }
    }
}
