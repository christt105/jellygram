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
    private InputPeerUser? _botPeer;

    private int _apiId;
    private string _apiHash = "";

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

        _sessionStream = File.Open(SessionPath, FileMode.Open, FileAccess.ReadWrite);
        _client = new WTelegram.Client(ConfigFunc, _sessionStream);
        Bot.Utils.TransferTuning.Apply(_client, "UserClient", defaultParallelTransfers: 8);

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

    private void EnsureReady()
    {
        if (!IsAuthenticated || _client == null || _botPeer == null)
            throw new InvalidOperationException("User client is not authenticated.");
    }

    public void Dispose() => DisposeClient();
}
