using Bot.Services;
using Telegram.Bot.Types;

namespace Bot.CallbackQueries.Callbacks.Watch;

/// <summary>
/// Accepts the guess a notify message showed as-is. The guess (tmdb id/season/episode) travels
/// packed in the callback data itself, since the row already carries it and there is no
/// single-row GET on /watch to re-fetch it from — avoids a backend change for this alone.
/// </summary>
[Callback(Id)]
public class ConfirmWatchedFileCallback : ICallbackQuery
{
    public const string Id = "confirm-watched-file";

    private readonly WTelegram.Bot _bot;
    private readonly ApiClient _apiClient;
    private readonly WatchedFileMessageRegistry _registry;
    private readonly int _watchedFileId;
    private readonly int _tmdbId;
    private readonly int? _season;
    private readonly int? _episode;

    private ConfirmWatchedFileCallback(
        int watchedFileId, int tmdbId, int? season, int? episode,
        WTelegram.Bot bot, ApiClient apiClient, WatchedFileMessageRegistry registry)
    {
        _watchedFileId = watchedFileId;
        _tmdbId = tmdbId;
        _season = season;
        _episode = episode;
        _bot = bot;
        _apiClient = apiClient;
        _registry = registry;
    }

    public async Task ExecuteAsync(Message? message)
    {
        var resolution = await _apiClient.ConfirmWatchedFileAsync(_watchedFileId, _tmdbId, _season, _episode);
        await WatchedFileMoveFlow.ExecuteAsync(_bot, _apiClient, _registry, message!, _watchedFileId, resolution);
    }

    public static string Pack(int watchedFileId, int tmdbId, int? season, int? episode) =>
        CallbackDataPacker.Pack(Id,
            [watchedFileId.ToString(), tmdbId.ToString(), season?.ToString() ?? "", episode?.ToString() ?? ""]);

    public static ICallbackQuery Create(string[] fields, BotDispatcher dispatcher)
    {
        var season = string.IsNullOrEmpty(fields[2]) ? (int?)null : int.Parse(fields[2]);
        var episode = string.IsNullOrEmpty(fields[3]) ? (int?)null : int.Parse(fields[3]);

        return new ConfirmWatchedFileCallback(
            int.Parse(fields[0]), int.Parse(fields[1]), season, episode,
            dispatcher.Bot, dispatcher.ApiClient, dispatcher.WatchedFileMessages);
    }
}
