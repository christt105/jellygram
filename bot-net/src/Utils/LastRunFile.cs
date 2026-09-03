using System.Globalization;

namespace Bot.Utils;

/// <summary>
/// A single UTC timestamp on disk, so the backup cadence survives a restart. It records the last
/// <i>attempt</i>, not the last success: a failed run that did not move the timestamp would come
/// due again immediately and spin.
/// </summary>
public sealed class LastRunFile
{
    private readonly string _path;

    public LastRunFile(string path)
    {
        _path = path;
    }

    /// <summary>Returns the stored timestamp, or null when there is none or it cannot be read.</summary>
    public DateTime? Read()
    {
        try
        {
            if (!File.Exists(_path)) return null;

            var raw = File.ReadAllText(_path).Trim();
            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
                return parsed.ToUniversalTime();

            Log.Warning($"[JellyfinBackup] Ignoring unreadable timestamp '{raw}' in {_path}.");
            return null;
        }
        catch (Exception ex)
        {
            Log.Warning($"[JellyfinBackup] Could not read {_path}: {ex.Message}");
            return null;
        }
    }

    public void Write(DateTime utc)
    {
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(_path));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            File.WriteAllText(_path, utc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        }
        catch (Exception ex)
        {
            Log.Warning($"[JellyfinBackup] Could not write {_path}: {ex.Message}");
        }
    }
}
