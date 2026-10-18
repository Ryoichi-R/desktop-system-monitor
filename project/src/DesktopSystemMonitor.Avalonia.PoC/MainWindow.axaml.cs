using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DesktopSystemMonitor.Avalonia.PoC;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnApplyClick(object? sender, RoutedEventArgs e)
    {
        AppliedText.Text = $"適用済み: {SettingsTextBox.Text ?? string.Empty}";
    }

    private void OnToggleStateClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }
}
