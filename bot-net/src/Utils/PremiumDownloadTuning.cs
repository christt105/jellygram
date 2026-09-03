namespace Bot.Utils;

/// <summary>
/// How many independent connections the user account opens to download one file.
/// </summary>
/// <remarks>
/// This is a second, orthogonal axis to <see cref="TransferTuning.ParallelTransfers"/>: that one
/// pipelines several parts inside one connection, this one spreads ranges of the same file over
/// several connections. The account's ceiling is per connection (measured around 27 MB/s, with
/// 41.5 MB/s as the best single-connection figure seen), so only the second axis lifts it: three
/// concurrent connections reached 51.8 MB/s aggregated with no throttling. The bot has no such
/// ceiling to lift, its limit is a rate limit (FLOOD_WAIT), so this never applies to the bot.
/// </remarks>
public static class PremiumDownloadTuning
{
    public const string ConnectionsVariable = "PREMIUM_DOWNLOAD_CONNECTIONS";

    public const int DefaultConnections = 3;

    /// <summary>
    /// Telegram tolerates a handful of transfer connections per account, not an arbitrary number,
    /// and the lab measurements flatten well before this, so absurd values are clamped instead of
    /// being taken literally.
    /// </summary>
    public const int MaxConnections = 8;

    public static int Connections => ParseConnections(Environment.GetEnvironmentVariable(ConnectionsVariable));

    public static int ParseConnections(string? value) =>
        int.TryParse(value, out var n) && n > 0
            ? Math.Min(n, MaxConnections)
            : DefaultConnections;

    /// <summary>
    /// Multi-connection downloads only make sense for the account, and only when it is logged in
    /// and asked for more than one connection. Anything else keeps the existing single-connection
    /// path untouched.
    /// </summary>
    public static bool UseMultipleConnections(bool sessionReady, int connections) =>
        sessionReady && connections > 1;
}
