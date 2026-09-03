using System.Text.Json;
using Bot.Models;

namespace Bot.Services;

/// <summary>
/// The three Jellyfin calls forcing an identification needs, behind an interface so
/// <see cref="JellyfinSeriesIdentifier"/> can be tested without a server.
/// </summary>
public interface IJellyfinClient
{
    /// <summary>Every Series item in the libraries, with its path and provider ids.</summary>
    Task<IReadOnlyList<JellyfinItem>> GetSeriesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a TMDB id into the RemoteSearchResult Jellyfin would have picked itself. Passing
    /// the id as a provider id means Jellyfin looks the show up directly instead of running the
    /// title search that can match the wrong show. Null when no provider returns a match.
    /// </summary>
    Task<JsonElement?> SearchSeriesByTmdbIdAsync(int tmdbId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a RemoteSearchResult onto an existing item, which is what the "Identify" dialog in
    /// the web UI does. The result is passed through verbatim as it came from
    /// <see cref="SearchSeriesByTmdbIdAsync"/>, since Jellyfin expects the whole object back.
    /// </summary>
    Task ApplyRemoteSearchAsync(string itemId, JsonElement result, CancellationToken cancellationToken = default);
}
