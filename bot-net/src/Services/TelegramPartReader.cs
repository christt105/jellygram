using Bot.Utils;
using TL;

namespace Bot.Services;

/// <summary>
/// Reads byte ranges of one document through a single WTelegram connection.
/// </summary>
/// <remarks>
/// <c>upload.getFile</c> is the same call the library's own downloader makes; going through it
/// directly is what allows one connection to be pointed at an arbitrary offset instead of always
/// starting the file from the beginning.
/// </remarks>
public sealed class TelegramPartReader : IFilePartReader
{
    private readonly InputFileLocationBase _location;
    private WTelegram.Client _client;

    private TelegramPartReader(WTelegram.Client client, InputFileLocationBase location)
    {
        _client = client;
        _location = location;
    }

    /// <summary>
    /// Binds a connection to the data center holding the document, mirroring what
    /// <c>DownloadFileAsync</c> does before its first request.
    /// </summary>
    public static async Task<TelegramPartReader> CreateAsync(WTelegram.Client client, Document document)
    {
        var target = document.dc_id == 0 ? client : await client.GetClientForDC(-document.dc_id, true);
        return new TelegramPartReader(target, document.ToFileLocation());
    }

    public async Task<byte[]> ReadPartAsync(long offset, int limit, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var client = Volatile.Read(ref _client);
        Upload_FileBase part;
        try
        {
            part = await client.Upload_GetFile(_location, offset, limit);
        }
        catch (RpcException ex) when (ex.Code == 303 && ex.Message == "FILE_MIGRATE_X")
        {
            var migrated = await client.GetClientForDC(-ex.X, true);
            Volatile.Write(ref _client, migrated);
            part = await migrated.Upload_GetFile(_location, offset, limit);
        }

        if (part is not Upload_File file)
            throw new WTelegram.WTException(
                $"upload.getFile returned an unsupported {part?.GetType().Name ?? "null"} at offset {offset}.");

        return file.bytes;
    }
}
