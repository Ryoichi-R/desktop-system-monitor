using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using DesktopSystemMonitor.App;
using DesktopSystemMonitor.Core.Platform;
using DesktopSystemMonitor.Core.Settings;
using Xunit;

namespace DesktopSystemMonitor.App.Avalonia.Tests;

internal sealed class MemorySettingsStore : IMacWidgetSettingsStore
{
    internal MacWidgetSettings Settings = new();
    internal bool FailLoad, FailSave;
    public MacWidgetSettings Load() => FailLoad ? throw new InvalidDataException("test") : Settings;
    public void Save(MacWidgetSettings settings)
    {
        if (FailSave) throw new IOException("test");
        Settings = settings;
    }
}

public sealed class MacWidgetSettingsTests
{
    [AvaloniaFact]
    public async Task Settings_Survive_Window_Restart_And_Menus_Stay_In_Sync()
    {
        var store = new MemorySettingsStore();
        await using (var first = new AvaloniaMainWindow())
        {
            first.EnableSettings(store);
            first.Show();
            first.SetWidgetScale(1.5);
            first.ToggleTopmost();
            first.Position = new PixelPoint(80, 90);
        }
        await using var second = new AvaloniaMainWindow();
        second.EnableSettings(store);
        second.Show();
        Assert.Equal(1.5, second.WidgetScale);
        Assert.Equal(420, second.Width);
        Assert.True(second.Topmost);
        Assert.True(second.FindControl<MenuItem>("TopmostMenu")!.IsChecked);
        Assert.True(second.FindControl<MenuItem>("ScaleLargeMenu")!.IsChecked);
        Assert.NotNull(store.Settings.X);
    }

    [AvaloniaFact]
    public async Task Read_Failure_Preserves_Original_And_Write_Failure_Is_Visible_And_Retryable()
    {
        var store = new MemorySettingsStore { FailLoad = true };
        await using var window = new AvaloniaMainWindow();
        window.EnableSettings(store);
        window.SetWidgetScale(1.25);
        Assert.Equal(1, store.Settings.Scale);
        Assert.Contains("読み込めません", window.SettingsMessage);
        store.FailLoad = false;
        window.EnableSettings(store);
        store.FailSave = true;
        window.SetWidgetScale(1.5);
        Assert.Contains("保存できません", window.SettingsMessage);
        store.FailSave = false;
        window.ToggleTopmost();
        Assert.Contains("自動保存", window.SettingsMessage);
        Assert.Equal(1.5, store.Settings.Scale);
    }

    [AvaloniaFact]
    public async Task Reduced_Mode_Uses_Windows_Width_And_Small_Scales()
    {
        await using var window = new AvaloniaMainWindow();

        window.SetDisplayMode(WidgetDisplayMode.Reduced);
        Assert.Equal(WidgetDisplayMode.Reduced, window.DisplayMode);
        Assert.Equal(150, window.Width);
        Assert.Equal(236, window.Height);
        Assert.True(window.FindControl<StackPanel>("ReducedContentPanel")!.IsVisible);
        Assert.False(window.FindControl<StackPanel>("StandardContentPanel")!.IsVisible);

        window.SetWidgetScale(0.5);
        Assert.Equal(75, window.Width);
        Assert.Equal(118, window.Height);
        window.SetWidgetScale(0.75);
        Assert.Equal(112.5, window.Width);
        Assert.Equal(177, window.Height);
    }

    [Fact]
    public void Position_Is_Clamped_For_Removed_Display_And_Larger_Widget()
    {
        Assert.Equal(new PixelPoint(540, 360), AvaloniaMainWindow.ClampPosition(new(9000, 9000), new(100, 20, 1000, 780), 280, 220, 2));
        Assert.Equal(new PixelPoint(-1000, 20), AvaloniaMainWindow.ClampPosition(new(-5000, -5000), new(-1000, 20, 800, 500), 280, 220, 1));
        Assert.Equal(new PixelPoint(0, 0), AvaloniaMainWindow.ClampPosition(new(10, 10), new(0, 0, 100, 100), 280, 220, 1));
    }

