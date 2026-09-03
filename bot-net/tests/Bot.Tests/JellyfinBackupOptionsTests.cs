using Bot.Utils;
using Xunit;

namespace Bot.Tests;

public class JellyfinBackupOptionsTests
{
    private const long OwnerChatId = 111;

    [Fact]
    public void FromEnvironment_ReturnsNullWhenNoBackupDirIsConfigured()
    {
        var options = JellyfinBackupOptions.FromEnvironment(_ => null, OwnerChatId);

        Assert.Null(options);
    }

    [Fact]
    public void FromEnvironment_FillsInDefaultsWhenOnlyTheDirIsSet()
    {
        var options = JellyfinBackupOptions.FromEnvironment(
            key => key == "JELLYFIN_BACKUP_DIR" ? "/data/jellyfin-appdata" : null, OwnerChatId);

        Assert.NotNull(options);
        Assert.Equal("/data/jellyfin-appdata", options.AppDataDir);
        Assert.Equal(["config", "data", "plugins"], options.Subdirectories);
        Assert.Equal(TimeSpan.FromHours(JellyfinBackupOptions.DefaultIntervalHours), options.Interval);
        Assert.Equal(OwnerChatId, options.ChatId);
        Assert.Equal(JellyfinBackupOptions.DefaultRetain, options.Retain);
    }

    [Fact]
    public void FromEnvironment_ParsesCustomSubdirsIntervalChatAndRetain()
    {
        var env = new Dictionary<string, string>
        {
            ["JELLYFIN_BACKUP_DIR"] = "/data/jellyfin-appdata",
            ["JELLYFIN_BACKUP_SUBDIRS"] = "config, data",
            ["JELLYFIN_BACKUP_INTERVAL_HOURS"] = "24",
            ["JELLYFIN_BACKUP_CHAT_ID"] = "222",
            ["JELLYFIN_BACKUP_RETAIN"] = "10",
        };

        var options = JellyfinBackupOptions.FromEnvironment(key => env.GetValueOrDefault(key), OwnerChatId);

        Assert.NotNull(options);
        Assert.Equal(["config", "data"], options.Subdirectories);
        Assert.Equal(TimeSpan.FromHours(24), options.Interval);
        Assert.Equal(222, options.ChatId);
        Assert.Equal(10, options.Retain);
    }

    [Fact]
    public void FromEnvironment_ClampsAnIntervalBelowTheMinimum()
    {
        var env = new Dictionary<string, string>
        {
            ["JELLYFIN_BACKUP_DIR"] = "/data/jellyfin-appdata",
            ["JELLYFIN_BACKUP_INTERVAL_HOURS"] = "0.1",
        };

        var options = JellyfinBackupOptions.FromEnvironment(key => env.GetValueOrDefault(key), OwnerChatId);

        Assert.Equal(JellyfinBackupOptions.MinimumInterval, options!.Interval);
    }

    [Fact]
    public void FromEnvironment_FallsBackToTheOwnerChatOnAnInvalidChatId()
    {
        var env = new Dictionary<string, string>
        {
            ["JELLYFIN_BACKUP_DIR"] = "/data/jellyfin-appdata",
            ["JELLYFIN_BACKUP_CHAT_ID"] = "not-a-number",
        };

        var options = JellyfinBackupOptions.FromEnvironment(key => env.GetValueOrDefault(key), OwnerChatId);

        Assert.Equal(OwnerChatId, options!.ChatId);
    }

    [Fact]
    public void Sources_ReturnsTheWholeAppDataDirWhenSubdirectoriesAreBlank()
    {
        var env = new Dictionary<string, string>
        {
            ["JELLYFIN_BACKUP_DIR"] = "/data/jellyfin-appdata",
            ["JELLYFIN_BACKUP_SUBDIRS"] = "",
        };

        var options = JellyfinBackupOptions.FromEnvironment(key => env.GetValueOrDefault(key), OwnerChatId);

        Assert.Equal(["/data/jellyfin-appdata"], options!.Sources());
    }

    [Fact]
    public void Sources_JoinsSubdirectoriesOntoTheAppDataDir()
    {
        var options = JellyfinBackupOptions.FromEnvironment(
            key => key == "JELLYFIN_BACKUP_DIR" ? "/data/jellyfin-appdata" : null, OwnerChatId);

        Assert.Equal(
            [
                Path.Combine("/data/jellyfin-appdata", "config"),
                Path.Combine("/data/jellyfin-appdata", "data"),
                Path.Combine("/data/jellyfin-appdata", "plugins"),
            ],
            options!.Sources());
    }
}
