using Bot.Utils;
using Xunit;

namespace Bot.Tests;

public class BackupRetentionTests
{
    [Fact]
    public void Apply_KeepsEverythingWhenUnderTheLimit()
    {
        var (remaining, toDelete) = BackupRetention.Apply(new[] { 1, 2, 3 }, keep: 4);

        Assert.Equal([1, 2, 3], remaining);
        Assert.Empty(toDelete);
    }

    [Fact]
    public void Apply_DropsTheOldestGenerationsBeyondTheLimit()
    {
        var (remaining, toDelete) = BackupRetention.Apply(new[] { 1, 2, 3, 4, 5 }, keep: 2);

        Assert.Equal([4, 5], remaining);
        Assert.Equal([1, 2, 3], toDelete);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Apply_KeepsEverythingWhenKeepIsNotPositive(int keep)
    {
        var (remaining, toDelete) = BackupRetention.Apply(new[] { 1, 2, 3 }, keep);

        Assert.Equal([1, 2, 3], remaining);
        Assert.Empty(toDelete);
    }

    [Fact]
    public void Apply_KeepsExactlyTheLimitAtTheBoundary()
    {
        var (remaining, toDelete) = BackupRetention.Apply(new[] { 1, 2, 3 }, keep: 3);

        Assert.Equal([1, 2, 3], remaining);
        Assert.Empty(toDelete);
    }
}
