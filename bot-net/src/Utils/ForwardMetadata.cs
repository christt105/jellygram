using Telegram.Bot.Types;
using TL;

namespace Bot.Utils;

public static class ForwardMetadata
{
    /// <summary>
    /// document_id is Telegram's own identifier for the underlying blob, identical between a
    /// message and any forward of it. from_type is one of Telegram's own forward-origin
    /// categories ("user", "hidden_user", "chat", "channel"), or null when the message wasn't
    /// forwarded at all. hidden is true only when the original sender's identity was hidden by
    /// their privacy settings, in which case from_id is always null.
    /// </summary>
    public record Info(long? DocumentId, string? FromType, string? FromId, string? FromName, bool Hidden);

    public static Info Extract(MessageOrigin? forwardOrigin, MessageBase? tlMessage)
    {
        var documentId = tlMessage is TL.Message { media: MessageMediaDocument { document: TL.Document doc } }
            ? doc.ID
            : (long?)null;

        return forwardOrigin switch
        {
            MessageOriginUser u => new Info(
                documentId, "user", u.SenderUser.Id.ToString(), FullName(u.SenderUser.FirstName, u.SenderUser.LastName), false),
            MessageOriginHiddenUser h => new Info(
                documentId, "hidden_user", null, h.SenderUserName, true),
            MessageOriginChat c => new Info(
                documentId, "chat", c.SenderChat.Id.ToString(), c.SenderChat.Title, false),
            MessageOriginChannel ch => new Info(
                documentId, "channel", ch.Chat.Id.ToString(), ch.Chat.Title, false),
            _ => new Info(documentId, null, null, null, false)
        };
    }

    private static string? FullName(string? first, string? last) =>
        string.IsNullOrEmpty(last) ? first : $"{first} {last}";
}
