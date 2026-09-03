using System.Globalization;

namespace Bot.Utils;

/// <summary>
/// Where Jellyfin keeps its own state, how much of it to archive, how often, and which chat
/// receives the archive. Backups stay off until <c>JELLYFIN_BACKUP_DIR</c> names a directory:
/// bot-net cannot guess where Jellyfin's appdata is mounted, the same way it cannot guess the
/// library paths behind <c>JELLYFIN_PATH_MAP</c>.
/// </summary>
public sealed record JellyfinBackupOptions(
    string AppDataDir,
    IReadOnlyList<string> Subdirectories,
    TimeSpan Interval,
    long ChatId,
    int Retain)
{
    /// <summary>
    /// Jellyfin's own state, minus <c>metadata</c>: images and nfo files are re-fetched from the
    /// providers on demand and can outgrow everything else by orders of magnitude. Add it to
    /// <c>JELLYFIN_BACKUP_SUBDIRS</c> to archive it anyway, or blank the variable to take the
    /// whole appdata directory.
    /// </summary>
    public const string DefaultSubdirectories = "config,data,plugins";

    public const double DefaultIntervalHours = 168;

    /// <summary>
    /// How many of the most recent backups to keep in the chat. Older ones are deleted as new
    /// ones land, so a weekly backup left running for years does not quietly fill the chat
    /// history: this many weeks of history is what stays reachable at any time.
    /// </summary>
    public const int DefaultRetain = 4;

    public static readonly TimeSpan MinimumInterval = TimeSpan.FromHours(1);

    /// <summary>
    /// Reads the configuration, or returns null when backups are not configured at all.
    /// </summary>
    /// <param name="read">Environment lookup, injected so tests do not touch the real environment.</param>
    /// <param name="ownerChatId">Chat used when no explicit target is configured.</param>
    public static JellyfinBackupOptions? FromEnvironment(Func<string, string?> read, long ownerChatId)
    {
        var appDataDir = read("JELLYFIN_BACKUP_DIR");
        if (string.IsNullOrWhiteSpace(appDataDir)) return null;

        var rawSubdirs = read("JELLYFIN_BACKUP_SUBDIRS") ?? DefaultSubdirectories;
        var subdirectories = rawSubdirs
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        return new JellyfinBackupOptions(
            appDataDir.Trim(),
            subdirectories,
            ReadInterval(read("JELLYFIN_BACKUP_INTERVAL_HOURS")),
            ReadChatId(read("JELLYFIN_BACKUP_CHAT_ID"), ownerChatId),
            ReadRetain(read("JELLYFIN_BACKUP_RETAIN")));
    }

    /// <summary>
    /// The absolute paths to archive: the configured subdirectories, or the whole appdata
    /// directory when none are named.
    /// </summary>
    public IReadOnlyList<string> Sources() =>
        Subdirectories.Count == 0
            ? [AppDataDir]
            : Subdirectories.Select(subdir => Path.Combine(AppDataDir, subdir)).ToList();

    private static TimeSpan ReadInterval(string? raw)
    {
        var fallback = TimeSpan.FromHours(DefaultIntervalHours);
        if (string.IsNullOrWhiteSpace(raw)) return fallback;

        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var hours) || hours <= 0)
        {
            Log.Warning($"[JellyfinBackup] JELLYFIN_BACKUP_INTERVAL_HOURS is not a positive number ('{raw}'), " +
                        $"falling back to {DefaultIntervalHours} hours.");
            return fallback;
        }

        var interval = TimeSpan.FromHours(hours);
        if (interval >= MinimumInterval) return interval;

        Log.Warning($"[JellyfinBackup] JELLYFIN_BACKUP_INTERVAL_HOURS is below the {MinimumInterval.TotalHours} hour " +
                    "minimum, using the minimum instead.");
        return MinimumInterval;
    }

    private static long ReadChatId(string? raw, long ownerChatId)
    {
        if (string.IsNullOrWhiteSpace(raw)) return ownerChatId;

        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var chatId) && chatId != 0)
            return chatId;

        Log.Warning($"[JellyfinBackup] JELLYFIN_BACKUP_CHAT_ID is not a valid chat id ('{raw}'), " +
                    "sending backups to the owner chat instead.");
        return ownerChatId;
    }

    private static int ReadRetain(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return DefaultRetain;

        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var retain))
            return retain;

        Log.Warning($"[JellyfinBackup] JELLYFIN_BACKUP_RETAIN is not a valid number ('{raw}'), " +
                    $"falling back to {DefaultRetain}.");
        return DefaultRetain;
    }
}
