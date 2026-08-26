using System.Xml.Linq;

namespace Bot.Utils;

/// <summary>
/// Writes Kodi/Jellyfin-compatible .nfo sidecars for seasons that are flagged
/// "local_metadata": content whose numbering isn't the one an online provider
/// (TheTVDB/TMDB) knows about, so Jellyfin must read the title/plot from disk
/// instead of trying to reconcile it against the real, unrelated online entry.
/// Every write is unconditional — small XML files, not worth guarding on
/// existence — so callers can invoke these on every episode placed.
/// </summary>
public static class NfoWriter
{
    public static void WriteTvShowNfo(string seriesRootDir, string title, string? overview)
    {
        var doc = new XElement("tvshow",
            new XElement("title", title),
            new XElement("plot", overview ?? ""));
        Save(doc, Path.Combine(seriesRootDir, "tvshow.nfo"));
    }

    public static void WriteSeasonNfo(string seasonDir, int seasonNumber)
    {
        var doc = new XElement("season",
            new XElement("seasonnumber", seasonNumber),
            new XElement("title", $"Temporada {seasonNumber}"));
        Save(doc, Path.Combine(seasonDir, "season.nfo"));
    }

    public static void WriteEpisodeNfo(string videoFilePath, int seasonNumber, int episodeNumber, string? episodeTitle)
    {
        var doc = new XElement("episodedetails",
            new XElement("title", episodeTitle ?? $"Episodio {episodeNumber}"),
            new XElement("season", seasonNumber),
            new XElement("episode", episodeNumber));
        var nfoPath = Path.ChangeExtension(videoFilePath, ".nfo");
        Save(doc, nfoPath);
    }

    private static void Save(XElement root, string path)
    {
        var doc = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root);
        doc.Save(path);
    }
}
