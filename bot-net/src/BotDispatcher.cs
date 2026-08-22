using Bot.Handlers;
using Bot.Services;
using Bot.Utils;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;
using TelegramBot.Handlers;
using Message = WTelegram.Types.Message;
using Update = WTelegram.Types.Update;

namespace Bot;

public class BotDispatcher
{
    private readonly CallbackQueryHandler _callbackQueryHandler;
    private readonly CommandHandler _commandHandler;

    private readonly FileHandler _fileHandler;
    private readonly MessageHandler _messageHandler;
    private readonly PendingActionHandler _pendingActionHandler;

    public BotDispatcher(WTelegram.Bot bot, ApiClient apiClient, TaskQueue queue, UserClientService? userClient = null)
    {
        Bot = bot;
        ApiClient = apiClient;
        Queue = queue;
        UserClient = userClient;

        _pendingActionHandler = new PendingActionHandler(Bot);
        _commandHandler = new CommandHandler(this);
        _fileHandler = new FileHandler(this);
        _messageHandler = new MessageHandler(Bot);
        _callbackQueryHandler = new CallbackQueryHandler(this);
    }

    public WTelegram.Bot Bot { get; }

    public ApiClient ApiClient { get; }

    public TaskQueue Queue { get; }

    public UserClientService? UserClient { get; }

    /// <summary>Where <see cref="UploadService"/> collects the bot's own id for its uploads.</summary>
    public UploadEchoRegistry UploadEchoes { get; } = new();

    /// <summary>Shared with <see cref="WatchNotificationService"/> so the Confirm/Correct
    /// callbacks and the removed-file reconciliation sweep track the same live messages.</summary>
    public WatchedFileMessageRegistry WatchedFileMessages { get; } = new();

    public PendingActionHandler PendingActionHandler => _pendingActionHandler;

    public async Task InitBot()
    {
        var me = await Bot.GetMe();
        Log.Info($"Bot connected as @{me.Username}");

        await Bot.DropPendingUpdates();

        // Register the command list so it shows up in Telegram's "/" autocomplete menu.
        await Bot.SetMyCommands(_commandHandler.GetMenuCommands());

        await Bot.SendMessage(AuthConfig.OwnerUserId, "Bot started");

        Bot.OnMessage += HandleMessage;
        Bot.OnUpdate += HandleUpdate;
        Bot.OnError += HandleError;

        Log.Info("Bot initialized. Waiting for updates...");
    }

    public async Task HandleMessage(Message msg, UpdateType type)
    {
        if (msg.From == null || !AuthConfig.IsAllowed(msg.From.Id))
        {
            Log.Info($"User {msg.From?.Username} with ID({msg.From?.Id}) is not allowed.");
            return;
        }

        if (msg.Document != null || msg.Video != null)
        {
            // A file the account uploaded arrives here as an ordinary incoming document. The
            // uploader is waiting for it to learn the bot's own id for the file, and registers
            // it itself with both ids; handling it again would enqueue a second identification.
            var incomingName = msg.Document?.FileName ?? msg.Video?.FileName;
            var incomingSize = msg.Document?.FileSize ?? msg.Video?.FileSize ?? 0;

            if (UploadEchoes.TryClaim(incomingName, incomingSize, msg.MessageId))
                Log.Info($"Message {msg.MessageId} is the bot's copy of {incomingName}, uploaded by the account.");
            else
                await _fileHandler.Handle(msg, type);
        }

        if (!string.IsNullOrEmpty(msg.Text))
        {
            if (msg.Text.StartsWith('/'))
                await _commandHandler.Handle(msg, type);
            else
                if (_pendingActionHandler.HasPendingAction())
                    await _pendingActionHandler.Handle(msg, type);
                else
                    await _messageHandler.Handle(msg, type);
        }
    }

    private async Task HandleUpdate(Update update)
    {
        switch (update.Type)
        {
            case UpdateType.CallbackQuery:
                if (update.CallbackQuery == null)
                {
                    Log.Error("Update type is CallbackQuery but no callback query was provided.");
                    return;
                }

                var callback = update.CallbackQuery;

                if (callback.From == null || !AuthConfig.IsAllowed(callback.From.Id))
                {
                    Log.Info($"User {callback.From?.Username} with ID({callback.From?.Id}) is not allowed.");
                    return;
                }

                await _callbackQueryHandler.HandleCallbackQueryAsync(callback);
                break;
            case UpdateType.Unknown:
                Console.WriteLine("Unknown update type: {0}", update.TLUpdate?.GetType().Name);
                break;
            default:
                Console.WriteLine($"No case to {update.Type}. {update.TLUpdate?.GetType().Name}");
                break;
        }
    }

    private Task HandleError(Exception e, HandleErrorSource src)
    {
        Log.Error($"Error ({src}) at {e.Source}", e);
        return Task.CompletedTask;
    }
}