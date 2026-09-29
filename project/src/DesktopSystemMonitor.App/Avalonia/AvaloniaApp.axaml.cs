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
                var desktopLayer = new NativeMenuItem("デスクトップ最背面") { ToggleType = MenuItemToggleType.CheckBox, IsChecked = window.DesktopLayer };
                desktopLayer.Click += (_, _) => window.ToggleDesktopLayer();
                var clickThrough = new NativeMenuItem("クリックを透過") { ToggleType = MenuItemToggleType.CheckBox, IsChecked = window.ClickThrough };
                clickThrough.Click += (_, _) => window.ToggleClickThrough();
                var fullScreenHide = new NativeMenuItem("全画面時に自動で隠す") { ToggleType = MenuItemToggleType.CheckBox, IsChecked = window.HideWhenFullScreen };
                fullScreenHide.Click += (_, _) => window.ToggleFullScreenHiding();
                window.WidgetSettingsChanged += (_, _) =>
                {
                    fullScreenHide.IsChecked = window.HideWhenFullScreen;
                    topmost.IsChecked = window.Topmost;
                    desktopLayer.IsChecked = window.DesktopLayer;
                    clickThrough.IsChecked = window.ClickThrough;
                    foreach (var entry in scaleItems) entry.Item.IsChecked = window.WidgetScale == entry.Scale;
                    standardMode.IsChecked = window.DisplayMode == WidgetDisplayMode.Standard;
                    reducedMode.IsChecked = window.DisplayMode == WidgetDisplayMode.Reduced;
                };
                var quit = new NativeMenuItem("終了");
                quit.Click += (_, _) => desktop.Shutdown();
                var menu = new NativeMenu();
                var settings = new NativeMenuItem("設定…");
                settings.Click += (_, _) => window.ShowSettings();
                var diskDetails = new NativeMenuItem("ディスクの読み書き…");
                diskDetails.Click += (_, _) => window.ShowDiskDetails();
                var processes = new NativeMenuItem("高負荷プロセス…");
                processes.Click += (_, _) => window.ShowProcessDetails();
                var appearance = new NativeMenuItem("表示") { Menu = new NativeMenu() };
                foreach (var item in new[] { scale, displayMode, topmost, desktopLayer, clickThrough, fullScreenHide })
                    appearance.Menu.Items.Add(item);
                menu.Items.Add(show);
                menu.Items.Add(appearance);
                menu.Items.Add(new NativeMenuItemSeparator());
                menu.Items.Add(diskDetails);
                menu.Items.Add(processes);
                menu.Items.Add(new NativeMenuItemSeparator());
                menu.Items.Add(settings);
                menu.Items.Add(new NativeMenuItemSeparator());
                menu.Items.Add(quit);
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
