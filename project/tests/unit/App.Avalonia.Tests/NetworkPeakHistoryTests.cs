using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using DesktopSystemMonitor.App;
using DesktopSystemMonitor.Core.Platform;
using Xunit;

namespace DesktopSystemMonitor.App.Avalonia.Tests;

public sealed class NetworkPeakHistoryTests
{
    private static SensorHostMetricGroup<SensorHostNetworkInterfaceReading> Group(double rx, double tx) => SensorHostMetricGroups.Ok(new[]
    {
        new SensorHostNetworkInterfaceReading { InterfaceIndex = 1, Name = "en0", ReceiveBytesPerSecond = SensorHostMetricValue.Ok(rx, 1), SendBytesPerSecond = SensorHostMetricValue.Ok(tx, 1) },
    }, 1);

    [Fact]
    public void Peak_Expires_And_Resets_On_Disconnect_Window_Change_And_Interface_Change()
    {
        var history = new NetworkPeakHistory();
        history.SetWindow(10);
        var start = DateTimeOffset.UnixEpoch;
        history.Update(Group(10000, 20000), start);
        for (int i = 1; i <= 10; i++) history.Update(Group(1000, 2000), start.AddSeconds(i));
        Assert.Equal("10.0 KB/s", history.Receive);
        history.Update(Group(1000, 2000), start.AddSeconds(11));
        Assert.Equal("1.00 KB/s", history.Receive);
        Assert.Equal("2.00 KB/s", history.Send);
        history.SetWindow(20);
        Assert.Equal("--", history.Receive);
        history.Update(Group(90000, 5000), start.AddSeconds(12));
        var changed = Group(2000, 1000);
        changed.Values[0] = changed.Values[0] with { Name = "en1" };
        history.Update(changed, start.AddSeconds(13));
        Assert.Equal("2.00 KB/s", history.Receive);
        history.Update(SensorHostMetricGroups.Unavailable<SensorHostNetworkInterfaceReading>(), start.AddSeconds(14));
        Assert.Equal("--", history.Receive);
        Assert.Throws<ArgumentOutOfRangeException>(() => history.SetWindow(0));
    }

    [Fact]
    public void Invalid_Warming_And_Clock_Discontinuity_Do_Not_Leave_Stale_Peaks()
    {
        var history = new NetworkPeakHistory();
        var start = DateTimeOffset.UnixEpoch;
        history.Update(Group(90000, 90000), start);
        history.Update(Group(1000, 2000), start.AddSeconds(10));
        Assert.Equal("1.00 KB/s", history.Receive);
        history.Update(Group(500, 500), start);
        Assert.Equal("500 B/s", history.Receive);
        history.Update(Group(double.NaN, 1000), start.AddSeconds(1));
        Assert.Equal("--", history.Receive);
        var warming = Group(1000, 1000);
        warming.Values[0] = warming.Values[0] with { ReceiveBytesPerSecond = SensorHostMetricValue.WarmingUp() };
        history.Update(warming, start.AddSeconds(2));
        Assert.Equal("--", history.Receive);
    }

    [AvaloniaFact]
    public async Task Transparent_Background_And_Peaks_Work_In_Both_Layouts_And_Persist_Window()
    {
        var store = new MemorySettingsStore();
        await using var window = new AvaloniaMainWindow();
        window.EnableSettings(store);
        var background = Assert.IsAssignableFrom<ISolidColorBrush>(window.FindControl<Border>("WidgetBackground")!.Background);
        Assert.Equal(0, background.Color.A);
        window.SetNetworkPeakWindow(20);
        window.UpdateNetworkPeaks(Group(1000, 2000), DateTimeOffset.UtcNow);
        Assert.Equal("20s max", window.FindControl<TextBlock>("NetworkPeakLabel")!.Text);
        Assert.Equal("20s max", window.FindControl<TextBlock>("ReducedNetworkPeakLabel")!.Text);
        Assert.Equal("1.00 KB/s", window.FindControl<TextBlock>("NetworkPeakReceiveText")!.Text);
        Assert.Equal("2.00 KB/s", window.FindControl<TextBlock>("ReducedNetworkPeakSendText")!.Text);
        Assert.Equal(20, store.Settings.NetworkPeakWindowSeconds);
        await using var restored = new AvaloniaMainWindow();
        restored.EnableSettings(store);
        Assert.Equal(20, restored.NetworkPeakWindowSeconds);
        Assert.Equal("--", restored.FindControl<TextBlock>("NetworkPeakReceiveText")!.Text);
    }
}
