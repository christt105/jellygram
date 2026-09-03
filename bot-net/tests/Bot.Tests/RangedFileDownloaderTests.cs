using Bot.Utils;
using Xunit;

namespace Bot.Tests;

public class RangedFileDownloaderTests : IDisposable
{
    private const int PartSize = 4096;

    private readonly string _root = Directory.CreateTempSubdirectory("RangedFileDownloaderTests").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string PathIn(string name) => Path.Combine(_root, name);

    private static byte[] Content(int size)
    {
        var bytes = new byte[size];
        new Random(20260902).NextBytes(bytes);
        return bytes;
    }

    /// <summary>Serves parts of an in-memory file and records which offsets it was asked for.</summary>
    private sealed class FakePartReader : IFilePartReader
    {
        private readonly byte[] _content;
        private readonly List<long> _offsets = new();

        public FakePartReader(byte[] content) => _content = content;

        public IReadOnlyList<long> Offsets
        {
            get { lock (_offsets) return _offsets.ToList(); }
        }

        public Task<byte[]> ReadPartAsync(long offset, int limit, CancellationToken cancellationToken)
        {
            lock (_offsets) _offsets.Add(offset);

            var available = (int)Math.Max(0, Math.Min(limit, _content.LongLength - offset));
            return Task.FromResult(_content.AsSpan((int)offset, available).ToArray());
        }
    }

    private sealed class FailingPartReader : IFilePartReader
    {
        public Task<byte[]> ReadPartAsync(long offset, int limit, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("connection dropped");
    }

    private sealed class ShortPartReader : IFilePartReader
    {
        public Task<byte[]> ReadPartAsync(long offset, int limit, CancellationToken cancellationToken) =>
            Task.FromResult(new byte[limit / 2]);
    }

    private static List<IFilePartReader> Readers(byte[] content, int count) =>
        Enumerable.Range(0, count).Select(_ => (IFilePartReader)new FakePartReader(content)).ToList();

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(8)]
    public async Task DownloadAsync_ReassemblesTheFileExactly(int connections)
    {
        var content = Content(10 * PartSize + 137);
        var destination = PathIn($"movie-{connections}.mkv");

        await new RangedFileDownloader(PartSize, partsInFlightPerReader: 4)
            .DownloadAsync(Readers(content, connections), content.Length, destination);

        Assert.Equal(content, await File.ReadAllBytesAsync(destination));
    }

    [Fact]
    public async Task DownloadAsync_GivesEachConnectionItsOwnRangeOnly()
    {
        var content = Content(9 * PartSize);
        var readers = Readers(content, 3);

        await new RangedFileDownloader(PartSize, partsInFlightPerReader: 2)
            .DownloadAsync(readers, content.Length, PathIn("ranges.mkv"));

        var perReader = readers.Cast<FakePartReader>().Select(r => r.Offsets.Order().ToList()).ToList();

        Assert.All(perReader, offsets => Assert.Equal(3, offsets.Count));
        Assert.Equal(
            Enumerable.Range(0, 9).Select(i => (long)(i * PartSize)),
            perReader.SelectMany(o => o).Order());
        for (var i = 1; i < perReader.Count; i++)
            Assert.True(perReader[i - 1][^1] < perReader[i][0], "Ranges must not overlap.");
    }

    [Fact]
    public async Task DownloadAsync_UsesTheOnlyConnectionForEverythingWhenAlone()
    {
        var content = Content(5 * PartSize);
        var readers = Readers(content, 1);

        await new RangedFileDownloader(PartSize, partsInFlightPerReader: 4)
            .DownloadAsync(readers, content.Length, PathIn("single.mkv"));

        Assert.Equal(5, ((FakePartReader)readers[0]).Offsets.Count);
        Assert.Equal(content, await File.ReadAllBytesAsync(PathIn("single.mkv")));
    }

    [Fact]
    public async Task DownloadAsync_LeavesNoFileBehindWhenAConnectionFails()
    {
        var content = Content(20 * PartSize);
        var destination = PathIn("broken.mkv");
        var readers = new List<IFilePartReader> { new FakePartReader(content), new FailingPartReader() };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RangedFileDownloader(PartSize, partsInFlightPerReader: 2)
                .DownloadAsync(readers, content.Length, destination));

        Assert.Equal("connection dropped", ex.Message);
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task DownloadAsync_RefusesAFileThatCameBackIncomplete()
    {
        var content = Content(6 * PartSize);
        var destination = PathIn("truncated.mkv");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RangedFileDownloader(PartSize, partsInFlightPerReader: 2)
                .DownloadAsync(new List<IFilePartReader> { new ShortPartReader() }, content.Length, destination));

        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task DownloadAsync_StopsWhenCancelled()
    {
        var content = Content(20 * PartSize);
        var destination = PathIn("cancelled.mkv");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new RangedFileDownloader(PartSize, partsInFlightPerReader: 2)
                .DownloadAsync(Readers(content, 3), content.Length, destination, null, cts.Token));

        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task DownloadAsync_ReportsProgressUpToTheFullSize()
    {
        var content = Content(7 * PartSize + 11);
        var reported = new List<long>();

        await new RangedFileDownloader(PartSize, partsInFlightPerReader: 2).DownloadAsync(
            Readers(content, 3), content.Length, PathIn("progress.mkv"),
            (transmitted, total) =>
            {
                Assert.Equal(content.Length, total);
                lock (reported) reported.Add(transmitted);
            });

        Assert.Equal(0, reported[0]);
        Assert.Equal(content.Length, reported.Max());
    }

    [Fact]
    public async Task DownloadAsync_WritesAnEmptyFileForAnEmptyDocument()
    {
        var destination = PathIn("empty.mkv");

        await new RangedFileDownloader(PartSize, partsInFlightPerReader: 2)
            .DownloadAsync(Readers(Array.Empty<byte>(), 3), 0, destination);

        Assert.Empty(await File.ReadAllBytesAsync(destination));
    }

    [Fact]
    public async Task DownloadAsync_OverwritesAFileLeftByAPreviousAttempt()
    {
        var content = Content(3 * PartSize);
        var destination = PathIn("retry.mkv");
        await File.WriteAllBytesAsync(destination, Content(9 * PartSize));

        await new RangedFileDownloader(PartSize, partsInFlightPerReader: 2)
            .DownloadAsync(Readers(content, 2), content.Length, destination);

        Assert.Equal(content, await File.ReadAllBytesAsync(destination));
    }

    [Fact]
    public void Constructor_RejectsNonPositiveTuning()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RangedFileDownloader(0, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RangedFileDownloader(PartSize, 0));
    }

    [Fact]
    public async Task DownloadAsync_NeedsAtLeastOneConnection()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new RangedFileDownloader(PartSize, 4).DownloadAsync(Array.Empty<IFilePartReader>(), 10, PathIn("none.mkv")));
    }
}
