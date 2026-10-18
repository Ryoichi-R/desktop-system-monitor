using DesktopSystemMonitor.Windows.Window;
using Xunit;

namespace DesktopSystemMonitor.Windows.Tests.Window;

public sealed class FullScreenDetectorTests
{
    private static readonly IntPtr OwnWindow = new(100);
    private static readonly IntPtr ForegroundWindow = new(200);
    private static readonly IntPtr ShellWindow = new(300);
    private static readonly IntPtr DesktopWindow = new(400);

    [Fact]
    public void IsForegroundFullScreenReturnsFalseWhenNoForegroundWindowExists()
    {
        var api = CreateFullScreenApi();
        api.ForegroundWindow = IntPtr.Zero;

        Assert.False(FullScreenDetector.IsForegroundFullScreen(OwnWindow, api));
    }

    [Fact]
    public void IsForegroundFullScreenReturnsFalseForOwnWindow()
    {
        var api = CreateFullScreenApi();
        api.ForegroundWindow = OwnWindow;

        Assert.False(FullScreenDetector.IsForegroundFullScreen(OwnWindow, api));
    }

    [Fact]
    public void IsForegroundFullScreenReturnsFalseForMinimizedWindow()
    {
        var api = CreateFullScreenApi();
        api.Minimized = true;

        Assert.False(FullScreenDetector.IsForegroundFullScreen(OwnWindow, api));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IsForegroundFullScreenReturnsFalseForExactDesktopHandles(bool useShellWindow)
    {
        var api = CreateFullScreenApi();
        api.ForegroundWindow = useShellWindow ? ShellWindow : DesktopWindow;

        Assert.False(FullScreenDetector.IsForegroundFullScreen(OwnWindow, api));
    }

    [Theory]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    public void IsForegroundFullScreenReturnsFalseForExplorerDesktopHost(string className)
    {
        var api = CreateFullScreenApi();
        api.ForegroundProcessId = api.ShellProcessId;
        api.ClassName = className;

        Assert.False(FullScreenDetector.IsForegroundFullScreen(OwnWindow, api));
    }

    [Theory]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    public void IsForegroundFullScreenDoesNotExcludeDesktopClassOwnedByAnotherProcess(string className)
    {
        var api = CreateFullScreenApi();
        api.ForegroundProcessId = api.ShellProcessId + 1;
        api.ClassName = className;

        Assert.True(FullScreenDetector.IsForegroundFullScreen(OwnWindow, api));
    }

    [Fact]
    public void IsForegroundFullScreenDoesNotExcludeOtherExplorerWindowClass()
    {
        var api = CreateFullScreenApi();
        api.ForegroundProcessId = api.ShellProcessId;
        api.ClassName = "CabinetWClass";

        Assert.True(FullScreenDetector.IsForegroundFullScreen(OwnWindow, api));
    }

    [Fact]
    public void IsForegroundFullScreenReturnsTrueForFullMonitorBounds()
    {
        var api = CreateFullScreenApi();

        Assert.True(FullScreenDetector.IsForegroundFullScreen(OwnWindow, api));
    }

    [Fact]
    public void IsForegroundFullScreenReturnsFalseWhenWidgetIsOnAnotherMonitor()
    {
        var api = CreateFullScreenApi();
        api.OwnMonitorBounds = new ScreenBounds(1920, 0, 3840, 1080);

        Assert.False(FullScreenDetector.IsForegroundFullScreen(OwnWindow, api));
    }

    [Fact]
    public void IsForegroundFullScreenAllowsOnePixelDifferenceOnEveryEdge()
    {
        var api = CreateFullScreenApi();
        api.WindowBounds = new ScreenBounds(1, 1, 1919, 1079);

        Assert.True(FullScreenDetector.IsForegroundFullScreen(OwnWindow, api));
    }

    [Fact]
    public void IsForegroundFullScreenReturnsFalseWhenWindowIsTwoPixelsInsideMonitor()
    {
        var api = CreateFullScreenApi();
        api.WindowBounds = new ScreenBounds(2, 2, 1918, 1078);

        Assert.False(FullScreenDetector.IsForegroundFullScreen(OwnWindow, api));
    }

    [Fact]
    public void IsForegroundFullScreenSupportsNegativeMonitorCoordinates()
    {
        var api = CreateFullScreenApi();
        api.WindowBounds = new ScreenBounds(-1920, 0, 0, 1080);
        api.MonitorBounds = new ScreenBounds(-1920, 0, 0, 1080);
        api.OwnMonitorBounds = new ScreenBounds(-1920, 0, 0, 1080);

        Assert.True(FullScreenDetector.IsForegroundFullScreen(OwnWindow, api));
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void IsForegroundFullScreenFallsBackToGeometryWhenShellMetadataIsUnavailable(
        bool processIdAvailable,
        bool classNameAvailable,
        bool shellWindowAvailable)
    {
        var api = CreateFullScreenApi();
        api.ProcessIdAvailable = processIdAvailable;
        api.ClassNameAvailable = classNameAvailable;
        api.ShellWindow = shellWindowAvailable ? ShellWindow : IntPtr.Zero;

        Assert.True(FullScreenDetector.IsForegroundFullScreen(OwnWindow, api));
    }

    [Theory]
    [InlineData(0u, 0u)]
    [InlineData(0u, 10u)]
    [InlineData(10u, 0u)]
    public void IsForegroundFullScreenFallsBackToGeometryWhenProcessIdIsZero(
        uint shellProcessId,
        uint foregroundProcessId)
    {
        var api = CreateFullScreenApi();
        api.ShellProcessId = shellProcessId;
        api.ForegroundProcessId = foregroundProcessId;
        api.ClassName = "WorkerW";

        Assert.True(FullScreenDetector.IsForegroundFullScreen(OwnWindow, api));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void IsForegroundFullScreenReturnsFalseWhenGeometryIsUnavailable(
        bool windowBoundsAvailable,
        bool monitorBoundsAvailable)
    {
        var api = CreateFullScreenApi();
        api.WindowBoundsAvailable = windowBoundsAvailable;
        api.MonitorBoundsAvailable = monitorBoundsAvailable;

        Assert.False(FullScreenDetector.IsForegroundFullScreen(OwnWindow, api));
    }

    [Fact]
    public void IsForegroundFullScreenReadsForegroundWindowOnlyOnce()
    {
        var api = CreateFullScreenApi();

        FullScreenDetector.IsForegroundFullScreen(OwnWindow, api);

        Assert.Equal(1, api.ForegroundWindowReadCount);
    }

    [Fact]
    public void User32ApiReadsDesktopWindowMetadata()
    {
        User32FullScreenWindowApi api = User32FullScreenWindowApi.Instance;
        IntPtr desktopWindow = api.GetDesktopWindow();

        Assert.NotEqual(IntPtr.Zero, desktopWindow);
        Assert.True(api.TryGetProcessId(desktopWindow, out uint processId));
        Assert.NotEqual(0u, processId);
        Assert.True(api.TryGetClassName(desktopWindow, out string className));
        Assert.NotEmpty(className);
        Assert.InRange(className.Length, 1, 256);
    }

    private static FakeFullScreenWindowApi CreateFullScreenApi()
    {
        return new FakeFullScreenWindowApi
        {
            ForegroundWindow = ForegroundWindow,
            ShellWindow = ShellWindow,
            DesktopWindow = DesktopWindow,
            ShellProcessId = 10,
            ForegroundProcessId = 20,
            ClassName = "TestWindow",
            WindowBounds = new ScreenBounds(0, 0, 1920, 1080),
            MonitorBounds = new ScreenBounds(0, 0, 1920, 1080),
            OwnMonitorBounds = new ScreenBounds(0, 0, 1920, 1080),
        };
    }

    private sealed class FakeFullScreenWindowApi : IFullScreenWindowApi
    {
        public IntPtr ForegroundWindow { get; set; }

        public IntPtr ShellWindow { get; set; }

        public IntPtr DesktopWindow { get; set; }

        public bool Minimized { get; set; }

        public bool WindowBoundsAvailable { get; set; } = true;

        public bool MonitorBoundsAvailable { get; set; } = true;

        public bool ProcessIdAvailable { get; set; } = true;

        public bool ClassNameAvailable { get; set; } = true;

        public ScreenBounds WindowBounds { get; set; }

        public ScreenBounds MonitorBounds { get; set; }

        public ScreenBounds OwnMonitorBounds { get; set; }

        public uint ShellProcessId { get; set; }

        public uint ForegroundProcessId { get; set; }

        public string ClassName { get; set; } = string.Empty;

        public int ForegroundWindowReadCount { get; private set; }

        public IntPtr GetForegroundWindow()
        {
            ForegroundWindowReadCount++;
            return ForegroundWindow;
        }

        public IntPtr GetShellWindow() => ShellWindow;

        public IntPtr GetDesktopWindow() => DesktopWindow;

        public bool IsIconic(IntPtr window) => Minimized;

        public bool TryGetWindowBounds(IntPtr window, out ScreenBounds bounds)
        {
            bounds = WindowBounds;
            return WindowBoundsAvailable;
        }

        public bool TryGetMonitorBounds(IntPtr window, out ScreenBounds bounds)
        {
            bounds = window == OwnWindow ? OwnMonitorBounds : MonitorBounds;
            return MonitorBoundsAvailable;
        }

        public bool TryGetProcessId(IntPtr window, out uint processId)
        {
            processId = window == ShellWindow ? ShellProcessId : ForegroundProcessId;
            return ProcessIdAvailable;
        }

        public bool TryGetClassName(IntPtr window, out string className)
        {
            className = ClassName;
            return ClassNameAvailable;
        }
    }
}
