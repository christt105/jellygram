using Bot.Utils;
using Xunit;

namespace Bot.Tests;

public class BackupHistoryFileTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Directory.CreateTempSubdirectory("backup-history-test-").FullName, "history.json");

    public void Dispose() => Directory.Delete(Path.GetDirectoryName(_path)!, recursive: true);

    [Fact]
    public void Read_ReturnsAnEmptyListWhenTheFileDoesNotExist()
    {
        Assert.Empty(new BackupHistoryFile(_path).Read());
    }

    [Fact]
    public void WriteThenRead_RoundTripsTheGenerations()
    {
        var file = new BackupHistoryFile(_path);
        List<int[]> generations = [[1, 2], [3]];

        file.Write(generations);

        Assert.Equal(generations, file.Read());
    }

    [Fact]
    public void Read_ReturnsAnEmptyListForUnparsableContent()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "not json");

        Assert.Empty(new BackupHistoryFile(_path).Read());
    }
}
