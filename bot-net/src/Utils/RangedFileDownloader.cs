using System.Runtime.ExceptionServices;

namespace Bot.Utils;

/// <summary>Reads one part of a remote file. One instance per connection.</summary>
public interface IFilePartReader
{
    /// <summary>
    /// Returns at most <paramref name="limit"/> bytes starting at <paramref name="offset"/>,
    /// or fewer at the end of the file.
    /// </summary>
    Task<byte[]> ReadPartAsync(long offset, int limit, CancellationToken cancellationToken);
}

/// <summary>
/// Downloads one file through several readers at once, each taking a different byte range.
/// </summary>
/// <remarks>
/// The parts land straight in their final position in the destination file through positional
/// writes, so there is no reassembly pass and no second copy of the file on disk: a 4 GB download
/// costs 4 GB of temp space, not 8. Positional writes are the only reason readers can share one
/// file handle safely, since they never touch the handle's own cursor.
///
/// A failure anywhere cancels the other readers and removes the destination file, so a partial
/// download is never left behind for the caller to mistake for a finished one.
/// </remarks>
public sealed class RangedFileDownloader
{
    private readonly int _partSize;
    private readonly int _partsInFlightPerReader;

    public RangedFileDownloader(int partSize, int partsInFlightPerReader)
    {
        if (partSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(partSize), "Part size must be positive.");
        if (partsInFlightPerReader <= 0)
            throw new ArgumentOutOfRangeException(nameof(partsInFlightPerReader), "Parts in flight must be positive.");

        _partSize = partSize;
        _partsInFlightPerReader = partsInFlightPerReader;
    }

    public async Task DownloadAsync(
        IReadOnlyList<IFilePartReader> readers,
        long fileSize,
        string destinationPath,
        Action<long, long>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        if (readers.Count == 0)
            throw new ArgumentException("At least one reader is required.", nameof(readers));

        var ranges = ByteRangePlanner.Split(fileSize, readers.Count, _partSize);
        onProgress?.Invoke(0, fileSize);

        if (ranges.Count == 0)
        {
            await File.WriteAllBytesAsync(destinationPath, Array.Empty<byte>(), cancellationToken);
            return;
        }

        long written = 0;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        async Task ReadRangeAsync(IFilePartReader reader, ByteRange range, Microsoft.Win32.SafeHandles.SafeFileHandle handle)
        {
            try
            {
                var options = new ParallelOptions
                {
                    MaxDegreeOfParallelism = _partsInFlightPerReader,
                    CancellationToken = cts.Token
                };

                await Parallel.ForEachAsync(PartOffsets(range), options, async (offset, token) =>
                {
                    var part = await reader.ReadPartAsync(offset, _partSize, token);
                    if (part.Length == 0)
                        throw new InvalidOperationException($"No data returned at offset {offset} of {fileSize}.");

                    var usable = (int)Math.Min(part.Length, range.End - offset);
                    await RandomAccess.WriteAsync(handle, part.AsMemory(0, usable), offset, token);

                    var total = Interlocked.Add(ref written, usable);
                    onProgress?.Invoke(total, fileSize);
                });
            }
            catch
            {
                cts.Cancel();
                throw;
            }
        }

        ExceptionDispatchInfo? failure = null;
        var handle = File.OpenHandle(
            destinationPath, FileMode.Create, FileAccess.Write, FileShare.None,
            FileOptions.Asynchronous, preallocationSize: fileSize);
        try
        {
            var workers = ranges.Select((range, index) => ReadRangeAsync(readers[index], range, handle)).ToArray();
            var all = Task.WhenAll(workers);
            try
            {
                await all;
            }
            catch (Exception ex)
            {
                var errors = all.Exception?.InnerExceptions;
                failure = ExceptionDispatchInfo.Capture(
                    errors?.FirstOrDefault(e => e is not OperationCanceledException) ?? errors?.FirstOrDefault() ?? ex);
            }
        }
        finally
        {
            handle.Dispose();
        }

        if (failure == null && Interlocked.Read(ref written) != fileSize)
            failure = ExceptionDispatchInfo.Capture(new InvalidOperationException(
                $"Downloaded {Interlocked.Read(ref written)} bytes out of {fileSize}."));

        if (failure != null)
        {
            TryDelete(destinationPath);
            failure.Throw();
        }
    }

    private IEnumerable<long> PartOffsets(ByteRange range)
    {
        for (var offset = range.Offset; offset < range.End; offset += _partSize)
            yield return offset;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            Log.Error($"[Downloader] Failed to remove the partial file {path}", ex);
        }
    }
}
