using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DesktopSystemMonitor.App;
using DesktopSystemMonitor.Core.Platform;
using DesktopSystemMonitor.Core.Settings;
using DesktopSystemMonitor.Mac;
using Xunit;

namespace DesktopSystemMonitor.App.Avalonia.Tests;

/// <summary>
/// Drives the main window's menu handlers, timer and screen-change handler. These paths are
/// not macOS-specific, so they are exercised on every platform the headless suite runs on.
/// </summary>
public sealed class AvaloniaMainWindowMenuTests
{
    private static void Click(MenuItem item) => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

    private static MenuItem? FindItem(ItemsControl root, string header)
    {
        foreach (MenuItem item in root.Items.OfType<MenuItem>())
        {
            if (item.Header?.ToString() == header) return item;
            MenuItem? nested = FindItem(item, header);
            if (nested is not null) return nested;
        }
        return null;
    }

    private static T? Field<T>(AvaloniaMainWindow window, string name) where T : class =>
        (T?)typeof(AvaloniaMainWindow)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(window);

    private static ContextMenu OpenContextMenu(AvaloniaMainWindow window)
    {
        window.MouseDown(new Point(20, 20), MouseButton.Right);
        window.MouseUp(new Point(20, 20), MouseButton.Right);
        return window.GetVisualDescendants().OfType<Border>().Select(b => b.ContextMenu).First(m => m is not null)!;
    }

    [AvaloniaFact]
    public async Task Context_Menu_Opens_Detail_Windows_And_Reuses_Them()
    {
        await using var window = new AvaloniaMainWindow();
        window.Show();
        ContextMenu menu = OpenContextMenu(window);

        Click(FindItem(menu, "高負荷プロセス…")!);
        var processes = Field<ProcessDetailsWindow>(window, "_processDetails");
        Assert.NotNull(processes);
        Click(FindItem(menu, "高負荷プロセス…")!);
        Assert.Same(processes, Field<ProcessDetailsWindow>(window, "_processDetails"));
        processes!.Close();
        Assert.Null(Field<ProcessDetailsWindow>(window, "_processDetails"));
        Assert.True(window.IsVisible);

        Click(FindItem(menu, "ディスクの読み書き…")!);
        Assert.NotNull(Field<DiskDetailsWindow>(window, "_diskDetails"));

        Click(FindItem(menu, "倍率を入力…（50〜150%）")!);
        Assert.NotNull(Field<ScaleInputWindow>(window, "_scaleInput"));
    }

    [AvaloniaFact]
    public async Task Menu_Toggles_Apply_And_Persist()
    {
        var store = new MemorySettingsStore();
        var registry = new FakeStartupRegistry();
        await using var window = new AvaloniaMainWindow();
        window.EnableSettings(store, registry);
        window.Show();

        Click(window.FindControl<MenuItem>("DisplayModeReducedMenu")!);
        Assert.Equal(WidgetDisplayMode.Reduced, window.DisplayMode);
        Click(window.FindControl<MenuItem>("DisplayModeStandardMenu")!);
        Assert.Equal(WidgetDisplayMode.Standard, window.DisplayMode);
        Assert.Equal(WidgetDisplayMode.Standard, store.Settings.DisplayMode);

        Click(window.FindControl<MenuItem>("DesktopLayerMenu")!);
        Assert.True(window.DesktopLayer);
        Assert.True(store.Settings.DesktopLayer);

        Click(window.FindControl<MenuItem>("ClickThroughMenu")!);
        Assert.True(window.ClickThrough);
        Assert.True(store.Settings.ClickThrough);

        bool hideBefore = window.HideWhenFullScreen;
        Click(window.FindControl<MenuItem>("FullScreenHideMenu")!);
        Assert.Equal(!hideBefore, window.HideWhenFullScreen);

        Click(window.FindControl<MenuItem>("StartAtLoginMenu")!);
        Assert.True(registry.Enabled);
        Assert.True(store.Settings.StartAtLogin);
        Click(window.FindControl<MenuItem>("StartAtLoginMenu")!);
        Assert.False(registry.Enabled);
        Assert.False(store.Settings.StartAtLogin);
    }

    [AvaloniaFact]
    public async Task Start_At_Login_Without_A_Registry_Reports_That_It_Cannot_Change()
    {
        var store = new MemorySettingsStore();
        await using var window = new AvaloniaMainWindow();
        window.EnableSettings(store);
        window.Show();

        Click(window.FindControl<MenuItem>("StartAtLoginMenu")!);

        Assert.False(store.Settings.StartAtLogin);
        Assert.Equal("自動起動を変更できません", window.SettingsMessage);
    }

