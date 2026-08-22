using System.Text.Json.Serialization;

namespace Bot.Models;

public class UploadFile
{
    [JsonPropertyName("message_id")] public int MessageId { get; set; }

    [JsonPropertyName("user_message_id")] public int? UserMessageId { get; set; }

    [JsonPropertyName("filename")] public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("filesize")] public long? FileSize { get; set; }

    [JsonPropertyName("mime_type")] public string? MimeType { get; set; }

    [JsonPropertyName("created_at")] public string? UploadDate { get; set; } // ISO string

    [JsonPropertyName("tmdb_id")] public int? TmdbId { get; set; }

    [JsonPropertyName("technical_metadata")] public string? TechnicalMetadata { get; set; }

    [JsonPropertyName("storage_peer")] public string StoragePeer { get; set; } = "bot";
}