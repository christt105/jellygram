using Bot.Models;

namespace Bot.Utils;

/// <summary>Which identity can read a stored file back, and under which id.</summary>
public enum DownloadIdentity
{
    /// <summary>The bot itself, through <c>message_id</c>. Always available.</summary>
    Bot,

    /// <summary>The user account, through <c>user_message_id</c>, for roughly 27 MB/s.</summary>
    UserAccount,

    /// <summary>The user account's Saved Messages, where older builds parked a few uploads.</summary>
    SavedMessages
}

public readonly record struct DownloadRoute(DownloadIdentity Identity, int MessageId);

public static class DownloadRouting
{
    /// <summary>
    /// Picks how to fetch a file. The user account is only ever an optimisation: every row
    /// carries a <c>message_id</c> the bot can read, so a missing <c>user_message_id</c> or a
    /// session that is not logged in simply falls back to the bot instead of failing.
    /// </summary>
    public static DownloadRoute Choose(DownloadFileItem file, bool userSessionReady)
    {
        // Saved Messages is the exception: those rows carry the account's numbering in
        // message_id, so the bot cannot read them at all and must not try.
        if (file.StoragePeer == "saved")
            return new DownloadRoute(DownloadIdentity.SavedMessages, file.MessageId);

        if (userSessionReady && file.UserMessageId is int userMessageId)
            return new DownloadRoute(DownloadIdentity.UserAccount, userMessageId);

        return new DownloadRoute(DownloadIdentity.Bot, file.MessageId);
    }
}
