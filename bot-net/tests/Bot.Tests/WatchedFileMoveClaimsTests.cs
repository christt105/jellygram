using Bot.Utils;
using Xunit;

namespace Bot.Tests;

public class WatchedFileMoveClaimsTests
{
    [Fact]
    public void TryClaim_SucceedsForAnUnclaimedRow()
    {
        Assert.True(WatchedFileMoveClaims.TryClaim(9001));
        WatchedFileMoveClaims.Release(9001);
    }

    [Fact]
    public void TryClaim_FailsWhileTheRowIsAlreadyClaimed()
    {
        Assert.True(WatchedFileMoveClaims.TryClaim(9002));
        Assert.False(WatchedFileMoveClaims.TryClaim(9002));

        WatchedFileMoveClaims.Release(9002);
        Assert.True(WatchedFileMoveClaims.TryClaim(9002));
        WatchedFileMoveClaims.Release(9002);
    }

    [Fact]
    public void TryClaim_TracksEachRowIndependently()
    {
        Assert.True(WatchedFileMoveClaims.TryClaim(9003));
        Assert.True(WatchedFileMoveClaims.TryClaim(9004));

        WatchedFileMoveClaims.Release(9003);
        Assert.False(WatchedFileMoveClaims.TryClaim(9004));

        WatchedFileMoveClaims.Release(9004);
    }

    [Fact]
    public void Release_IsANoOpForARowThatWasNeverClaimed()
    {
        WatchedFileMoveClaims.Release(9005);
    }

    [Fact]
    public void TryClaim_IsGrantedToASingleCallerUnderConcurrency()
    {
        const int rowId = 9006;
        var granted = 0;

        Parallel.For(0, 64, _ =>
        {
            if (WatchedFileMoveClaims.TryClaim(rowId)) Interlocked.Increment(ref granted);
        });

        Assert.Equal(1, granted);
        WatchedFileMoveClaims.Release(rowId);
    }
}
