using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using DesktopSystemMonitor.App;
using DesktopSystemMonitor.Core.Settings;
using Xunit;

namespace DesktopSystemMonitor.App.Avalonia.Tests;

public sealed class MacSettingsWindowTests
{
    [AvaloniaFact]
    public async Task Settings_Changes_Apply_Persist_And_Restore()
    {
        var store = new MemorySettingsStore();
        await using var monitor = new AvaloniaMainWindow();
        monitor.EnableSettings(store);
        var dialog = new MacSettingsWindow(monitor);
        dialog.ScaleInput.Value = 87;
        dialog.DisplayModeInput.SelectedIndex = 1;
        dialog.LayerInput.SelectedIndex = 1;
        Assert.True(monitor.Topmost);
        dialog.LayerInput.SelectedIndex = 2;
        Assert.False(monitor.Topmost);
        Assert.True(monitor.DesktopLayer);
        dialog.ClickThroughInput.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        dialog.FullScreenInput.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        dialog.CpuPeakInput.Value = 20;
        dialog.NetworkPeakInput.Value = 30;
        await using var restored = new AvaloniaMainWindow();
        restored.EnableSettings(store);
        Assert.Equal(0.87, restored.WidgetScale);
        Assert.Equal(WidgetDisplayMode.Reduced, restored.DisplayMode);
        Assert.True(restored.DesktopLayer);
        Assert.True(restored.ClickThrough);
        Assert.False(restored.HideWhenFullScreen);
        Assert.Equal(20, restored.CpuPeakWindowSeconds);
        Assert.Equal(30, restored.NetworkPeakWindowSeconds);
        dialog.LayerInput.SelectedIndex = 0;
        Assert.False(monitor.DesktopLayer);
        Assert.False(monitor.Topmost);
        dialog.Close();
    }

    [AvaloniaFact]
    public async Task Menu_Changes_Update_Open_Settings_And_Failures_Are_Visible()
    {
        await using var monitor = new AvaloniaMainWindow();
        monitor.EnableSettings(new FailingStore());
        var dialog = new MacSettingsWindow(monitor);
        monitor.SetWidgetScale(1.25);
        Assert.Equal(125m, dialog.ScaleInput.Value);
        Assert.Contains("保存できません", dialog.StatusText.Text);
        dialog.StartupInput.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(dialog.StartupInput.IsChecked);
        Assert.Contains("自動起動を変更できません", dialog.StatusText.Text);
        dialog.Close();
    }

    [AvaloniaFact]
    public async Task Settings_Window_Is_Reused_And_Closes_With_Monitor()
    {
        await using var monitor = new AvaloniaMainWindow();
        monitor.Show();
        monitor.ShowSettings();
        var first = monitor.SettingsWindow;
        Assert.NotNull(first);
        monitor.ShowSettings();
        Assert.Same(first, monitor.SettingsWindow);
        first.Close();
        Assert.Null(monitor.SettingsWindow);
        monitor.ShowSettings();
        Assert.NotSame(first, monitor.SettingsWindow);
        var second = monitor.SettingsWindow!;
        monitor.Close();
        Assert.False(second.IsVisible);
        Assert.Null(monitor.SettingsWindow);
    }

    [AvaloniaFact]
    public async Task Context_Menu_Ends_With_Separated_Quit_And_Opens_Settings()
    {
        await using var monitor = new AvaloniaMainWindow();
        var menu = monitor.FindControl<Border>("WidgetBackground")!.ContextMenu!;
        Assert.IsType<Separator>(menu.Items[^2]);
        Assert.Equal("終了", Assert.IsType<MenuItem>(menu.Items[^1]).Header);
        var settings = Assert.IsType<MenuItem>(menu.Items[0]);
        Assert.Equal("設定…", settings.Header);
        settings.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.True(monitor.SettingsWindow!.IsVisible);
        monitor.SettingsWindow.Close();
    }

    private sealed class FailingStore : IMacWidgetSettingsStore
    {
        public MacWidgetSettings Load() => new();
        public void Save(MacWidgetSettings settings) => throw new IOException("test");
    }
}
