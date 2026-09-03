using Bot.Utils;
using TL;

namespace Bot.Services;

/// <summary>
/// Wraps a WTelegram.Client user session alongside the bot.
/// Used for file transfers when the owner has Telegram Premium (4 GB limit, higher speed).
/// The session is created out of band by <see cref="Bot.Utils.ConsoleAuth"/>; this service only
/// restores it, because Telegram invalidates login codes sent through a chat.
/// </summary>
public class UserClientService : IDisposable
{
    private WTelegram.Client? _client;
    private Stream? _sessionStream;
    private byte[]? _sessionSnapshot;
    private InputPeerUser? _botPeer;

    private int _apiId;
    private string _apiHash = "";

    private const int DefaultParallelTransfers = 8;

    public bool IsAuthenticated { get; private set; }
    public bool IsPremium { get; private set; }

    public const string SessionPath = "/data/user_client.session";

    public const string ReauthInstructions = "docker compose run --rm -it bot-net auth";

    public long SplitLimitBytes
    {
        get
        {
            var envMb = Environment.GetEnvironmentVariable("UPLOAD_SPLIT_LIMIT_MB");
            if (envMb != null && long.TryParse(envMb, out var mb))
                return mb * 1_000_000L;
            return IsAuthenticated && IsPremium ? 3_900_000_000L : 1_950_000_000L;
        }
    }

    public static long FallbackSplitLimitBytes
    {
        get
        {
            var envMb = Environment.GetEnvironmentVariable("UPLOAD_SPLIT_LIMIT_MB");
            if (envMb != null && long.TryParse(envMb, out var mb))
                return mb * 1_000_000L;
            return 1_950_000_000L;
        }
    }

    /// <summary>
    /// Only ever resumes: "-1" tells WTelegram to accept whichever account the saved session
    /// belongs to, so it validates the session without asking for the phone number. Being asked
    /// for a credential means the session is unusable and a new one has to be created out of band.
    /// </summary>
    private string? ConfigFunc(string what) => what switch
    {
        "api_id" => _apiId.ToString(),
        "api_hash" => _apiHash,
        "user_id" => "-1",
        "phone_number" or "verification_code" or "password" =>
            throw new InvalidOperationException(
                $"The saved session is missing or expired; re-authenticate with `{ReauthInstructions}`"),
        _ => null
    };

    private void DisposeClient()
    {
        _client?.Dispose();
        _sessionStream?.Dispose();
        _client = null;
        _sessionStream = null;
        _sessionSnapshot = null;
        _botPeer = null;
    }

    /// <summary>
    /// Tries to restore a saved session on startup. Returns false if no session exists,
    /// if it has expired, or if the bot chat cannot be resolved (caller should prompt /auth).
    /// A false here is never fatal: everything falls back to the bot's own transfers.
    /// </summary>
    public async Task<bool> TryResumeSessionAsync(int apiId, string apiHash, string botUsername)
    {
        if (!File.Exists(SessionPath))
            return false;

        _apiId = apiId;
        _apiHash = apiHash;

        // Read before the client takes the file for itself: extra download connections are built
        // from these bytes, and the client holds the file without sharing it.
        _sessionSnapshot = await File.ReadAllBytesAsync(SessionPath);

        _sessionStream = File.Open(SessionPath, FileMode.Open, FileAccess.ReadWrite);
        _client = new WTelegram.Client(ConfigFunc, _sessionStream);
        TransferTuning.Apply(_client, "UserClient", DefaultParallelTransfers);

        try
        {
            var user = await _client.LoginUserIfNeeded();

            // The account and the bot are the two participants of the same private chat, which
            // is where every file is stored. Without that peer the account has nothing to say.
            var resolved = await _client.Contacts_ResolveUsername(botUsername.TrimStart('@'));
            var botUser = resolved.users.Values.First();
            _botPeer = new InputPeerUser(botUser.id, botUser.access_hash);

            IsAuthenticated = true;
            IsPremium = (user.flags & User.Flags.premium) != 0;
            Log.Info($"[UserClient] Session restored. Premium={IsPremium}, chat with @{botUsername} resolved.");
            return true;
        }
        catch (Exception ex)
        {
            Log.Info($"[UserClient] Session resume failed: {ex.Message}");
            DisposeClient();
            return false;
        }
    }

    // ── File operations ─────────────────────────────────────────────────────

    /// <summary>
    /// Uploads a file to the chat the account shares with the bot, and returns the message ID
    /// <b>in the account's own numbering</b>. The bot knows the very same message under a
    /// different ID, which it only learns from the copy Telegram delivers to it; storing this
    /// one alone would leave a file the bot could never read.
    /// </summary>
    public async Task<int> SendDocumentToBotChatAsync(
        string filePath,
        string fileName,
        string mimeType,
        Action<long, long>? onProgress = null)
    {
        EnsureReady();

        WTelegram.Client.ProgressCallback? cb = onProgress != null
            ? (transmitted, total) => onProgress(transmitted, total)
            : null;

        var inputFile = await _client!.UploadFileAsync(filePath, cb);
        var sent = await _client.SendMediaAsync(_botPeer!, fileName, inputFile, mimeType);
        return sent.id;
    }

