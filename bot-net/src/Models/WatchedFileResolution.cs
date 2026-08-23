using System.Text.Json.Serialization;

namespace Bot.Models;

/// <summary>Response of /watch/{id}/confirm and /watch/{id}/correct: the resolved identity
/// bot-net needs to build the destination path and move the file. No file I/O happens
/// server-side, only the identity resolution against TMDB.</summary>
public class WatchedFileResolution
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("filename")] public string Filename { get; set; } = "";
    [JsonPropertyName("tmdb_id")] public int TmdbId { get; set; }
    [JsonPropertyName("media_type")] public string MediaType { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("season")] public int? Season { get; set; }
    [JsonPropertyName("episode")] public int? Episode { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = "";

    /// <summary>Builds the identity a move needs directly from a WatchedFile row, for rows that
    /// reached confirmed/corrected without a POST /watch/{id}/confirm|correct round trip in this
    /// process (e.g. actioned from the web) — the backend already persisted the resolved guess_*
    /// fields on the row itself when it handled that request. Null if the row isn't actually
    /// resolved yet (a defensive check; the caller filters by status already).</summary>
    public static WatchedFileResolution? FromWatchedFile(WatchedFile row)
    {
        if (row.GuessTmdbId is null || row.GuessMediaType is null || row.GuessTitle is null) return null;

        return new WatchedFileResolution
        {
            Id = row.Id,
            Path = row.Path,
            Filename = row.Filename,
            TmdbId = row.GuessTmdbId.Value,
            MediaType = row.GuessMediaType,
            Title = row.GuessTitle,
            Season = row.GuessSeason,
            Episode = row.GuessEpisode,
            Status = row.Status,
        };
    }
}
