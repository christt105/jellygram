using System.Diagnostics;
using System.IO;
using Bot.Models;
using Bot.Utils;
using Telegram.Bot.Types;
using File = System.IO.File;

namespace Bot.Services;

public class ProgressStream : Stream
{
    private readonly Stream _baseStream;
    private readonly Action<long> _onProgress;
    private long _totalRead = 0;

    public ProgressStream(Stream baseStream, Action<long> onProgress)
    {
        _baseStream = baseStream;
        _onProgress = onProgress;
    }

    public override bool CanRead => _baseStream.CanRead;
    public override bool CanSeek => _baseStream.CanSeek;
    public override bool CanWrite => _baseStream.CanWrite;
    public override long Length => _baseStream.Length;
    public override long Position
    {
        get => _baseStream.Position;
        set => _baseStream.Position = value;
    }

    public override void Flush() => _baseStream.Flush();

    public override int Read(byte[] buffer, int offset, int count)
    {
        int read = _baseStream.Read(buffer, offset, count);
        if (read > 0)
        {
            _totalRead += read;
            _onProgress(_totalRead);
        }
        return read;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        int read = await _baseStream.ReadAsync(buffer, offset, count, cancellationToken);
        if (read > 0)
        {
            _totalRead += read;
            _onProgress(_totalRead);
        }
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int read = await _baseStream.ReadAsync(buffer, cancellationToken);
        if (read > 0)
        {
            _totalRead += read;
            _onProgress(_totalRead);
        }
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin) => _baseStream.Seek(offset, origin);
    public override void SetLength(long value) => _baseStream.SetLength(value);
    public override void Write(byte[] buffer, int offset, int count) => _baseStream.Write(buffer, offset, count);
}

/// <summary>
/// A stored file, as the two identities number it. <paramref name="MessageId"/> is the bot's
/// own and is always there; <paramref name="UserMessageId"/> is only filled in when the account
/// carried the upload, and merely makes reading the file back faster.
/// </summary>
public readonly record struct StoredUpload(int MessageId, int? UserMessageId);

public class UploadService
{
    private readonly WTelegram.Bot _bot;
    private readonly ApiClient _apiClient;
    private readonly TaskQueue _queue;
    private readonly UserClientService? _userClient;
    private readonly UploadEchoRegistry? _echoes;

    /// <summary>
    /// How long to wait for the bot's copy of a file the account sent. It normally lands within
    /// seconds of the send finishing; this only has to outlast a hiccup in the update stream.
    /// </summary>
    private static readonly TimeSpan EchoTimeout = TimeSpan.FromMinutes(2);

    public UploadService(WTelegram.Bot bot, ApiClient apiClient, TaskQueue queue,
        UserClientService? userClient = null, UploadEchoRegistry? echoes = null)
    {
        _bot = bot;
        _apiClient = apiClient;
        _queue = queue;
        _userClient = userClient;
        _echoes = echoes;
    }