    /// <summary>
    /// Fetches a document from the bot chat by the message ID the account numbers it with.
    /// Returns null if the message doesn't exist or has no document.
    /// </summary>
    public Task<Document?> GetDocumentFromBotChatAsync(int messageId) =>
        GetDocumentAsync(_botPeer!, messageId);

    /// <summary>
    /// Fetches a document from Saved Messages by message ID, for the handful of files older
    /// builds parked there. Returns null if the message doesn't exist or has no document.
    /// </summary>
    public Task<Document?> GetDocumentFromSavedAsync(int messageId) =>
        GetDocumentAsync(InputPeer.Self, messageId);

    private async Task<Document?> GetDocumentAsync(InputPeer peer, int messageId)
    {
        EnsureReady();

        var result = await _client!.GetMessages(
            peer,
            new InputMessage[] { new InputMessageID { id = messageId } }
        );

        var msg = result.Messages.OfType<Message>().FirstOrDefault();
        if (msg?.media is MessageMediaDocument { document: Document doc })
            return doc;

        return null;
    }

    /// <summary>
    /// Downloads a document to a stream.
    /// </summary>
    public async Task DownloadDocumentAsync(Document doc, Stream output, Action<long, long>? onProgress = null)
    {
        EnsureReady();

        WTelegram.Client.ProgressCallback? cb = onProgress != null
            ? (transmitted, total) => onProgress(transmitted, total)
            : null;

        await _client!.DownloadFileAsync(doc, output, null, cb);
    }

    /// <summary>
    /// Downloads a document to a file, spreading its byte ranges over several connections when
    /// <c>PREMIUM_DOWNLOAD_CONNECTIONS</c> asks for more than one. The account's throughput is
    /// capped per connection rather than by a rate limit, so this is the only axis that lifts it.
    /// One connection, or an unusable extra one, is the single-connection download as it was.
    /// </summary>
    public async Task DownloadDocumentToFileAsync(Document doc, string destinationPath, Action<long, long>? onProgress = null)
    {
        EnsureReady();

        var connections = PremiumDownloadTuning.Connections;
        if (!PremiumDownloadTuning.UseMultipleConnections(IsAuthenticated, connections))
        {
            await DownloadOverOneConnectionAsync(doc, destinationPath, onProgress);
            return;
        }

        var extraClients = await OpenExtraConnectionsAsync(connections - 1);
        if (extraClients.Count == 0)
        {
            Log.Info("[UserClient] No extra connection available, downloading over one.");
            await DownloadOverOneConnectionAsync(doc, destinationPath, onProgress);
            return;
        }

        try
        {
            var readers = new List<IFilePartReader> { await TelegramPartReader.CreateAsync(_client!, doc) };
            foreach (var extra in extraClients)
                readers.Add(await TelegramPartReader.CreateAsync(extra, doc));

            var downloader = new RangedFileDownloader(
                TransferTuning.FilePartSizeBytes,
                TransferTuning.ParallelTransfers(DefaultParallelTransfers));

            Log.Info($"[UserClient] Downloading {doc.size} bytes over {readers.Count} connections.");
            await downloader.DownloadAsync(readers, doc.size, destinationPath, onProgress);
            return;
        }
        catch (Exception ex)
        {
            Log.Error($"[UserClient] Multi-connection download failed, retrying over one connection: {ex.Message}");
        }
        finally
        {
            foreach (var extra in extraClients)
                extra.Dispose();
        }

        await DownloadOverOneConnectionAsync(doc, destinationPath, onProgress);
    }

    private async Task DownloadOverOneConnectionAsync(Document doc, string destinationPath, Action<long, long>? onProgress)
    {
        await using var output = File.Create(destinationPath);
        await DownloadDocumentAsync(doc, output, onProgress);
    }

    /// <summary>
    /// Opens up to <paramref name="count"/> further connections for the same account, each one a
    /// client of its own restored from a copy of the session taken at startup. The copy carries the
    /// MTProto session id along with the authorisation key, which two live connections cannot
    /// share, so <c>DisableUpdates</c> is called first: it renews that id, and a download
    /// connection has no use for the update stream anyway. The copies never write back, so the
    /// session file on disk stays the property of the main client.
    /// Returns fewer clients, possibly none, if any of them cannot be opened.
    /// </summary>
    private async Task<List<WTelegram.Client>> OpenExtraConnectionsAsync(int count)
    {
        var clients = new List<WTelegram.Client>(count);
        if (_sessionSnapshot == null)
            return clients;

        for (var i = 0; i < count; i++)
        {
            WTelegram.Client? client = null;
            try
            {
                client = new WTelegram.Client(ConfigFunc, (byte[])_sessionSnapshot.Clone(), _ => { });
                client.DisableUpdates(true);
                TransferTuning.Apply(client, $"UserClient+{i + 1}", DefaultParallelTransfers);
                await client.LoginUserIfNeeded();
                clients.Add(client);
            }
            catch (Exception ex)
            {
                Log.Info($"[UserClient] Extra connection {i + 1} could not be opened: {ex.Message}");
                client?.Dispose();
                break;
            }
        }

        return clients;
    }

    private void EnsureReady()
    {
        if (!IsAuthenticated || _client == null || _botPeer == null)
            throw new InvalidOperationException("User client is not authenticated.");
    }

    public void Dispose() => DisposeClient();
}
