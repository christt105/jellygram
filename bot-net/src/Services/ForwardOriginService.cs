using Bot.Utils;

namespace Bot.Services;

/// <summary>
/// Re-reads historical messages in the owner's chat with the bot to recover document_id and
/// forward-origin data for files uploaded before that started being captured. Used by the
/// backend's one-off backfill script, not by any live upload path.
/// </summary>
public class ForwardOriginService
{
    private readonly BotHolder _holder;

    public ForwardOriginService(BotHolder holder)
    {
        _holder = holder;
    }

    public async Task<Dictionary<string, ForwardMetadata.Info>> LookupAsync(IEnumerable<int> messageIds)
    {
        if (!_holder.IsReady)
            throw new InvalidOperationException("Bot not yet initialised.");

        var messages = await _holder.Bot.GetMessagesById(_holder.ChatId, messageIds);

        return messages.ToDictionary(
            m => m.MessageId.ToString(),
            m => ForwardMetadata.Extract(m.ForwardOrigin, m.TLMessage));
    }
}
