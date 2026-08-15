using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Message = WTelegram.Types.Message;

namespace Bot.Handlers;

public class PendingActionHandler
{
    private readonly WTelegram.Bot _bot;

    public PendingActionHandler(WTelegram.Bot bot)
    {
        _bot = bot;
    }

    public PendingAction? CurrentAction { get; private set; }

    public async Task SetPendingAction(PendingAction action)
    {
        if (CurrentAction != null)
        {
            if (action.Owner != null && CurrentAction.Owner != null && action.Owner.Equals(CurrentAction.Owner))
            {
                Log.Info("Trying to set the same pending action again, ignoring");
                return;
            }

            Log.Info("Overwriting existing pending action");
            await _bot.SendMessage(CurrentAction.ChatId, "Previous action cancelled");
            if (CurrentAction.CancelCallback != null)
                await CurrentAction.CancelCallback.Invoke();

            Clear();
        }

        CurrentAction = action;
    }

    public async Task CancelPendingAction()
    {
        if (CurrentAction != null && CurrentAction.CancelCallback != null)
            await CurrentAction.CancelCallback.Invoke();
        CurrentAction = null;
    }

    internal async Task Handle(Message? msg, UpdateType type)
    {
        if (msg == null)
            return;
        if (CurrentAction == null)
            return;

        if (string.IsNullOrEmpty(msg.Text))
            return;

        var action = CurrentAction;
        Clear();
        await action.Callback?.Invoke(msg.Text)!;
    }

    internal bool HasPendingAction()
    {
        return CurrentAction != null;
    }

    public void Clear()
    {
        CurrentAction = null;
    }

    public class PendingAction
    {
        public PendingAction(string id, ChatId chatId, object? owner = null, Func<string, Task>? callback = null,
            Func<Task>? cancelCallback = null)
        {
            Id = id;
            ChatId = chatId;
            Owner = owner;
            Callback = callback;
            CancelCallback = cancelCallback;
        }

        public string Id { get; set; }
        public ChatId ChatId { get; set; }
        public object? Owner { get; set; }
        public Func<string, Task>? Callback { get; set; }
        public Func<Task>? CancelCallback { get; set; }
    }
}