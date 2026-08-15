using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bot.Models;
using File = Bot.Models.File;

namespace Bot.Services;

public class ApiClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;

    public ApiClient(string baseUrl = "http://backend:8000")
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl)
        };

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    public async Task<Dictionary<string, object>> HealthAsync()
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<Dictionary<string, object>>("/", _jsonOptions)
                   ?? new Dictionary<string, object> { ["status"] = "unhealthy" };
        }
        catch (HttpRequestException e)
        {
            return new Dictionary<string, object>
            {
                ["status"] = "unhealthy",
                ["error"] = e.Message
            };
        }
    }

    public async Task<UploadFileResult> UploadAsync(UploadFile fileMeta)
    {
        var payload = new
        {
            message_id = fileMeta.MessageId,
            filename = fileMeta.FileName,
            filesize = fileMeta.FileSize,
            mime_type = fileMeta.MimeType,
            created_at = fileMeta.UploadDate ?? DateTime.UtcNow.ToString("o"), // ISO 8601
            tmdb_id = fileMeta.TmdbId,
            technical_metadata = fileMeta.TechnicalMetadata,
            storage_peer = fileMeta.StoragePeer
        };

        var response = await _httpClient.PostAsJsonAsync("/upload", payload);

        if (!response.IsSuccessStatusCode)
        {
            var text = await response.Content.ReadAsStringAsync();
            throw new Exception($"Upload failed: {response.StatusCode} {text}");
        }

        var result = await response.Content.ReadFromJsonAsync<UploadFileResult>(_jsonOptions);
        if (result == null)
            throw new Exception("Upload failed: response is null");

        return result;
    }

    public async Task<bool> IdentifyCollectionAsync(int collectionId, int tmdbId)
    {
        var payload = new { tmdb_id = tmdbId };
        var response = await _httpClient.PostAsJsonAsync($"/collections/{collectionId}/identify", payload);
        
        if (!response.IsSuccessStatusCode)
        {
            var text = await response.Content.ReadAsStringAsync();
            Log.Error($"Identify failed: {response.StatusCode} {text}");
            return false;
        }
        
        return true;
    }

    public async Task<bool> ReidentifyCollectionAsync(int collectionId, int? tmdbId = null)
    {
        var payload = tmdbId.HasValue ? new { tmdb_id = (int?)tmdbId.Value } : new { tmdb_id = (int?)null };
        var response = await _httpClient.PostAsJsonAsync($"/collections/{collectionId}/reidentify", payload);

        if (!response.IsSuccessStatusCode)
        {
            var text = await response.Content.ReadAsStringAsync();
            Log.Error($"ReidentifyCollection failed: {response.StatusCode} {text}");
            return false;
        }

        return true;
    }


    public async Task<bool> ReidentifySeriesAsync(int seriesId, int tmdbId)
    {
        var response = await _httpClient.PostAsync($"/series/{seriesId}/reidentify?new_tmdb_id={tmdbId}", null);
        if (!response.IsSuccessStatusCode)
        {
            var text = await response.Content.ReadAsStringAsync();
            Log.Error($"ReidentifySeries failed: {response.StatusCode} {text}");
            return false;
        }
        return true;
    }

    public async Task<bool> ReidentifyMovieAsync(int movieId, int tmdbId)
    {
        var response = await _httpClient.PostAsync($"/movies/{movieId}/reidentify?new_tmdb_id={tmdbId}", null);
        if (!response.IsSuccessStatusCode)
        {
            var text = await response.Content.ReadAsStringAsync();
            Log.Error($"ReidentifyMovie failed: {response.StatusCode} {text}");
            return false;
        }
        return true;
    }

    private async Task<T?> GetSafeAsync<T>(string url)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<T>(url, _jsonOptions);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }
    }

    public async Task<List<Movie>?> GetMoviesAsync()
    {
        return await GetSafeAsync<List<Movie>>("/movies");
    }

    public async Task<List<Series>?> GetSeriesAsync()
    {
        return await GetSafeAsync<List<Series>>("/series");
    }

    public async Task<Series?> GetSeriesAsync(int localId)
    {
        return await GetSafeAsync<Series>($"/series/{localId}");
    }

    public async Task<Movie?> GetMovieAsync(int localId)
    {
        return await GetSafeAsync<Movie>($"/movies/{localId}");
    }

    public async Task<Movie?> GetMovieByTmdbAsync(int tmdbId)
    {
        return await GetSafeAsync<Movie>($"/movies/tmdb/{tmdbId}");
    }


    public Task<List<Dictionary<string, object>>?> SearchMoviesAsync(string query)
    {
        return _httpClient.GetFromJsonAsync<List<Dictionary<string, object>>>(
            $"/movies/search?q={Uri.EscapeDataString(query)}", _jsonOptions);
    }

    public async Task<List<Collection>?> GetCollectionsAsync(int movieId)
    {
        return await GetSafeAsync<List<Collection>>($"/movies/{movieId}/collections");
    }

    public async Task<Collection?> GetCollectionAsync(int collectionId)
    {
        return await GetSafeAsync<Collection>($"/collections/{collectionId}");
    }

    public async Task<File?> GetFileAsync(int fileId)
    {
        return await GetSafeAsync<File>($"/files/{fileId}");
    }

    public async Task<File?> PatchFileAsync(int fileId, FileUpdate update)
    {
        var response = await _httpClient.PatchAsync(
            $"/files/{fileId}",
            JsonContent.Create(update, options: _jsonOptions)
        );

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        if (!response.IsSuccessStatusCode)
        {
            var text = await response.Content.ReadAsStringAsync();
            throw new Exception($"PatchFile failed: {response.StatusCode} {text}");
        }

        return await response.Content.ReadFromJsonAsync<File>(_jsonOptions);
    }


    public async Task<bool> DeleteFileAsync(int fileId)
    {
        var resp = await _httpClient.DeleteAsync($"/files/{fileId}");
        return resp.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteCollectionAsync(int collectionId)
    {
        var resp = await _httpClient.DeleteAsync($"/collections/{collectionId}");
        return resp.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteSeriesAsync(int seriesId)
    {
        var resp = await _httpClient.DeleteAsync($"/series/{seriesId}");
        return resp.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteMovieAsync(int movieId)
    {
        var resp = await _httpClient.DeleteAsync($"/movies/{movieId}");
        return resp.IsSuccessStatusCode;
    }

    public async Task<Collection?> CreateCollectionAsync(CreateCollectionRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync("/collections", request, _jsonOptions);

        if (response.IsSuccessStatusCode) return await response.Content.ReadFromJsonAsync<Collection>(_jsonOptions);
        var text = await response.Content.ReadAsStringAsync();
        throw new Exception($"CreateCollection failed: {response.StatusCode} {text}");
    }

    public async Task<Collection?> PatchCollectionAsync(int collectionId, UpdateCollectionRequest update)
    {
        var response = await _httpClient.PatchAsync(
            $"/collections/{collectionId}",
            JsonContent.Create(update, options: _jsonOptions)
        );

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        if (!response.IsSuccessStatusCode)
        {
            var text = await response.Content.ReadAsStringAsync();
            throw new Exception($"PatchCollection failed: {response.StatusCode} {text}");
        }

        return await response.Content.ReadFromJsonAsync<Collection>(_jsonOptions);
    }

    /// <summary>
    /// Clears a collection's local path. Needs its own method because
    /// <see cref="UpdateCollectionRequest"/> drops null fields, so it cannot express
    /// "set this back to empty".
    /// </summary>
    public async Task<bool> ClearCollectionLocalPathAsync(int collectionId)
    {
        var payload = new Dictionary<string, string?> { ["local_path"] = null };
        var response = await _httpClient.PatchAsync(
            $"/collections/{collectionId}",
            JsonContent.Create(payload, options: _jsonOptions)
        );
        return response.IsSuccessStatusCode;
    }

    public async Task<List<Collection>?> GetOrphansAsync()
    {
        return await GetSafeAsync<List<Collection>>("/maintenance/orphans");
    }
    public async Task<List<DownloadTask>?> GetPendingDownloadsAsync()
    {
        return await GetSafeAsync<List<DownloadTask>>("/downloads/pending");
    }

    public async Task<bool> UpdateDownloadStatusAsync(int taskId, string status, int progress, string? errorMessage = null, string? localPath = null)
    {
        var payload = new
        {
            status = status,
            progress = progress,
            error_message = errorMessage,
            local_path = localPath
        };
        var response = await _httpClient.PostAsJsonAsync($"/downloads/{taskId}/status", payload);
        return response.IsSuccessStatusCode;
    }

    public async Task<List<UploadTask>?> GetPendingUploadsAsync()
    {
        return await GetSafeAsync<List<UploadTask>>("/uploads/pending");
    }

    public async Task<bool> UpdateUploadStatusAsync(int taskId, string status, int progress, string? errorMessage = null)
    {
        var payload = new
        {
            status = status,
            progress = progress,
            error_message = errorMessage
        };
        var response = await _httpClient.PostAsJsonAsync($"/uploads/{taskId}/status", payload);
        return response.IsSuccessStatusCode;
    }

    public async Task<List<UploadTask>?> GetUploadsQueueAsync()
    {
        return await GetSafeAsync<List<UploadTask>>("/uploads/queue");
    }

    public async Task<List<QueueDownloadTask>?> GetDownloadsQueueAsync()
    {
        return await GetSafeAsync<List<QueueDownloadTask>>("/downloads/queue");
    }

    public async Task<bool> CancelUploadTaskAsync(int taskId)
    {
        var response = await _httpClient.DeleteAsync($"/uploads/{taskId}");
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> CancelDownloadTaskAsync(int taskId)
    {
        var response = await _httpClient.DeleteAsync($"/downloads/{taskId}");
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> RetryUploadTaskAsync(int taskId)
    {
        var response = await _httpClient.PostAsync($"/uploads/{taskId}/retry", null);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> RetryDownloadTaskAsync(int taskId)
    {
        var response = await _httpClient.PostAsync($"/downloads/{taskId}/retry", null);
        return response.IsSuccessStatusCode;
    }

    public async Task<List<TmdbSearchResult>?> SearchTmdbAsync(string query)
    {
        return await GetSafeAsync<List<TmdbSearchResult>>($"/tmdb/search?query={Uri.EscapeDataString(query)}");
    }

    public async Task<bool> CreateManualMediaAsync(int tmdbId, string mediaType)
    {
        var payload = new { tmdb_id = tmdbId, media_type = mediaType };
        var response = await _httpClient.PostAsJsonAsync("/maintenance/create-media", payload);
        return response.IsSuccessStatusCode;
    }

    public async Task<byte[]?> DownloadBackupAsync()
    {
        var response = await _httpClient.GetAsync("/maintenance/backup");
        if (!response.IsSuccessStatusCode)
        {
            var text = await response.Content.ReadAsStringAsync();
            Log.Error($"Backup failed: {response.StatusCode} {text}");
            return null;
        }

        return await response.Content.ReadAsByteArrayAsync();
    }
}