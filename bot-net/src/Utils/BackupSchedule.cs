namespace Bot.Utils;

/// <summary>
/// Spacing of the periodic backup runs, kept apart from the service so it can be reasoned
/// about without a clock or a Telegram session.
/// </summary>
public static class BackupSchedule
{
    /// <summary>
    /// How long to wait before the next run. A container that has never run one backs up right
    /// away; one restarting mid-interval waits out the remainder instead of backing up on every
    /// boot. A timestamp in the future (the clock moved, or the state file was written by
    /// another machine) is capped at a whole interval.
    /// </summary>
    public static TimeSpan DelayUntilNextRun(DateTime? lastRunUtc, DateTime nowUtc, TimeSpan interval)
    {
        if (lastRunUtc is null) return TimeSpan.Zero;

        var remaining = lastRunUtc.Value + interval - nowUtc;
        if (remaining <= TimeSpan.Zero) return TimeSpan.Zero;

        return remaining > interval ? interval : remaining;
    }
}
