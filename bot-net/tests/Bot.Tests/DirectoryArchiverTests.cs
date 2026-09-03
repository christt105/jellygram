using System.Formats.Tar;
using System.IO.Compression;
using Bot.Utils;
using Xunit;

namespace Bot.Tests;

public class DirectoryArchiverTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("archiver-test-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Create_PacksEveryFileUnderTheGivenSourcesIntoTheArchive()
    {
        var configDir = Path.Combine(_root, "config");
        Directory.CreateDirectory(configDir);
        File.WriteAllText(Path.Combine(configDir, "settings.xml"), "<config/>");
        File.WriteAllText(Path.Combine(configDir, "nested.txt"), "nested");

        var dataDir = Path.Combine(_root, "data");
        Directory.CreateDirectory(Path.Combine(dataDir, "sub"));
        File.WriteAllText(Path.Combine(dataDir, "sub", "library.db"), "db-bytes");

        var archivePath = Path.Combine(_root, "out", "backup.tar.gz");

        var result = DirectoryArchiver.Create(_root, [configDir, dataDir], archivePath);

        Assert.Equal(3, result.Entries);
        Assert.Empty(result.Skipped);
        Assert.True(File.Exists(archivePath));

        var names = ReadEntryNames(archivePath);
        Assert.Contains("config/settings.xml", names);
        Assert.Contains("config/nested.txt", names);
        Assert.Contains("data/sub/library.db", names);
    }

    [Fact]
    public void Create_ArchivesASingleFileSourceByItself()
    {
        var filePath = Path.Combine(_root, "plugins.json");
        File.WriteAllText(filePath, "[]");
        var archivePath = Path.Combine(_root, "backup.tar.gz");

        var result = DirectoryArchiver.Create(_root, [filePath], archivePath);

        Assert.Equal(1, result.Entries);
        Assert.Contains("plugins.json", ReadEntryNames(archivePath));
    }

    [Fact]
    public void Create_SkipsSourcesThatDoNotExistWithoutFailing()
    {
        var archivePath = Path.Combine(_root, "backup.tar.gz");

        var result = DirectoryArchiver.Create(_root, [Path.Combine(_root, "missing")], archivePath);

        Assert.Equal(0, result.Entries);
        Assert.Empty(result.Skipped);
        Assert.True(File.Exists(archivePath));
    }

    private static List<string> ReadEntryNames(string archivePath)
    {
        using var input = File.OpenRead(archivePath);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var tar = new TarReader(gzip);

        var names = new List<string>();
        while (tar.GetNextEntry() is { } entry) names.Add(entry.Name);
        return names;
    }
}
