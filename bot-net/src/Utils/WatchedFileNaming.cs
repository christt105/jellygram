namespace Bot.Utils;

/// <summary>
/// Builds the same mnamer-style destination path <see cref="Services.DownloadService"/> uses,
/// for a file resolved through the /watch confirm/correct flow. The backend never fetches or
/// stores a TVDB id for watched files, so shows always fall back to the TMDB id tag;
/// <see cref="MnamerNaming"/> already treats both as optional.
/// </summary>
public static class WatchedFileNaming
{
    public static string BuildDestinationPath(
        string moviesDir, string showsDir, string mediaType, string title, int tmdbId, int? year,
        int? season, int? episode, string extension)
    {
        if (mediaType == "movie")
        {
            var dirName = MnamerNaming.MovieDirectory(title, year, tmdbId);
            var fileName = MnamerNaming.MovieFile(title, year, "", extension);
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
