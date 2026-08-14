namespace Bot.Utils;

/// <summary>
/// Transfer settings shared by every WTelegram client in the process.
/// </summary>
/// <remarks>
/// The library defaults are 2 parts of 512 KB in flight, which leaves the connection waiting on
/// round-trips rather than filling the link. Raising <see cref="ParallelTransfers"/> is safe to tune
/// per identity; raising the part size is not, and stays fixed at 512 KB for everyone.
///
/// 512 KB is the protocol's own maximum for uploads (upload.saveBigFilePart requires
/// 524288 % part_size == 0, i.e. part_size can only divide 512 KB), even though downloads
/// (upload.getFile) allow up to 1 MB. FilePartSize is one property shared by both directions on the
/// same WTelegram.Client, unvalidated by the library, so setting it above 512 KB broke every upload
/// through the account with a transport-level "Broken pipe" on the first request — confirmed by
/// reverting the part size alone back to 512 KB with everything else unchanged, which fixed it.
/// Concurrency was never the cause: the same failure reproduced at both 8 and 2 parallel transfers.
/// </remarks>
public static class TransferTuning
{
    private const int MaxFilePartSizeBytes = 512 * 1024;

    public static int ParallelTransfers(int defaultValue) =>
        int.TryParse(Environment.GetEnvironmentVariable("TELEGRAM_PARALLEL_TRANSFERS"), out var n) && n > 0
            ? n
            : defaultValue;

    public static int FilePartSizeBytes =>
        Math.Min(
            (int.TryParse(Environment.GetEnvironmentVariable("TELEGRAM_FILE_PART_SIZE_KB"), out var kb) && kb > 0
                ? kb
                : 512) * 1024,
            MaxFilePartSizeBytes);

    public static void Apply(WTelegram.Client client, string who, int defaultParallelTransfers)
    {
        var parallelTransfers = ParallelTransfers(defaultParallelTransfers);
        client.FilePartSize = FilePartSizeBytes;
        client.ParallelTransfers = parallelTransfers;
        Log.Info($"[{who}] Transfers: {parallelTransfers} parts of {FilePartSizeBytes / 1024} KB in flight");
    }
}
