using System.Formats.Tar;
using System.IO.Compression;

namespace Bot.Utils;

/// <summary>What went into an archive: the number of files stored, and the ones that could not be read.</summary>
public readonly record struct ArchiveResult(int Entries, IReadOnlyList<string> Skipped);

/// <summary>
/// Packs directories into a gzipped tar, without shelling out to <c>tar</c>.
/// </summary>
public static class DirectoryArchiver
{
    private static readonly EnumerationOptions WalkOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    /// <summary>
    /// Writes every file under <paramref name="sources"/> into a gzipped tar at
    /// <paramref name="archivePath"/>, named relative to <paramref name="root"/> so the archive
    /// unpacks back onto the same layout. Files that cannot be read are reported and left out
    /// instead of aborting the run: a live Jellyfin keeps sockets, logs and caches open, and one
    /// of them vanishing mid-walk must not cost the whole backup.
    /// </summary>
    public static ArchiveResult Create(string root, IEnumerable<string> sources, string archivePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(archivePath));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var skipped = new List<string>();
        var entries = 0;

        using (var output = File.Create(archivePath))
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
        using (var tar = new TarWriter(gzip, TarEntryFormat.Pax))
        {
            foreach (var source in sources)
            {
                foreach (var file in EnumerateFiles(source))
                {
                    var name = Path.GetRelativePath(root, file).Replace('\\', '/');
                    try
                    {
                        using var content = File.Open(file, FileMode.Open, FileAccess.Read,
                            FileShare.ReadWrite | FileShare.Delete);
                        tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = content });
                        entries++;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        skipped.Add(name);
                    }
                }
            }
        }

        return new ArchiveResult(entries, skipped);
    }

    private static IEnumerable<string> EnumerateFiles(string source)
    {
        if (File.Exists(source)) return [source];
        if (!Directory.Exists(source)) return [];

        return Directory.EnumerateFiles(source, "*", WalkOptions);
    }
}
