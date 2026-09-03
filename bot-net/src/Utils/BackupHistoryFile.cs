using System.Text.Json;

namespace Bot.Utils;

/// <summary>
/// The bot message ids of every backup still kept in the chat, one entry per generation, oldest
/// first. Persisted next to the last-run timestamp so a container restart does not lose track of
/// what <see cref="BackupRetention"/> can still prune.
/// </summary>
public sealed class BackupHistoryFile
{
    private readonly string _path;

    public BackupHistoryFile(string path)
    {
        _path = path;
    }

    /// <summary>Returns the stored generations, oldest first, or an empty list when there are none.</summary>
    public List<int[]> Read()
    {
        try
        {
            if (!File.Exists(_path)) return [];

            return JsonSerializer.Deserialize<List<int[]>>(File.ReadAllText(_path)) ?? [];
        }
        catch (Exception ex)
        {
            Log.Warning($"[JellyfinBackup] Ignoring unreadable history at {_path}: {ex.Message}");
            return [];
        }
    }

    public void Write(IReadOnlyList<int[]> generations)
    {
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(_path));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            File.WriteAllText(_path, JsonSerializer.Serialize(generations));
        }
        catch (Exception ex)
        {
            Log.Warning($"[JellyfinBackup] Could not write {_path}: {ex.Message}");
        }
    }
}
