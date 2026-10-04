namespace DesktopSystemMonitor.Core.Layout;

/// <summary>
/// Coordinate rectangle shared by platform layers. Keep all values passed to
/// one geometry operation in the same unit system; platform boundaries own
/// conversions between DIPs and physical pixels. Core stays free of WPF/Win32.
/// </summary>
public readonly record struct Rect(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;

    public bool Contains(double x, double y) =>
        x >= Left && y >= Top && x < Right && y < Bottom;
}

public sealed record MonitorInfo(
    string DeviceName,
    Rect WorkArea,
    double Dpi)
{
    /// <summary>
    /// The monitor work area in physical desktop pixels when the provider can
    /// supply it. <see cref="WorkArea"/> is a 96-DPI baseline projection until
    /// a window-backed DPI value is available for that monitor.
    /// </summary>
    public Rect? PhysicalWorkArea { get; init; }
}
