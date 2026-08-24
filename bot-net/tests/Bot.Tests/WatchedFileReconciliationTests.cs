using Bot.Utils;
using Xunit;

namespace Bot.Tests;

public class WatchedFileReconciliationTests
{
    [Fact]
    public void ToRelativePath_StripsTheRootAndUsesForwardSlashes()
    {
        var relative = WatchedFileReconciliation.ToRelativePath(
            "/data/media/downloads", "/data/media/downloads/Release Folder/Movie.mkv");

        Assert.Equal("Release Folder/Movie.mkv", relative);
    }

    [Fact]
    public void ToFullPath_IsTheInverseOfToRelativePath()
    {
        const string root = "/data/media/downloads";
        const string full = "/data/media/downloads/Release Folder/Movie.mkv";

        var relative = WatchedFileReconciliation.ToRelativePath(root, full);
        var roundTripped = WatchedFileReconciliation.ToFullPath(root, relative);

        Assert.Equal(full, roundTripped);
    }

    [Theory]
    [InlineData(false, false, RenameAction.Ignore)]
    [InlineData(true, true, RenameAction.Rename)]
    [InlineData(false, true, RenameAction.TrackNew)]
    [InlineData(true, false, RenameAction.MarkMissing)]
    public void DecideRenameAction_CoversAllFourCombinations(bool oldWasVideo, bool newIsVideo, RenameAction expected)
    {
        Assert.Equal(expected, WatchedFileReconciliation.DecideRenameAction(oldWasVideo, newIsVideo));
    }

    [Fact]
    public void Compute_FindsFilesOnDiskWithoutABackendRow()
    {
        var diff = WatchedFileReconciliation.Compute(
            onDiskRelativePaths: ["Movie.mkv", "New Release/Episode.mkv"],
            backendActiveRelativePaths: ["Movie.mkv"]);

        Assert.Equal(["New Release/Episode.mkv"], diff.New);
        Assert.Empty(diff.Missing);
    }

    [Fact]
    public void Compute_FindsBackendRowsWithoutAMatchingFile()
    {
        var diff = WatchedFileReconciliation.Compute(
            onDiskRelativePaths: ["Movie.mkv"],
            backendActiveRelativePaths: ["Movie.mkv", "Deleted.mkv"]);

        Assert.Empty(diff.New);
        Assert.Equal(["Deleted.mkv"], diff.Missing);
    }

    [Fact]
    public void Compute_TreatsARenameWhileOfflineAsAMissingRowAndANewFile()
    {
        var diff = WatchedFileReconciliation.Compute(
            onDiskRelativePaths: ["Movie (Renamed).mkv"],
            backendActiveRelativePaths: ["Movie.mkv"]);

        Assert.Equal(["Movie (Renamed).mkv"], diff.New);
        Assert.Equal(["Movie.mkv"], diff.Missing);
    }

    [Fact]
    public void Compute_IsANoOpWhenEverythingMatches()
    {
        var diff = WatchedFileReconciliation.Compute(
            onDiskRelativePaths: ["Movie.mkv", "Show/Episode.mkv"],
            backendActiveRelativePaths: ["Movie.mkv", "Show/Episode.mkv"]);

        Assert.Empty(diff.New);
        Assert.Empty(diff.Missing);
    }
}
