namespace DesktopSystemMonitor.Core.Formatting;

public static class DurationFormatter
{
    public const string Unavailable = "N/A";

    public static string FormatApproximate(TimeSpan? duration)
    {
        if (duration is null || duration <= TimeSpan.Zero || duration > TimeSpan.FromDays(7))
        {
            return Unavailable;
        }

        double minutes = duration.Value.TotalMinutes;
        int roundedMinutes = minutes >= 10
            ? checked((int)(Math.Round(minutes / 5d, MidpointRounding.AwayFromZero) * 5d))
            : Math.Max(1, checked((int)Math.Round(minutes, MidpointRounding.AwayFromZero)));
        int hours = roundedMinutes / 60;
        int remainder = roundedMinutes % 60;
        return hours > 0 ? $"≈{hours}h {remainder}m" : $"≈{remainder}m";
    }
}