    [AvaloniaFact]
    public async Task Diagnostic_Logging_Menu_Toggles_Persists_And_Records_Categories_Only()
    {
        // Redirect the static log away from the user profile for the duration of the test.
        string directory = Path.Combine(Path.GetTempPath(), "dsm-diag-" + Guid.NewGuid().ToString("N"));
        MacDiagnosticLog original = Program.Diagnostics;
        Program.Diagnostics = new MacDiagnosticLog(directory);
        try
        {
            var store = new MemorySettingsStore();
            await using var window = new AvaloniaMainWindow();
            window.EnableSettings(store);
            window.Show();
            string logPath = Path.Combine(directory, "mac-monitor.log");

            Click(window.FindControl<MenuItem>("DiagnosticLoggingMenu")!);
            Assert.True(window.DiagnosticLogging);
            Assert.True(store.Settings.DiagnosticLogging);
            Assert.True(Program.Diagnostics.Enabled);
            Assert.Contains("category=LoggingEnabled", File.ReadAllText(logPath));

            Click(window.FindControl<MenuItem>("DiagnosticLoggingMenu")!);
            Assert.False(window.DiagnosticLogging);
            Assert.False(store.Settings.DiagnosticLogging);
            Assert.False(Program.Diagnostics.Enabled);
            Assert.Contains("category=LoggingDisabled", File.ReadAllText(logPath));
        }
        finally
        {
            Program.Diagnostics = original;
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Settings_Layer_Choice_Normal_Clears_The_Topmost_Layer()
    {
        var store = new MemorySettingsStore();
        await using var monitor = new AvaloniaMainWindow();
        monitor.EnableSettings(store);
        var dialog = new MacSettingsWindow(monitor);

        dialog.LayerInput.SelectedIndex = 1;
        Assert.True(monitor.Topmost);
        dialog.LayerInput.SelectedIndex = 0;

        Assert.False(monitor.Topmost);
        Assert.False(monitor.DesktopLayer);
        dialog.Close();
    }
    [AvaloniaFact]
    public async Task Live_Timer_Samples_The_Session_Without_A_Full_Screen_Detector()
    {
        await using var window = new AvaloniaMainWindow();
        var session = new CountingSession();
        window.StartLiveMonitoring(session);

        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (session.Calls == 0 && DateTime.UtcNow < deadline) await Task.Delay(50);

        Assert.True(session.Calls > 0, "the one-second live timer never sampled the session");
    }

    [AvaloniaFact]
    public async Task Screen_Change_Restores_The_Position_And_Saves_Settings()
    {
        var store = new CountingStore();
        await using var window = new AvaloniaMainWindow();
        window.EnableSettings(store);
        window.Show();
        int savesBefore = store.SaveCount;

        typeof(AvaloniaMainWindow)
            .GetMethod("OnScreensChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [null, EventArgs.Empty]);
        Dispatcher.UIThread.RunJobs();

        Assert.True(window.IsVisible);
        Assert.Equal(savesBefore + 1, store.SaveCount);
    }

    [AvaloniaFact]
    public async Task Screen_Change_After_Disposal_Does_Nothing()
    {
        var store = new CountingStore();
        var window = new AvaloniaMainWindow();
        window.EnableSettings(store);
        window.Show();
        await window.DisposeAsync();
        int savesBefore = store.SaveCount;

        typeof(AvaloniaMainWindow)
            .GetMethod("OnScreensChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [null, EventArgs.Empty]);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(savesBefore, store.SaveCount);
    }

    [Fact]
    public void Out_Of_Range_Position_Is_Dropped_When_Settings_Are_Normalized()
    {
        MacWidgetSettings tooFarRight = new MacWidgetSettings { X = 200_000, Y = 10 }.Normalize();
        Assert.Null(tooFarRight.X);
        Assert.Null(tooFarRight.Y);
        MacWidgetSettings tooFarDown = new MacWidgetSettings { X = 10, Y = -200_000 }.Normalize();
        Assert.Null(tooFarDown.X);
        Assert.Null(tooFarDown.Y);
        MacWidgetSettings inRange = new MacWidgetSettings { X = -5, Y = 7 }.Normalize();
        Assert.Equal(-5, inRange.X);
        Assert.Equal(7, inRange.Y);
    }

    private sealed class FakeStartupRegistry : IStartupRegistry
    {
        internal bool Enabled;
        public bool IsEnabled(string executablePath) => Enabled;
        public void Enable(string executablePath) => Enabled = true;
        public void Disable() => Enabled = false;
    }

    private sealed class CountingStore : IMacWidgetSettingsStore
    {
        internal int SaveCount;
        private MacWidgetSettings _settings = new();
        public MacWidgetSettings Load() => _settings;
        public void Save(MacWidgetSettings settings)
        {
            SaveCount++;
            _settings = settings;
        }
    }

    private sealed class CountingSession : IMacSensorSession
    {
        internal int Calls;
        public ValueTask<SensorHostMetrics> SampleAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            return ValueTask.FromResult(SensorHostMetrics.Empty);
        }
        public void StopPolling() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
