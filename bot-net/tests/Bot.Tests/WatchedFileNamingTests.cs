using Bot.Utils;
using Xunit;

namespace Bot.Tests;

public class WatchedFileNamingTests
{
    private const string MoviesDir = "/data/jellyfin/movies";
    private const string ShowsDir = "/data/jellyfin/shows";

    [Fact]
    public void BuildDestinationPath_MovieUsesYearAndTmdbIdTag()
    {
        var path = WatchedFileNaming.BuildDestinationPath(
            MoviesDir, ShowsDir, "movie", "the great escape", 1234, 1963, null, null, ".mkv");

        Assert.Equal(
            "/data/jellyfin/movies/The Great Escape (1963) [tmdbid-1234]/The Great Escape (1963).mkv",
            path);
    }

    [Fact]
    public void BuildDestinationPath_MovieOmitsYearWhenMissing()
    {
        var path = WatchedFileNaming.BuildDestinationPath(
            MoviesDir, ShowsDir, "movie", "the great escape", 1234, null, null, null, ".mkv");

        Assert.Equal(
            "/data/jellyfin/movies/The Great Escape [tmdbid-1234]/The Great Escape.mkv",
            path);
    }

    [Fact]
    public void BuildDestinationPath_ShowUsesTmdbIdTagAndSeasonEpisode()
    {
        var path = WatchedFileNaming.BuildDestinationPath(
            MoviesDir, ShowsDir, "tv", "some show", 5678, null, 2, 5, ".mp4");

        Assert.Equal(
            "/data/jellyfin/shows/Some Show [tmdbid-5678]/Season 02/Some Show S02E05.mp4",
            path);
    }

    [Fact]
    public void BuildDestinationPath_ShowDefaultsSeasonAndEpisodeWhenMissing()
    {
        var path = WatchedFileNaming.BuildDestinationPath(
            MoviesDir, ShowsDir, "tv", "some show", 5678, null, null, null, ".mp4");

        Assert.Equal(
            "/data/jellyfin/shows/Some Show [tmdbid-5678]/Season 01/Some Show S01E00.mp4",
            path);
    }
}
