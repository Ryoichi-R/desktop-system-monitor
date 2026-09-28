using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia;
using Avalonia.VisualTree;
using Avalonia.Interactivity;

using DesktopSystemMonitor.App;
using DesktopSystemMonitor.Core.Metrics;
using DesktopSystemMonitor.Core.Platform;
using DesktopSystemMonitor.Core.Settings;
using DesktopSystemMonitor.Mac;

using Xunit;

namespace DesktopSystemMonitor.App.Avalonia.Tests;

public sealed class AvaloniaMainWindowTests
{
    [AvaloniaFact]
    public void Application_Initializes_And_Builds()
    {
        var app = new AvaloniaApp();

        app.Initialize();
        app.OnFrameworkInitializationCompleted();

        Assert.NotNull(Program.BuildAvaloniaApp());
    }

    [AvaloniaFact]
    public void Loads_Layout_With_NA_Safe_Defaults()
    {
        var window = new AvaloniaMainWindow();
        window.Show();

        Assert.Equal(280, window.Width);
        Assert.Equal(WindowDecorations.None, window.WindowDecorations);
        Assert.False(window.CanResize);
        Assert.Equal("N/A", window.FindControl<TextBlock>("CpuText")!.Text);
        Assert.Equal("N/A", window.FindControl<TextBlock>("MemoryText")!.Text);
        Assert.Equal("N/A", window.FindControl<TextBlock>("GpuText")!.Text);
    }

    [AvaloniaFact]
    public void Refresh_Leaves_Unavailable_Native_Metrics_As_NA()
    {
        var window = new AvaloniaMainWindow();
        window.Show();
        var button = window.FindControl<MenuItem>("RefreshButton")!;

        button.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        Assert.Equal("ネイティブ指標: 未接続（N/A）", window.FindControl<MenuItem>("StatusMenu")!.Header?.ToString());
    }

    [AvaloniaFact]
    public void Refresh_Formats_Available_Metrics()
    {
        var window = new AvaloniaMainWindow(new AvailableMetricSourceFactory());
        window.Show();

        window.FindControl<MenuItem>("RefreshButton")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        Assert.Equal("13", window.FindControl<TextBlock>("CpuText")!.Text);
        Assert.Equal("35", window.FindControl<TextBlock>("MemoryText")!.Text);
        Assert.Equal("57", window.FindControl<TextBlock>("GpuText")!.Text);
        Assert.Equal("ネイティブ指標: 更新済み", window.FindControl<MenuItem>("StatusMenu")!.Header?.ToString());
    }

    [AvaloniaFact]
    public void Refresh_Converts_Source_Failure_To_NA()
    {
        var window = new AvaloniaMainWindow(new ThrowingMetricSourceFactory());
        window.Show();

        window.FindControl<MenuItem>("RefreshButton")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        Assert.Equal("ネイティブ指標: 未接続（N/A）", window.FindControl<MenuItem>("StatusMenu")!.Header?.ToString());
    }

    [Theory]
    [InlineData(SensorHostMetricStatus.Ok, 25.5, "CPU: 25.5%")]
    [InlineData(SensorHostMetricStatus.Unavailable, 0, "CPU: N/A")]
    [InlineData(SensorHostMetricStatus.WarmingUp, 0, "CPU: 計測準備中")]
    [InlineData(SensorHostMetricStatus.Stale, 25.5, "CPU: N/A")]
    public void Live_Display_Does_Not_Treat_Missing_Or_Stale_Values_As_Zero(SensorHostMetricStatus status, double value, string expected)
    {
        var metric = new SensorHostMetricValue { Status = status, Value = value, SampledAtMonotonicTicks = 0 };
        Assert.Equal(expected, AvaloniaMainWindow.FormatLivePercent("CPU", metric));
    }

