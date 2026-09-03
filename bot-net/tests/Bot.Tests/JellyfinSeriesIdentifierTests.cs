using System.Text.Json;
using Bot.Models;
using Bot.Services;
using Bot.Utils;
using Xunit;

namespace Bot.Tests;

public class JellyfinSeriesIdentifierTests
{
    private const string SeriesFolder = "/data/media/shows/El Instituto [tmdbid-249039]";
    private const int TmdbId = 249039;

    private static JellyfinItem Item(string id, string path, string? tmdbId = null)
    {
        var providerIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (tmdbId is not null) providerIds["Tmdb"] = tmdbId;

        return new JellyfinItem(id, Path.GetFileName(path), path, providerIds);
    }

    private sealed class FakeJellyfinClient : IJellyfinClient
    {
        private readonly Queue<Func<IReadOnlyList<JellyfinItem>>> _polls = new();

        public int PollCount { get; private set; }
        public int SearchCount { get; private set; }
        public string? AppliedToItemId { get; private set; }
        public JsonElement? SearchResult { get; set; } = JsonDocument.Parse("""{"Name":"El Instituto"}""").RootElement;
        public Exception? ApplyThrows { get; set; }
        public IReadOnlyList<JellyfinItem> SteadyState { get; set; } = [];

        public void ThenReturns(IReadOnlyList<JellyfinItem> items) => _polls.Enqueue(() => items);

        public void ThenThrows(Exception ex) => _polls.Enqueue(() => throw ex);

        public Task<IReadOnlyList<JellyfinItem>> GetSeriesAsync(CancellationToken cancellationToken = default)
        {
            PollCount++;
            return Task.FromResult(_polls.Count > 0 ? _polls.Dequeue()() : SteadyState);
        }

        public Task<JsonElement?> SearchSeriesByTmdbIdAsync(int tmdbId, CancellationToken cancellationToken = default)
        {
            SearchCount++;
            return Task.FromResult(SearchResult);
        }

        public Task ApplyRemoteSearchAsync(
            string itemId, JsonElement result, CancellationToken cancellationToken = default)
        {
            if (ApplyThrows is not null) throw ApplyThrows;

            AppliedToItemId = itemId;
            return Task.CompletedTask;
        }
    }

    private sealed class Recorder
    {
        public List<TimeSpan> Delays { get; } = [];
        public List<string> Notifications { get; } = [];

        public Task Delay(TimeSpan delay, CancellationToken cancellationToken)
        {
            Delays.Add(delay);
            return Task.CompletedTask;
        }

        public Task Notify(string text)
        {
            Notifications.Add(text);
            return Task.CompletedTask;
        }

        public TimeSpan TotalDelay => Delays.Aggregate(TimeSpan.Zero, (total, delay) => total + delay);
    }

    private static (JellyfinSeriesIdentifier Identifier, Recorder Recorder) Build(
        IJellyfinClient? client, IEnumerable<PathMapping>? mappings = null)
    {
        var recorder = new Recorder();
        return (new JellyfinSeriesIdentifier(client, recorder.Notify, recorder.Delay, mappings), recorder);
    }

    [Fact]
    public async Task IdentifyAsync_AppliesTheTmdbIdWhenTheItemIsAlreadyThere()
    {
        var client = new FakeJellyfinClient { SteadyState = [Item("abc123", SeriesFolder)] };
        var (identifier, recorder) = Build(client);

        var outcome = await identifier.IdentifyAsync(SeriesFolder, TmdbId, "El Instituto");

        Assert.Equal(JellyfinSeriesIdentifier.Outcome.Applied, outcome);
        Assert.Equal("abc123", client.AppliedToItemId);
        Assert.Equal(1, client.PollCount);
        Assert.Empty(recorder.Delays);
        Assert.Empty(recorder.Notifications);
    }

    [Fact]
    public async Task IdentifyAsync_WaitsForTheLibraryMonitorToCreateTheItem()
    {
        var client = new FakeJellyfinClient { SteadyState = [Item("abc123", SeriesFolder)] };
        client.ThenReturns([]);
        client.ThenReturns([]);
        client.ThenReturns([Item("other", "/data/media/shows/Another Show [tvdbid-1]")]);
        var (identifier, recorder) = Build(client);

        var outcome = await identifier.IdentifyAsync(SeriesFolder, TmdbId, "El Instituto");

        Assert.Equal(JellyfinSeriesIdentifier.Outcome.Applied, outcome);
        Assert.Equal("abc123", client.AppliedToItemId);
        Assert.Equal(4, client.PollCount);
        Assert.Equal(3, recorder.Delays.Count);
    }

