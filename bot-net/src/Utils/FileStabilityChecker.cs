namespace Bot.Utils;

/// <summary>
/// Samples a file's size until it holds steady across consecutive reads, so a file still being
/// written (a slow download, or an archive still being extracted) is not reported before it is
/// actually done. <paramref name="readSize"/> is injected so this stays testable without a real
/// filesystem: it returns null once the file disappears, which aborts the wait.
/// </summary>
public static class FileStabilityChecker
{
    public static async Task<long?> WaitForStableSizeAsync(
        Func<long?> readSize,
        int requiredSamples,
        TimeSpan interval,
        CancellationToken ct)
    {
        long? lastSize = null;
        var consecutive = 0;

        while (!ct.IsCancellationRequested)
        {
            var size = readSize();
            if (size is null) return null;

            if (size == lastSize)
                consecutive++;
            else
            {
                consecutive = 1;
                lastSize = size;
            }

            if (consecutive >= requiredSamples) return size;

            await Task.Delay(interval, ct);
        }

        return null;
    }
}
