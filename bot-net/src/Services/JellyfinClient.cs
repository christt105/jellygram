using System.Net.Http.Json;
using System.Text.Json;
using Bot.Models;

namespace Bot.Services;

/// <summary>
/// Minimal Jellyfin API client built on a plain HttpClient: three endpoints do not justify
/// pulling in a generated SDK the way the web frontend does.
/// </summary>
public class JellyfinClient : IJellyfinClient, IDisposable
{
    private const string SeriesQuery = "Items?recursive=true&includeItemTypes=Series&fields=Path,ProviderIds";

    private readonly HttpClient _http;

    public JellyfinClient(string baseUrl, string token, HttpMessageHandler? handler = null)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = new Uri($"{baseUrl.TrimEnd('/')}/");
        _http.DefaultRequestHeaders.Add("X-Emby-Token", token);
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Null when JELLYFIN_URL or JELLYFIN_TOKEN is unset, so a deployment without them keeps
    /// working with the folder id tag alone instead of failing on every move.
    /// </summary>
    public static JellyfinClient? FromEnvironment()
    {
        var url = Environment.GetEnvironmentVariable("JELLYFIN_URL");
        var token = Environment.GetEnvironmentVariable("JELLYFIN_TOKEN");

        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(token))
        {
            Log.Warning(
                "[Jellyfin] JELLYFIN_URL or JELLYFIN_TOKEN is not set: confirmed series will not be identified " +
                "through the API, only through the id tag in the folder name.");
            return null;
        }

        return new JellyfinClient(url, token);
    }

    public void Dispose() => _http.Dispose();

    public async Task<IReadOnlyList<JellyfinItem>> GetSeriesAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync(SeriesQuery, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("Items", out var items) ||
            items.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return items.EnumerateArray().Select(ToItem).Where(item => item is not null).ToList()!;
    }

    public async Task<JsonElement?> SearchSeriesByTmdbIdAsync(
        int tmdbId, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            SearchInfo = new { ProviderIds = new Dictionary<string, string> { ["Tmdb"] = tmdbId.ToString() } },
            IncludeDisabledProviders = true
        };

        using var response = await _http.PostAsJsonAsync("Items/RemoteSearch/Series", payload, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (document.RootElement.ValueKind != JsonValueKind.Array) return null;

        foreach (var result in document.RootElement.EnumerateArray())
            return result.Clone();

        return null;
    }

    public async Task ApplyRemoteSearchAsync(
        string itemId, JsonElement result, CancellationToken cancellationToken = default)
    {
        using var response = await _http.PostAsJsonAsync(
            $"Items/RemoteSearch/Apply/{itemId}?replaceAllImages=false", result, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static JellyfinItem? ToItem(JsonElement element)
    {
        if (!element.TryGetProperty("Id", out var id) || id.ValueKind != JsonValueKind.String) return null;

        var path = element.TryGetProperty("Path", out var pathElement) && pathElement.ValueKind == JsonValueKind.String
            ? pathElement.GetString()!
            : "";

        var name = element.TryGetProperty("Name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String
            ? nameElement.GetString()!
            : "";

        var providerIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (element.TryGetProperty("ProviderIds", out var ids) && ids.ValueKind == JsonValueKind.Object)
        {
            foreach (var provider in ids.EnumerateObject())
            {
                if (provider.Value.ValueKind == JsonValueKind.String)
                    providerIds[provider.Name] = provider.Value.GetString()!;
            }
        }

        return new JellyfinItem(id.GetString()!, name, path, providerIds);
    }
}
