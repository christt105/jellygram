using System.Text.Json.Serialization;

namespace Bot.Models;

public class WatchedFile
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("filename")] public string Filename { get; set; } = "";
    [JsonPropertyName("filesize")] public long Filesize { get; set; }
    [JsonPropertyName("first_seen_at")] public DateTime FirstSeenAt { get; set; }

    [JsonPropertyName("guess_media_type")] public string? GuessMediaType { get; set; }
    [JsonPropertyName("guess_tmdb_id")] public int? GuessTmdbId { get; set; }
    [JsonPropertyName("guess_tvdb_id")] public int? GuessTvdbId { get; set; }
    [JsonPropertyName("guess_title")] public string? GuessTitle { get; set; }
    [JsonPropertyName("guess_year")] public int? GuessYear { get; set; }
    [JsonPropertyName("guess_season")] public int? GuessSeason { get; set; }
    [JsonPropertyName("guess_episode")] public int? GuessEpisode { get; set; }
    [JsonPropertyName("confidence")] public double Confidence { get; set; }
    [JsonPropertyName("guess_source")] public string? GuessSource { get; set; }

    [JsonPropertyName("status")] public string Status { get; set; } = "pending";
    [JsonPropertyName("notified_at")] public DateTime? NotifiedAt { get; set; }
    [JsonPropertyName("moved_path")] public string? MovedPath { get; set; }
    [JsonPropertyName("error_message")] public string? ErrorMessage { get; set; }
}
