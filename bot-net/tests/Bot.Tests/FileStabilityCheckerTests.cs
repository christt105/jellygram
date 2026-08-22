using Bot.Utils;
using Xunit;

namespace Bot.Tests;

public class FileStabilityCheckerTests
{
    private static readonly TimeSpan NoWait = TimeSpan.Zero;

    [Fact]
    public async Task WaitForStableSizeAsync_ReturnsOnceTheSizeHoldsForTheRequiredSamples()
    {
        var sizes = new Queue<long?>([100, 250, 500, 500, 500]);

        var result = await FileStabilityChecker.WaitForStableSizeAsync(
            () => sizes.Dequeue(), requiredSamples: 3, NoWait, CancellationToken.None);

        Assert.Equal(500, result);
    }

    [Fact]
    public async Task WaitForStableSizeAsync_KeepsWaitingWhileTheSizeStillGrows()
    {
        var sizes = new Queue<long?>([100, 200, 300, 300]);

        var result = await FileStabilityChecker.WaitForStableSizeAsync(
            () => sizes.Dequeue(), requiredSamples: 2, NoWait, CancellationToken.None);

        Assert.Equal(300, result);
    }

    [Fact]
    public async Task WaitForStableSizeAsync_ReturnsNullWhenTheFileDisappears()
    {
        var sizes = new Queue<long?>([100, 200, null]);

        var result = await FileStabilityChecker.WaitForStableSizeAsync(
            () => sizes.Dequeue(), requiredSamples: 3, NoWait, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task WaitForStableSizeAsync_SucceedsImmediatelyWithASingleRequiredSample()
    {
        var sizes = new Queue<long?>([42]);

        var result = await FileStabilityChecker.WaitForStableSizeAsync(
            () => sizes.Dequeue(), requiredSamples: 1, NoWait, CancellationToken.None);

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task WaitForStableSizeAsync_StopsWhenCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await FileStabilityChecker.WaitForStableSizeAsync(
            () => 100, requiredSamples: 3, NoWait, cts.Token);

        Assert.Null(result);
    }
}
