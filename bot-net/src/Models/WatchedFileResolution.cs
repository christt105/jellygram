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
}