    [Fact]
    public async Task IdentifyAsync_BacksOffBetweenPolls()
    {
        var client = new FakeJellyfinClient { SteadyState = [Item("abc123", SeriesFolder)] };
        client.ThenReturns([]);
        client.ThenReturns([]);
        client.ThenReturns([]);
        var (identifier, recorder) = Build(client);

        await identifier.IdentifyAsync(SeriesFolder, TmdbId, "El Instituto");

        Assert.Equal(
            [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(7.5), TimeSpan.FromSeconds(11.25)],
            recorder.Delays);
    }

    [Fact]
    public async Task IdentifyAsync_RetriesAPollThatThrowsAndThenSucceeds()
    {
        var client = new FakeJellyfinClient { SteadyState = [Item("abc123", SeriesFolder)] };
        client.ThenThrows(new HttpRequestException("Connection refused"));
        client.ThenThrows(new HttpRequestException("Connection refused"));
        var (identifier, recorder) = Build(client);

        var outcome = await identifier.IdentifyAsync(SeriesFolder, TmdbId, "El Instituto");

        Assert.Equal(JellyfinSeriesIdentifier.Outcome.Applied, outcome);
        Assert.Equal("abc123", client.AppliedToItemId);
        Assert.Empty(recorder.Notifications);
    }

    [Fact]
    public async Task IdentifyAsync_GivesUpWithinTheTimeBudgetAndWarns()
    {
        var client = new FakeJellyfinClient();
        var (identifier, recorder) = Build(client);

        var outcome = await identifier.IdentifyAsync(SeriesFolder, TmdbId, "El Instituto");

        Assert.Equal(JellyfinSeriesIdentifier.Outcome.ItemNotFound, outcome);
        Assert.Null(client.AppliedToItemId);
        Assert.Equal(0, client.SearchCount);
        Assert.True(recorder.TotalDelay <= TimeSpan.FromMinutes(2));
        Assert.True(recorder.TotalDelay >= TimeSpan.FromSeconds(90));
        Assert.All(recorder.Delays, delay => Assert.True(delay <= TimeSpan.FromSeconds(20)));
        var notification = Assert.Single(recorder.Notifications);
        Assert.Contains("El Instituto", notification);
    }

    [Fact]
    public async Task IdentifyAsync_GivesUpWhenJellyfinIsDownForTheWholeBudget()
    {
        var client = new FakeJellyfinClient();
        for (var i = 0; i < 20; i++) client.ThenThrows(new HttpRequestException("Connection refused"));
        var (identifier, recorder) = Build(client);

        var outcome = await identifier.IdentifyAsync(SeriesFolder, TmdbId, "El Instituto");

        Assert.Equal(JellyfinSeriesIdentifier.Outcome.ItemNotFound, outcome);
        Assert.Single(recorder.Notifications);
    }

    [Fact]
    public async Task IdentifyAsync_LeavesAnItemThatAlreadyCarriesTheRightTmdbId()
    {
        var client = new FakeJellyfinClient
        {
            SteadyState = [Item("abc123", SeriesFolder, TmdbId.ToString())]
        };
        var (identifier, recorder) = Build(client);

        var outcome = await identifier.IdentifyAsync(SeriesFolder, TmdbId, "El Instituto");

        Assert.Equal(JellyfinSeriesIdentifier.Outcome.AlreadyIdentified, outcome);
        Assert.Equal(0, client.SearchCount);
        Assert.Null(client.AppliedToItemId);
        Assert.Empty(recorder.Notifications);
    }

    [Fact]
    public async Task IdentifyAsync_ReplacesAWrongTmdbId()
    {
        var client = new FakeJellyfinClient { SteadyState = [Item("abc123", SeriesFolder, "111111")] };
        var (identifier, _) = Build(client);

        var outcome = await identifier.IdentifyAsync(SeriesFolder, TmdbId, "El Instituto");

        Assert.Equal(JellyfinSeriesIdentifier.Outcome.Applied, outcome);
        Assert.Equal("abc123", client.AppliedToItemId);
    }

