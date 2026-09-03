using System.Net;
using System.Net.Http;
using Bot.Services;
using Bot.Utils;
using Xunit;

namespace Bot.Tests;

public class JellyfinClientTests
{
    private sealed class CannedHandler : HttpMessageHandler
    {
        private readonly string _body;

        public CannedHandler(string body) => _body = body;

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    private const string VirtualFolders = """
        [
          { "Name": "Peliculas", "CollectionType": "movies", "Locations": ["/media/disco/Peliculas"] },
          { "Name": "Series", "CollectionType": "tvshows", "Locations": ["/media/disco/Series", "/media/disco/Anime"] },
          { "Name": "Musica", "CollectionType": "music", "Locations": ["/media/disco/Musica"] },
          { "Name": "Vacia", "CollectionType": "movies", "Locations": [] }
        ]
        """;

    [Fact]
    public async Task GetLibraryLocationsAsync_ReadsOneEntryPerFolderOfEveryMovieAndShowLibrary()
    {
        var handler = new CannedHandler(VirtualFolders);
        using var client = new JellyfinClient("http://jellyfin:8096/", "token", handler: handler);

        var locations = await client.GetLibraryLocationsAsync();

        Assert.Equal(
            [
                new LibraryLocation("Peliculas", LibraryKind.Movies, "/media/disco/Peliculas"),
                new LibraryLocation("Series", LibraryKind.Shows, "/media/disco/Series"),
                new LibraryLocation("Series", LibraryKind.Shows, "/media/disco/Anime")
            ],
            locations);
    }

    [Fact]
    public async Task GetLibraryLocationsAsync_AuthenticatesAgainstTheVirtualFoldersEndpoint()
    {
        var handler = new CannedHandler("[]");
        using var client = new JellyfinClient("http://jellyfin:8096", "s3cret", handler: handler);

        await client.GetLibraryLocationsAsync();

        Assert.Equal("http://jellyfin:8096/Library/VirtualFolders", handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal("s3cret", handler.LastRequest.Headers.GetValues("X-Emby-Token").Single());
    }
}
