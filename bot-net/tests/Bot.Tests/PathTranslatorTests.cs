using Bot.Utils;
using Xunit;

namespace Bot.Tests;

public class PathTranslatorTests
{
    private static readonly PathMapping[] Mappings =
    [
        new("/media/disco/Peliculas", "/data/import/movies"),
        new("/media/disco/Series", "/data/import/shows")
    ];

    [Fact]
    public void Translate_RewritesTheMatchingPrefix()
    {
        Assert.Equal(
            "/data/import/movies/Enola Holmes 3 (2026)/Enola Holmes 3 (2026).mkv",
            PathTranslator.Translate("/media/disco/Peliculas/Enola Holmes 3 (2026)/Enola Holmes 3 (2026).mkv", Mappings));
    }

    [Fact]
    public void Translate_PicksTheMappingThatMatches()
    {
        Assert.Equal(
            "/data/import/shows/Pokémon/Season 01",
            PathTranslator.Translate("/media/disco/Series/Pokémon/Season 01", Mappings));
    }

    [Fact]
    public void Translate_MapsThePrefixItself()
    {
        Assert.Equal("/data/import/movies", PathTranslator.Translate("/media/disco/Peliculas", Mappings));
    }

    [Fact]
    public void Translate_OnlyRewritesTheLeadingPrefix()
    {
        Assert.Equal(
            "/data/import/movies/backup/media/disco/Peliculas/Movie.mkv",
            PathTranslator.Translate("/media/disco/Peliculas/backup/media/disco/Peliculas/Movie.mkv", Mappings));
    }

    [Fact]
    public void Translate_LeavesUnmatchedPathsAlone()
    {
        Assert.Equal("/elsewhere/Movie.mkv", PathTranslator.Translate("/elsewhere/Movie.mkv", Mappings));
    }

    [Fact]
    public void ParseMap_ReadsCommaSeparatedPairs()
    {
        var parsed = PathTranslator.ParseMap(
            "/media/disco/Peliculas:/data/import/movies, /media/disco/Series:/data/import/shows").ToArray();

        Assert.Equal(Mappings, parsed);
    }

    [Fact]
    public void ParseMap_TrimsTrailingSlashes()
    {
        var parsed = PathTranslator.ParseMap("/media/movies/:/data/import/movies/").Single();

        Assert.Equal(new PathMapping("/media/movies", "/data/import/movies"), parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/media/movies")]
    [InlineData("/media/movies:")]
    [InlineData(":/data/import/movies")]
    public void ParseMap_SkipsWhatItCannotRead(string? map)
    {
        Assert.Empty(PathTranslator.ParseMap(map));
    }

    [Fact]
    public void TryTranslate_ReportsThatAMappingMatched()
    {
        Assert.True(PathTranslator.TryTranslate("/media/disco/Peliculas/Movie.mkv", Mappings, out var localPath));
        Assert.Equal("/data/import/movies/Movie.mkv", localPath);
    }

    [Fact]
    public void TryTranslate_ReportsThatNoMappingMatched()
    {
        Assert.False(PathTranslator.TryTranslate("/elsewhere/Movie.mkv", Mappings, out var localPath));
        Assert.Equal("/elsewhere/Movie.mkv", localPath);
    }

    [Fact]
    public void ConfiguredMappings_TakesTheImplicitTargetsFromTheLibraryDirectories()
    {
        using var _ = new EnvironmentScope(new()
        {
            ["JELLYFIN_PATH_MAP"] = null,
            ["MEDIA_ROOT"] = "/host/media",
            ["MOVIES_SUBDIR"] = null,
            ["SHOWS_SUBDIR"] = null,
            ["JELLYFIN_MOVIES_DIR"] = "/data/media/peliculas",
            ["JELLYFIN_SHOWS_DIR"] = "/data/media/series"
        });

        Assert.Equal(
            [
                new PathMapping("/host/media/movies", "/data/media/peliculas"),
                new PathMapping("/host/media/shows", "/data/media/series")
            ],
            PathTranslator.ConfiguredMappings().ToArray());
    }

    [Fact]
    public void ConfiguredMappings_HonoursTheSubdirectoryOverrides()
    {
        using var _ = new EnvironmentScope(new()
        {
            ["JELLYFIN_PATH_MAP"] = null,
            ["MEDIA_ROOT"] = "/host/media/",
            ["MOVIES_SUBDIR"] = "Peliculas",
            ["SHOWS_SUBDIR"] = "Series",
            ["JELLYFIN_MOVIES_DIR"] = "/data/media/Peliculas",
            ["JELLYFIN_SHOWS_DIR"] = "/data/media/Series"
        });

        Assert.Equal(
            [
                new PathMapping("/host/media/Peliculas", "/data/media/Peliculas"),
                new PathMapping("/host/media/Series", "/data/media/Series")
            ],
            PathTranslator.ConfiguredMappings().ToArray());
    }

    [Fact]
    public void ConfiguredMappings_PutsTheExplicitMapFirst()
    {
        using var _ = new EnvironmentScope(new()
        {
            ["JELLYFIN_PATH_MAP"] = "/media/disco/Peliculas:/data/media/movies",
            ["MEDIA_ROOT"] = "/host/media",
            ["MOVIES_SUBDIR"] = null,
            ["SHOWS_SUBDIR"] = null,
            ["JELLYFIN_MOVIES_DIR"] = "/data/media/movies",
            ["JELLYFIN_SHOWS_DIR"] = "/data/media/shows"
        });

        Assert.Equal(
            new PathMapping("/media/disco/Peliculas", "/data/media/movies"),
            PathTranslator.ConfiguredMappings().First());
    }

    [Fact]
    public void ConfiguredMappings_AreEmptyWithoutAMediaRoot()
    {
        using var _ = new EnvironmentScope(new()
        {
            ["JELLYFIN_PATH_MAP"] = null,
            ["MEDIA_ROOT"] = null
        });

        Assert.Empty(PathTranslator.ConfiguredMappings());
    }
}
