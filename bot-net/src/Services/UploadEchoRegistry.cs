namespace Bot.Services;

/// <summary>
/// Pairs an upload sent through the user account with the copy the bot receives back.
/// A private chat numbers its messages once per participant, so the account's own id is
/// useless to the bot: the only way to learn the bot's id for a file the account sent is
/// to read it off the incoming message Telegram delivers to the bot moments later.
/// </summary>
public class UploadEchoRegistry
{
    private readonly object _gate = new();
    private readonly List<UploadEcho> _waiting = new();

    /// <summary>
    /// Announces an upload about to be sent. Must be called before sending: the echo can
    /// reach the bot before the send call has even returned to the uploader.
    /// </summary>
    public UploadEcho Expect(string fileName, long fileSize)
    {
        var echo = new UploadEcho(this, fileName, fileSize);
        lock (_gate) _waiting.Add(echo);
        return echo;
    }

    /// <summary>
    /// Hands an incoming document to whoever is waiting for it. False means nobody is, so the
    /// message is a file the owner sent by hand and has to be registered the usual way.
    /// </summary>
    public bool TryClaim(string? fileName, long fileSize, int messageId)
    {
        lock (_gate)
        {
            var echo = _waiting.FirstOrDefault(e => e.Matches(fileName, fileSize));
            if (echo == null) return false;

            _waiting.Remove(echo);
            echo.Complete(messageId);
            return true;
        }
    }

    internal void Forget(UploadEcho echo)
    {
        lock (_gate) _waiting.Remove(echo);
    }
}

/// <summary>
/// A single pending echo. Dispose stops waiting for it, so a message that shows up afterwards
/// is treated as an ordinary incoming file rather than being silently dropped.
/// </summary>
public sealed class UploadEcho : IDisposable
{
    private readonly UploadEchoRegistry _registry;
    private readonly string _fileName;
    private readonly long _fileSize;

    private readonly TaskCompletionSource<int> _received =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal UploadEcho(UploadEchoRegistry registry, string fileName, long fileSize)
    {
        _registry = registry;
        _fileName = fileName;
        _fileSize = fileSize;
    }

    /// <summary>
    /// Name and byte size together identify the echo: the account may have several uploads of
    /// the same size in flight (7z volumes are all one part long), and Telegram clients are
    /// known to rewrite names on their way in, so neither one alone is enough.
    /// </summary>
    internal bool Matches(string? fileName, long fileSize) =>
        fileSize == _fileSize && string.Equals(fileName, _fileName, StringComparison.Ordinal);

    internal void Complete(int messageId) => _received.TrySetResult(messageId);

    /// <summary>
    /// Waits for the bot to see its own copy of the upload. Returns null if it never arrives,
    /// which leaves the caller without an id the bot could read the file back with.
    /// </summary>
    public async Task<int?> WaitAsync(TimeSpan timeout)
    {
        using var timer = new CancellationTokenSource();
        var expired = Task.Delay(timeout, timer.Token);

        if (await Task.WhenAny(_received.Task, expired) != _received.Task)
            return null;

        timer.Cancel();
        return await _received.Task;
    }

    public void Dispose() => _registry.Forget(this);
}

/// <summary>
/// Thrown when the bot never saw a file the account uploaded, leaving no id in the bot's
/// numbering to store. Retrying would only put a second copy of the file in the chat.
/// </summary>
public class UploadEchoMissingException : Exception
{
    public UploadEchoMissingException(string message) : base(message) { }
}
