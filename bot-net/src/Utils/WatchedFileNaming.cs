namespace Bot.Utils;

/// <summary>
/// Builds the same mnamer-style destination path <see cref="Services.DownloadService"/> uses,
/// for a file resolved through the /watch confirm/correct flow. The confirm/correct endpoints
/// return no release year or TVDB id (the backend never fetches or stores them for watched
/// files), so both are passed as null here; <see cref="MnamerNaming"/> already treats them as
/// optional, it just omits the year/falls back to the TMDB id tag.
/// </summary>
public static class WatchedFileNaming
{
    public static string BuildDestinationPath(
        string moviesDir, string showsDir, string mediaType, string title, int tmdbId,
        int? season, int? episode, string extension)
    {
        if (mediaType == "movie")
        {
            var dirName = MnamerNaming.MovieDirectory(title, null, tmdbId);
            var fileName = MnamerNaming.MovieFile(title, null, "", extension);
            return Path.Combine(moviesDir, dirName, fileName);
        }

        var seasonNumber = season ?? 1;
        var episodeNumber = episode ?? 0;

        var showDirName = MnamerNaming.ShowDirectory(title, null, tmdbId);
        var seasonDir = MnamerNaming.SeasonDirectory(seasonNumber);
        var episodeFileName = MnamerNaming.EpisodeFile(title, seasonNumber, episodeNumber, "", extension);

        return Path.Combine(showsDir, showDirName, seasonDir, episodeFileName);
    }
}