    [AvaloniaFact]
    public async Task Window_Disposal_Is_Idempotent()
    {
        var window = new AvaloniaMainWindow();
        await window.DisposeAsync();
        await window.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task Live_Polling_Shows_Actual_Values_And_Disconnect_State()
    {
        var session = new FakeSession();
        await using var window = new AvaloniaMainWindow();
        window.StartLiveMonitoring(session);
        await window.RefreshLiveAsync();
        Assert.Equal("25", window.FindControl<TextBlock>("MemoryText")!.Text);
        Assert.Equal("N/A", window.FindControl<TextBlock>("CpuText")!.Text);
        session.Metrics = SensorHostMetrics.Empty;
        await window.RefreshLiveAsync();
        Assert.Equal("N/A", window.FindControl<TextBlock>("MemoryText")!.Text);
        Assert.Contains("再接続", window.FindControl<MenuItem>("StatusMenu")!.Header?.ToString());
        await window.DisposeAsync();
        Assert.True(session.Stopped);
        Assert.True(session.Disposed);
        await window.RefreshLiveAsync();
    }

    [AvaloniaFact]
    public async Task Closing_While_Polling_Cancels_Without_Updating_Closed_Window()
    {
        var session = new FakeSession { Hang = true };
        var window = new AvaloniaMainWindow();
        window.StartLiveMonitoring(session);
        Task sampling = window.RefreshLiveAsync();
        await window.RefreshLiveAsync();
        Assert.Equal(1, session.Calls);
        await window.DisposeAsync();
        await sampling;
        Assert.True(session.Stopped);
    }

    [AvaloniaFact]
    public async Task Widget_Bars_Clear_On_Disconnect_And_Scale_Preserves_Aspect()
    {
        await using var window = new AvaloniaMainWindow();
        window.UpdateMetric("Cpu", true, 75);
        var bar = (global::Avalonia.Media.ScaleTransform)window.FindControl<global::Avalonia.Controls.Shapes.Rectangle>("CpuBar")!.RenderTransform!;
        var reducedBar = (global::Avalonia.Media.ScaleTransform)window.FindControl<global::Avalonia.Controls.Shapes.Rectangle>("ReducedCpuBar")!.RenderTransform!;
        Assert.Equal(0.75, bar.ScaleX);
        Assert.Equal(0.75, reducedBar.ScaleX);
        window.UpdateMetric("Cpu", false, 75);
        Assert.Equal(0, bar.ScaleX);
        Assert.Equal("", window.FindControl<TextBlock>("CpuUnit")!.Text);
        window.UpdateMetric("Cpu", true, double.NaN);
        Assert.Equal("N/A", window.FindControl<TextBlock>("CpuText")!.Text);
        Assert.Equal("N/A", window.FindControl<TextBlock>("ReducedCpuText")!.Text);
        window.SetWidgetScale(0.5);
        Assert.Equal(140, window.Width);
        Assert.Equal(110, window.Height);
        window.SetWidgetScale(0.75);
        Assert.Equal(210, window.Width);
        Assert.Equal(165, window.Height);
        window.SetWidgetScale(1.5);
        Assert.Equal(420, window.Width);
        Assert.Equal(330, window.Height);
        Assert.Throws<ArgumentOutOfRangeException>(() => window.SetWidgetScale(0));
    }

    [AvaloniaFact]
    public async Task Context_Menu_Opens_And_Controls_Scale_Topmost_And_Exit()
    {
        await using var window = new AvaloniaMainWindow();
        window.Show();
        window.MouseDown(new Point(20, 20), MouseButton.Right);
        window.MouseUp(new Point(20, 20), MouseButton.Right);
        var menu = window.GetVisualDescendants().OfType<Border>().Select(b => b.ContextMenu).First(m => m is not null)!;
        Assert.True(menu.IsOpen);
        var scaleMenu = (MenuItem)menu.Items[1]!;
        double[] widths = [140, 210, 280, 350, 420];
        for (int i = 0; i < widths.Length; i++)
        {
            ((MenuItem)scaleMenu.Items[i + 1]!).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(widths[i], window.Width);
        }
        MenuItem displayMode = menu.Items.OfType<MenuItem>().Single(item => item.Header?.ToString() == "表示形式");
        ((MenuItem)displayMode.Items[1]!).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal(225, window.Width);
        Assert.True(window.FindControl<StackPanel>("ReducedContentPanel")!.IsVisible);
        MenuItem topmost = menu.Items.OfType<MenuItem>().Single(item => item.Header?.ToString() == "常に手前に表示");
        topmost.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.True(window.Topmost);
        MenuItem exit = menu.Items.OfType<MenuItem>().Single(item => item.Header?.ToString() == "終了");
        exit.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.False(window.IsVisible);
    }

    [Fact]
    public void Memory_Detail_Uses_Gib_And_Rejects_Unavailable_Values()
    {
        var metrics = SensorHostMetrics.Empty with
        {
            MemoryUsedBytes = SensorHostMetricValue.Ok(2 * 1073741824d, 1),
            MemoryTotalBytes = SensorHostMetricValue.Ok(8 * 1073741824d, 1),
        };
        Assert.Equal("2.0/8.0 GiB", AvaloniaMainWindow.FormatMemoryUsage(metrics));
        Assert.Equal("N/A", AvaloniaMainWindow.FormatMemoryUsage(SensorHostMetrics.Empty));
    }

    [AvaloniaFact]
    public async Task Live_Gpu_And_Network_Display_And_Clear_After_Disconnect()
    {
        var item = new SensorHostNetworkInterfaceReading
        {
            InterfaceIndex = 1,
            Name = "en0",
            ReceiveBytesPerSecond = SensorHostMetricValue.Ok(1000, 1),
            SendBytesPerSecond = SensorHostMetricValue.Ok(2000, 1),
        };
        var group = SensorHostMetricGroups.Ok(new[] { item }, 1);
        Assert.Equal("1.00 KB/s", AvaloniaMainWindow.FormatNetwork(group, true));
        Assert.Equal("2.00 KB/s", AvaloniaMainWindow.FormatNetwork(group, false));
        Assert.Equal("--", AvaloniaMainWindow.FormatNetwork(SensorHostMetricGroups.Ok(new[] { item with { ReceiveBytesPerSecond = SensorHostMetricValue.WarmingUp() } }, 1), true));
        Assert.Equal("N/A", AvaloniaMainWindow.FormatNetwork(SensorHostMetricGroups.Ok(new[] { item with { ReceiveBytesPerSecond = SensorHostMetricValue.Unavailable() } }, 1), true));
        var session = new FakeSession { Metrics = SensorHostMetrics.Empty with { GpuUtilizationPercent = SensorHostMetricValue.Ok(45, 1), NetworkInterfaces = group } };
        await using var window = new AvaloniaMainWindow();
        window.StartLiveMonitoring(session);
        await window.RefreshLiveAsync();
        Assert.Equal("45", window.FindControl<TextBlock>("GpuText")!.Text);
        Assert.Equal("45", window.FindControl<TextBlock>("ReducedGpuText")!.Text);
        Assert.Equal("1.00 KB/s", window.FindControl<TextBlock>("ReducedNetworkReceiveText")!.Text);
        Assert.Equal("2.00 KB/s", window.FindControl<TextBlock>("ReducedNetworkSendText")!.Text);
        session.Metrics = SensorHostMetrics.Empty;
        await window.RefreshLiveAsync();
        Assert.Equal("N/A", window.FindControl<TextBlock>("GpuText")!.Text);
    }

    private sealed class FakeSession : IMacSensorSession
    {
        internal SensorHostMetrics Metrics = SensorHostMetrics.Empty with
        {
            CpuUtilizationPercent = SensorHostMetricValue.WarmingUp(),
            MemoryUtilizationPercent = SensorHostMetricValue.Ok(25, 1),
        };
        internal bool Hang, Stopped, Disposed;
        internal int Calls;
        public async ValueTask<SensorHostMetrics> SampleAsync(CancellationToken cancellationToken)
        {
            Calls++;
            if (Hang) await Task.Delay(Timeout.Infinite, cancellationToken);
            return Metrics;
        }
        public void StopPolling() => Stopped = true;
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }

    [AvaloniaFact]
    public async Task Desktop_Startup_Registers_Tray_Menu_And_Owns_The_Main_Window()
    {
        var lifetime = new ClassicDesktopStyleApplicationLifetime();
        var app = new AvaloniaApp { ApplicationLifetime = lifetime, SettingsStoreFactory = () => new MemorySettingsStore() };
        app.Initialize();
        app.OnFrameworkInitializationCompleted();
        var window = Assert.IsType<AvaloniaMainWindow>(lifetime.MainWindow);
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                var tray = Assert.Single(TrayIcon.GetIcons(app)!);
                Assert.NotNull(tray.Icon);
                Assert.True(tray.IsVisible);
                Assert.Equal(15, tray.Menu!.Items.Count);
                window.SetWidgetScale(1.25);
                window.ToggleTopmost();
                var scaleMenu = Assert.IsType<NativeMenuItem>(tray.Menu.Items[1]);
                Assert.True(Assert.IsType<NativeMenuItem>(scaleMenu.Menu!.Items[4]).IsChecked);
                Assert.False(Assert.IsType<NativeMenuItem>(scaleMenu.Menu.Items[1]).IsChecked);
                var displayMode = Assert.IsType<NativeMenuItem>(tray.Menu.Items[2]);
                Assert.True(Assert.IsType<NativeMenuItem>(displayMode.Menu!.Items[0]).IsChecked);
                Assert.True(Assert.IsType<NativeMenuItem>(tray.Menu.Items[3]).IsChecked);
                Assert.False(Assert.IsType<NativeMenuItem>(tray.Menu.Items[5]).IsChecked);
                Assert.False(Assert.IsType<NativeMenuItem>(tray.Menu.Items[6]).IsChecked);
                Assert.False(Assert.IsType<NativeMenuItem>(tray.Menu.Items[7]).IsChecked);
                window.SetDisplayMode(WidgetDisplayMode.Reduced);
                Assert.True(Assert.IsType<NativeMenuItem>(displayMode.Menu.Items[1]).IsChecked);
                Assert.False(Assert.IsType<NativeMenuItem>(displayMode.Menu.Items[0]).IsChecked);
            }
        }
        finally
        {
            await window.DisposeAsync();
            foreach (var tray in TrayIcon.GetIcons(app) ?? []) tray.Dispose();
        }
    }

    private class AvailableMetricSourceFactory : IMetricSourceFactory
    {
        public virtual IMetricSource<CpuSnapshot> CreateCpu() => new FixedSource<CpuSnapshot>(new CpuSnapshot
        {
            UtilizationStatus = MetricStatus.Ok,
            UtilizationPercent = 12.5,
            FrequencyStatus = MetricStatus.Ok,
            FrequencyMhz = 1,
            FrequencyIsEstimate = false,
            RawUtilizationPercent = 12.5,
        });

        public virtual IMetricSource<MemorySnapshot> CreateMemory() => new FixedSource<MemorySnapshot>(new MemorySnapshot
        {
            Status = MetricStatus.Ok,
            UtilizationPercent = 34.5,
            UsedBytes = 1,
            TotalBytes = 2,
        });

        public virtual IGpuMetricSource CreateGpu() => new FixedGpuSource();
        public INetworkMetricSource CreateNetwork() => throw new NotSupportedException();
        public IPowerMetricSource CreatePower(Action<string, Exception> onDiagnostic) => throw new NotSupportedException();
        public IDiskMetricSource CreateDisk() => throw new NotSupportedException();
        public IBatteryMetricSource CreateBattery() => throw new NotSupportedException();
    }

    private sealed class ThrowingMetricSourceFactory : AvailableMetricSourceFactory
    {
        public override IMetricSource<CpuSnapshot> CreateCpu() => new ThrowingSource<CpuSnapshot>();
    }

    private class FixedSource<T>(T snapshot) : IMetricSource<T>
    {
        public ValueTask<T> SampleAsync(CancellationToken cancellationToken) => ValueTask.FromResult(snapshot);
        public void ResetBaseline() { }
        public void Dispose() { }
    }

    private sealed class ThrowingSource<T> : IMetricSource<T>
    {
        public ValueTask<T> SampleAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("test");
        public void ResetBaseline() { }
        public void Dispose() { }
    }

    private sealed class FixedGpuSource : FixedSource<GpuSnapshot>, IGpuMetricSource
    {
        public FixedGpuSource() : base(new GpuSnapshot
        {
            OverallStatus = MetricStatus.Ok,
            Adapters = [new GpuAdapterSnapshot
            {
                Luid = 1,
                DisplayName = "Test GPU",
                UtilizationStatus = MetricStatus.Ok,
                UtilizationPercent = 56.5,
                BusiestEngineType = "3D",
                MemoryStatus = MetricStatus.Ok,
                DedicatedUsageBytes = 1,
                DedicatedLimitBytes = 2,
                IsIntegrated = false,
            }],
        })
        {
        }

        public void SetPreferredAdapter(ulong? luid) { }
    }
}
