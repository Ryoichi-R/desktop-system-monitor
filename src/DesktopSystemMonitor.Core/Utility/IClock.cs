namespace DesktopSystemMonitor.Core.Utility;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
    long GetTimestampTicks();
    double TicksToSeconds(long deltaTicks);
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    public long GetTimestampTicks() => System.Diagnostics.Stopwatch.GetTimestamp();
    public double TicksToSeconds(long deltaTicks) =>
        (double)deltaTicks / System.Diagnostics.Stopwatch.Frequency;
}
