using Bot.Utils;
using Xunit;

namespace Bot.Tests;

public class JellyfinPathCheckTests
{
    private static readonly PathMapping[] Mappings =
    [
        new("/media/disco/Peliculas", "/data/media/movies"),
        new("/media/disco/Series", "/data/media/shows")
    ];

    private static readonly LibraryLocation Movies =
        new("Peliculas", LibraryKind.Movies, "/media/disco/Peliculas");

    private static readonly LibraryLocation Shows =
        new("Series", LibraryKind.Shows, "/media/disco/Series");

    private static Func<string, bool> Existing(params string[] paths) => paths.Contains;

    [Fact]
    public void Check_ResolvesALocationWhoseMappedTargetExists()
    {
        var results = JellyfinPathCheck.Check([Movies], Mappings, Existing("/data/media/movies"));

        Assert.Equal(LibraryPathStatus.Resolved, Assert.Single(results).Status);
        Assert.Equal("/data/media/movies", results[0].LocalPath);
    }

    [Fact]
    public void Check_ResolvesALocationJellyfinSeesUnderTheSamePathAsBotNet()
    {
        var location = new LibraryLocation("Peliculas", LibraryKind.Movies, "/data/media/movies");

        var results = JellyfinPathCheck.Check([location], [], Existing("/data/media/movies"));

        Assert.Equal(LibraryPathStatus.Resolved, Assert.Single(results).Status);
    }

    [Fact]
    public void Check_FlagsAMappingWhoseTargetIsNotThere()
    {
        var results = JellyfinPathCheck.Check([Movies], Mappings, Existing("/data/media/peliculas"));

        Assert.Equal(LibraryPathStatus.Mapped, Assert.Single(results).Status);
    }

    [Fact]
    public void Check_FlagsALocationNoMappingMatches()
    {
        var results = JellyfinPathCheck.Check([Movies], [], Existing("/data/media/movies"));

        var result = Assert.Single(results);
        Assert.Equal(LibraryPathStatus.Unmapped, result.Status);
        Assert.Equal("/media/disco/Peliculas", result.LocalPath);
    }

    [Fact]
    public void Check_SkipsAnEmptyReportedPath()
    {
        var location = new LibraryLocation("Peliculas", LibraryKind.Movies, "   ");

        Assert.Empty(JellyfinPathCheck.Check([location], Mappings, Existing()));
    }

    [Fact]
    public void Warnings_AreSilentWhenEveryLocationResolves()
    {
        var results = JellyfinPathCheck.Check(
            [Movies, Shows], Mappings, Existing("/data/media/movies", "/data/media/shows"));

        Assert.Empty(JellyfinPathCheck.Warnings(results));
    }

    [Fact]
    public void Warnings_NameTheLibraryAndBothPathsForABadMapping()
    {
        var results = JellyfinPathCheck.Check([Movies], Mappings, Existing());

        var warning = Assert.Single(JellyfinPathCheck.Warnings(results));
        Assert.Contains("Peliculas", warning);
        Assert.Contains("/media/disco/Peliculas", warning);
        Assert.Contains("/data/media/movies", warning);
        Assert.Contains("JELLYFIN_PATH_MAP", warning);
    }

    [Fact]
    public void Warnings_SuggestAnEntryForAnUnmappedLocation()
    {
        using var _ = new EnvironmentScope(new()
        {
            ["JELLYFIN_MOVIES_DIR"] = "/data/media/movies",
            ["JELLYFIN_SHOWS_DIR"] = "/data/media/shows"
        });

        var results = JellyfinPathCheck.Check([Movies, Shows], [], Existing());
        var warnings = JellyfinPathCheck.Warnings(results).ToArray();

        Assert.Equal(2, warnings.Length);
        Assert.Contains("/media/disco/Peliculas:/data/media/movies", warnings[0]);
        Assert.Contains("/media/disco/Series:/data/media/shows", warnings[1]);
    }

    [Fact]
    public void Warnings_OnlyCoverTheLocationsThatDoNotResolve()
    {
        var results = JellyfinPathCheck.Check([Movies, Shows], Mappings, Existing("/data/media/shows"));

        var warning = Assert.Single(JellyfinPathCheck.Warnings(results));
        Assert.Contains("Peliculas", warning);
    }
}