    [Fact]
    public async Task IdentifyAsync_WarnsWhenTheRemoteSearchFindsNothing()
    {
        var client = new FakeJellyfinClient
        {
            SteadyState = [Item("abc123", SeriesFolder)],
            SearchResult = null
        };
        var (identifier, recorder) = Build(client);

        var outcome = await identifier.IdentifyAsync(SeriesFolder, TmdbId, "El Instituto");

        Assert.Equal(JellyfinSeriesIdentifier.Outcome.NoRemoteMatch, outcome);
        Assert.Null(client.AppliedToItemId);
        Assert.Single(recorder.Notifications);
    }

    [Fact]
    public async Task IdentifyAsync_WarnsWhenApplyFails()
    {
        var client = new FakeJellyfinClient
        {
            SteadyState = [Item("abc123", SeriesFolder)],
            ApplyThrows = new HttpRequestException("401 Unauthorized")
        };
        var (identifier, recorder) = Build(client);

        var outcome = await identifier.IdentifyAsync(SeriesFolder, TmdbId, "El Instituto");

        Assert.Equal(JellyfinSeriesIdentifier.Outcome.Failed, outcome);
        Assert.Single(recorder.Notifications);
    }

    [Fact]
    public async Task IdentifyAsync_DoesNothingWithoutAConfiguredClient()
    {
        var (identifier, recorder) = Build(null);

        var outcome = await identifier.IdentifyAsync(SeriesFolder, TmdbId, "El Instituto");

        Assert.Equal(JellyfinSeriesIdentifier.Outcome.Disabled, outcome);
        Assert.Empty(recorder.Notifications);
    }

    [Fact]
    public async Task IdentifyAsync_MatchesThePathJellyfinReportsThroughTheConfiguredMap()
    {
        var client = new FakeJellyfinClient
        {
            SteadyState = [Item("abc123", "/media/disco/Series/El Instituto [tmdbid-249039]")]
        };
        var (identifier, _) = Build(client, [new PathMapping("/media/disco/Series", "/data/media/shows")]);

        var outcome = await identifier.IdentifyAsync(SeriesFolder, TmdbId, "El Instituto");

        Assert.Equal(JellyfinSeriesIdentifier.Outcome.Applied, outcome);
        Assert.Equal("abc123", client.AppliedToItemId);
    }

    [Fact]
    public async Task IdentifyAsync_FallsBackToTheFolderNameWhenNoPathMatches()
    {
        var client = new FakeJellyfinClient
        {
            SteadyState =
            [
                Item("other", "/elsewhere/Another Show [tvdbid-1]"),
                Item("abc123", "/unmapped/El Instituto [tmdbid-249039]")
            ]
        };
        var (identifier, _) = Build(client);

        var outcome = await identifier.IdentifyAsync(SeriesFolder, TmdbId, "El Instituto");

        Assert.Equal(JellyfinSeriesIdentifier.Outcome.Applied, outcome);
        Assert.Equal("abc123", client.AppliedToItemId);
    }

    [Fact]
    public async Task IdentifyAsync_DoesNotGuessWhenTheFolderNameIsAmbiguous()
    {
        var client = new FakeJellyfinClient
        {
            SteadyState =
            [
                Item("one", "/unmapped/a/El Instituto [tmdbid-249039]"),
                Item("two", "/unmapped/b/El Instituto [tmdbid-249039]")
            ]
        };
        var (identifier, _) = Build(client);

        var outcome = await identifier.IdentifyAsync(SeriesFolder, TmdbId, "El Instituto");

        Assert.Equal(JellyfinSeriesIdentifier.Outcome.ItemNotFound, outcome);
        Assert.Null(client.AppliedToItemId);
    }

    [Fact]
    public async Task QueueIdentification_NeverThrowsIntoTheCallersPath()
    {
        var client = new FakeJellyfinClient();
        client.ThenThrows(new InvalidOperationException("boom"));
        var (identifier, _) = Build(client);

        identifier.QueueIdentification(SeriesFolder, TmdbId, "El Instituto");

        await Task.Yield();
    }
}
