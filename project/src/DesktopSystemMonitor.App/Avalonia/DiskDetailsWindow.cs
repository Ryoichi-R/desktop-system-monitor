using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using DesktopSystemMonitor.Core.Platform;

namespace DesktopSystemMonitor.App;

internal sealed class DiskDetailsWindow : Window
{
    internal TextBlock ReadRate { get; } = new() { Name = "DiskReadRate", FontSize = 20 };
    internal TextBlock WriteRate { get; } = new() { Name = "DiskWriteRate", FontSize = 20 };

    internal DiskDetailsWindow()
    {
        Title = "ディスクの読み書き";
        Width = 380;
        Height = 250;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var close = new Button { Content = "閉じる", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close();
        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "物理ディスクの読み書き速度（合計）" },
                ReadRate, WriteRate,
                new TextBlock { Text = "内蔵・外付けを含みます。ディスクの使用率（稼働時間の割合）と空き容量は未対応です。", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                close
            }
        };
        Update(SensorHostMetrics.Empty);
    }

    internal void Update(SensorHostMetrics metrics)
    {
        ReadRate.Text = "読み込み  " + Format(metrics.DiskReadBytesPerSecond);
        WriteRate.Text = "書き込み  " + Format(metrics.DiskWriteBytesPerSecond);
    }

    internal static string Format(SensorHostMetricValue metric)
    {
        if (metric.Status == SensorHostMetricStatus.WarmingUp) return "計測準備中";
        if (metric.Status != SensorHostMetricStatus.Ok || metric.Value is not double rate || !double.IsFinite(rate) || rate < 0) return "N/A";
        return rate >= 1_000_000_000 ? $"{rate / 1_000_000_000:0.00} GB/s"
            : rate >= 1_000_000 ? $"{rate / 1_000_000:0.00} MB/s"
            : $"{rate / 1000:0.00} KB/s";
    }
}
