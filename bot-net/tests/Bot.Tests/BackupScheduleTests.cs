using Bot.Utils;
using Xunit;

namespace Bot.Tests;

public class BackupScheduleTests
{
    private static readonly DateTime Now = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(168);

    [Fact]
    public void DelayUntilNextRun_RunsImmediatelyWhenThereIsNoLastRun()
    {
        var delay = BackupSchedule.DelayUntilNextRun(null, Now, Interval);

        Assert.Equal(TimeSpan.Zero, delay);
    }

    [Fact]
    public void DelayUntilNextRun_WaitsOutTheRemainderOfTheInterval()
    {
        var lastRun = Now - TimeSpan.FromHours(100);

        var delay = BackupSchedule.DelayUntilNextRun(lastRun, Now, Interval);

        Assert.Equal(TimeSpan.FromHours(68), delay);
    }

    [Fact]
    public void DelayUntilNextRun_RunsImmediatelyWhenTheIntervalHasAlreadyElapsed()
    {
        var lastRun = Now - TimeSpan.FromHours(200);

        var delay = BackupSchedule.DelayUntilNextRun(lastRun, Now, Interval);

        Assert.Equal(TimeSpan.Zero, delay);
    }

    [Fact]
    public void DelayUntilNextRun_CapsAFutureLastRunAtAWholeInterval()
    {
        var lastRun = Now + TimeSpan.FromHours(500);

        var delay = BackupSchedule.DelayUntilNextRun(lastRun, Now, Interval);

        Assert.Equal(Interval, delay);
    }

    [Fact]
    public void DelayUntilNextRun_RunsImmediatelyExactlyAtTheIntervalBoundary()
    {
        var lastRun = Now - Interval;

        var delay = BackupSchedule.DelayUntilNextRun(lastRun, Now, Interval);

        Assert.Equal(TimeSpan.Zero, delay);
    }
}
