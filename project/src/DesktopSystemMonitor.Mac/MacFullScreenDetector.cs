using DesktopSystemMonitor.Core.Platform;
using DesktopSystemMonitor.Mac.Interop;

namespace DesktopSystemMonitor.Mac;

internal readonly record struct WindowCandidate(int Owner, int Layer, double Alpha, ScreenRect Bounds);
internal sealed record ScreenWindows(ScreenRect Display, WindowCandidate[] Windows);

public sealed class MacFullScreenDetector : IFullScreenDetector
{
    private readonly Func<IntPtr, ScreenWindows> _read;
    private readonly int _ownPid;
    public MacFullScreenDetector() : this(NativeScreenReader.Read, Environment.ProcessId) { }
    internal MacFullScreenDetector(Func<IntPtr, ScreenWindows> read, int ownPid) { _read = read; _ownPid = ownPid; }

    public bool IsForegroundFullScreen(IntPtr ownWindow)
    {
        if (ownWindow == IntPtr.Zero) return false;
        try
        {
            var state = _read(ownWindow);
            return IsCovered(state.Display, state.Windows, _ownPid);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
        {
            return false; // Missing metadata must restore the widget, never leave it hidden.
        }
    }

    internal static bool IsCovered(ScreenRect display, WindowCandidate[] windows, int ownPid)
    {
        if (!Usable(display)) return false;
        foreach (var window in windows)
        {
            var bounds = window.Bounds;
            if (window.Owner == ownPid || window.Layer != 0 || !double.IsFinite(window.Alpha) || window.Alpha <= 0 || !Usable(bounds)) continue;
            double left = Math.Max(display.X, bounds.X), top = Math.Max(display.Y, bounds.Y);
            double right = Math.Min(display.X + display.Width, bounds.X + bounds.Width);
            double bottom = Math.Min(display.Y + display.Height, bounds.Y + bounds.Height);
            if (right <= left || bottom <= top) continue;
            // Select the first visible normal-level window on this display. Requiring all
            // four edges avoids treating a normal maximized window below the menu bar as fullscreen.
            return left - display.X <= 2 && top - display.Y <= 2
                && display.X + display.Width - right <= 2 && display.Y + display.Height - bottom <= 2;
        }
        return false;
    }

    private static bool Usable(ScreenRect r) => double.IsFinite(r.X) && double.IsFinite(r.Y)
        && double.IsFinite(r.Width) && double.IsFinite(r.Height) && r.Width > 0 && r.Height > 0
        && double.IsFinite(r.X + r.Width) && double.IsFinite(r.Y + r.Height);
}

internal static class NativeScreenReader
{
    internal static ScreenWindows Read(IntPtr ownWindow)
    {
        nint screen = ScreenNative.Send(ownWindow, ScreenNative.Selector("screen"));
        if (screen == 0) throw new IOException("Window display unavailable.");
        nint description = ScreenNative.Send(screen, ScreenNative.Selector("deviceDescription"));
        if (description == 0) throw new IOException("Display description unavailable.");
        nint displayKey = Key("NSScreenNumber");
        double displayId;
        try { displayId = Number(ScreenNative.SendObject(description, ScreenNative.Selector("objectForKey:"), displayKey)); }
        finally { ScreenNative.Release(displayKey); }
        if (displayId <= 0 || displayId > uint.MaxValue || displayId != Math.Truncate(displayId)) throw new InvalidDataException("Display ID invalid.");
        return new(ScreenNative.DisplayBounds((uint)displayId), ReadWindows());
    }

    internal static WindowCandidate[] ReadWindows()
    {
        nint windows = ScreenNative.Windows(1 | 16, 0); // On-screen only, excluding desktop elements.
        if (windows == 0) throw new IOException("Window metadata unavailable.");
        var keys = new List<nint>();
        try
        {
            if (ScreenNative.Type(windows) != ScreenNative.ArrayType()) throw new InvalidDataException("Window list invalid.");
            long count = ScreenNative.Count(windows);
            if (count is < 0 or > 4096) throw new InvalidDataException("Window count invalid.");
            foreach (string key in new[] { "kCGWindowOwnerPID", "kCGWindowLayer", "kCGWindowAlpha", "kCGWindowBounds" }) keys.Add(Key(key));
            var result = new List<WindowCandidate>();
            for (int i = 0; i < count; i++)
            {
                nint dictionary = ScreenNative.At(windows, i);
                if (dictionary == 0 || ScreenNative.Type(dictionary) != ScreenNative.DictionaryType()) throw new InvalidDataException("Window metadata invalid.");
                double pid = Number(ScreenNative.Value(dictionary, keys[0]));
                double layer = Number(ScreenNative.Value(dictionary, keys[1]));
                double alpha = Number(ScreenNative.Value(dictionary, keys[2]));
                nint rect = ScreenNative.Value(dictionary, keys[3]);
                if (pid <= 0 || pid > int.MaxValue || pid != Math.Truncate(pid) || layer < int.MinValue || layer > int.MaxValue
                    || layer != Math.Truncate(layer) || rect == 0 || ScreenNative.Type(rect) != ScreenNative.DictionaryType()
                    || !ScreenNative.Rectangle(rect, out var windowBounds)) throw new InvalidDataException("Window geometry invalid.");
                result.Add(new((int)pid, (int)layer, alpha, windowBounds));
            }
            return result.ToArray();
        }
        finally
        {
            foreach (nint key in keys) ScreenNative.Release(key);
            ScreenNative.Release(windows);
        }
    }

    private static nint Key(string text)
    {
        nint key = ScreenNative.String(0, text, 0x08000100);
        return key != 0 ? key : throw new IOException("Window metadata key unavailable.");
    }

    private static double Number(nint number)
    {
        if (number == 0 || ScreenNative.Type(number) != ScreenNative.NumberType() || !ScreenNative.Number(number, 6, out double value) || !double.IsFinite(value))
            throw new InvalidDataException("Window metadata number invalid.");
        return value;
    }
}
