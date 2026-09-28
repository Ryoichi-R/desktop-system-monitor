using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Controls;
using DesktopSystemMonitor.Core.Platform;
using DesktopSystemMonitor.Core.Settings;
using DesktopSystemMonitor.Mac;

namespace DesktopSystemMonitor.App;

public sealed class AvaloniaApp : Application
{
    internal Func<IMacWidgetSettingsStore> SettingsStoreFactory { get; set; } = MacWidgetSettingsStore.CreateDefault;
    internal Func<IStartupRegistry> StartupRegistryFactory { get; set; } = () => new MacStartupRegistry();

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new AvaloniaMainWindow();
            desktop.MainWindow = window;
            if (OperatingSystem.IsMacOS())
            {
                window.EnableSettings(SettingsStoreFactory(), StartupRegistryFactory());
                Program.Diagnostics.Record(MacDiagnosticEvent.Started);
                window.StartLiveMonitoring();
                var show = new NativeMenuItem("モニターを表示");
                show.Click += (_, _) => window.ShowMonitor();
                var topmost = new NativeMenuItem("常に手前に表示") { ToggleType = MenuItemToggleType.CheckBox, IsChecked = window.Topmost };
                topmost.Click += (_, _) => window.ToggleTopmost();
                var scale = new NativeMenuItem("表示倍率") { Menu = new NativeMenu() };
                var customScale = new NativeMenuItem("倍率を入力…（50〜150%）");
                customScale.Click += (_, _) => window.ShowScaleInput();
                scale.Menu.Items.Add(customScale);
                var scaleItems = new List<(NativeMenuItem Item, double Scale)>();
                foreach (double value in new[] { 0.5, 0.75, 1d, 1.25, 1.5 })
                {
                    var item = new NativeMenuItem($"{value * 100:0}%") { ToggleType = MenuItemToggleType.Radio, IsChecked = window.WidgetScale == value };
                    item.Click += (_, _) => window.SetWidgetScale(value);
                    scale.Menu.Items.Add(item);
                    scaleItems.Add((item, value));
                }
                var displayMode = new NativeMenuItem("表示形式") { Menu = new NativeMenu() };
                var standardMode = new NativeMenuItem("通常版（280 DIP）") { ToggleType = MenuItemToggleType.Radio, IsChecked = window.DisplayMode == WidgetDisplayMode.Standard };
                var reducedMode = new NativeMenuItem("縮小版（150 DIP）") { ToggleType = MenuItemToggleType.Radio, IsChecked = window.DisplayMode == WidgetDisplayMode.Reduced };
                standardMode.Click += (_, _) => window.SetDisplayMode(WidgetDisplayMode.Standard);
                reducedMode.Click += (_, _) => window.SetDisplayMode(WidgetDisplayMode.Reduced);
                displayMode.Menu.Items.Add(standardMode);
                displayMode.Menu.Items.Add(reducedMode);
                var peakWindow = new NativeMenuItem("ネットワーク最大値の集計期間") { Menu = new NativeMenu() };
                var peakItems = new List<(NativeMenuItem Item, int Seconds)>();
                foreach (int seconds in new[] { 10, 20, 30, 60 })
                {
                    var item = new NativeMenuItem($"過去{seconds}秒") { ToggleType = MenuItemToggleType.Radio, IsChecked = window.NetworkPeakWindowSeconds == seconds };
                    item.Click += (_, _) => window.SetNetworkPeakWindow(seconds);
                    peakWindow.Menu.Items.Add(item);
                    peakItems.Add((item, seconds));
                }
                var cpuPeakWindow = new NativeMenuItem("CPU最大値の集計期間") { Menu = new NativeMenu() };
                var cpuPeakItems = new List<(NativeMenuItem Item, int Seconds)>();
                foreach (int seconds in new[] { 10, 20, 30, 60 })
                {
                    var item = new NativeMenuItem($"過去{seconds}秒") { ToggleType = MenuItemToggleType.Radio, IsChecked = window.CpuPeakWindowSeconds == seconds };
                    item.Click += (_, _) => window.SetCpuPeakWindow(seconds);
                    cpuPeakWindow.Menu.Items.Add(item);
                    cpuPeakItems.Add((item, seconds));
                }
                var settingsStatus = new NativeMenuItem(window.SettingsMessage) { IsEnabled = false };
                var desktopLayer = new NativeMenuItem("デスクトップ最背面") { ToggleType = MenuItemToggleType.CheckBox, IsChecked = window.DesktopLayer };
                desktopLayer.Click += (_, _) => window.ToggleDesktopLayer();
                var clickThrough = new NativeMenuItem("クリックを透過") { ToggleType = MenuItemToggleType.CheckBox, IsChecked = window.ClickThrough };
                clickThrough.Click += (_, _) => window.ToggleClickThrough();
                var startAtLogin = new NativeMenuItem("ログイン時に自動起動") { ToggleType = MenuItemToggleType.CheckBox, IsChecked = window.StartAtLogin };
                startAtLogin.Click += (_, _) => window.ToggleStartAtLogin();
                var fullScreenHide = new NativeMenuItem("全画面時に自動で隠す") { ToggleType = MenuItemToggleType.CheckBox, IsChecked = window.HideWhenFullScreen };
                fullScreenHide.Click += (_, _) => window.ToggleFullScreenHiding();
                var diagnosticLogging = new NativeMenuItem("診断ログを記録") { ToggleType = MenuItemToggleType.CheckBox, IsChecked = window.DiagnosticLogging };
                diagnosticLogging.Click += (_, _) => window.ToggleDiagnosticLogging();
                window.WidgetSettingsChanged += (_, _) =>
                {
                    diagnosticLogging.IsChecked = window.DiagnosticLogging;
                    fullScreenHide.IsChecked = window.HideWhenFullScreen;
                    topmost.IsChecked = window.Topmost;
                    desktopLayer.IsChecked = window.DesktopLayer;
                    clickThrough.IsChecked = window.ClickThrough;
                    startAtLogin.IsChecked = window.StartAtLogin;
                    foreach (var entry in scaleItems) entry.Item.IsChecked = window.WidgetScale == entry.Scale;
                    standardMode.IsChecked = window.DisplayMode == WidgetDisplayMode.Standard;
                    reducedMode.IsChecked = window.DisplayMode == WidgetDisplayMode.Reduced;
                    foreach (var entry in peakItems) entry.Item.IsChecked = window.NetworkPeakWindowSeconds == entry.Seconds;
                    foreach (var entry in cpuPeakItems) entry.Item.IsChecked = window.CpuPeakWindowSeconds == entry.Seconds;
                    settingsStatus.Header = window.SettingsMessage;
                };
                var quit = new NativeMenuItem("終了");
                quit.Click += (_, _) => desktop.Shutdown();
                var menu = new NativeMenu();
                menu.Items.Add(show);
                menu.Items.Add(scale);
                menu.Items.Add(displayMode);
                menu.Items.Add(topmost);
                menu.Items.Add(settingsStatus);
                menu.Items.Add(desktopLayer);
                menu.Items.Add(clickThrough);
                menu.Items.Add(startAtLogin);
                menu.Items.Add(quit);
                menu.Items.Add(peakWindow);
                menu.Items.Add(cpuPeakWindow);
                var diskDetails = new NativeMenuItem("ディスクの読み書き…");
                diskDetails.Click += (_, _) => window.ShowDiskDetails();
                menu.Items.Add(diskDetails);
                var processes = new NativeMenuItem("高負荷プロセス…");
                processes.Click += (_, _) => window.ShowProcessDetails();
                menu.Items.Add(processes);
                menu.Items.Add(fullScreenHide);
                menu.Items.Add(diagnosticLogging);
                using Stream icon = typeof(AvaloniaApp).Assembly.GetManifestResourceStream("DesktopSystemMonitor.App.MacTray.ico")!;
                TrayIcon.SetIcons(this, new TrayIcons { new TrayIcon { Icon = new WindowIcon(icon), ToolTipText = "Desktop System Monitor", Menu = menu, IsVisible = true } });
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<AvaloniaApp>()
        .UsePlatformDetect()
        .With(new MacOSPlatformOptions { ShowInDock = false })
        .LogToTrace();
}
