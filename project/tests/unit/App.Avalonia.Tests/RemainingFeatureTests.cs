using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using DesktopSystemMonitor.App;
using DesktopSystemMonitor.Core.Platform;
using Xunit;

namespace DesktopSystemMonitor.App.Avalonia.Tests;

public sealed class RemainingFeatureTests
{
    [AvaloniaFact]
    public async Task Fullscreen_Hides_And_Restores_Without_Closing_And_Menu_Can_Override()
    {
        var store = new MemorySettingsStore();
        await using var window = new AvaloniaMainWindow();
        window.EnableSettings(store);
        window.Show();
        window.UpdateFullScreenVisibility(true);
        Assert.False(window.IsVisible);
        window.UpdateFullScreenVisibility(false);
        Assert.True(window.IsVisible);
        window.UpdateFullScreenVisibility(true);
        window.ShowMonitor();
        window.UpdateFullScreenVisibility(true);
        Assert.True(window.IsVisible);
        window.UpdateFullScreenVisibility(false);
        window.UpdateFullScreenVisibility(true);
        Assert.False(window.IsVisible);
        window.ToggleFullScreenHiding();
        Assert.True(window.IsVisible);
        Assert.False(store.Settings.HideWhenFullScreen);
        window.UpdateFullScreenVisibility(true);
        Assert.True(window.IsVisible);
        window.ToggleFullScreenHiding();
        Assert.True(store.Settings.HideWhenFullScreen);
        Assert.True(window.FindControl<MenuItem>("FullScreenHideMenu")!.IsChecked);
        window.Close();
        window.UpdateFullScreenVisibility(false);
        Assert.False(window.IsVisible);
    }

    [AvaloniaFact]
    public void Process_View_Does_Not_Retain_Stale_Rows()
    {
        var window = new ProcessDetailsWindow();
        window.Show();
        var metric = SensorHostMetricValue.Ok(1, 1);
        var process = new SensorHostProcessReading { ProcessId = 1, Name = "sample", CpuUtilizationPercent = metric, PrivateBytes = SensorHostMetricValue.Unavailable(), IoReadBytesPerSecond = metric, IoWriteBytesPerSecond = metric };
        window.Update(SensorHostMetricGroups.Ok(new[] { process }, 1));
        Assert.Single(window.Rows.Children);
        window.Update(SensorHostMetricGroups.Unavailable<SensorHostProcessReading>());
        Assert.Empty(window.Rows.Children);
        Assert.Contains("N/A", window.Status.Text);
        window.Update(new() { Status = SensorHostMetricStatus.WarmingUp, Values = [], SampledAtMonotonicTicks = 0 });
        Assert.Equal("計測準備中", window.Status.Text);
        window.Close();
    }
}
