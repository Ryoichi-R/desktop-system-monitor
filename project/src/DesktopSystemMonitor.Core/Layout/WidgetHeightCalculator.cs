namespace DesktopSystemMonitor.Core.Layout;

public static class WidgetHeightCalculator
{
    public const double RowHeight = 48d;
    public const double RowGap = 4d;
    public const double VerticalPadding = 16d;

    public static double Calculate(int visibleRowCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(visibleRowCount, 2);
        return VerticalPadding + (RowHeight * visibleRowCount) + (RowGap * (visibleRowCount - 1));
    }

    [Obsolete("Use Calculate(int visibleRowCount).", error: false)]
    public static double Calculate(bool showDisk, bool showBattery, bool batteryPresent)
    {
        int rows = 4 + (showDisk ? 1 : 0) + (showBattery && batteryPresent ? 1 : 0);
        return Calculate(rows);
    }
}
