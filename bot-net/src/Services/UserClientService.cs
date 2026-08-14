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
    }

    /// <summary>
    /// Tries to restore a saved session on startup. Returns false if no session exists
    /// or if it has expired (caller should prompt /auth).
    /// </summary>
    public async Task<bool> TryResumeSessionAsync(int apiId, string apiHash)
    {
        if (!File.Exists(SessionPath))
            return false;

        _apiId = apiId;
        _apiHash = apiHash;

        _sessionStream = File.Open(SessionPath, FileMode.Open, FileAccess.ReadWrite);
        _client = new WTelegram.Client(ConfigFunc, _sessionStream);
        Bot.Utils.TransferTuning.Apply(_client, "UserClient", defaultParallelTransfers: 2);

        try
        {
            var user = await _client.LoginUserIfNeeded();
            IsAuthenticated = true;
            IsPremium = (user.flags & User.Flags.premium) != 0;
            Log.Info($"[UserClient] Session restored. Premium={IsPremium}");
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
    /// Uploads a file to Saved Messages. Returns the message ID.
    /// </summary>
    public async Task<int> SendDocumentToSavedAsync(
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
        var sent = await _client.SendMediaAsync(InputPeer.Self, fileName, inputFile, mimeType);
        return sent.id;
    }

    /// <summary>
    /// Fetches a document from Saved Messages by message ID.
    /// Returns null if the message doesn't exist or has no document.
    /// </summary>
    public async Task<Document?> GetDocumentFromSavedAsync(int messageId)
    {
        EnsureReady();

        var result = await _client!.GetMessages(
            InputPeer.Self,
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
        if (!IsAuthenticated || _client == null)
            throw new InvalidOperationException("User client is not authenticated.");
    }

    public void Dispose() => DisposeClient();
}
