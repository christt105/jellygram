using System.Text.Json.Serialization;

namespace Bot.Models;

public class DownloadTask
{
    [JsonPropertyName("task_id")] public int TaskId { get; set; }
    [JsonPropertyName("collection_id")] public int CollectionId { get; set; }
    [JsonPropertyName("media_type")] public string MediaType { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; }
    [JsonPropertyName("year")] public int? Year { get; set; }
    [JsonPropertyName("season_number")] public int? SeasonNumber { get; set; }
    [JsonPropertyName("episode_number")] public int? EpisodeNumber { get; set; }
    [JsonPropertyName("quality")] public string Quality { get; set; }
    [JsonPropertyName("name_suffix")] public string? NameSuffix { get; set; }
    [JsonPropertyName("tmdb_id")] public int? TmdbId { get; set; }
    [JsonPropertyName("tvdb_id")] public int? TvdbId { get; set; }
    [JsonPropertyName("files")] public DownloadFileItem[] Files { get; set; }
}

public class DownloadFileItem
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("message_id")] public int MessageId { get; set; }
    [JsonPropertyName("user_message_id")] public int? UserMessageId { get; set; }
    [JsonPropertyName("filename")] public string Filename { get; set; }
    [JsonPropertyName("filesize")] public long Filesize { get; set; }
    [JsonPropertyName("storage_peer")] public string StoragePeer { get; set; } = "bot";
}
