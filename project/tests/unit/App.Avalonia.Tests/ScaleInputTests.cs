using Avalonia.Headless.XUnit;
using DesktopSystemMonitor.App;
using Xunit;

namespace DesktopSystemMonitor.App.Avalonia.Tests;

public sealed class ScaleInputTests
{
    [AvaloniaFact]
    public async Task All_Integer_Percentages_Resize_And_Restore()
    {
        var store = new MemorySettingsStore();
        await using var window = new AvaloniaMainWindow();
        window.EnableSettings(store);
        for (int percent = 50; percent <= 150; percent++)
        {
            var dialog = new ScaleInputWindow(window.WidgetScale, window.SetWidgetScale);
            dialog.PercentInput.Text = percent.ToString();
            Assert.True(dialog.ApplyInput());
            Assert.Equal(percent / 100d, window.WidgetScale);
            Assert.Equal(280 * percent / 100d, window.Width, 8);
            Assert.Equal(percent / 100d, store.Settings.Normalize().Scale);
        }
        window.SetWidgetScale(0.87);
        await using var restored = new AvaloniaMainWindow();
        restored.EnableSettings(store);
        Assert.Equal(0.87, restored.WidgetScale);
    }

    [AvaloniaFact]
    public void Invalid_Input_And_Cancel_Do_Not_Apply()
    {
        double applied = 1;
        var dialog = new ScaleInputWindow(applied, value => applied = value);
        foreach (string input in new[] { "", "49", "151", "87.5", "abc", "NaN", "999999999999" })
        {
            dialog.PercentInput.Text = input;
            Assert.False(dialog.ApplyInput());
            Assert.Equal(1, applied);
        }
        dialog.PercentInput.Text = "87";
        dialog.Close();
        Assert.Equal(1, applied);
        Assert.Equal(1, new MacWidgetSettings { Scale = double.NaN }.Normalize().Scale);
        Assert.Equal(0.88, new MacWidgetSettings { Scale = 0.876 }.Normalize().Scale);
    }
}
