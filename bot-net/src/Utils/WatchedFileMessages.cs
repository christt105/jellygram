using System.Text.RegularExpressions;

namespace Bot.Utils;

/// <summary>
/// Pure text formatting/parsing for the watched-file Telegram flow: the notify message, the
/// outcome messages after a move attempt, and the "&lt;id&gt; [season &lt;n&gt; episode &lt;n&gt;]"
/// manual correction syntax read back from the user's reply (the "tmdb" prefix is accepted but
/// optional, since a bare id is what people naturally type when asked for "the TMDB id").
/// </summary>
public static partial class WatchedFileMessages
{
    public const string CorrectionSyntaxHint =
        "Reply with the TMDB id, e.g. `12345` or `12345 season <n> episode <n>` (`tmdb 12345` also works).";

    private const string NotifyPrefix = "📁 New file detected: ";

    [GeneratedRegex(@"^(?:tmdb\s+)?(\d+)(?:\s+season\s+(\d+))?(?:\s+episode\s+(\d+))?$", RegexOptions.IgnoreCase)]
    private static partial Regex CorrectionPattern();

    public static string FormatSeasonEpisode(int? season, int? episode)
    {
        if (season is null) return "";
        var text = $" S{season:D2}";
        if (episode is not null) text += $"E{episode:D2}";
        return text;
    }

    public static string BuildNotifyText(
        string filename, string? mediaType, string? title, int? season, int? episode, double confidence,
        int? tmdbId = null)
    {
        var guessLine = title is null
            ? "No automatic guess could be made for this file."
            : $"Guess: {title}{FormatSeasonEpisode(season, episode)} ({mediaType})\nConfidence: {confidence:P0}"
              + BuildTmdbLinkSuffix(tmdbId, mediaType);

        return $"""
                {NotifyPrefix}{filename}

                {guessLine}
                """;
    }

    /// <summary>Appends a clickable TMDB link so a guess can be sanity-checked before
    /// confirming - title alone doesn't distinguish e.g. a well-known film from an obscure
    /// same-named one, or a series whose Telegram-displayed title is localized.</summary>
    private static string BuildTmdbLinkSuffix(int? tmdbId, string? mediaType)
    {
        if (tmdbId is null || mediaType is not ("movie" or "tv")) return "";
        return $"\n<a href=\"https://www.themoviedb.org/{mediaType}/{tmdbId}\">TMDB</a>";
    }

    /// <summary>Recovers the filename from a notify message's text, so the Correct callback
    /// does not need it packed separately into the callback data.</summary>
    public static string ExtractFilenameFromNotifyText(string? notifyText)
    {
        var firstLine = notifyText?.Split('\n').FirstOrDefault() ?? "";
        return firstLine.StartsWith(NotifyPrefix, StringComparison.Ordinal)
            ? firstLine[NotifyPrefix.Length..]
            : "this file";
    }

    public static string BuildMovedText(string filename, string destinationPath) =>
        $"""
         ✅ Moved: {filename}

         {destinationPath}
         """;

    public static string BuildErrorText(string filename, string error) =>
        $"""
         ❌ Failed to move: {filename}

         {error}
         """;

    public static string BuildMissingText(string filename) =>
        $"""
         ⚠️ {filename} is no longer on disk, nothing to move.

         It was probably deleted or moved by hand.
         """;

    public static string BuildRemovedWhileNotifiedText(string filename) =>
        $"""
         ⚠️ {filename} was removed from disk before it was confirmed.
         """;

    public static string BuildConfirmPromptText(string filename, string destinationPath, bool destinationExists)
    {
        var warning = destinationExists
            ? "\n⚠️ A file already exists at that path — confirming will keep both as separate files.\n"
            : "";

        return $"""
                Move {filename} to:
                {destinationPath}
                {warning}
                Are you sure?
                """;
    }

    public static string BuildCorrectionPromptText(string filename) =>
        $"""
         ✏️ Correcting: {filename}

         {CorrectionSyntaxHint}
         """;

    public static string BuildCorrectionInvalidText() =>
        $"""
         ❌ Could not parse that reply.

         {CorrectionSyntaxHint}
         """;

    public readonly record struct ParsedCorrection(int TmdbId, int? Season, int? Episode);

    public static bool TryParseCorrection(string text, out ParsedCorrection result)
    {
        result = default;

        var match = CorrectionPattern().Match(text.Trim());
        if (!match.Success) return false;

        var tmdbId = int.Parse(match.Groups[1].Value);
        int? season = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : null;
        int? episode = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : null;

        result = new ParsedCorrection(tmdbId, season, episode);
        return true;
    }
}
