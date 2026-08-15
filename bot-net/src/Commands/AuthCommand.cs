using Bot.Services;
using Telegram.Bot.Types.Enums;
using Message = WTelegram.Types.Message;

namespace Bot.Commands;

public class AuthCommand : ICommand
{
    private readonly WTelegram.Bot _bot;
    private readonly UserClientService _userClient;

    public AuthCommand(WTelegram.Bot bot, UserClientService userClient)
    {
        _bot = bot;
        _userClient = userClient;
    }

    public string Key => "/auth";
    public string Description => "Show the status of the Telegram user account used for large transfers.";
    public string Usage => "/auth";

    public async Task Execute(string[] args, Message msg)
    {
        var limitGb = _userClient.SplitLimitBytes / 1_000_000_000.0;

        if (_userClient.IsAuthenticated)
        {
            var premiumNote = _userClient.IsPremium
                ? "Premium account detected."
                : "Account is not Premium.";

            await _bot.SendMessage(msg.Chat.Id,
                $"User account authenticated. {premiumNote}\nUpload limit: {limitGb:F1} GB.");
            return;
        }

        await _bot.SendMessage(msg.Chat.Id,
            $"No user account session. Transfers use the bot API, limited to {limitGb:F1} GB per part.\n\n" +
            "Logging in has to happen from the server terminal, because Telegram invalidates any " +
            "login code sent through a chat. On the host, in the directory holding the stack:\n\n" +
            $"<code>{UserClientService.ReauthInstructions}</code>\n\n" +
            "Then restart the worker with <code>docker compose restart bot-net</code>.",
            ParseMode.Html);
    }
}
