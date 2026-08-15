using Bot.Services;
using Bot.Utils;
using Microsoft.Data.Sqlite;

namespace Bot;

public class Worker : BackgroundService
{
    private readonly TaskQueue _queue = new();
    private readonly BotHolder _botHolder;

    public Worker(BotHolder botHolder)
    {
        _botHolder = botHolder;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            if (!Bot.Utils.DependencyChecker.CheckExecutables(out var missing))
            {
                Log.Warning($"[Startup] Required system dependencies are missing: {string.Join(", ", missing)}. Make sure they are installed and available in the PATH.");
            }

            var apiId = int.Parse(Environment.GetEnvironmentVariable("TELEGRAM_API_ID")!);
            var apiHash = Environment.GetEnvironmentVariable("TELEGRAM_API_HASH")!;
            var botToken = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN")!;
            var authUserId = AuthConfig.OwnerUserId;

            var backendUrl = Environment.GetEnvironmentVariable("BACKEND_URL") ?? "http://backend:8000";

            await using var connection = new SqliteConnection(@"Data Source=/data/bot.sqlite");
            using var apiClient = new ApiClient(backendUrl);
            using var userClient = new UserClientService();

            var bot = new WTelegram.Bot(botToken, apiId, apiHash, connection);
            TransferTuning.Apply(bot.Client, "Bot", defaultParallelTransfers: 4);

            // Register so PreviewService and HTTP endpoints can use the live bot instance
            _botHolder.Register(bot, apiClient, authUserId);

            var botDispatcher = new BotDispatcher(bot, apiClient, _queue, userClient);

            await botDispatcher.InitBot();

            // Try to resume a saved user session (non-fatal if unavailable)
            if (!await userClient.TryResumeSessionAsync(apiId, apiHash))
                Log.Info($"[Startup] No saved user session. Authenticate with `{UserClientService.ReauthInstructions}`.");

            var downloadService = new DownloadService(bot, apiClient, _queue, userClient);
            _ = downloadService.PollAndProcessAsync(stoppingToken);

            var uploadService = new UploadService(bot, apiClient, _queue, userClient);
            _ = uploadService.PollAndProcessAsync(stoppingToken);

            _ = _queue.StartProcessing(stoppingToken);

            while (!stoppingToken.IsCancellationRequested) await Task.Delay(1000, stoppingToken);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to run the bot", ex);
        }
    }
}