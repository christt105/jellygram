namespace Bot.Models;

/// <summary>
/// The slice of a Jellyfin BaseItemDto this bot needs: the item id to act on, the path as
/// Jellyfin's own process sees it (see <see cref="Utils.PathTranslator"/>) and the provider ids
/// already stored for it, used to skip an identification that is already correct.
/// </summary>
public record JellyfinItem(string Id, string Name, string Path, IReadOnlyDictionary<string, string> ProviderIds)
{
    public string? TmdbId => ProviderIds.TryGetValue("Tmdb", out var id) ? id : null;
}