    [Fact]
    public void Store_Roundtrips_Normalizes_And_Rejects_Unknown_Version_Without_Modifying_It()
    {
        string directory = Path.Combine(Path.GetTempPath(), "monitor-settings-test-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "settings.json");
        var store = new MacWidgetSettingsStore(path);
        try
        {
            Assert.Equal(1, store.Load().Scale);
            store.Save(new() { Scale = 0.75, DisplayMode = WidgetDisplayMode.Reduced, Topmost = true, X = -120, Y = 20 });
            Assert.Equal(new MacWidgetSettings { Scale = 0.75, DisplayMode = WidgetDisplayMode.Reduced, Topmost = true, X = -120, Y = 20 }, store.Load());
            store.Save(new() { DesktopLayer = true, ClickThrough = true, StartAtLogin = true, Topmost = true });
            Assert.Equal(new MacWidgetSettings { DesktopLayer = true, ClickThrough = true, StartAtLogin = true }, store.Load());
            store.Save(new() { Scale = 8, X = int.MaxValue, Y = 1 });
            Assert.Equal(new MacWidgetSettings(), store.Load());
            store.Save(new() { Scale = 0.1, DisplayMode = (WidgetDisplayMode)999 });
            Assert.Equal(new MacWidgetSettings(), store.Load());
            Assert.Single(Directory.GetFiles(directory));
            File.WriteAllText(path, "{\"Version\":2}");
            Assert.Throws<InvalidDataException>(() => store.Load());
            Assert.Equal("{\"Version\":2}", File.ReadAllText(path));
            File.WriteAllText(path, "null");
            Assert.Throws<InvalidDataException>(() => store.Load());
            File.WriteAllText(path, new string(' ', 65537));
            Assert.Throws<InvalidDataException>(() => store.Load());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [AvaloniaFact]
    public async Task Startup_Toggle_Uses_Registry_And_Persists_Only_After_Success()
    {
        var store = new MemorySettingsStore();
        var registry = new FakeStartupRegistry();
        await using var window = new AvaloniaMainWindow();
        window.EnableSettings(store, registry);

        window.ToggleStartAtLogin();
        Assert.True(window.StartAtLogin);
        Assert.Equal(1, registry.EnableCalls);

        window.ToggleStartAtLogin();
        Assert.False(window.StartAtLogin);
        Assert.Equal(1, registry.DisableCalls);
    }

    [AvaloniaFact]
    public async Task Layer_Toggles_Are_Mutually_Exclusive_And_Persisted()
    {
        var store = new MemorySettingsStore();
        await using var first = new AvaloniaMainWindow();
        first.EnableSettings(store);

        first.ToggleTopmost();
        first.ToggleDesktopLayer();
        first.ToggleClickThrough();

        Assert.False(first.Topmost);
        Assert.True(first.DesktopLayer);
        Assert.True(first.ClickThrough);
        Assert.False(store.Settings.Topmost);
        Assert.True(store.Settings.DesktopLayer);
        Assert.True(store.Settings.ClickThrough);
    }

    [AvaloniaFact]
    public async Task Startup_Failure_Does_Not_Persist_The_New_State()
    {
        var store = new MemorySettingsStore();
        var registry = new FakeStartupRegistry { ThrowOnEnable = true };
        await using var window = new AvaloniaMainWindow();
        window.EnableSettings(store, registry);

        window.ToggleStartAtLogin();

        Assert.False(window.StartAtLogin);
        Assert.False(store.Settings.StartAtLogin);
        Assert.Contains("自動起動を変更できません", window.SettingsMessage);
    }

    private sealed class FakeStartupRegistry : IStartupRegistry
    {
        public int EnableCalls { get; private set; }
        public int DisableCalls { get; private set; }
        public bool ThrowOnEnable { get; init; }
        public bool IsEnabled(string executablePath) => EnableCalls > DisableCalls;
        public void Enable(string executablePath)
        {
            if (ThrowOnEnable) throw new IOException("test");
            EnableCalls++;
        }
        public void Disable() => DisableCalls++;
    }
}
