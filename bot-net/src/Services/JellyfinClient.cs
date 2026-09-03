using System.Net.Http.Json;
using System.Text.Json;
using Bot.Utils;

namespace Bot.Services;

public class JellyfinClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;

    public JellyfinClient(string baseUrl, string token, TimeSpan? timeout = null, HttpMessageHandler? handler = null)
    {
        _httpClient = new HttpClient(handler ?? new HttpClientHandler())
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
            Timeout = timeout ?? TimeSpan.FromSeconds(15)
        };
        _httpClient.DefaultRequestHeaders.Add("X-Emby-Token", token);

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    /// <summary>
    /// Reads the movie and show libraries Jellyfin knows about, one entry per configured folder.
    /// Libraries of any other kind (music, photos, ...) are left out: bot-net never reads from
    /// them, so their paths not resolving here is not a misconfiguration.
    /// </summary>
    public async Task<IReadOnlyList<LibraryLocation>> GetLibraryLocationsAsync(
        CancellationToken cancellationToken = default)
    {
        var folders = await _httpClient.GetFromJsonAsync<List<VirtualFolder>>(
            "Library/VirtualFolders", _jsonOptions, cancellationToken) ?? [];

        var locations = new List<LibraryLocation>();

        foreach (var folder in folders)
        {
            var kind = KindOf(folder.CollectionType);
            if (kind is null) continue;

            foreach (var path in folder.Locations ?? [])
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                locations.Add(new LibraryLocation(folder.Name ?? "unnamed", kind.Value, path));
            }
        }

        return locations;
    }

    private static LibraryKind? KindOf(string? collectionType) => collectionType?.ToLowerInvariant() switch
    {
        "movies" => LibraryKind.Movies,
        "tvshows" => LibraryKind.Shows,
        _ => null
    };

    private sealed class VirtualFolder
    {
        public string? Name { get; set; }
        public string? CollectionType { get; set; }
        public List<string>? Locations { get; set; }
    }
}
