namespace Bot.Utils;

/// <summary>
/// Which backup generations fall outside the retention window. Kept apart from the history file
/// and the Telegram calls so the trimming rule itself can be tested without either.
/// </summary>
public static class BackupRetention
{
    /// <summary>
    /// Splits <paramref name="generations"/> (oldest first) into what stays and what to delete,
    /// keeping only the most recent <paramref name="keep"/> entries. A non-positive
    /// <paramref name="keep"/> keeps everything, since <see cref="JellyfinBackupOptions"/> treats
    /// that as an explicit opt-out of pruning.
    /// </summary>
    public static (IReadOnlyList<T> Remaining, IReadOnlyList<T> ToDelete) Apply<T>(
        IReadOnlyList<T> generations, int keep)
    {
        if (keep <= 0 || generations.Count <= keep)
            return (generations, []);

        var cut = generations.Count - keep;
        return (generations.Skip(cut).ToList(), generations.Take(cut).ToList());
    }
}
