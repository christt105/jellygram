using Bot.Utils;
using Xunit;

namespace Bot.Tests;

public class SafeFileMoverTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("SafeFileMoverTests").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string PathIn(params string[] parts) => Path.Combine([_root, ..parts]);

    [Fact]
    public async Task MoveAsync_RenamesTheFileWhenPossible()
    {
        var source = PathIn("source.txt");
        var dest = PathIn("dest.txt");
        await File.WriteAllTextAsync(source, "content");

        var (ok, error) = await SafeFileMover.MoveAsync(source, dest);

        Assert.True(ok, error);
        Assert.False(File.Exists(source));
        Assert.Equal("content", await File.ReadAllTextAsync(dest));
    }

    [Fact]
    public async Task MoveAsync_CreatesTheDestinationDirectory()
    {
        var source = PathIn("source.txt");
        var dest = PathIn("nested", "deeper", "dest.txt");
        await File.WriteAllTextAsync(source, "content");

        var (ok, error) = await SafeFileMover.MoveAsync(source, dest);

        Assert.True(ok, error);
        Assert.Equal("content", await File.ReadAllTextAsync(dest));
    }

    [Fact]
    public async Task MoveAsync_FallsBackToCopyAndDeleteOnIOException()
    {
        var source = PathIn("source.txt");
        var dest = PathIn("dest.txt");
        await File.WriteAllTextAsync(source, "content");

        var (ok, error) = await SafeFileMover.MoveAsync(
            source, dest, rename: (_, _, _) => throw new IOException("simulated EXDEV"));

        Assert.True(ok, error);
        Assert.False(File.Exists(source));
        Assert.Equal("content", await File.ReadAllTextAsync(dest));
    }

    [Fact]
    public async Task MoveAsync_MovesADirectoryRecursively()
    {
        var source = PathIn("source-dir");
        Directory.CreateDirectory(Path.Combine(source, "inner"));
        await File.WriteAllTextAsync(Path.Combine(source, "top.txt"), "top");
        await File.WriteAllTextAsync(Path.Combine(source, "inner", "nested.txt"), "nested");
        var dest = PathIn("dest-dir");

        var (ok, error) = await SafeFileMover.MoveAsync(source, dest);

        Assert.True(ok, error);
        Assert.False(Directory.Exists(source));
        Assert.Equal("top", await File.ReadAllTextAsync(Path.Combine(dest, "top.txt")));
        Assert.Equal("nested", await File.ReadAllTextAsync(Path.Combine(dest, "inner", "nested.txt")));
    }

    [Fact]
    public async Task MoveAsync_FallsBackToCopyAndDeleteForADirectoryOnIOException()
    {
        var source = PathIn("source-dir");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "top.txt"), "top");
        var dest = PathIn("dest-dir");

        var (ok, error) = await SafeFileMover.MoveAsync(
            source, dest, rename: (_, _, _) => throw new IOException("simulated EXDEV"));

        Assert.True(ok, error);
        Assert.False(Directory.Exists(source));
        Assert.Equal("top", await File.ReadAllTextAsync(Path.Combine(dest, "top.txt")));
    }

    [Fact]
    public async Task MoveAsync_ReportsAMissingSource()
    {
        var (ok, error) = await SafeFileMover.MoveAsync(PathIn("missing.txt"), PathIn("dest.txt"));

        Assert.False(ok);
        Assert.Contains("not found", error, StringComparison.OrdinalIgnoreCase);
    }
}
