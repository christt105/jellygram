using Bot.Services;
using Xunit;

namespace Bot.Tests;

public class WatchedFileMoveFlowTests
{
    [Fact]
    public void SeriesFolderOf_ClimbsPastTheSeasonFolder()
    {
        Assert.Equal(
            "/data/media/shows/El Instituto [tvdbid-451626]",
            WatchedFileMoveFlow.SeriesFolderOf(
                "/data/media/shows/El Instituto [tvdbid-451626]/Season 01/El Instituto - S01E01.mkv"));
    }

    [Fact]
    public void SeriesFolderOf_WorksWithTheTmdbFallbackTag()
    {
        Assert.Equal(
            "/data/media/shows/El Instituto [tmdbid-249039]",
            WatchedFileMoveFlow.SeriesFolderOf(
                "/data/media/shows/El Instituto [tmdbid-249039]/Season 02/El Instituto - S02E03.mkv"));
    }

    [Fact]
    public void SeriesFolderOf_IsNullWhenThereIsNoShowFolderAbove()
    {
        Assert.Null(WatchedFileMoveFlow.SeriesFolderOf("El Instituto - S01E01.mkv"));
    }
}
