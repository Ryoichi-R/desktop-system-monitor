using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;

using DesktopSystemMonitor.Avalonia.PoC;

using Xunit;

namespace DesktopSystemMonitor.Avalonia.PoC.Tests;

public sealed class MainWindowHeadlessTests
{
    [AvaloniaFact]
    public void Layout_Uses_Minimum_Size_And_Shows_Controls()
    {
        var window = new MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(320, window.MinWidth);
        Assert.Equal(200, window.MinHeight);
        Assert.NotNull(window.FindControl<TextBox>("SettingsTextBox"));
        Assert.NotNull(window.FindControl<Button>("ApplyButton"));
    }

    [AvaloniaFact]
    public void Settings_Input_And_Button_Click_Update_Status()
    {
        var window = new MainWindow();
        window.Show();
        var textBox = window.FindControl<TextBox>("SettingsTextBox")!;
        var button = window.FindControl<Button>("ApplyButton")!;
        textBox.Text = "コンパクト";

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal("適用済み: コンパクト", window.FindControl<TextBlock>("AppliedText")!.Text);
    }

    [AvaloniaFact]
    public void Window_State_Toggles_Between_Normal_And_Maximized()
    {
        var window = new MainWindow();
        window.Show();
        var button = window.FindControl<Button>("ToggleStateButton")!;

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(WindowState.Maximized, window.WindowState);

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(WindowState.Normal, window.WindowState);
    }
}
