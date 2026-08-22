using Bot.Services;
using Xunit;

namespace Bot.Tests;

public class UploadEchoRegistryTests
{
    private static readonly TimeSpan NoWait = TimeSpan.FromMilliseconds(50);

    [Fact]
    public async Task WaitAsync_ReturnsTheIdTheBotGaveTheFile()
    {
        var registry = new UploadEchoRegistry();
        using var echo = registry.Expect("Mugen Train.mkv", 1048576);

        Assert.True(registry.TryClaim("Mugen Train.mkv", 1048576, 4242));

        Assert.Equal(4242, await echo.WaitAsync(NoWait));
    }

    [Fact]
    public async Task WaitAsync_ReturnsNullWhenTheEchoNeverArrives()
    {
        var registry = new UploadEchoRegistry();
        using var echo = registry.Expect("Mugen Train.mkv", 1048576);

        Assert.Null(await echo.WaitAsync(NoWait));
    }

    [Fact]
    public async Task WaitAsync_PicksUpAnEchoThatArrivesWhileWaiting()
    {
        var registry = new UploadEchoRegistry();
        using var echo = registry.Expect("Mugen Train.mkv", 1048576);

        var waiting = echo.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(registry.TryClaim("Mugen Train.mkv", 1048576, 4242));

        Assert.Equal(4242, await waiting);
    }

    [Fact]
    public void TryClaim_LetsThroughAFileNobodyIsWaitingFor()
    {
        var registry = new UploadEchoRegistry();

        Assert.False(registry.TryClaim("Sent By Hand.mkv", 1048576, 4242));
    }

    [Fact]
    public void TryClaim_RequiresTheSizeToMatch()
    {
        var registry = new UploadEchoRegistry();
        using var echo = registry.Expect("Mugen Train.mkv", 1048576);

        Assert.False(registry.TryClaim("Mugen Train.mkv", 999, 4242));
    }

    [Fact]
    public void TryClaim_RequiresTheNameToMatch()
    {
        var registry = new UploadEchoRegistry();
        using var echo = registry.Expect("Mugen Train.mkv", 1048576);

        Assert.False(registry.TryClaim("Another Film.mkv", 1048576, 4242));
    }

    [Fact]
    public async Task TryClaim_TellsApartTwoVolumesOfTheSameSize()
    {
        var registry = new UploadEchoRegistry();
        using var first = registry.Expect("Mugen Train.zip.001", 1048576);
        using var second = registry.Expect("Mugen Train.zip.002", 1048576);

        Assert.True(registry.TryClaim("Mugen Train.zip.002", 1048576, 20));
        Assert.True(registry.TryClaim("Mugen Train.zip.001", 1048576, 10));

        Assert.Equal(10, await first.WaitAsync(NoWait));
        Assert.Equal(20, await second.WaitAsync(NoWait));
    }

    [Fact]
    public void TryClaim_ClaimsAnUploadOnlyOnce()
    {
        var registry = new UploadEchoRegistry();
        using var echo = registry.Expect("Mugen Train.mkv", 1048576);

        Assert.True(registry.TryClaim("Mugen Train.mkv", 1048576, 4242));
        // A second copy of the same file is a new file the owner sent, not this upload again.
        Assert.False(registry.TryClaim("Mugen Train.mkv", 1048576, 4243));
    }

    [Fact]
    public void Dispose_StopsClaimingSoALateEchoIsRegisteredAsAnOrdinaryFile()
    {
        var registry = new UploadEchoRegistry();
        var echo = registry.Expect("Mugen Train.mkv", 1048576);
        echo.Dispose();

        Assert.False(registry.TryClaim("Mugen Train.mkv", 1048576, 4242));
    }
}