    public async Task PollAndProcessAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var pendingTasks = await _apiClient.GetPendingUploadsAsync();
                if (pendingTasks != null && pendingTasks.Count > 0)
                {
                    foreach (var task in pendingTasks)
                    {
                        Log.Info($"[Uploader] Found pending upload task {task.Id} for Jellyfin item {task.JellyfinId}");
                        // Mark as uploading immediately to avoid duplicate pickups
                        await _apiClient.UpdateUploadStatusAsync(task.Id, "uploading", 0);
                        
                        // Enqueue work
                        await _queue.Enqueue(() => ProcessUploadTask(task));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("[Uploader] Error in polling loop", ex);
            }

            await Task.Delay(5000, stoppingToken);
        }
    }

    private async Task ProcessUploadTask(UploadTask task)
    {
        var tempDir = Path.Combine("/data/temp/uploads", task.Id.ToString());
        try
        {
            Log.Info($"[Uploader] Starting task {task.Id} ({task.Title})");
            Directory.CreateDirectory(tempDir);

            // 1. Translate the path Jellyfin reported into a container path
            var localPath = PathTranslator.Translate(task.Path);
            Log.Info($"[Uploader] Translated path: {task.Path} -> {localPath}");

            if (!File.Exists(localPath) && !Directory.Exists(localPath))
            {
                var hint = localPath == task.Path
                    ? " — no JELLYFIN_PATH_MAP or IMPORT_*_DIR prefix matched the path Jellyfin reported"
                    : "";
                throw new Exception($"Local file or directory not found: {localPath}{hint}");
            }

            // 2. Discover video files to upload
            var videoExtensions = new[] { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm" };
            var filesToUpload = new List<string>();

            if (File.Exists(localPath))
            {
                if (videoExtensions.Contains(Path.GetExtension(localPath).ToLowerInvariant()))
                {
                    filesToUpload.Add(localPath);
                }
            }
            else if (Directory.Exists(localPath))
            {
                var discovered = Directory.GetFiles(localPath, "*.*", SearchOption.AllDirectories)
                    .Where(f => videoExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .OrderBy(f => f)
                    .ToList();
                filesToUpload.AddRange(discovered);
            }

            if (filesToUpload.Count == 0)
            {
                throw new Exception("No video files found to upload.");
            }

            Log.Info($"[Uploader] Found {filesToUpload.Count} video file(s) to process.");

            // Calculate total size for progress reporting
            long totalBytesToUpload = 0;
            var fileSizes = new Dictionary<string, long>();
            foreach (var videoFile in filesToUpload)
            {
                var len = new FileInfo(videoFile).Length;
                totalBytesToUpload += len;
                fileSizes[videoFile] = len;
            }

            long totalUploadedBytes = 0;
            long lastReportedTime = DateTime.UtcNow.Ticks;

            // 3. Process and upload each video file
            for (int fileIndex = 0; fileIndex < filesToUpload.Count; fileIndex++)
            {
                var videoFile = filesToUpload[fileIndex];
                var fileInfo = new FileInfo(videoFile);
                var fileSize = fileInfo.Length;
                
                string? technicalMetadata = null;
                try
                {
                    technicalMetadata = await MediaProbe.ReadMetadataAsync(videoFile);
                }
                catch (Exception ex)
                {
                    Log.Error($"[Uploader] Failed to extract metadata for {videoFile}: {ex.Message}");
                }

                Log.Info($"[Uploader] Processing file {fileIndex + 1}/{filesToUpload.Count}: {fileInfo.Name} ({fileSize} bytes)");

                // The account can send parts the bot API would refuse, so the size a file is cut
                // into follows whichever identity is going to carry it.
                var useUserClient = _echoes != null && _userClient?.IsAuthenticated == true;
                var splitLimit = useUserClient
                    ? _userClient!.SplitLimitBytes
                    : UserClientService.FallbackSplitLimitBytes;

                if (fileSize > splitLimit)
                {
                    Log.Info($"[Uploader] File exceeds split limit ({splitLimit / 1_000_000} MB). Splitting with 7z store-only...");
                    var partsDir = Path.Combine(tempDir, $"file_{fileIndex}");
                    if (Directory.Exists(partsDir))
                        Directory.Delete(partsDir, true);
                    Directory.CreateDirectory(partsDir);

                    var archiveBaseName = Path.GetFileNameWithoutExtension(videoFile) + ".zip";
                    var archivePath = Path.Combine(partsDir, archiveBaseName);
                    await SplitAndPackage(videoFile, archivePath, splitLimit);

                    var parts = Directory.GetFiles(partsDir, "*.*")
                        .Where(f => !f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(f => f)
                        .ToList();

                    Log.Info($"[Uploader] Split complete. Created {parts.Count} parts.");

                    for (int partIndex = 0; partIndex < parts.Count; partIndex++)
                    {
                        var partPath = parts[partIndex];
                        var partInfo = new FileInfo(partPath);
                        Log.Info($"[Uploader] Uploading part {partIndex + 1}/{parts.Count}: {partInfo.Name}");

                        long[] pp = [totalUploadedBytes, lastReportedTime];
                        var stored = await SendSingleFileWithRetryAsync(
                            partPath, partInfo.Name, "application/zip",
                            task.Id, pp, totalBytesToUpload, useUserClient);
                        totalUploadedBytes = pp[0];
                        lastReportedTime = pp[1];

                        await _apiClient.UploadAsync(new UploadFile
                        {
                            MessageId = stored.MessageId,
                            UserMessageId = stored.UserMessageId,
                            FileName = partInfo.Name,
                            FileSize = partInfo.Length,
                            MimeType = "application/zip",
                            UploadDate = DateTime.UtcNow.ToString("O"),
                            TmdbId = task.TmdbId,
                            TechnicalMetadata = partIndex == 0 ? technicalMetadata : null,
                            StoragePeer = "bot"
                        });
                    }
                }
                else
                {
                    Log.Info($"[Uploader] File is within limit. Uploading directly...");
                    var mime = GuessMime(videoFile);

                    long[] dp = [totalUploadedBytes, lastReportedTime];
                    var stored = await SendSingleFileWithRetryAsync(
                        videoFile, fileInfo.Name, mime,
                        task.Id, dp, totalBytesToUpload, useUserClient);
                    totalUploadedBytes = dp[0];
                    lastReportedTime = dp[1];

                    await _apiClient.UploadAsync(new UploadFile
                    {
                        MessageId = stored.MessageId,
                        UserMessageId = stored.UserMessageId,
                        FileName = fileInfo.Name,
                        FileSize = fileInfo.Length,
                        MimeType = mime,
                        UploadDate = DateTime.UtcNow.ToString("O"),
                        TmdbId = task.TmdbId,
                        TechnicalMetadata = technicalMetadata,
                        StoragePeer = "bot"
                    });
                }
            }

            // 4. Update Status
            await _apiClient.UpdateUploadStatusAsync(task.Id, "completed", 100);
            Log.Info($"[Uploader] Upload task {task.Id} completed successfully.");
        }
        catch (Exception ex)
        {
            Log.Error($"[Uploader] Failed to process upload task {task.Id}", ex);
            await _apiClient.UpdateUploadStatusAsync(task.Id, "failed", 0, ex.Message);
        }
        finally
        {
            // 5. Cleanup temp folder
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[Uploader] Failed to clean up temp folder: {tempDir}", ex);
            }
        }
    }

    // progress[0] = totalUploadedBytes, progress[1] = lastReportedTime
    private async Task<StoredUpload> SendSingleFileWithRetryAsync(
        string filePath, string fileName, string mimeType,
        int taskId, long[] progress, long totalBytesToUpload, bool useUserClient)
    {
        const int maxRetries = 3;
        int attempt = 0;

        while (true)
        {
            attempt++;
            long lastBytes = 0;
            try
            {
                if (useUserClient)
                {
                    // Registered before sending: Telegram can deliver the bot its copy while the
                    // account is still finishing the call that reports the account's own id.
                    using var echo = _echoes!.Expect(fileName, new FileInfo(filePath).Length);

                    var userMessageId = await _userClient!.SendDocumentToBotChatAsync(filePath, fileName, mimeType,
                        (transmitted, totalSize) =>
                        {
                            var delta = transmitted - lastBytes;
                            lastBytes = transmitted;
                            progress[0] += delta;

                            var nowTicks = DateTime.UtcNow.Ticks;
                            var elapsed = (nowTicks - progress[1]) / (double)TimeSpan.TicksPerSecond;
                            if (elapsed >= 3 || progress[0] == totalBytesToUpload)
                            {
                                progress[1] = nowTicks;
                                var percent = (int)(progress[0] * 100 / totalBytesToUpload);
                                _ = _apiClient.UpdateUploadStatusAsync(taskId, "uploading", percent);
                            }
                        });

                    var botMessageId = await echo.WaitAsync(EchoTimeout)
                        ?? throw new UploadEchoMissingException(
                            $"The account uploaded {fileName} but the bot did not see its copy within " +
                            $"{EchoTimeout.TotalMinutes:F0} minutes, so there is no id it could read the file " +
                            "back with. The file is in the chat: check whether it arrived late and was " +
                            "registered on its own before uploading it again.");

                    Log.Info($"[Uploader] {fileName} stored as {botMessageId} (bot) / {userMessageId} (account).");
                    return new StoredUpload(botMessageId, userMessageId);
                }
                else
                {
                    WTelegram.Types.Message? sent;
                    await using (var fileStream = File.OpenRead(filePath))
                    {
                        var progressStream = new ProgressStream(fileStream, transmitted =>
                        {
                            var delta = transmitted - lastBytes;
                            lastBytes = transmitted;
                            progress[0] += delta;

                            var nowTicks = DateTime.UtcNow.Ticks;
                            var elapsed = (nowTicks - progress[1]) / (double)TimeSpan.TicksPerSecond;
                            if (elapsed >= 3 || progress[0] == totalBytesToUpload)
                            {
                                progress[1] = nowTicks;
                                var percent = (int)(progress[0] * 100 / totalBytesToUpload);
                                _ = _apiClient.UpdateUploadStatusAsync(taskId, "uploading", percent);
                            }
                        });

                        sent = await _bot.SendDocument(
                            AuthConfig.OwnerUserId,
                            new InputFileStream(progressStream, fileName),
                            caption: fileName
                        );
                    }
                    return new StoredUpload(sent!.MessageId, null);
                }
            }
            catch (UploadEchoMissingException)
            {
                // The file did reach the chat, only unannounced. Sending it again would leave
                // two copies behind, so this one is not worth a retry.
                throw;
            }
            catch (Exception ex)
            {
                Log.Error($"[Uploader] Attempt {attempt}/{maxRetries} failed for {fileName}: {ex.Message}");
                progress[0] -= lastBytes;

                if (attempt >= maxRetries) throw;

                await Task.Delay((int)Math.Pow(2, attempt) * 1000);
            }
        }
    }

    private static async Task SplitAndPackage(string filePath, string archivePath, long splitLimitBytes)
    {
        var partSizeMb = splitLimitBytes / 1_000_000;
        var startInfo = new ProcessStartInfo
        {
            FileName = "7z",
            Arguments = $"a -mx0 -v{partSizeMb}m \"{archivePath}\" \"{filePath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(startInfo);
        if (process == null) throw new Exception("Failed to start 7z process.");
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync();
            throw new Exception($"7z splitting failed with exit code {process.ExitCode}: {error}");
        }
    }

    private static string GuessMime(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".mkv" => "video/x-matroska",
            ".mp4" => "video/mp4",
            ".avi" => "video/x-msvideo",
            ".mov" => "video/quicktime",
            ".wmv" => "video/x-ms-wmv",
            ".webm" => "video/webm",
            _ => "video/octet-stream"
        };

}
