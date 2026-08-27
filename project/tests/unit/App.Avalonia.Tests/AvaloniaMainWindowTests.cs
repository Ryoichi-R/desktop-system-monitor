using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;

using DesktopSystemMonitor.App;
using DesktopSystemMonitor.Core.Metrics;

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

        Assert.Equal(320, window.MinWidth);
        Assert.Equal("CPU: N/A", window.FindControl<TextBlock>("CpuText")!.Text);
        Assert.Equal("メモリ: N/A", window.FindControl<TextBlock>("MemoryText")!.Text);
        Assert.Equal("GPU: N/A", window.FindControl<TextBlock>("GpuText")!.Text);
    }

    [AvaloniaFact]
    public void Refresh_Leaves_Unavailable_Native_Metrics_As_NA()
    {
        var window = new AvaloniaMainWindow();
        window.Show();
        var button = window.FindControl<Button>("RefreshButton")!;

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal("ネイティブ指標: 未接続（N/A）", window.FindControl<TextBlock>("StatusText")!.Text);
    }

    [AvaloniaFact]
    public void Refresh_Formats_Available_Metrics()
    {
        var window = new AvaloniaMainWindow(new AvailableMetricSourceFactory());
        window.Show();

        window.FindControl<Button>("RefreshButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal("CPU: 12.5%", window.FindControl<TextBlock>("CpuText")!.Text);
        Assert.Equal("メモリ: 34.5%", window.FindControl<TextBlock>("MemoryText")!.Text);
        Assert.Equal("GPU: 56.5%", window.FindControl<TextBlock>("GpuText")!.Text);
        Assert.Equal("ネイティブ指標: 更新済み", window.FindControl<TextBlock>("StatusText")!.Text);
    }

    [AvaloniaFact]
    public void Refresh_Converts_Source_Failure_To_NA()
    {
        var window = new AvaloniaMainWindow(new ThrowingMetricSourceFactory());
        window.Show();

        window.FindControl<Button>("RefreshButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal("ネイティブ指標: 未接続（N/A）", window.FindControl<TextBlock>("StatusText")!.Text);
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
