using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using DesktopSystemMonitor.Core.Settings;

namespace DesktopSystemMonitor.App;

internal sealed class MacSettingsWindow : Window
{
    private readonly AvaloniaMainWindow _monitor;
    private bool _refreshing;
    internal NumericUpDown ScaleInput { get; } = Number(50, 150);
    internal NumericUpDown CpuPeakInput { get; } = Number(10, 60);
    internal NumericUpDown NetworkPeakInput { get; } = Number(10, 60);
    internal ComboBox DisplayModeInput { get; } = new() { ItemsSource = new[] { "通常", "コンパクト" }, MinWidth = 210 };
    internal ComboBox LayerInput { get; } = new() { ItemsSource = new[] { "通常", "常に手前に表示", "デスクトップ最背面" }, MinWidth = 210 };
    internal CheckBox ClickThroughInput { get; } = new() { Content = "モニターのクリックを透過する" };
    internal CheckBox FullScreenInput { get; } = new() { Content = "全画面表示中はモニターを隠す" };
    internal CheckBox StartupInput { get; } = new() { Content = "ログイン時に自動起動する" };
    internal CheckBox DiagnosticsInput { get; } = new() { Content = "診断ログを記録する" };
    internal TextBlock StatusText { get; } = new() { TextWrapping = TextWrapping.Wrap, MaxWidth = 450, VerticalAlignment = VerticalAlignment.Center };

    internal MacSettingsWindow(AvaloniaMainWindow monitor)
    {
        _monitor = monitor;
        Title = "Desktop System Monitor 設定";
        Width = 720;
        Height = 540;
        MinWidth = 660;
        MinHeight = 460;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var close = new Button { Content = "閉じる", IsCancel = true, MinWidth = 88, HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close();
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 16, 0, 0) };
        footer.Children.Add(StatusText);
        Grid.SetColumn(close, 1);
        footer.Children.Add(close);
        DockPanel.SetDock(footer, Dock.Bottom);
        var tabs = new TabControl
        {
            TabStripPlacement = Dock.Left,
            ItemsSource = new[]
            {
                Category("表示", Field("表示倍率（%）", ScaleInput), Hint("50〜150%を1%単位で変更できます。"),
                    Field("表示形式", DisplayModeInput), Field("表示階層", LayerInput), ClickThroughInput,
                    Hint("位置を変えるにはクリック透過を解除し、モニターをドラッグします。")),
                Category("動作", FullScreenInput, StartupInput,
                    Hint("全画面中もメニューバーの「モニターを表示」から再表示できます。")),
                Category("計測", Field("CPU最大値の集計期間（秒）", CpuPeakInput),
                    Field("ネットワーク最大値の集計期間（秒）", NetworkPeakInput),
                    Hint("直近10〜60秒の最大値を表示します。計測は1秒ごとに更新されます。")),
                Category("診断", DiagnosticsInput, Hint("問題の調査が必要なときに有効にしてください。"))
            }
        };
        Content = new DockPanel { Margin = new Thickness(20), Children = { footer, tabs } };
        Refresh();
        ScaleInput.ValueChanged += (_, _) => Change(() => { if (ScaleInput.Value is decimal value) _monitor.SetWidgetScale((double)value / 100); });
        CpuPeakInput.ValueChanged += (_, _) => Change(() => { if (CpuPeakInput.Value is decimal value) _monitor.SetCpuPeakWindow((int)value); });
        NetworkPeakInput.ValueChanged += (_, _) => Change(() => { if (NetworkPeakInput.Value is decimal value) _monitor.SetNetworkPeakWindow((int)value); });
        DisplayModeInput.SelectionChanged += (_, _) => Change(() => _monitor.SetDisplayMode(DisplayModeInput.SelectedIndex == 1 ? WidgetDisplayMode.Reduced : WidgetDisplayMode.Standard));
        LayerInput.SelectionChanged += (_, _) => Change(() =>
        {
            switch (LayerInput.SelectedIndex)
            {
                case 1: if (!_monitor.Topmost) _monitor.ToggleTopmost(); break;
                case 2: if (!_monitor.DesktopLayer) _monitor.ToggleDesktopLayer(); break;
                case 0:
                    if (_monitor.DesktopLayer) _monitor.ToggleDesktopLayer();
                    else if (_monitor.Topmost) _monitor.ToggleTopmost();
                    break;
            }
        });
        ClickThroughInput.Click += (_, _) => Change(_monitor.ToggleClickThrough);
        FullScreenInput.Click += (_, _) => Change(_monitor.ToggleFullScreenHiding);
        StartupInput.Click += (_, _) => Change(_monitor.ToggleStartAtLogin);
        DiagnosticsInput.Click += (_, _) => Change(_monitor.ToggleDiagnosticLogging);
        _monitor.WidgetSettingsChanged += OnSettingsChanged;
        Closed += (_, _) => _monitor.WidgetSettingsChanged -= OnSettingsChanged;
    }

    private void Change(Action change)
    {
        if (_refreshing) return;
        change();
        Refresh();
    }

    private void OnSettingsChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        _refreshing = true;
        try
        {
            ScaleInput.Value = (decimal)Math.Round(_monitor.WidgetScale * 100);
            DisplayModeInput.SelectedIndex = _monitor.DisplayMode == WidgetDisplayMode.Reduced ? 1 : 0;
            LayerInput.SelectedIndex = _monitor.DesktopLayer ? 2 : _monitor.Topmost ? 1 : 0;
            ClickThroughInput.IsChecked = _monitor.ClickThrough;
            FullScreenInput.IsChecked = _monitor.HideWhenFullScreen;
            StartupInput.IsChecked = _monitor.StartAtLogin;
            DiagnosticsInput.IsChecked = _monitor.DiagnosticLogging;
            CpuPeakInput.Value = _monitor.CpuPeakWindowSeconds;
            NetworkPeakInput.Value = _monitor.NetworkPeakWindowSeconds;
            StatusText.Text = _monitor.SettingsMessage;
        }
        finally { _refreshing = false; }
    }

    private static NumericUpDown Number(int minimum, int maximum) => new()
    {
        Minimum = minimum,
        Maximum = maximum,
        Increment = 1,
        FormatString = "0",
        AllowSpin = true,
        ClipValueToMinMax = true,
        MinWidth = 120,
    };

    private static TextBlock Hint(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.7 };

    private static StackPanel Field(string label, Control input)
    {
        Avalonia.Automation.AutomationProperties.SetName(input, label);
        return new StackPanel { Spacing = 6, Children = { new TextBlock { Text = label }, input } };
    }

    private static TabItem Category(string title, params Control[] controls)
    {
        var content = new StackPanel { Margin = new Thickness(24, 4, 8, 12), Spacing = 14 };
        content.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeight.SemiBold });
        foreach (var control in controls) content.Children.Add(control);
        return new TabItem { Header = title, MinWidth = 108, Padding = new Thickness(16, 12), Content = new ScrollViewer { Content = content } };
    }
}
