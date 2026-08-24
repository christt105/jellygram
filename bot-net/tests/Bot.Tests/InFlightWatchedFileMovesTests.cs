using Bot.Utils;
using Xunit;

namespace Bot.Tests;

public class InFlightWatchedFileMovesTests
{
    [Fact]
    public void IsInFlight_FalseForUntrackedPath()
    {
        Assert.False(InFlightWatchedFileMoves.IsInFlight("never/marked.mkv"));
    }

    [Fact]
    public void Mark_MakesPathInFlightUntilUnmarked()
    {
        const string path = "movies/Some Movie/file.mkv";

        InFlightWatchedFileMoves.Mark(path);
        Assert.True(InFlightWatchedFileMoves.IsInFlight(path));

        InFlightWatchedFileMoves.Unmark(path);
        Assert.False(InFlightWatchedFileMoves.IsInFlight(path));
    }

    [Fact]
    public void Unmark_IsANoOpForAPathThatWasNeverMarked()
    {
        InFlightWatchedFileMoves.Unmark("never/marked/either.mkv");
    }
}
