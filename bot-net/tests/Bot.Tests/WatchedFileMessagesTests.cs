using Bot.Utils;
using Xunit;

namespace Bot.Tests;

public class WatchedFileMessagesTests
{
    [Fact]
    public void FormatSeasonEpisode_ReturnsEmptyWhenSeasonIsNull()
    {
        Assert.Equal("", WatchedFileMessages.FormatSeasonEpisode(null, null));
    }

    [Fact]
    public void FormatSeasonEpisode_PadsSeasonAndEpisode()
    {
        Assert.Equal(" S02E05", WatchedFileMessages.FormatSeasonEpisode(2, 5));
    }

    [Fact]
    public void FormatSeasonEpisode_OmitsEpisodeWhenMissing()
    {
        Assert.Equal(" S02", WatchedFileMessages.FormatSeasonEpisode(2, null));
    }

    [Fact]
    public void BuildNotifyText_IncludesGuessAndConfidenceWhenTitleIsKnown()
    {
        var text = WatchedFileMessages.BuildNotifyText(
            "Movie.mkv", "movie", "Some Movie", null, null, 0.83);

        Assert.Contains("Movie.mkv", text);
        Assert.Contains("Some Movie", text);
        Assert.Contains("83", text);
    }

    [Fact]
    public void BuildNotifyText_ExplainsNoGuessWhenTitleIsNull()
    {
        var text = WatchedFileMessages.BuildNotifyText("Weird.mkv", null, null, null, null, 0.0);

        Assert.Contains("No automatic guess", text);
    }

    [Fact]
    public void ExtractFilenameFromNotifyText_RoundTripsWithBuildNotifyText()
    {
        var notifyText = WatchedFileMessages.BuildNotifyText("Movie.mkv", "movie", "Some Movie", null, null, 0.5);

        Assert.Equal("Movie.mkv", WatchedFileMessages.ExtractFilenameFromNotifyText(notifyText));
    }

    [Fact]
    public void ExtractFilenameFromNotifyText_FallsBackWhenTextIsUnexpected()
    {
        Assert.Equal("this file", WatchedFileMessages.ExtractFilenameFromNotifyText("something else"));
        Assert.Equal("this file", WatchedFileMessages.ExtractFilenameFromNotifyText(null));
    }

    [Theory]
    [InlineData("tmdb 1234", 1234, null, null)]
    [InlineData("TMDB 1234", 1234, null, null)]
    [InlineData("  tmdb 1234  ", 1234, null, null)]
    [InlineData("tmdb 1234 season 2 episode 5", 1234, 2, 5)]
    [InlineData("tmdb 1234 season 2", 1234, 2, null)]
    [InlineData("tmdb 1234 episode 5", 1234, null, 5)]
    public void TryParseCorrection_ParsesValidReplies(string input, int tmdbId, int? season, int? episode)
    {
        var ok = WatchedFileMessages.TryParseCorrection(input, out var result);

        Assert.True(ok);
        Assert.Equal(tmdbId, result.TmdbId);
        Assert.Equal(season, result.Season);
        Assert.Equal(episode, result.Episode);
    }

    [Theory]
    [InlineData("not a valid reply")]
    [InlineData("tmdb")]
    [InlineData("tmdb abc")]
    public void TryParseCorrection_RejectsInvalidReplies(string input)
    {
        Assert.False(WatchedFileMessages.TryParseCorrection(input, out _));
    }
}
