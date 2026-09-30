using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace DesktopSystemMonitor.App;

internal sealed class ScaleInputWindow : Window
{
    internal TextBox PercentInput { get; }
    internal TextBlock ValidationMessage { get; }
    private readonly Action<double> _apply;

    internal ScaleInputWindow(double scale, Action<double> apply)
    {
        _apply = apply;
        Title = "表示倍率";
        Width = 340;
        Height = 200;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        PercentInput = new TextBox { Text = (scale * 100).ToString("0", CultureInfo.InvariantCulture), Name = "PercentInput" };
        ValidationMessage = new TextBlock { Text = "50〜150の整数を入力してください（1%単位）", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var applyButton = new Button { Content = "適用", IsDefault = true };
        applyButton.Click += (_, _) => ApplyInput();
        var cancel = new Button { Content = "キャンセル", IsCancel = true };
        cancel.Click += (_, _) => Close();
        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "表示倍率（%）" }, PercentInput, ValidationMessage,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { applyButton, cancel } }
            }
        };
        Opened += (_, _) => { PercentInput.Focus(); PercentInput.SelectAll(); };
    }

    internal bool ApplyInput()
    {
        if (!int.TryParse(PercentInput.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int percent)
            || percent is < 50 or > 150)
        {
            ValidationMessage.Text = "50〜150の整数を入力してください。変更は未適用です。";
            return false;
        }
        _apply(percent / 100d);
        Close();
        return true;
    }
}
