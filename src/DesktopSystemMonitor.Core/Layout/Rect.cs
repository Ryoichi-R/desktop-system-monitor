namespace DesktopSystemMonitor.Core.Layout;

/// <summary>
/// Device-independent rectangle in DIP space. Kept as a plain struct so Core
/// stays free of WPF/Win32 dependencies.
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
    double Dpi);
