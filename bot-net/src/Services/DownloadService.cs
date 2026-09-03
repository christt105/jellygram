using System.Diagnostics;
using Bot.Models;
using Bot.Utils;
using TL;

namespace Bot.Services;

public class DownloadService
{
    private readonly WTelegram.Bot _bot;
    private readonly ApiClient _apiClient;
    private readonly TaskQueue _queue;
    private readonly UserClientService? _userClient;

    public DownloadService(WTelegram.Bot bot, ApiClient apiClient, TaskQueue queue, UserClientService? userClient = null)
    {
        _bot = bot;
        _apiClient = apiClient;
        _queue = queue;
        _userClient = userClient;
    }

    public async Task PollAndProcessAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var pendingTasks = await _apiClient.GetPendingDownloadsAsync();
                if (pendingTasks != null && pendingTasks.Count > 0)
                {
                    foreach (var task in pendingTasks)
                    {
                        Log.Info($"[Downloader] Found pending download task {task.TaskId} for collection {task.CollectionId}");
                        // Mark as downloading immediately to avoid duplicate pickups
                        await _apiClient.UpdateDownloadStatusAsync(task.TaskId, "downloading", 0);
                        
                        // Enqueue work
                        await _queue.Enqueue(() => ProcessDownloadTask(task));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("[Downloader] Error in polling loop", ex);
            }

            await Task.Delay(5000, stoppingToken);
        }
    }

    private async Task ProcessDownloadTask(DownloadTask task)
    {
        var tempDir = Path.Combine("/data/temp/downloads", task.TaskId.ToString());
        try
        {
            Log.Info($"[Downloader] Starting task {task.TaskId} ({task.Title})");

            // 1. Refuse payloads we could not unpack, before spending the bandwidth on them
            var announcedNames = task.Files.Select(f => f.Filename).ToList();
            if (!ArchiveDetector.CanProduceVideo(announcedNames))
                throw new Exception(
                    $"Nothing to extract from {DescribeFiles(announcedNames)}: expected a video file " +
                    "or a supported archive (rar, zip, 7z, or their split volumes).");

            Directory.CreateDirectory(tempDir);

            // 2. Download all files
            var totalSize = task.Files.Sum(f => f.Filesize);
            long totalDownloaded = 0;
            long lastReportedTime = DateTime.UtcNow.Ticks;
            var taskStopwatch = System.Diagnostics.Stopwatch.StartNew();

            foreach (var file in task.Files)
            {
                var route = DownloadRouting.Choose(file, _userClient?.IsAuthenticated == true);
                Log.Info($"[Downloader] Fetching message {route.MessageId} for file {file.Filename} via {route.Identity}");
                var fileStopwatch = System.Diagnostics.Stopwatch.StartNew();

                TL.Document? doc = null;

                if (route.Identity == DownloadIdentity.SavedMessages)
                {
                    // Saved Messages is numbered by the account alone, so there is no bot id to
                    // fall back to: without the session the file simply cannot be read.
                    if (_userClient?.IsAuthenticated != true)
                        throw new Exception(
                            $"{file.Filename} is stored in the account's Saved Messages and needs its " +
                            $"session to be read back; authenticate with `{UserClientService.ReauthInstructions}`.");

                    doc = await _userClient.GetDocumentFromSavedAsync(file.MessageId)
                        ?? throw new Exception($"Message {file.MessageId} not found in Saved Messages.");
                }
                else if (route.Identity == DownloadIdentity.UserAccount)
                {
                    // Purely an optimisation: message_id still points at the bot's own copy, so
                    // anything going wrong here costs speed and nothing else.
                    try
                    {
                        doc = await _userClient!.GetDocumentFromBotChatAsync(route.MessageId);
                    }
                    catch (Exception ex)
                    {
                        Log.Info($"[Downloader] The account could not resolve message {route.MessageId}: {ex.Message}");
                    }

                    if (doc == null)
                    {
                        Log.Info($"[Downloader] Falling back to the bot for {file.Filename}.");
                        route = new DownloadRoute(DownloadIdentity.Bot, file.MessageId);
                    }
                }

                var filePath = Path.Combine(tempDir, file.Filename);
                Log.Info($"[Downloader] Downloading {file.Filename} ({file.Filesize} bytes) to {filePath}");

                int maxRetries = 3;
                int attempt = 0;
                bool downloadSuccess = false;

                while (attempt < maxRetries && !downloadSuccess)
                {
                    attempt++;
                    long lastBytes = 0;
                    try
                    {
                        doc ??= await ResolveThroughBotAsync(file.MessageId);

                        void OnProgress(long transmitted, long size)
                        {
                            var delta = transmitted - lastBytes;
                            lastBytes = transmitted;
                            Interlocked.Add(ref totalDownloaded, delta);

                            var nowTicks = DateTime.UtcNow.Ticks;
                            var elapsedSeconds = (nowTicks - lastReportedTime) / (double)TimeSpan.TicksPerSecond;

                            if (elapsedSeconds >= 3 || totalDownloaded == totalSize)
                            {
                                lastReportedTime = nowTicks;
                                var percent = (int)(totalDownloaded * 100 / totalSize);
                                _ = _apiClient.UpdateDownloadStatusAsync(task.TaskId, "downloading", percent);
                            }
                        }

                        if (route.Identity == DownloadIdentity.Bot)
                        {
                            await using var fileStream = System.IO.File.Create(filePath);
                            await _bot.Client.DownloadFileAsync(doc, fileStream, null, OnProgress);
                        }
                        else
                        {
                            // The account owns the file itself: with more than one connection the
                            // parts are written where they belong instead of in arrival order.
                            await _userClient!.DownloadDocumentToFileAsync(doc, filePath, OnProgress);
                        }
                        downloadSuccess = true;
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[Downloader] Attempt {attempt}/{maxRetries} failed to download {file.Filename}: {ex.Message}");
                        Interlocked.Add(ref totalDownloaded, -lastBytes);

                        if (attempt >= maxRetries) throw;

                        // The account is only ever the faster of two ways to the same file, so a
                        // transfer that breaks on it spends its remaining attempts on the bot.
                        if (route.Identity == DownloadIdentity.UserAccount)
                        {
                            Log.Info($"[Downloader] Retrying {file.Filename} through the bot instead of the account.");
                            route = new DownloadRoute(DownloadIdentity.Bot, file.MessageId);
                            doc = null;
                        }

                        var delayMs = (int)Math.Pow(2, attempt) * 1000;
                        Log.Info($"[Downloader] Waiting {delayMs}ms before retrying...");
                        await Task.Delay(delayMs);
                    }
                }
                fileStopwatch.Stop();
                var fileMb = file.Filesize / 1_000_000.0;
                var fileSec = fileStopwatch.Elapsed.TotalSeconds;
                var fileMbps = fileSec > 0 ? fileMb / fileSec : 0;
                Log.Info($"[Downloader] Finished {file.Filename} — {fileMb:F1} MB in {fileSec:F1}s ({fileMbps:F1} MB/s) via {route.Identity}");
            }

            // 3. Check and extract if it's an archive
            var allFiles = Directory.GetFiles(tempDir, "*.*");
            var archivePath = ArchiveDetector.FindEntry(allFiles);

            if (archivePath != null)
            {
                Log.Info($"[Downloader] Archive found: {archivePath}. Extracting...");
                var extractDir = Path.Combine(tempDir, "extracted");
                Directory.CreateDirectory(extractDir);
                await ExtractArchive(archivePath, extractDir);
                Log.Info("[Downloader] Extraction complete.");
            }

            // 4. Find the main video file
            var videoFiles = Directory.GetFiles(tempDir, "*.*", SearchOption.AllDirectories)
                .Where(MediaLibrary.IsVideo)
                .ToList();

            if (videoFiles.Count == 0)
                throw new Exception(archivePath != null
                    ? $"{Path.GetFileName(archivePath)} extracted without any video file inside."
                    : $"No video file in {DescribeFiles(allFiles.Select(Path.GetFileName)!)}.");

            // Pick the largest video file as the main media file
            var mainVideo = videoFiles.OrderByDescending(f => new FileInfo(f).Length).First();
            var extension = Path.GetExtension(mainVideo);

            // 5. Construct the same paths mnamer would have used
            var moviesDir = MediaLibrary.MoviesDir;
            var showsDir = MediaLibrary.ShowsDir;
            string fullPath;

            var versionTag = MediaNaming.BuildVersionTag(task.Quality, task.NameSuffix);

            if (task.MediaType == "movie")
            {
                var dirName = MnamerNaming.MovieDirectory(task.Title, task.Year, task.TmdbId);
                var fileName = MnamerNaming.MovieFile(task.Title, task.Year, versionTag, extension);
                fullPath = Path.Combine(moviesDir, dirName, fileName);
            }
            else
            {
                var seasonNumber = task.SeasonNumber ?? 1;
                var dirName = MnamerNaming.ShowDirectory(task.Title, task.TvdbId, task.TmdbId);
                var seasonDir = MnamerNaming.SeasonDirectory(seasonNumber);
                var fileName = MnamerNaming.EpisodeFile(
                    task.Title, seasonNumber, task.EpisodeNumber ?? 0, versionTag, extension);
                fullPath = Path.Combine(showsDir, dirName, seasonDir, fileName);
            }

            var finalDir = Path.GetDirectoryName(fullPath)!;
            Directory.CreateDirectory(finalDir);

            var collection = await _apiClient.GetCollectionAsync(task.CollectionId);
            var resolvedPath = MediaNaming.ResolveFreePath(fullPath, collection?.LocalPath);
            if (resolvedPath != fullPath)
                Log.Info($"[Downloader] {fullPath} is taken by another collection, using {resolvedPath} instead.");
            fullPath = resolvedPath;

            Log.Info($"[Downloader] Moving video to final path: {fullPath}");
            System.IO.File.Move(mainVideo, fullPath, overwrite: true);

            // Fix permissions so Jellyfin (or other host users) can modify/delete the files
            try
            {
                var chmodDirInfo = new ProcessStartInfo("chmod", $"777 \"{finalDir}\"") { UseShellExecute = false, CreateNoWindow = true };
                Process.Start(chmodDirInfo)?.WaitForExit();
                
                var chmodFileInfo = new ProcessStartInfo("chmod", $"666 \"{fullPath}\"") { UseShellExecute = false, CreateNoWindow = true };
                Process.Start(chmodFileInfo)?.WaitForExit();

                var parentDir = Path.GetDirectoryName(finalDir);
                if (parentDir != null && parentDir != showsDir && parentDir != moviesDir)
                {
                    var chmodParentInfo = new ProcessStartInfo("chmod", $"777 \"{parentDir}\"") { UseShellExecute = false, CreateNoWindow = true };
                    Process.Start(chmodParentInfo)?.WaitForExit();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[Downloader] Failed to set permissions for {fullPath}", ex);
            }

            if (task.MediaType == "tv" && task.LocalMetadata)
            {
                var seasonNumber = task.SeasonNumber ?? 1;
                var seriesRootDir = Path.GetDirectoryName(finalDir)!;
                NfoWriter.WriteTvShowNfo(seriesRootDir, task.Title, task.Overview);
                NfoWriter.WriteSeasonNfo(finalDir, seasonNumber);
                NfoWriter.WriteEpisodeNfo(fullPath, seasonNumber, task.EpisodeNumber ?? 0, task.EpisodeTitle);
            }

            // 6. Read technical metadata from the file we just landed
            await StoreTechnicalMetadata(task.CollectionId, fullPath);

            // 7. Update Status
            await _apiClient.UpdateDownloadStatusAsync(task.TaskId, "completed", 100, localPath: fullPath);
            taskStopwatch.Stop();
            var totalMb = totalSize / 1_000_000.0;
            var totalSec = taskStopwatch.Elapsed.TotalSeconds;
            var totalMbps = totalSec > 0 ? totalMb / totalSec : 0;
            Log.Info($"[Downloader] Task {task.TaskId} done — {totalMb:F1} MB in {totalSec:F1}s ({totalMbps:F1} MB/s avg)");
        }
        catch (Exception ex)
        {
            Log.Error($"[Downloader] Failed to process task {task.TaskId}", ex);
            await _apiClient.UpdateDownloadStatusAsync(task.TaskId, "failed", 0, ex.Message);
        }
        finally
        {
            // 8. Cleanup temp folder
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[Downloader] Failed to clean up temp folder: {tempDir}", ex);
            }
        }
    }

    /// <summary>
    /// Fetches a message from the bot's own chat with the owner. Every file has an id in this
    /// numbering, so this is the way back to any of them when nothing faster is available.
    /// </summary>
    private async Task<TL.Document> ResolveThroughBotAsync(int messageId)
    {
        var messages = await _bot.GetMessagesById(AuthConfig.OwnerUserId, new[] { messageId });
        var msg = messages.FirstOrDefault();
        if (msg == null || (msg.Document == null && msg.Video == null))
            throw new Exception($"Message {messageId} not found or has no document/video.");

        var tlMessage = msg.TLMessage as TL.Message;
        if (tlMessage?.media == null)
            throw new Exception($"Message {messageId} has no media in TLMessage.");

        if (tlMessage.media is MessageMediaDocument mmd && mmd.document is TL.Document doc)
            return doc;

        throw new Exception($"Message {messageId} media document is null.");
    }

    private async Task StoreTechnicalMetadata(int collectionId, string filePath)
    {
        try
        {
            var metadata = await MediaProbe.ReadMetadataAsync(filePath);
            await _apiClient.PatchCollectionAsync(collectionId, new UpdateCollectionRequest
            {
                TechnicalMetadata = metadata
            });
            Log.Info($"[Downloader] Stored technical metadata for collection {collectionId}.");
        }
        catch (Exception ex)
        {
            Log.Error($"[Downloader] Failed to store technical metadata for collection {collectionId}", ex);
        }
    }

    /// <summary>
    /// Names a handful of files for an error message the owner reads in Telegram, so a
    /// rejected download says which files it choked on without pasting a whole season.
    /// </summary>
    private static string DescribeFiles(IEnumerable<string> filenames)
    {
        var names = filenames.ToList();
        if (names.Count == 0) return "an empty download";

        var listed = string.Join(", ", names.Take(3));
        return names.Count > 3 ? $"{listed} (+{names.Count - 3} more)" : listed;
    }

    private static async Task ExtractArchive(string archivePath, string outputDir)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "7z",
            Arguments = $"x \"{archivePath}\" -o\"{outputDir}\" -y",
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
            throw new Exception($"7z extraction failed with exit code {process.ExitCode}: {error}");
        }
    }
}
