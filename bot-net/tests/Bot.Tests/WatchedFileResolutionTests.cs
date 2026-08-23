using Bot.Models;
using Xunit;

namespace Bot.Tests;

public class WatchedFileResolutionTests
{
    private static WatchedFile MakeRow(
        int? guessTmdbId = 42, string? guessMediaType = "movie", string? guessTitle = "Some Movie",
        int? guessYear = 2024, int? guessSeason = null, int? guessEpisode = null, string status = "confirmed") =>
        new()
        {
            Id = 7,
            Path = "downloads/some.movie.mkv",
            Filename = "some.movie.mkv",
            GuessTmdbId = guessTmdbId,
            GuessMediaType = guessMediaType,
            GuessTitle = guessTitle,
            GuessYear = guessYear,
            GuessSeason = guessSeason,
            GuessEpisode = guessEpisode,
            Status = status,
        };

    [Fact]
    public void FromWatchedFile_MapsAResolvedRow()
    {
        var row = MakeRow(guessSeason: 2, guessEpisode: 5, status: "corrected");

        var resolution = WatchedFileResolution.FromWatchedFile(row);

        Assert.NotNull(resolution);
        Assert.Equal(row.Id, resolution!.Id);
        Assert.Equal(row.Path, resolution.Path);
        Assert.Equal(row.Filename, resolution.Filename);
        Assert.Equal(42, resolution.TmdbId);
        Assert.Equal("movie", resolution.MediaType);
        Assert.Equal("Some Movie", resolution.Title);
        Assert.Equal(2024, resolution.Year);
        Assert.Equal(2, resolution.Season);
        Assert.Equal(5, resolution.Episode);
        Assert.Equal("corrected", resolution.Status);
    }

    [Fact]
    public void FromWatchedFile_ReturnsNullWhenGuessTmdbIdIsMissing()
    {
        Assert.Null(WatchedFileResolution.FromWatchedFile(MakeRow(guessTmdbId: null)));
    }

    [Fact]
    public void FromWatchedFile_ReturnsNullWhenGuessMediaTypeIsMissing()
    {
        Assert.Null(WatchedFileResolution.FromWatchedFile(MakeRow(guessMediaType: null)));
    }

    [Fact]
    public void FromWatchedFile_ReturnsNullWhenGuessTitleIsMissing()
    {
        Assert.Null(WatchedFileResolution.FromWatchedFile(MakeRow(guessTitle: null)));
    }
}
