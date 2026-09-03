using Bot.Utils;

namespace Bot.Services;

/// <summary>
/// Startup check: asks Jellyfin where its libraries live and confirms bot-net can reach every
/// one of those paths through the configured mappings, the same way an upload will.
/// </summary>
public static class JellyfinPathAudit
{
    public static async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var url = Environment.GetEnvironmentVariable("JELLYFIN_URL");
        var token = Environment.GetEnvironmentVariable("JELLYFIN_TOKEN");

        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(token))
        {
            Log.Warning("[Startup] JELLYFIN_URL or JELLYFIN_TOKEN is unset, skipping the library path check. " +
                        "Uploads will fail later if JELLYFIN_PATH_MAP does not match what Jellyfin reports.");
            return;
        }

        try
        {
            using var jellyfin = new JellyfinClient(url, token);
            var locations = await jellyfin.GetLibraryLocationsAsync(cancellationToken);

            if (locations.Count == 0)
            {
                Log.Warning("[Startup] Jellyfin reported no movie or show library locations, " +
                            "so the library path check has nothing to verify.");
                return;
            }

            var results = JellyfinPathCheck.Check(locations, PathTranslator.ConfiguredMappings());
            foreach (var warning in JellyfinPathCheck.Warnings(results))
                Log.Warning($"[Startup] {warning}");

            var resolved = results.Count(result => result.Status == LibraryPathStatus.Resolved);
            var summary = $"[Startup] Jellyfin library path check: {resolved}/{results.Count} locations resolve inside bot-net.";

            if (resolved == results.Count) Log.Info(summary);
            else Log.Warning(summary);
        }
        catch (Exception ex)
        {
            Log.Warning($"[Startup] Could not check the Jellyfin library paths: {ex.Message}. " +
                        "A wrong JELLYFIN_PATH_MAP will not surface until an upload fails.");
        }
    }
}
