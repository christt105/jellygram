using Bot.Models;
using Bot.Utils;

namespace Bot.Services;

/// <summary>
/// Forces the TMDB identification of a series folder through Jellyfin's API right after a
/// /watch-confirmed episode lands in it, instead of trusting Jellyfin to read the id tag in the
/// folder name (which it honors inconsistently, and which falls back to the TMDB id when the show
/// has no TVDB entry, exactly the case where a title search can match the wrong show).
///
/// The item to act on only exists once Jellyfin's LibraryMonitor has noticed the new folder, which
/// takes anywhere from a few seconds to over a minute, so the work is polled in the background and
/// never blocks the move or the Telegram reply. Every failure mode is terminal for the
/// identification only: the file is already moved and stays moved.
/// </summary>
public class JellyfinSeriesIdentifier
{
    public enum Outcome
    {
        /// <summary>No JELLYFIN_URL/JELLYFIN_TOKEN, nothing was attempted.</summary>
        Disabled,

        /// <summary>The TMDB id was written onto the Jellyfin item.</summary>
        Applied,

        /// <summary>The item already carried the right TMDB id, so it was left alone.</summary>
        AlreadyIdentified,

        /// <summary>Jellyfin never created an item for the folder within the time budget.</summary>
        ItemNotFound,

        /// <summary>Jellyfin's metadata providers returned no match for the TMDB id.</summary>
        NoRemoteMatch,

        /// <summary>Jellyfin answered, but the search or apply call failed.</summary>
        Failed
    }

    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan Budget = TimeSpan.FromMinutes(2);

    private readonly IJellyfinClient? _client;
    private readonly Func<string, Task>? _notify;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly IReadOnlyList<PathMapping>? _pathMappings;

    public JellyfinSeriesIdentifier(
        IJellyfinClient? client,
        Func<string, Task>? notify = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        IEnumerable<PathMapping>? pathMappings = null)
    {
        _client = client;
        _notify = notify;
        _delay = delay ?? Task.Delay;
        _pathMappings = pathMappings?.ToList();
    }

    /// <summary>
    /// Starts the identification without waiting for it. Nothing the background work does can
    /// surface as an exception on the caller's path, which has already committed the move.
    /// </summary>
    public void QueueIdentification(
        string seriesFolder, int tmdbId, string title, CancellationToken cancellationToken = default)
    {
        if (_client is null) return;

        _ = Task.Run(async () =>
        {
            try
            {
                await IdentifyAsync(seriesFolder, tmdbId, title, cancellationToken);
            }
            catch (Exception ex)
            {
                Log.Error($"[Jellyfin] Identification of {title} (tmdb {tmdbId}) blew up", ex);
            }
        }, cancellationToken);
    }

    public async Task<Outcome> IdentifyAsync(
        string seriesFolder, int tmdbId, string title, CancellationToken cancellationToken = default)
    {
        if (_client is null) return Outcome.Disabled;

        var item = await WaitForItemAsync(seriesFolder, title, cancellationToken);

        if (item is null)
        {
            Log.Warning(
                $"[Jellyfin] No item appeared for {seriesFolder} within {Budget.TotalSeconds:F0}s. " +
                $"{title} keeps whatever identification Jellyfin gives it on its next scan.");
            await NotifyAsync(
                $"⚠️ Could not force the TMDB id of <b>{title}</b> in Jellyfin: no library item showed up for " +
                $"<code>{seriesFolder}</code>. Check its identification by hand.");
            return Outcome.ItemNotFound;
        }

        if (item.TmdbId == tmdbId.ToString())
        {
            Log.Info($"[Jellyfin] {title} is already identified as tmdb {tmdbId}, leaving it alone.");
            return Outcome.AlreadyIdentified;
        }

        try
        {
            var match = await _client.SearchSeriesByTmdbIdAsync(tmdbId, cancellationToken);

            if (match is null)
            {
                Log.Warning($"[Jellyfin] Remote search returned no match for tmdb {tmdbId} ({title}).");
                await NotifyAsync(
                    $"⚠️ Jellyfin found no metadata match for <b>{title}</b> (tmdb {tmdbId}). " +
                    "Check its identification by hand.");
                return Outcome.NoRemoteMatch;
            }

            await _client.ApplyRemoteSearchAsync(item.Id, match.Value, cancellationToken);
            Log.Info($"[Jellyfin] Applied tmdb {tmdbId} to item {item.Id} ({title}).");
            return Outcome.Applied;
        }
        catch (Exception ex)
        {
            Log.Error($"[Jellyfin] Could not apply tmdb {tmdbId} to {title}", ex);
            await NotifyAsync(
                $"⚠️ Could not force the TMDB id of <b>{title}</b> in Jellyfin: {ex.Message}. " +
                "Check its identification by hand.");
            return Outcome.Failed;
        }
    }

    /// <summary>
    /// Polls until the folder shows up as a Series item, backing off from 5s to 20s between
    /// attempts and giving up after two minutes. A poll that throws (Jellyfin restarting, network
    /// blip) is treated like an empty answer and retried, since the budget is the real limit.
    /// </summary>
    private async Task<JellyfinItem?> WaitForItemAsync(
        string seriesFolder, string title, CancellationToken cancellationToken)
    {
        var spent = TimeSpan.Zero;
        var delay = InitialDelay;

        while (true)
        {
            try
            {
                var series = await _client!.GetSeriesAsync(cancellationToken);
                var item = FindByFolder(series, seriesFolder);
                if (item is not null) return item;
            }
            catch (Exception ex)
            {
                Log.Warning($"[Jellyfin] Poll for {title} failed, retrying: {ex.Message}");
            }

            if (spent + delay > Budget) return null;

            await _delay(delay, cancellationToken);
            spent += delay;
            delay = TimeSpan.FromTicks(Math.Min((long)(delay.Ticks * 1.5), MaxDelay.Ticks));
        }
    }

    /// <summary>
    /// Matches on the path Jellyfin reports, translated back into this container's view. The
    /// folder name is used as a fallback when no path matches at all, which covers a missing or
    /// wrong JELLYFIN_PATH_MAP, and only when a single item carries that name so it cannot pick
    /// the wrong show.
    /// </summary>
    private JellyfinItem? FindByFolder(IReadOnlyList<JellyfinItem> series, string seriesFolder)
    {
        var target = Normalize(seriesFolder);

        foreach (var item in series)
        {
            if (item.Path.Length == 0) continue;

            if (Normalize(Translate(item.Path)) == target || Normalize(item.Path) == target)
                return item;
        }

        var folderName = Path.GetFileName(seriesFolder.TrimEnd('/'));
        var byName = series.Where(item => Path.GetFileName(item.Path.TrimEnd('/')) == folderName).ToList();

        if (byName.Count == 1)
        {
            Log.Warning(
                $"[Jellyfin] Matched {folderName} by folder name: no reported path mapped to {seriesFolder}. " +
                "Check JELLYFIN_PATH_MAP.");
            return byName[0];
        }

        return null;
    }

    private string Translate(string reportedPath) =>
        _pathMappings is null
            ? PathTranslator.Translate(reportedPath)
            : PathTranslator.Translate(reportedPath, _pathMappings);

    private static string Normalize(string path) => path.TrimEnd('/');

    private async Task NotifyAsync(string text)
    {
        if (_notify is null) return;

        try
        {
            await _notify(text);
        }
        catch (Exception ex)
        {
            Log.Error("[Jellyfin] Could not send the identification warning", ex);
        }
    }
}
