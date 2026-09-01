namespace Bot.Utils;

/// <summary>
/// Builds the same mnamer-style destination path <see cref="Services.DownloadService"/> uses,
/// for a file resolved through the /watch confirm/correct flow. The backend fetches the TVDB id
/// from TMDB's external_ids once the TMDB id is confirmed, so shows get the same
/// "[tvdbid-x]" tag the normal download flow writes; it's still optional because that lookup
/// can fail or the show may genuinely have no TVDB entry, in which case <see cref="MnamerNaming"/>
/// falls back to the TMDB id tag.
/// </summary>
public static class WatchedFileNaming
{
    public static string BuildDestinationPath(
        string moviesDir, string showsDir, string mediaType, string title, int tmdbId, int? tvdbId, int? year,
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

        var showDirName = MnamerNaming.ShowDirectory(title, tvdbId, tmdbId);
        var seasonDir = MnamerNaming.SeasonDirectory(seasonNumber);
        var episodeFileName = MnamerNaming.EpisodeFile(title, seasonNumber, episodeNumber, "", extension);

        return Path.Combine(showsDir, showDirName, seasonDir, episodeFileName);
    }
}
