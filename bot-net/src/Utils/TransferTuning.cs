namespace Bot.Utils;

/// <summary>
/// Transfer settings shared by every WTelegram client in the process.
/// </summary>
/// <remarks>
/// The library defaults are 2 parts of 512 KB in flight, which leaves the connection waiting on
/// round-trips rather than filling the link. Measured on the same 720 MB file, interleaved on both
/// identities: 8.8/9.4 MB/s (bot/user) at 2x512 KB, 18.0/27.2 at 4x1 MB, 18.4/27.3 at 8x1 MB.
/// The two ceilings are a different kind of limit: the bot's is a per-account rate limit, so 8
/// parts gets no faster than 4, it just doubles the FLOOD_WAIT count (86 in one run, all against
/// the bot); the user account's is per-connection, so it keeps climbing with more parts.
/// </remarks>
public static class TransferTuning
{
    public static int ParallelTransfers(int defaultValue) =>
        int.TryParse(Environment.GetEnvironmentVariable("TELEGRAM_PARALLEL_TRANSFERS"), out var n) && n > 0
            ? n
            : defaultValue;

    public static int FilePartSizeBytes =>
        (int.TryParse(Environment.GetEnvironmentVariable("TELEGRAM_FILE_PART_SIZE_KB"), out var kb) && kb > 0
            ? kb
            : 1024) * 1024;

    public static void Apply(WTelegram.Client client, string who, int defaultParallelTransfers)
    {
        var parallelTransfers = ParallelTransfers(defaultParallelTransfers);
        client.FilePartSize = FilePartSizeBytes;
        client.ParallelTransfers = parallelTransfers;
        Log.Info($"[{who}] Transfers: {parallelTransfers} parts of {FilePartSizeBytes / 1024} KB in flight");
    }
}
