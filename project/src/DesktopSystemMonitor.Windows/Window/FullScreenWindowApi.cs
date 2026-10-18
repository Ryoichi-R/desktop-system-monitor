using System.Runtime.InteropServices;

namespace DesktopSystemMonitor.Windows.Window;

internal readonly record struct ScreenBounds(int Left, int Top, int Right, int Bottom);

internal interface IFullScreenWindowApi
{
    IntPtr GetForegroundWindow();

    IntPtr GetShellWindow();

    IntPtr GetDesktopWindow();

    bool IsIconic(IntPtr window);

    bool TryGetWindowBounds(IntPtr window, out ScreenBounds bounds);

    bool TryGetMonitorBounds(IntPtr window, out ScreenBounds bounds);

    bool TryGetProcessId(IntPtr window, out uint processId);

    bool TryGetClassName(IntPtr window, out string className);
}

internal sealed partial class User32FullScreenWindowApi : IFullScreenWindowApi
{
    private const uint MonitorDefaultToNearest = 2;
    private const int WindowClassNameBufferLength = 257;

    public static User32FullScreenWindowApi Instance { get; } = new();

    private User32FullScreenWindowApi()
    {
    }

    public IntPtr GetForegroundWindow() => NativeGetForegroundWindow();

    public IntPtr GetShellWindow() => NativeGetShellWindow();

    public IntPtr GetDesktopWindow() => NativeGetDesktopWindow();

    public bool IsIconic(IntPtr window) => NativeIsIconic(window);

    public bool TryGetWindowBounds(IntPtr window, out ScreenBounds bounds)
    {
        if (NativeGetWindowRect(window, out RECT rect))
        {
            bounds = ToBounds(rect);
            return true;
        }

        bounds = default;
        return false;
    }

    public bool TryGetMonitorBounds(IntPtr window, out ScreenBounds bounds)
    {
        IntPtr monitor = NativeMonitorFromWindow(window, MonitorDefaultToNearest);
        var monitorInfo = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (monitor != IntPtr.Zero && NativeGetMonitorInfo(monitor, ref monitorInfo))
        {
            bounds = ToBounds(monitorInfo.rcMonitor);
            return true;
        }

        bounds = default;
        return false;
    }

    public bool TryGetProcessId(IntPtr window, out uint processId)
    {
        uint threadId = NativeGetWindowThreadProcessId(window, out processId);
        return threadId != 0 && processId != 0;
    }

    public unsafe bool TryGetClassName(IntPtr window, out string className)
    {
        Span<char> buffer = stackalloc char[WindowClassNameBufferLength];
        fixed (char* bufferPointer = buffer)
        {
            int written = NativeGetClassName(window, bufferPointer, buffer.Length);
            if (written > 0 && written < buffer.Length)
            {
                className = new string(buffer[..written]);
                return true;
            }
        }

        className = string.Empty;
        return false;
    }

    private static ScreenBounds ToBounds(RECT rect)
    {
        return new ScreenBounds(rect.Left, rect.Top, rect.Right, rect.Bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [LibraryImport("user32.dll", EntryPoint = "GetForegroundWindow")]
    private static partial IntPtr NativeGetForegroundWindow();

    [LibraryImport("user32.dll", EntryPoint = "GetShellWindow")]
    private static partial IntPtr NativeGetShellWindow();

    [LibraryImport("user32.dll", EntryPoint = "GetDesktopWindow")]
    private static partial IntPtr NativeGetDesktopWindow();

    [LibraryImport("user32.dll", EntryPoint = "IsIconic")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool NativeIsIconic(IntPtr window);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool NativeGetWindowRect(IntPtr window, out RECT rect);

    [LibraryImport("user32.dll", EntryPoint = "MonitorFromWindow")]
    private static partial IntPtr NativeMonitorFromWindow(IntPtr window, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool NativeGetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
    private static partial uint NativeGetWindowThreadProcessId(IntPtr window, out uint processId);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW", StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial int NativeGetClassName(IntPtr window, char* className, int maxCount);
}
