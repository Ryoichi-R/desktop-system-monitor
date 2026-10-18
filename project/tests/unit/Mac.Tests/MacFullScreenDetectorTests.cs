using DesktopSystemMonitor.Mac.Interop;
using Xunit;

namespace DesktopSystemMonitor.Mac.Tests;

public sealed class MacFullScreenDetectorTests
{
    private static ScreenRect Rect(double x = 0, double y = 0, double w = 1920, double h = 1080) => new() { X = x, Y = y, Width = w, Height = h };
    private static WindowCandidate Window(ScreenRect rect, int pid = 2, int layer = 0, double alpha = 1) => new(pid, layer, alpha, rect);

    [Fact]
    public void Fullscreen_Is_Per_Display_And_Maximized_Or_Occluded_Windows_Do_Not_Hide()
    {
        Assert.True(MacFullScreenDetector.IsCovered(Rect(), [Window(Rect())], 1));
        Assert.False(MacFullScreenDetector.IsCovered(Rect(), [Window(Rect(y: 24, h: 1056))], 1));
        Assert.False(MacFullScreenDetector.IsCovered(Rect(), [Window(Rect(w: 900, h: 700)), Window(Rect())], 1));
        Assert.True(MacFullScreenDetector.IsCovered(Rect(), [Window(Rect(x: 1920)), Window(Rect())], 1));
        Assert.False(MacFullScreenDetector.IsCovered(Rect(x: 1920), [Window(Rect())], 1));
        Assert.True(MacFullScreenDetector.IsCovered(Rect(), [Window(Rect(w: 200), pid: 1), Window(Rect(), layer: 3), Window(Rect(), alpha: 0), Window(Rect())], 1));
        Assert.False(MacFullScreenDetector.IsCovered(Rect(), [], 1));
        Assert.False(MacFullScreenDetector.IsCovered(Rect(w: double.NaN), [Window(Rect())], 1));
        Assert.False(MacFullScreenDetector.IsCovered(Rect(), [Window(Rect(), alpha: double.NaN)], 1));
    }

    [MacFact]
    public void Native_Window_Metadata_Has_Valid_Geometry_Without_Reading_Titles()
    {
        var windows = NativeScreenReader.ReadWindows();
        Assert.InRange(windows.Length, 1, 4096);
        Assert.All(windows, window =>
        {
            Assert.True(window.Owner > 0);
            Assert.True(double.IsFinite(window.Alpha));
            Assert.True(double.IsFinite(window.Bounds.Width));
        });
    }

    [Fact]
    public void Missing_Metadata_Fails_Open()
    {
        var detector = new MacFullScreenDetector(_ => throw new IOException("test"), 1);
        Assert.False(detector.IsForegroundFullScreen((IntPtr)1));
        Assert.False(detector.IsForegroundFullScreen(IntPtr.Zero));
        var working = new MacFullScreenDetector(_ => new(Rect(), [Window(Rect())]), 1);
        Assert.True(working.IsForegroundFullScreen((IntPtr)1));
    }
}
