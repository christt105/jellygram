using Bot.Utils;
using Xunit;

namespace Bot.Tests;

public class LastRunFileTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Directory.CreateTempSubdirectory("last-run-test-").FullName, "last-run");

    public void Dispose() => Directory.Delete(Path.GetDirectoryName(_path)!, recursive: true);

    [Fact]
    public void Read_ReturnsNullWhenTheFileDoesNotExist()
    {
        Assert.Null(new LastRunFile(_path).Read());
    }

    [Fact]
    public void WriteThenRead_RoundTripsTheTimestamp()
    {
        var file = new LastRunFile(_path);
        var timestamp = new DateTime(2026, 3, 1, 8, 30, 0, DateTimeKind.Utc);

        file.Write(timestamp);

        Assert.Equal(timestamp, file.Read());
    }

    [Fact]
    public void Write_CreatesTheParentDirectoryWhenMissing()
    {
        var nestedPath = Path.Combine(Path.GetDirectoryName(_path)!, "nested", "last-run");
        var file = new LastRunFile(nestedPath);

        file.Write(DateTime.UtcNow);

        Assert.True(File.Exists(nestedPath));
    }

    [Fact]
    public void Read_ReturnsNullForUnparsableContent()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "not a timestamp");

        Assert.Null(new LastRunFile(_path).Read());
    }
}
