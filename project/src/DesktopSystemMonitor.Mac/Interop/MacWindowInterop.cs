using System.Runtime.InteropServices;

namespace DesktopSystemMonitor.Mac.Interop;

internal interface IMacWindowInterop
{
    int DesktopWindowLevel { get; }
    int NormalWindowLevel { get; }
    int FloatingWindowLevel { get; }
    void SetLevel(IntPtr window, int level);
    int GetLevel(IntPtr window);
    void SetIgnoresMouseEvents(IntPtr window, bool enabled);
    bool GetIgnoresMouseEvents(IntPtr window);
    void SetCollectionBehavior(IntPtr window, nuint behavior);
}

internal sealed partial class MacWindowInterop : IMacWindowInterop
{
    private const uint DesktopWindowLevelKey = 2;
    private static readonly IntPtr NsWindowLevel = Selector("setLevel:");
    private static readonly IntPtr NsWindowLevelGetter = Selector("level");
    private static readonly IntPtr NsIgnoresMouseEvents = Selector("setIgnoresMouseEvents:");
    private static readonly IntPtr NsIgnoresMouseEventsGetter = Selector("ignoresMouseEvents");
    private static readonly IntPtr NsCollectionBehavior = Selector("setCollectionBehavior:");

    public MacWindowInterop()
    {
        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("macOS window interop is available only on macOS.");
        }

        NormalWindowLevel = 0;
        FloatingWindowLevel = 3;
        DesktopWindowLevel = CoreGraphics.CGWindowLevelForKey(DesktopWindowLevelKey);
    }

    public int DesktopWindowLevel { get; }
    public int NormalWindowLevel { get; }
    public int FloatingWindowLevel { get; }

    public void SetLevel(IntPtr window, int level) => ObjC.SetLevel(window, NsWindowLevel, level);

    public int GetLevel(IntPtr window) => checked((int)ObjC.GetInteger(window, NsWindowLevelGetter));

    public void SetIgnoresMouseEvents(IntPtr window, bool enabled) =>
        ObjC.SetBoolean(window, NsIgnoresMouseEvents, enabled);

    public bool GetIgnoresMouseEvents(IntPtr window) => ObjC.GetBoolean(window, NsIgnoresMouseEventsGetter);

    public void SetCollectionBehavior(IntPtr window, nuint behavior) =>
        ObjC.SetUnsignedInteger(window, NsCollectionBehavior, behavior);

    private static IntPtr Selector(string name)
    {
        IntPtr selector = ObjC.RegisterSelector(name);
        if (selector == IntPtr.Zero) throw new InvalidOperationException($"Objective-C selector is unavailable: {name}");
        return selector;
    }

    private static partial class ObjC
    {
        [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
        private static partial IntPtr SelRegisterName(string name);

        [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static partial IntPtr MsgSend(IntPtr receiver, IntPtr selector);

        [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static partial IntPtr MsgSend(IntPtr receiver, IntPtr selector, nint value);

        [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static partial IntPtr MsgSend(IntPtr receiver, IntPtr selector, byte value);

        internal static IntPtr RegisterSelector(string name) => SelRegisterName(name);
        internal static void SetLevel(IntPtr receiver, IntPtr selector, int level) => _ = MsgSend(receiver, selector, (nint)level);
        internal static nint GetInteger(IntPtr receiver, IntPtr selector) => (nint)MsgSend(receiver, selector);
        internal static void SetUnsignedInteger(IntPtr receiver, IntPtr selector, nuint value) => _ = MsgSend(receiver, selector, (nint)value);
        internal static void SetBoolean(IntPtr receiver, IntPtr selector, bool value) => _ = MsgSend(receiver, selector, value ? (byte)1 : (byte)0);
        internal static bool GetBoolean(IntPtr receiver, IntPtr selector) => MsgSend(receiver, selector) != IntPtr.Zero;
    }

    private static partial class CoreGraphics
    {
        [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics", EntryPoint = "CGWindowLevelForKey")]
        internal static partial int CGWindowLevelForKey(uint key);
    }
}
