namespace Bot.Utils;

/// <summary>A half-open slice of a file, <c>[Offset, Offset + Length)</c>.</summary>
public readonly record struct ByteRange(long Offset, long Length)
{
    public long End => Offset + Length;
}

/// <summary>
/// Cuts a file into the contiguous slices that several connections download in parallel.
/// </summary>
/// <remarks>
/// Every cut lands on a multiple of the transfer block size because <c>upload.getFile</c> rejects
/// unaligned offsets: they must be multiples of 4 KB, and a single request may not straddle a 1 MB
/// boundary. Aligning to the block size (512 KB here) satisfies both, so no slice ever asks for a
/// part that another slice has already taken.
/// </remarks>
public static class ByteRangePlanner
{
    public static IReadOnlyList<ByteRange> Split(long fileSize, int connections, int blockSize)
    {
        if (blockSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(blockSize), "Block size must be positive.");
        if (fileSize <= 0)
            return Array.Empty<ByteRange>();
        if (connections <= 1)
            return new[] { new ByteRange(0, fileSize) };

        var totalBlocks = (fileSize + blockSize - 1) / blockSize;
        var slices = (int)Math.Min(connections, totalBlocks);
        var blocksPerSlice = totalBlocks / slices;
        var slicesWithAnExtraBlock = (int)(totalBlocks % slices);

        var ranges = new List<ByteRange>(slices);
        long offset = 0;
        for (var i = 0; i < slices; i++)
        {
            var blocks = blocksPerSlice + (i < slicesWithAnExtraBlock ? 1 : 0);
            var end = Math.Min(offset + blocks * blockSize, fileSize);
            ranges.Add(new ByteRange(offset, end - offset));
            offset = end;
        }

        return ranges;
    }
}
