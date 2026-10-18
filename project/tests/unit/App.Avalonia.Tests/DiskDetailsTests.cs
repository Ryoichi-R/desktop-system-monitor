using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using DesktopSystemMonitor.App;
using DesktopSystemMonitor.Core.Platform;
using Xunit;

namespace DesktopSystemMonitor.App.Avalonia.Tests;

public sealed class DiskDetailsTests
{
    [AvaloniaFact]
    public void Details_Show_Rates_And_Clear_When_Host_Is_Unavailable()
    {
        var window = new DiskDetailsWindow();
        window.Show();
        window.Update(SensorHostMetrics.Empty with { DiskReadBytesPerSecond = SensorHostMetricValue.Ok(1234567, 1), DiskWriteBytesPerSecond = SensorHostMetricValue.WarmingUp() });
        Assert.Contains("1.23 MB/s", window.ReadRate.Text);
        Assert.Contains("計測準備中", window.WriteRate.Text);
        window.Update(SensorHostMetrics.Empty);
        Assert.Contains("N/A", window.ReadRate.Text);
        Assert.Contains("N/A", window.WriteRate.Text);
        Assert.Equal("0.00 KB/s", DiskDetailsWindow.Format(SensorHostMetricValue.Ok(0, 1)));
        Assert.Equal("1.00 GB/s", DiskDetailsWindow.Format(SensorHostMetricValue.Ok(1_000_000_000, 1)));
        Assert.Equal("N/A", DiskDetailsWindow.Format(SensorHostMetricValue.Ok(double.NaN, 1)));
        window.Close();
    }

    [AvaloniaFact]
    public async Task Auxiliary_Windows_Close_Without_Closing_Monitor_And_Clean_Up_With_Monitor()
    {
        await using var monitor = new AvaloniaMainWindow();
        monitor.Show();
        T? Child<T>(string name) where T : Window => (T?)typeof(AvaloniaMainWindow)
            .GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(monitor);
        monitor.ShowDiskDetails();
        var disk = Child<DiskDetailsWindow>("_diskDetails")!;
        monitor.ShowDiskDetails();
        Assert.Same(disk, Child<DiskDetailsWindow>("_diskDetails"));
        disk.Close();
        Assert.Null(Child<DiskDetailsWindow>("_diskDetails"));
        Assert.True(monitor.IsVisible);
        monitor.ShowScaleInput();
        var scale = Child<ScaleInputWindow>("_scaleInput")!;
        scale.PercentInput.Text = "90";
        Assert.True(scale.ApplyInput());
        Assert.True(monitor.IsVisible);
        Assert.Null(Child<ScaleInputWindow>("_scaleInput"));
        Assert.Equal(0.9, monitor.WidgetScale);
        monitor.ShowDiskDetails();
        disk = Child<DiskDetailsWindow>("_diskDetails")!;
        monitor.ShowScaleInput();
        scale = Child<ScaleInputWindow>("_scaleInput")!;
        monitor.Close();
        Assert.False(monitor.IsVisible);
        Assert.False(disk.IsVisible);
        Assert.False(scale.IsVisible);
    }
}
