using Bot.Services;
using Xunit;

namespace Bot.Tests;

public class WatchedFileMessageRegistryTests
{
    [Fact]
    public void TryUntrack_ReturnsTheTrackedReference()
    {
        var registry = new WatchedFileMessageRegistry();
        registry.Track(1, chatId: 42, messageId: 100);

        var ok = registry.TryUntrack(1, out var reference);

        Assert.True(ok);
        Assert.Equal(42, reference.ChatId);
        Assert.Equal(100, reference.MessageId);
    }

    [Fact]
    public void TryUntrack_ReturnsFalseWhenNotTracked()
    {
        var registry = new WatchedFileMessageRegistry();

        Assert.False(registry.TryUntrack(1, out _));
    }

    [Fact]
    public void TryUntrack_RemovesTheEntrySoASecondCallFails()
    {
        var registry = new WatchedFileMessageRegistry();
        registry.Track(1, chatId: 42, messageId: 100);

        Assert.True(registry.TryUntrack(1, out _));
        Assert.False(registry.TryUntrack(1, out _));
    }

    [Fact]
    public void Snapshot_ReflectsCurrentlyTrackedEntriesOnly()
    {
        var registry = new WatchedFileMessageRegistry();
        registry.Track(1, chatId: 42, messageId: 100);
        registry.Track(2, chatId: 43, messageId: 101);
        registry.TryUntrack(1, out _);

        var snapshot = registry.Snapshot();

        Assert.False(snapshot.ContainsKey(1));
        Assert.True(snapshot.ContainsKey(2));
    }
}
