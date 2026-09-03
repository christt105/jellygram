using Bot.Utils;

namespace Bot.Services;

/// <summary>
/// Periodically archives Jellyfin's own state (not the media library) and sends it through
/// <see cref="UploadService"/> so a copy lives outside the host running Jellyfin. Disabled unless
/// <see cref="JellyfinBackupOptions.FromEnvironment"/> finds a configured backup directory.
/// </summary>
public class JellyfinBackupService
{
    private const string LastRunPath = "/data/jellyfin-backup-last-run";
    private const string HistoryPath = "/data/jellyfin-backup-history.json";
    private const string ScratchDir = "/data/temp/backups";

    private readonly WTelegram.Bot _bot;
    private readonly UploadService _uploadService;
    private readonly JellyfinBackupOptions _options;
    private readonly LastRunFile _lastRun;
    private readonly BackupHistoryFile _history;

    public JellyfinBackupService(WTelegram.Bot bot, UploadService uploadService, JellyfinBackupOptions options)
    {
        _bot = bot;
        _uploadService = uploadService;
        _options = options;
        _lastRun = new LastRunFile(LastRunPath);
        _history = new BackupHistoryFile(HistoryPath);
    }

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        Log.Info($"[JellyfinBackup] Enabled: archiving {string.Join(", ", _options.Sources())} " +
                 $"every {_options.Interval.TotalHours}h, keeping the last {_options.Retain} in chat {_options.ChatId}.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = BackupSchedule.DelayUntilNextRun(_lastRun.Read(), DateTime.UtcNow, _options.Interval);
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await RunOnceAsync(stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        var archivePath = Path.Combine(ScratchDir, $"jellyfin-backup-{DateTime.UtcNow:yyyyMMdd-HHmmss}.tar.gz");
        try
        {
            var result = DirectoryArchiver.Create(_options.AppDataDir, _options.Sources(), archivePath);
            if (result.Skipped.Count > 0)
                Log.Warning($"[JellyfinBackup] {result.Skipped.Count} file(s) could not be read and were left out of the archive.");

            Log.Info($"[JellyfinBackup] Archived {result.Entries} file(s), uploading...");

            var caption = $"Jellyfin backup {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC";
            var messageIds = await _uploadService.SendLocalFileAsync(archivePath, _options.ChatId, caption, "application/gzip");

            await ApplyRetentionAsync(messageIds, stoppingToken);

            Log.Info($"[JellyfinBackup] Backup uploaded ({messageIds.Count} message(s)).");
        }
        catch (Exception ex)
        {
            Log.Error("[JellyfinBackup] Backup run failed", ex);
        }
        finally
        {
            _lastRun.Write(DateTime.UtcNow);
            try
            {
                if (File.Exists(archivePath)) File.Delete(archivePath);
            }
            catch (Exception ex)
            {
                Log.Error($"[JellyfinBackup] Failed to clean up {archivePath}", ex);
            }
        }
    }

    private async Task ApplyRetentionAsync(IReadOnlyList<int> newMessageIds, CancellationToken stoppingToken)
    {
        var generations = _history.Read();
        generations.Add(newMessageIds.ToArray());

        var (remaining, toDelete) = BackupRetention.Apply(generations, _options.Retain);

        foreach (var generation in toDelete)
        {
            try
            {
                await _bot.DeleteMessages(_options.ChatId, generation);
            }
            catch (Exception ex)
            {
                Log.Warning($"[JellyfinBackup] Could not delete a pruned backup's message(s): {ex.Message}");
            }
        }

        _history.Write(remaining.ToList());
    }
}
