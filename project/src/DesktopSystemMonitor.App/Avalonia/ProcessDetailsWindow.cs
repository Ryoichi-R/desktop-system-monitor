using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using DesktopSystemMonitor.Core.Platform;

namespace DesktopSystemMonitor.App;

internal sealed class ProcessDetailsWindow : Window
{
    internal TextBlock Status { get; } = new();
    internal StackPanel Rows { get; } = new() { Spacing = 8 };

    internal ProcessDetailsWindow()
    {
        Title = "高負荷プロセス";
        Width = 700; Height = 460; MinWidth = 620; MinHeight = 320;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = new DockPanel
        {
            Margin = new Thickness(20),
            Children =
            {
                Header(),
                new ScrollViewer { Content = Rows, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }
            }
        };
        Update(SensorHostMetricGroups.Unavailable<SensorHostProcessReading>());
    }

    private StackPanel Header()
    {
        var panel = new StackPanel { Spacing = 10, Margin = new Thickness(0, 0, 0, 16) };
        panel.Children.Add(new TextBlock { Text = "CPU使用率の上位10件（取得可能なプロセス）", FontSize = 18 });
        panel.Children.Add(new TextBlock { Text = "CPUはMac全体を100%。アクティビティモニタの1コア=100%とは基準が異なります。", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        panel.Children.Add(Status);
        panel.Children.Add(Row("プロセス（PID）", "CPU", "読込", "書込"));
        DockPanel.SetDock(panel, Dock.Top);
        return panel;
    }

    internal void Update(SensorHostMetricGroup<SensorHostProcessReading> group)
    {
        Rows.Children.Clear();
        Status.Text = group.Status switch
        {
            SensorHostMetricStatus.Ok => group.Values.Length == 0 ? "比較できるプロセスがありません" : "1秒周期で更新中。終了直後・権限不足のプロセスは表示されません。",
            SensorHostMetricStatus.WarmingUp => "計測準備中",
            _ => "取得できません（N/A）",
        };
        if (group.Status != SensorHostMetricStatus.Ok) return;
        foreach (var process in group.Values.Take(10))
        {
            string cpu = process.CpuUtilizationPercent.Status == SensorHostMetricStatus.Ok && process.CpuUtilizationPercent.Value is double value && double.IsFinite(value) && value is >= 0 and <= 100
                ? $"{value:0.0}%" : "N/A";
            Rows.Children.Add(Row($"{process.Name} ({process.ProcessId})", cpu, DiskDetailsWindow.Format(process.IoReadBytesPerSecond), DiskDetailsWindow.Format(process.IoWriteBytesPerSecond)));
        }
    }

    private static Grid Row(string name, string cpu, string read, string write)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,70,120,120") };
        string[] values = [name, cpu, read, write];
        for (int i = 0; i < values.Length; i++)
        {
            var text = new TextBlock { Text = values[i], TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 8, 0) };
            Grid.SetColumn(text, i);
            grid.Children.Add(text);
        }
        return grid;
    }
}
