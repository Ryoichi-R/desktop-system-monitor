using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using DesktopSystemMonitor.App;
using DesktopSystemMonitor.Core.Platform;
using Xunit;
namespace DesktopSystemMonitor.App.Avalonia.Tests;

public sealed class ThermalDisplayTests
{
    [AvaloniaFact]
    public async Task Details_Display_And_Clear_In_Both_Layouts_And_Peak_Setting_Persists()
    {
        var store = new MemorySettingsStore();
        await using var window = new AvaloniaMainWindow();
        window.EnableSettings(store);
        window.SetCpuPeakWindow(20);
        window.UpdateThermalDetails(SensorHostMetrics.Empty with
        {
            CpuTemperatureCelsius = SensorHostMetricValue.Ok(42.5, 1),
            GpuTemperatureCelsius = SensorHostMetricValue.Ok(38, 1),
            GpuPowerWatts = SensorHostMetricValue.Ok(12.5, 1),
            CpuUtilizationPercent = SensorHostMetricValue.Ok(55, 1),
        }, DateTimeOffset.UtcNow);
        Assert.Equal("42.5°C*", window.FindControl<TextBlock>("CpuTemperatureText")!.Text);
        Assert.Equal("38.0°C*", window.FindControl<TextBlock>("ReducedGpuTemperatureText")!.Text);
        Assert.Equal("12.5 W", window.FindControl<TextBlock>("GpuPowerText")!.Text);
        Assert.Equal("20s max 55%", window.FindControl<TextBlock>("ReducedCpuPeakText")!.Text);
        Assert.Equal(20, store.Settings.CpuPeakWindowSeconds);
        window.UpdateThermalDetails(SensorHostMetrics.Empty, DateTimeOffset.UtcNow);
        Assert.Equal("N/A", window.FindControl<TextBlock>("CpuTemperatureText")!.Text);
        Assert.Equal("20s max --", window.FindControl<TextBlock>("CpuPeakText")!.Text);
        await using var restored = new AvaloniaMainWindow();
        restored.EnableSettings(store);
        Assert.Equal(20, restored.CpuPeakWindowSeconds);
    }

    [Fact]
    public void Cpu_Peak_Expires_And_Clears_After_Gap_Or_Invalid_Value()
    {
        var peak = new CpuPeakHistory();
        peak.SetWindow(10);
        var start = DateTimeOffset.UnixEpoch;
        peak.Update(SensorHostMetricValue.Ok(80, 1), start);
        for (int i = 1; i <= 10; i++) peak.Update(SensorHostMetricValue.Ok(10, 1), start.AddSeconds(i));
        Assert.Equal("10s max 80%", peak.Text);
        peak.Update(SensorHostMetricValue.Ok(10, 1), start.AddSeconds(11));
        Assert.Equal("10s max 10%", peak.Text);
        peak.Update(SensorHostMetricValue.Ok(5, 1), start.AddSeconds(20));
        Assert.Equal("10s max 5%", peak.Text);
        peak.Update(SensorHostMetricValue.Ok(double.NaN, 1), start.AddSeconds(21));
        Assert.Equal("10s max --", peak.Text);
        Assert.Throws<ArgumentOutOfRangeException>(() => peak.SetWindow(1));
        Assert.Equal("N/A", AvaloniaMainWindow.FormatDetail(SensorHostMetricValue.Ok(500, 1), true));
    }
}
