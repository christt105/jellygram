using Bot.Commands;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Message = WTelegram.Types.Message;

namespace Bot.Handlers;

public class CommandHandler
{
    private readonly BotDispatcher _bot;

    private readonly Dictionary<string, ICommand> _commands;

    public CommandHandler(BotDispatcher bot)
    {
        _bot = bot;

        var commands = new List<ICommand>
        {
            new StartCommand(bot.Bot),
            new VersionCommand(bot.Bot),
            new HealthCommand(bot.Bot, bot.ApiClient),
            new MoviesCommand(bot.Bot, bot.ApiClient),
            new SeriesCommand(bot.Bot, bot.ApiClient),
            new SerieCommand(bot.Bot, bot.ApiClient),
            new MovieCommand(bot.Bot, bot.ApiClient),
            new SearchCommand(bot.Bot, bot.ApiClient),
            new QueueCommand(bot.Bot, bot.ApiClient),
            new OrphansCommand(bot.Bot, bot.ApiClient),
            new BackupCommand(bot.Bot, bot.ApiClient),
            new AddCommand(bot),
            new ImportCommand(bot)
        };

        if (bot.UserClient != null)
            commands.Add(new AuthCommand(bot.Bot, bot.UserClient));

        commands.Add(new HelpCommand(bot.Bot, commands.ToArray()));

        _commands = commands.ToDictionary(c => c.Key, c => c);

        if (_commands.Any(c => !c.Key.StartsWith('/')))
        {
            var invalidCommands = string.Join(", ",
                _commands.Select(c => !c.Key.StartsWith('/'))
            );
            Log.Error(
                $"Commands {invalidCommands} will be ignored as it needs to be prefixed with `/`");
        }
    }

    // Commands shown in the Telegram "/" menu (command without the leading slash, as Telegram expects).
    public IEnumerable<BotCommand> GetMenuCommands()
    {
        return _commands.Values
            .Where(c => c.Key.StartsWith('/'))
            .Select(c => new BotCommand { Command = c.Key.TrimStart('/'), Description = c.Description });
    }

    public async Task Handle(Message msg, UpdateType type)
    {
        var parts = msg.Text.Split(" ");
        var command = parts[0];
        var args = parts.Skip(1).ToArray();

        if (!_commands.TryGetValue(command, out var commandHandler))
        {
            await _bot.Bot.SendMessage(msg.Chat.Id,
                $"Command {command} is not recognized. Use /help to get the available commands.");
            return;
        }

        await commandHandler.Execute(args, msg);
    }
}