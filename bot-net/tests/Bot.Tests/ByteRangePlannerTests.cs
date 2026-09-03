using Bot.Utils;
using Xunit;

namespace Bot.Tests;

public class ByteRangePlannerTests
{
    private const int Block = 512 * 1024;

    private static void AssertCoversExactly(IReadOnlyList<ByteRange> ranges, long fileSize)
    {
        Assert.All(ranges, range => Assert.True(range.Length > 0, "A range must not be empty."));
        Assert.Equal(0, ranges[0].Offset);
        Assert.Equal(fileSize, ranges[^1].End);
        Assert.Equal(fileSize, ranges.Sum(r => r.Length));

        for (var i = 1; i < ranges.Count; i++)
            Assert.Equal(ranges[i - 1].End, ranges[i].Offset);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(8)]
    public void Split_CoversTheWholeFileWithoutGapsOrOverlaps(int connections)
    {
        var fileSize = 3_900_000_000L;

        var ranges = ByteRangePlanner.Split(fileSize, connections, Block);

        Assert.Equal(connections, ranges.Count);
        AssertCoversExactly(ranges, fileSize);
    }

    [Fact]
    public void Split_AlignsEveryCutToTheBlockSize()
    {
        var ranges = ByteRangePlanner.Split(fileSize: 1_000_000_000, connections: 3, Block);

        Assert.All(ranges, range => Assert.Equal(0, range.Offset % Block));
        Assert.All(ranges.SkipLast(1), range => Assert.Equal(0, range.Length % Block));
    }

    [Fact]
    public void Split_KeepsTheSlicesWithinOneBlockOfEachOther()
    {
        var ranges = ByteRangePlanner.Split(fileSize: 10 * Block + 1, connections: 4, Block);

        var blocks = ranges.Select(r => (r.Length + Block - 1) / Block).ToList();
        Assert.Equal(1, blocks.Max() - blocks.Min());
    }

    [Fact]
    public void Split_ReturnsOneRangeForOneConnection()
    {
        var ranges = ByteRangePlanner.Split(fileSize: 123_456_789, connections: 1, Block);

        Assert.Single(ranges);
        Assert.Equal(new ByteRange(0, 123_456_789), ranges[0]);
    }

    [Fact]
    public void Split_ReturnsOneRangeForZeroOrNegativeConnections()
    {
        Assert.Single(ByteRangePlanner.Split(fileSize: 4096, connections: 0, Block));
        Assert.Single(ByteRangePlanner.Split(fileSize: 4096, connections: -3, Block));
    }

    [Fact]
    public void Split_NeverMakesMoreRangesThanBlocks()
    {
        var ranges = ByteRangePlanner.Split(fileSize: 2 * Block, connections: 8, Block);

        Assert.Equal(2, ranges.Count);
        AssertCoversExactly(ranges, 2 * Block);
    }

    [Fact]
    public void Split_GivesTheWholeFileToOneRangeWhenItIsSmallerThanABlock()
    {
        var ranges = ByteRangePlanner.Split(fileSize: 17, connections: 4, Block);

        Assert.Single(ranges);
        Assert.Equal(new ByteRange(0, 17), ranges[0]);
    }

    [Fact]
    public void Split_HasNothingToSplitForAnEmptyFile()
    {
        Assert.Empty(ByteRangePlanner.Split(fileSize: 0, connections: 3, Block));
        Assert.Empty(ByteRangePlanner.Split(fileSize: -1, connections: 3, Block));
    }

    [Fact]
    public void Split_RejectsANonPositiveBlockSize()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ByteRangePlanner.Split(1024, 2, 0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4095)]
    [InlineData(4096)]
    [InlineData(Block - 1)]
    [InlineData(Block)]
    [InlineData(Block + 1)]
    [InlineData(7 * Block - 3)]
    public void Split_StaysConsistentAcrossSizesAndConnectionCounts(long fileSize)
    {
        for (var connections = 1; connections <= 8; connections++)
            AssertCoversExactly(ByteRangePlanner.Split(fileSize, connections, Block), fileSize);
    }
}
