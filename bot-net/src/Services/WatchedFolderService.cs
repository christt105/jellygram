using System.Collections.Concurrent;
using Bot.Models;
using Bot.Utils;

namespace Bot.Services;

/// <summary>
/// Watches the downloads folder for new video files, waits for their size to stop growing,
/// and reports them to the backend's /watch endpoints. Runs a reconciliation sweep against
/// the backend's active rows on startup first, since FileSystemWatcher only sees events raised
/// while the process is running and guarantees nothing about downtime.
/// </summary>
public class WatchedFolderService
{
    private const int StabilitySamples = 3;
    private const int ReconciliationSamples = 2;
    private static readonly TimeSpan StabilityInterval = TimeSpan.FromSeconds(5);

    private readonly ApiClient _apiClient;
    private readonly ConcurrentDictionary<string, byte> _tracking = new();

    private string _root = "";
    private CancellationToken _lifetimeToken;

    public WatchedFolderService(ApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        _lifetimeToken = stoppingToken;
        _root = MediaLibrary.DownloadsDir;

        try
        {
            Directory.CreateDirectory(_root);
        }
        catch (Exception ex)
        {
            Log.Error($"[WatchedFolder] Cannot access downloads directory {_root}", ex);
            return;
        }

        await ReconcileAsync(stoppingToken);

        using var watcher = new FileSystemWatcher(_root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
        };
        watcher.Created += OnCreated;
        watcher.Deleted += OnDeleted;
        watcher.Renamed += OnRenamed;
        watcher.Error += (_, e) => Log.Error("[WatchedFolder] FileSystemWatcher error", e.GetException());
        watcher.EnableRaisingEvents = true;

        Log.Info($"[WatchedFolder] Watching {_root} for new downloads.");

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ReconcileAsync(CancellationToken ct)
    {
        var onDiskRelative = MediaLibrary.EnumerateVideos(_root)
            .Select(path => WatchedFileReconciliation.ToRelativePath(_root, path))
            .ToList();

        var pending = await _apiClient.GetWatchedFilesAsync("pending") ?? [];
        var notified = await _apiClient.GetWatchedFilesAsync("notified") ?? [];
        var backendActiveRelative = pending.Concat(notified).Select(w => w.Path);

        var diff = WatchedFileReconciliation.Compute(onDiskRelative, backendActiveRelative);

        foreach (var relativePath in diff.Missing)
        {
            Log.Info($"[WatchedFolder] Reconciliation: {relativePath} is no longer on disk, marking missing.");
            await _apiClient.MarkWatchedFileMissingAsync(relativePath);
        }

        foreach (var relativePath in diff.New)
        {
            Log.Info($"[WatchedFolder] Reconciliation: {relativePath} has no watched row yet, verifying stability.");
            var fullPath = WatchedFileReconciliation.ToFullPath(_root, relativePath);
            await TrackAndReportAsync(fullPath, ReconciliationSamples, StabilityInterval, ct);
        }
    }

    private void OnCreated(object? sender, FileSystemEventArgs e)
    {
        if (Directory.Exists(e.FullPath)) return;
        if (!MediaLibrary.IsVideo(e.FullPath)) return;

        _ = TrackAndReportAsync(e.FullPath, StabilitySamples, StabilityInterval, _lifetimeToken);
    }

    private void OnDeleted(object? sender, FileSystemEventArgs e)
    {
        if (!MediaLibrary.IsVideo(e.FullPath)) return;

        var relativePath = WatchedFileReconciliation.ToRelativePath(_root, e.FullPath);
        _ = _apiClient.MarkWatchedFileMissingAsync(relativePath);
    }

    private void OnRenamed(object? sender, RenamedEventArgs e)
    {
        if (Directory.Exists(e.FullPath))
        {
            foreach (var newFilePath in MediaLibrary.EnumerateVideos(e.FullPath))
            {
                var oldFilePath = e.OldFullPath + newFilePath[e.FullPath.Length..];
                HandleRenamedFile(oldFilePath, newFilePath);
            }
            return;
        }

        HandleRenamedFile(e.OldFullPath, e.FullPath);
    }

    private void HandleRenamedFile(string oldPath, string newPath)
    {
        var action = WatchedFileReconciliation.DecideRenameAction(
            MediaLibrary.IsVideo(oldPath), MediaLibrary.IsVideo(newPath));

        switch (action)
        {
            case RenameAction.Ignore:
                break;

            case RenameAction.Rename:
                var oldRelative = WatchedFileReconciliation.ToRelativePath(_root, oldPath);
                var newRelative = WatchedFileReconciliation.ToRelativePath(_root, newPath);
                _ = _apiClient.RenameWatchedFileAsync(oldRelative, newRelative);
                break;

            case RenameAction.TrackNew:
                _ = TrackAndReportAsync(newPath, StabilitySamples, StabilityInterval, _lifetimeToken);
                break;

            case RenameAction.MarkMissing:
                var missingRelative = WatchedFileReconciliation.ToRelativePath(_root, oldPath);
                _ = _apiClient.MarkWatchedFileMissingAsync(missingRelative);
                break;
        }
    }

    private async Task TrackAndReportAsync(string fullPath, int requiredSamples, TimeSpan interval, CancellationToken ct)
    {
        if (!_tracking.TryAdd(fullPath, 0)) return;

        try
        {
            var size = await FileStabilityChecker.WaitForStableSizeAsync(
                () => TryGetFileSize(fullPath), requiredSamples, interval, ct);

            if (size is null) return;

            var relativePath = WatchedFileReconciliation.ToRelativePath(_root, fullPath);
            var filename = Path.GetFileName(fullPath);

            Log.Info($"[WatchedFolder] {relativePath} is stable at {size} bytes, reporting.");
            await _apiClient.ReportWatchedFileAsync(relativePath, filename, size.Value);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Error($"[WatchedFolder] Failed to track {fullPath}", ex);
        }
        finally
        {
            _tracking.TryRemove(fullPath, out _);
        }
    }

    private static long? TryGetFileSize(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
