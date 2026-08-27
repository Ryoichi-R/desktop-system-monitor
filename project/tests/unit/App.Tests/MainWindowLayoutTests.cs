using System;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DesktopSystemMonitor.Core.Formatting;
using DesktopSystemMonitor.Core.Metrics;
using DesktopSystemMonitor.Core.Settings;
using DesktopSystemMonitor.Windows.Window;
using System.Xml.Linq;
using Xunit;
using Rectangle = System.Windows.Shapes.Rectangle;
using WidgetHeightCalculator = DesktopSystemMonitor.Core.Layout.WidgetHeightCalculator;

namespace DesktopSystemMonitor.App.Tests;

[Collection("WpfWindow")]
public sealed class MainWindowLayoutTests
{
    [ThreadStatic]
    private static System.Collections.Generic.List<Window> testWindows;

    [Fact]
    public void window_is_a_transparent_overlay()
    {
        XElement window = LoadWindow();
        XNamespace presentation = window.Name.Namespace;
        XElement root = FindNamedElement(window, "Border", "WidgetRoot");

        Assert.Equal("280", window.Attribute("Width")?.Value);
        Assert.Equal("220", window.Attribute("Height")?.Value);
        Assert.Equal("True", window.Attribute("AllowsTransparency")?.Value);
        Assert.Equal("Transparent", window.Attribute("Background")?.Value);
        Assert.Equal("1", window.Attribute("Opacity")?.Value);
        Assert.Equal("Transparent", root.Attribute("Background")?.Value);
        Assert.Null(root.Attribute("BorderBrush"));
        Assert.Empty(window.Descendants(presentation + "DropShadowEffect"));
    }

    [Fact]
    public void metric_rows_use_separate_units_and_fill_only_bars()
    {
        XElement window = LoadWindow();
        XNamespace presentation = window.Name.Namespace;
        XElement standard = FindStandardPanel(window);

        Assert.Equal(["CPU", "MEM", "GPU", "DISK", "NET", "BAT"], FindMetricLabels(window));

        string[] primaryBindings = standard.Descendants()
            .Select(block => block.Attribute("Text")?.Value)
            .Where(value => value is not null)
            .Cast<string>()
            .ToArray();
        Assert.Contains("{Binding CpuValue}", primaryBindings);
        Assert.Contains("{Binding CpuUnit}", primaryBindings);
        Assert.Contains("{Binding MemoryValue}", primaryBindings);
        Assert.Contains("{Binding MemoryUnit}", primaryBindings);
        Assert.Contains("{Binding GpuValue}", primaryBindings);
        Assert.Contains("{Binding GpuUnit}", primaryBindings);
        Assert.Contains("{Binding CpuPower}", primaryBindings);
        Assert.Contains("{Binding GpuPower}", primaryBindings);

        XElement[] valueBlocks = standard.Descendants()
            .Where(block => block.Attribute("Text")?.Value is "{Binding CpuValue}" or "{Binding MemoryValue}" or "{Binding GpuValue}")
            .ToArray();
        Assert.Equal(3, valueBlocks.Length);
        Assert.All(valueBlocks, block => Assert.Equal("Tabular", block.Attribute("Typography.NumeralAlignment")?.Value));
        Assert.All(valueBlocks, block => Assert.Equal("24", block.Attribute("FontSize")?.Value));
        XElement[] unitBlocks = standard.Descendants()
            .Where(block => block.Attribute("Text")?.Value is "{Binding CpuUnit}" or "{Binding MemoryUnit}" or "{Binding GpuUnit}")
            .ToArray();
        Assert.All(unitBlocks, block => Assert.Equal("13", block.Attribute("FontSize")?.Value));
        Assert.All(unitBlocks, block => Assert.Equal("{DynamicResource WidgetMuted}", block.Attribute("Foreground")?.Value));

        XElement[] bars = standard.Descendants(presentation + "Rectangle")
            .Where(bar => bar.Descendants(presentation + "ScaleTransform").Any())
            .Where(bar => bar.Descendants(presentation + "ScaleTransform").Single().Attribute("ScaleX")?.Value
                is "{Binding CpuBarFraction}" or "{Binding MemoryBarFraction}" or "{Binding GpuBarFraction}")
            .ToArray();
        Assert.Equal(3, bars.Length);
        Assert.All(bars, bar => Assert.Equal("{DynamicResource WidgetAccent}", bar.Attribute("Fill")?.Value));
        string[] scaleBindings = bars
            .Select(bar => bar.Descendants(presentation + "ScaleTransform").Single().Attribute("ScaleX")?.Value)
            .Where(value => value is not null)
            .Cast<string>()
            .ToArray();
        Assert.Equal(
            ["{Binding CpuBarFraction}", "{Binding MemoryBarFraction}", "{Binding GpuBarFraction}"],
            scaleBindings);
        Assert.All(
            standard.Descendants(presentation + "Border"),
            border => Assert.Null(border.Attribute("BorderBrush")));
    }

    [Fact]
    public void metric_groups_have_equal_heights_and_spacing()
    {
        XElement window = LoadWindow();
        XNamespace presentation = window.Name.Namespace;
        XElement border = FindNamedElement(window, "Border", "WidgetRoot");
        XElement stack = FindStandardPanel(window);
        XElement[] groups = stack.Elements(presentation + "Grid").ToArray();
        Assert.Equal(6, groups.Length);
        Assert.All(groups, group => Assert.Equal("48", group.Attribute("Height")?.Value));
        Assert.Equal("0,4,0,0", groups[1].Attribute("Margin")?.Value);
        Assert.Equal("8", border.Attribute("Padding")?.Value);
        foreach (string label in new[] { "CPU", "MEM", "GPU", "DISK", "NET", "BAT" })
        {
            XElement group = FindOuterRowElement(window, label);
            string[] innerRowHeights = group.Element(presentation + "Grid.RowDefinitions")!
                .Elements(presentation + "RowDefinition")
                .Select(row => row.Attribute("Height")?.Value ?? string.Empty)
                .ToArray();
            XElement labelBlock = group.Descendants(presentation + "TextBlock")
                .Single(block => block.Attribute("Text")?.Value == label);

            Assert.Equal(["29", "14", "0", "5"], innerRowHeights);
            Assert.Equal("0", labelBlock.Attribute("Grid.Row")?.Value ?? "0");
        }
    }

    [Fact]
    public void network_arrows_use_direction_specific_brushes()
    {
        XElement window = LoadWindow();
        XNamespace presentation = window.Name.Namespace;
        XElement network = FindOuterRowElement(window, "NET");
        XElement rx = network.Descendants(presentation + "Run").Single(element => element.Attribute("Text")?.Value?.StartsWith("▼", StringComparison.Ordinal) == true);
        XElement tx = network.Descendants(presentation + "Run").Single(element => element.Attribute("Text")?.Value?.StartsWith("▲", StringComparison.Ordinal) == true);

        Assert.Equal("{DynamicResource WidgetRxAccent}", rx.Attribute("Foreground")?.Value);
        Assert.Equal("{DynamicResource WidgetTxAccent}", tx.Attribute("Foreground")?.Value);
    }

    [Fact]
    public void reduced_panel_keeps_only_primary_values_and_vertical_network_rates()
    {
        XElement window = LoadWindow();
        XNamespace presentation = window.Name.Namespace;
        XElement reduced = FindNamedElement(window, "StackPanel", "ReducedContentPanel");
        XElement[] rows = reduced.Elements(presentation + "Grid").ToArray();

        Assert.Equal(6, rows.Length);
        Assert.Equal(["CPU", "MEM", "GPU", "DISK", "NET", "BAT"],
            rows.Select(row => row.Descendants(presentation + "TextBlock")
                .First(text => text.Attribute("Text")?.Value is "CPU" or "MEM" or "GPU" or "DISK" or "NET" or "BAT")
                .Attribute("Text")!.Value).ToArray());
        Assert.All(rows, row => Assert.Equal(["29", "14", "0", "5"],
            row.Element(presentation + "Grid.RowDefinitions")!
                .Elements(presentation + "RowDefinition")
                .Select(definition => definition.Attribute("Height")?.Value).ToArray()));

        string[] reducedBindings = reduced.Descendants()
            .Select(element => element.Attribute("Text")?.Value)
            .Where(value => value is not null)
            .Cast<string>()
            .ToArray();
        Assert.DoesNotContain("{Binding CpuFrequency}", reducedBindings);
        Assert.DoesNotContain("{Binding CpuTemperature}", reducedBindings);
        Assert.DoesNotContain("{Binding CpuPower}", reducedBindings);
        Assert.DoesNotContain("{Binding MemoryUsage}", reducedBindings);
        Assert.DoesNotContain("{Binding GpuMemory}", reducedBindings);
        Assert.DoesNotContain("{Binding DiskRead}", reducedBindings);
        Assert.Contains("{Binding NetworkPeakRx}", reducedBindings);
        Assert.Contains("{Binding NetworkPeakTx}", reducedBindings);
        Assert.Contains("{Binding NetworkPeakLabel}", reducedBindings);
        Assert.DoesNotContain("{Binding BatterySecondary}", reducedBindings);
        Assert.Contains("{Binding BatteryFlowMarker}", reducedBindings);

        XElement rx = FindNamedElement(reduced, "TextBlock", "ReducedNetworkReceiveRate");
        XElement tx = FindNamedElement(reduced, "TextBlock", "ReducedNetworkSendRate");
        Assert.Equal("13", rx.Attribute("FontSize")?.Value);
        Assert.Equal("13", tx.Attribute("FontSize")?.Value);
        Assert.Equal("Bottom", rx.Attribute("VerticalAlignment")?.Value);
        Assert.Equal("Top", tx.Attribute("VerticalAlignment")?.Value);
        XElement peakRx = FindNamedElement(reduced, "TextBlock", "ReducedNetworkPeakReceiveRate");
        XElement peakTx = FindNamedElement(reduced, "TextBlock", "ReducedNetworkPeakSendRate");
        XElement peakPanel = FindNamedElement(reduced, "Grid", "ReducedNetworkPeakPanel");
        XElement peakLabel = FindNamedElement(reduced, "TextBlock", "ReducedNetworkPeakLabel");
        Assert.Equal("2", peakPanel.Attribute("Grid.Row")?.Value);
        Assert.Equal("2", peakPanel.Attribute("Grid.RowSpan")?.Value);
        Assert.Equal("0", peakRx.Attribute("Grid.Row")?.Value);
        Assert.Equal("1", peakTx.Attribute("Grid.Row")?.Value);
        Assert.Equal("0", peakLabel.Attribute("Grid.Row")?.Value);
        Assert.Null(peakLabel.Attribute("Grid.RowSpan"));
        Assert.Equal("Right", peakLabel.Attribute("TextAlignment")?.Value);
        Assert.Equal("Collapsed", peakRx.Attribute("Visibility")?.Value);
        Assert.Equal("Collapsed", peakTx.Attribute("Visibility")?.Value);
        Assert.Single(
            reduced.Descendants(presentation + "TextBlock"),
            block => block.Attribute("Text")?.Value == "{Binding NetworkPeakLabel}");
        foreach (string arrowName in new[]
                 {
                     "ReducedNetworkReceiveArrow",
                     "ReducedNetworkSendArrow",
                     "ReducedNetworkPeakReceiveArrow",
                     "ReducedNetworkPeakSendArrow",
                 })
        {
            XElement arrow = FindNamedElement(reduced, "TextBlock", arrowName);
            Assert.Equal("1", arrow.Attribute("Grid.Column")?.Value);
        }
        foreach (string barHostName in new[]
                 {
                     "ReducedCpuBarHost",
                     "ReducedMemoryBarHost",
                     "ReducedGpuBarHost",
                     "ReducedDiskBarHost",
                 })
        {
            XElement barHost = FindNamedElement(reduced, "Grid", barHostName);
            Assert.Equal("2", barHost.Attribute("Grid.ColumnSpan")?.Value);
            Assert.Null(barHost.Attribute("Grid.Column"));
        }
    }

    [Theory]
    [InlineData(75)]
    [InlineData(100)]
    [InlineData(125)]
    [InlineData(150)]
    [InlineData(200)]
    public void visible_elements_remain_inside_window_at_each_scale(double scalePercent)
    {
        RunInSta(() =>
        {
            var window = new MainWindow();
            window.ViewModel = MaxContentViewModel();
            window.ApplyVisualSettings(new AppSettings { UiScalePercent = scalePercent }.Normalized());
            Layout(window);

            Rect client = new(0, 0, window.Width, window.Height);
            foreach (FrameworkElement element in Descendants(window.StandardContentPanel).OfType<FrameworkElement>()
                         .Where(element => element is TextBlock or Rectangle))
            {
                Rect bounds = element.TransformToAncestor(window).TransformBounds(new Rect(element.RenderSize));
                Assert.True(bounds.Left >= -0.01, $"{element.GetType().Name} left {bounds.Left}");
                Assert.True(bounds.Top >= -0.01, $"{element.GetType().Name} top {bounds.Top}");
                Assert.True(bounds.Right <= client.Right + 0.01, $"{element.GetType().Name} right {bounds.Right} > {client.Right}");
                Assert.True(bounds.Bottom <= client.Bottom + 0.01, $"{element.GetType().Name} bottom {bounds.Bottom} > {client.Bottom}");
            }
            foreach (TextBlock text in Descendants(window.StandardContentPanel).OfType<TextBlock>())
            {
                Assert.True(
                    text.DesiredSize.Width <= text.ActualWidth + text.Margin.Left + text.Margin.Right + 0.5,
                    $"Text '{text.Text}' needs {text.DesiredSize.Width} DIP but has {text.ActualWidth} DIP at {scalePercent}%");
                Assert.True(
                    text.DesiredSize.Height <= text.ActualHeight + text.Margin.Top + text.Margin.Bottom + 0.5,
                    $"Text '{text.Text}' needs {text.DesiredSize.Height} DIP but has {text.ActualHeight} DIP at {scalePercent}%");
            }
            Assert.Equal(1d, window.Opacity);
        });
    }

    [Fact]
    public void legacy_background_settings_do_not_change_rendered_overlay()
    {
        byte[] first = RenderOverlay(new AppSettings
        {
            BackgroundColor = "#FFFF0000",
            Opacity = 0.2,
        });
        byte[] second = RenderOverlay(new AppSettings
        {
            BackgroundColor = "#FF00FF00",
            Opacity = 1.0,
        });

        Assert.Equal(first, second);
        Assert.Equal(0, AlphaAt(first, 0, 0));
        Assert.Equal(0, AlphaAt(first, 4, 4));
        Assert.Equal(0, AlphaAt(first, 140, 54));
        Assert.Equal(0, AlphaAt(first, 200, 45));
    }

    [Fact]
    public void memory_text_and_bar_bind_to_visible_elements()
    {
        RunInSta(() =>
        {
            var viewModel = new MetricViewModel();
            viewModel.Apply(new MetricSnapshot
            {
                TakenAt = DateTimeOffset.UtcNow,
                Cpu = CpuSnapshot.Warmup(),
                Memory = new MemorySnapshot
                {
                    Status = MetricStatus.Ok,
                    UtilizationPercent = 75,
                    UsedBytes = 12L << 30,
                    TotalBytes = 16L << 30,
                },
                Gpu = GpuSnapshot.Warmup(),
                Network = NetworkSnapshot.Warmup(),
            }, RateUnitSystem.DecimalBytes);
            var window = new MainWindow { ViewModel = viewModel };
            window.ApplyVisualSettings(new AppSettings().Normalized());
            Layout(window);

            string[] text = Descendants(window.StandardContentPanel).OfType<TextBlock>().Select(block => block.Text).ToArray();
            Assert.Contains("75", text);
            Assert.Contains("%", text);
            Assert.Contains("12.0/16.0 GiB", text);
            Rectangle memoryBar = Descendants(window.StandardContentPanel).OfType<Rectangle>()
                .Single(rectangle => rectangle.RenderTransform is ScaleTransform transform && transform.ScaleX == 0.75);
            var transform = Assert.IsType<ScaleTransform>(memoryBar.RenderTransform);
            Assert.Equal(0.75, transform.ScaleX);
            Assert.True(memoryBar.RenderSize.Width > 0);
            Assert.InRange(memoryBar.RenderSize.Height, 4.5, 5.5);
        });
    }

    [Fact]
    public void settings_fields_are_stacked_without_overlap()
    {
        RunInSta(() =>
        {
            var window = new SettingsWindow(new AppSettings().Normalized());
            Layout(window);
            string[][] pages =
            [
                ["IntervalBox", "ScaleBox"],
                ["HorizontalMarginBox", "VerticalMarginBox"],
                ["FontBox", "ForegroundBox", "MutedBox", "AccentBox", "RxAccentBox", "TxAccentBox", "BackgroundColorBox"],
                ["GpuLuidBox"],
                ["NetworkLuidsBox", "NetworkPeakWindowBox"],
                ["BatteryChargeTargetBox"],
            ];
            int[] tabIndexes = [0, 1, 2, 4, 6, 7];
            for (int page = 0; page < pages.Length; page++)
            {
                window.CategoryTabs.SelectedIndex = tabIndexes[page];
                Layout(window);
                Rect[] boxes = pages[page].Select(name => Assert.IsType<TextBox>(window.FindName(name)))
                    .Select(box => box.TransformToAncestor(window).TransformBounds(new Rect(box.RenderSize)))
                    .OrderBy(bounds => bounds.Top)
                    .ToArray();
                for (int i = 1; i < boxes.Length; i++)
                {
                    Assert.True(boxes[i - 1].Bottom <= boxes[i].Top, $"Settings page {page}, rows {i - 1} and {i} overlap.");
                }
            }
        });
    }

    [Fact]
    public void settings_offer_fixed_kilobits_per_second()
    {
        RunInSta(() =>
        {
            var window = new SettingsWindow(new AppSettings().Normalized());
            var unitBox = Assert.IsType<ComboBox>(window.FindName("UnitBox"));
            ComboBoxItem fixedItem = unitBox.Items.Cast<ComboBoxItem>()
                .Single(item => item.Tag?.ToString() == nameof(RateUnitSystem.FixedKilobitsPerSecond));

            Assert.Equal("Kb/s固定", fixedItem.Content);
        });
    }

    [Fact]
    public void settings_use_topmost_only_during_foreground_presentation()
    {
        RunInSta(() =>
        {
            var window = new SettingsWindow(new AppSettings().Normalized())
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000,
                Top = -10000,
            };
            (testWindows ??= []).Add(window);

            window.RequestForegroundPresentation(useTopmostPulse: true);
            Assert.True(window.Topmost);

            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            Assert.False(window.Topmost);
        });
    }

    [Fact]
    public void settings_skip_topmost_pulse_when_owner_already_handles_zorder()
    {
        RunInSta(() =>
        {
            var window = new SettingsWindow(new AppSettings().Normalized())
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000,
                Top = -10000,
            };
            (testWindows ??= []).Add(window);

            window.RequestForegroundPresentation(useTopmostPulse: false);
            Assert.False(window.Topmost);

            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            Assert.False(window.Topmost);
        });
    }

    [Fact]
    public void settings_dialog_keeps_main_window_as_its_native_owner()
    {
        RunInSta(() =>
        {
            var main = new MainWindow
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000,
                Top = -10000,
                ShowActivated = false,
            };
            (testWindows ??= []).Add(main);
            main.Show();

            var dialog = new SettingsWindow(new AppSettings().Normalized())
            {
                Owner = main,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -9000,
                Top = -9000,
                ShowActivated = false,
            };
            (testWindows ??= []).Add(dialog);
            dialog.Show();

            IntPtr mainHandle = new WindowInteropHelper(main).Handle;
            IntPtr dialogHandle = new WindowInteropHelper(dialog).Handle;

            Assert.Equal(mainHandle, WindowInterop.GetWindow(dialogHandle, WindowInterop.GW_OWNER));
        });
    }

    [Fact]
    public void settings_save_fixed_kilobits_per_second_selection()
    {
        RunInSta(() =>
        {
            var window = new SettingsWindow(new AppSettings().Normalized());
            var unitBox = Assert.IsType<ComboBox>(window.FindName("UnitBox"));
            unitBox.SelectedItem = unitBox.Items.Cast<ComboBoxItem>()
                .Single(item => item.Tag?.ToString() == nameof(RateUnitSystem.FixedKilobitsPerSecond));
            window.Loaded += (_, _) => window.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.True(window.ShowDialog());
            Assert.Equal(RateUnitSystem.FixedKilobitsPerSecond, window.Result?.NetworkUnitSystem);
        });
    }

    [Fact]
    public void settings_migrate_legacy_background_alpha_and_preserve_split_preference()
    {
        RunInSta(() =>
        {
            var window = new SettingsWindow(new AppSettings
            {
                BackgroundEnabled = true,
                BackgroundColor = "#80112233",
                BackgroundOpacity = null,
                HideBackgroundBehindWindows = true,
                LayerMode = WindowLayerMode.AlwaysOnTop,
            }.Normalized());

            Assert.Equal("#112233", window.BackgroundColorBox.Text);
            Assert.Equal(128d / 255d * 100d, window.BackgroundOpacitySlider.Value, 8);
            Assert.Equal(nameof(BackgroundFillMode.Solid), ((ComboBoxItem)window.BackgroundFillModeBox.SelectedItem).Tag);
            Assert.False(window.BackgroundEdgeFadeSlider.IsEnabled);
            Assert.True(window.HideBackgroundBehindWindowsBox.IsEnabled);

            window.BackgroundFillModeBox.SelectedItem = window.BackgroundFillModeBox.Items.Cast<ComboBoxItem>()
                .Single(item => item.Tag?.ToString() == nameof(BackgroundFillMode.EdgeFade));
            window.BackgroundEdgeFadeSlider.Value = 30;
            Assert.True(window.BackgroundEdgeFadeSlider.IsEnabled);
            Assert.IsType<LinearGradientBrush>(window.BackgroundColorPreviewFadeHost.OpacityMask);
            Assert.IsType<LinearGradientBrush>(window.BackgroundColorPreview.OpacityMask);

            window.LayerBox.SelectedItem = window.LayerBox.Items.Cast<ComboBoxItem>()
                .Single(item => item.Tag?.ToString() == nameof(WindowLayerMode.Normal));
            Assert.False(window.HideBackgroundBehindWindowsBox.IsEnabled);
            Assert.True(window.HideBackgroundBehindWindowsBox.IsChecked);

            window.LayerBox.SelectedItem = window.LayerBox.Items.Cast<ComboBoxItem>()
                .Single(item => item.Tag?.ToString() == nameof(WindowLayerMode.AlwaysOnTop));
            window.Loaded += (_, _) => window.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.True(window.ShowDialog());
            SettingsDraft result = Assert.IsType<SettingsDraft>(window.Result);
            Assert.Equal("#FF112233", result.BackgroundColor);
            Assert.Equal(128d / 255d, result.BackgroundOpacity, 8);
            Assert.Equal(BackgroundFillMode.EdgeFade, result.BackgroundFillMode);
            Assert.Equal(30, result.BackgroundEdgeFadePercent);
            Assert.True(result.BackgroundEnabled);
            Assert.True(result.HideBackgroundBehindWindows);
        });
    }

    [Fact]
    public void main_window_uses_exactly_one_inline_background_source()
    {
        RunInSta(() =>
        {
            var window = new MainWindow();
            var inline = new AppSettings
            {
                BackgroundEnabled = true,
                BackgroundColor = "#FF123456",
                BackgroundOpacity = 0.5,
                HideBackgroundBehindWindows = false,
            }.Normalized();

            window.ApplyVisualSettings(inline);
            var brush = Assert.IsType<SolidColorBrush>(window.BackgroundSurface.Background);
            Assert.Equal(128, brush.Color.A);
            Assert.Equal(0x12, brush.Color.R);
            Assert.Equal(0x34, brush.Color.G);
            Assert.Equal(0x56, brush.Color.B);
            Assert.Null(window.BackgroundFadeHost.OpacityMask);
            Assert.Null(window.BackgroundSurface.OpacityMask);
            Assert.Equal(Colors.Transparent, Assert.IsType<SolidColorBrush>(window.WidgetRoot.Background).Color);
            Assert.Equal(1d, window.Opacity);

            window.ApplyVisualSettings(inline with { HideBackgroundBehindWindows = true });
            Assert.Equal(Colors.Transparent, Assert.IsType<SolidColorBrush>(window.BackgroundSurface.Background).Color);
        });
    }

    [Fact]
    public void main_window_can_fade_the_background_to_transparent_at_the_edges()
    {
        RunInSta(() =>
        {
            var window = new MainWindow();
            var settings = new AppSettings
            {
                BackgroundEnabled = true,
                BackgroundColor = "#FF123456",
                BackgroundOpacity = 0.5,
                BackgroundFillMode = BackgroundFillMode.EdgeFade,
                BackgroundEdgeFadePercent = 25,
            }.Normalized();

            window.ApplyVisualSettings(settings);

            var fill = Assert.IsType<SolidColorBrush>(window.BackgroundSurface.Background);
            var horizontal = Assert.IsType<LinearGradientBrush>(window.BackgroundFadeHost.OpacityMask);
            var vertical = Assert.IsType<LinearGradientBrush>(window.BackgroundSurface.OpacityMask);
            Assert.Equal(128, fill.Color.A);
            Assert.Equal(new Point(0, 0.5), horizontal.StartPoint);
            Assert.Equal(new Point(1, 0.5), horizontal.EndPoint);
            Assert.Equal(0, horizontal.GradientStops[0].Color.A);
            Assert.Equal(1d / 6d, horizontal.GradientStops[1].Offset, 10);
            Assert.Equal(255, horizontal.GradientStops[1].Color.A);
            Assert.Equal(5d / 6d, horizontal.GradientStops[2].Offset, 10);
            Assert.Equal(0, horizontal.GradientStops[3].Color.A);
            Assert.Equal(new Point(0.5, 0), vertical.StartPoint);
            Assert.Equal(new Point(0.5, 1), vertical.EndPoint);
            Assert.Equal(0, vertical.GradientStops[0].Color.A);
            Assert.Equal(1d / 6d, vertical.GradientStops[1].Offset, 10);
            Assert.Equal(5d / 6d, vertical.GradientStops[2].Offset, 10);
            Assert.Equal(0, vertical.GradientStops[3].Color.A);
            Assert.Null(window.WidgetRoot.OpacityMask);
            Assert.Equal(420, window.Width);
            Assert.Equal(330, window.Height);
            Assert.Equal(280, window.WidgetRoot.Width);
            Assert.Equal(220, window.WidgetRoot.Height);
            Assert.Equal(HorizontalAlignment.Center, window.WidgetRoot.HorizontalAlignment);
            Assert.Equal(VerticalAlignment.Center, window.WidgetRoot.VerticalAlignment);
            Layout(window);
            Rect informationBounds = window.WidgetRoot.TransformToAncestor(window)
                .TransformBounds(new Rect(window.WidgetRoot.RenderSize));
            Assert.InRange(informationBounds.Left, 69, 71);
            Assert.InRange(informationBounds.Right, 349, 351);
            Assert.InRange(informationBounds.Top, 54, 56);
            Assert.InRange(informationBounds.Bottom, 274, 276);
        });
    }

    [Theory]
    [InlineData(75)]
    [InlineData(100)]
    [InlineData(125)]
    [InlineData(150)]
    [InlineData(200)]
    public void reduced_mode_uses_150_dip_information_width_without_overflow(double scalePercent)
    {
        RunInSta(() =>
        {
            var window = new MainWindow { ViewModel = ReducedMaxContentViewModel() };
            window.ApplyVisualSettings(new AppSettings
            {
                DisplayMode = WidgetDisplayMode.Reduced,
                UiScalePercent = scalePercent,
                ShowDiskMetrics = true,
            }.Normalized());
            Layout(window);

            double scale = scalePercent / 100d;
            Assert.InRange(Math.Abs(window.Width - (150d * scale)), 0, 0.5);
            Assert.InRange(Math.Abs(window.Height - (WidgetHeightCalculator.Calculate(5) * scale)), 0, 0.5);
            Assert.Equal(Visibility.Collapsed, window.StandardContentPanel.Visibility);
            Assert.Equal(Visibility.Visible, window.ReducedContentPanel.Visibility);
            Assert.Equal(Visibility.Visible, window.ReducedDiskRow.Visibility);
            Assert.Equal(Visibility.Visible, window.ReducedNetworkRow.Visibility);
            Assert.All(
                new[]
                {
                    window.ReducedCpuBarHost,
                    window.ReducedMemoryBarHost,
                    window.ReducedGpuBarHost,
                    window.ReducedDiskBarHost,
                },
                bar => Assert.InRange(Math.Abs(bar.ActualWidth - 134d), 0, 0.5));

            Rect client = new(0, 0, window.Width, window.Height);
            foreach (FrameworkElement element in Descendants(window.ReducedContentPanel).OfType<FrameworkElement>()
                         .Where(element => element is TextBlock or Rectangle))
            {
                Rect bounds = element.TransformToAncestor(window).TransformBounds(new Rect(element.RenderSize));
                Assert.True(bounds.Left >= -0.01, $"{element.GetType().Name} left {bounds.Left}");
                Assert.True(bounds.Top >= -0.01, $"{element.GetType().Name} top {bounds.Top}");
                Assert.True(bounds.Right <= client.Right + 0.01, $"{element.GetType().Name} right {bounds.Right} > {client.Right}");
                Assert.True(bounds.Bottom <= client.Bottom + 0.01, $"{element.GetType().Name} bottom {bounds.Bottom} > {client.Bottom}");
            }
            Assert.All(
                Descendants(window.ReducedContentPanel).OfType<TextBlock>(),
                text => Assert.True(
                    text.DesiredSize.Width <= text.ActualWidth + text.Margin.Left + text.Margin.Right + 0.5,
                    $"Text '{text.Text}' needs {text.DesiredSize.Width} DIP but has {text.ActualWidth} DIP at {scalePercent}%"));
        });
    }

    [Fact]
    public void repeated_display_mode_switches_do_not_accumulate_width_or_scale()
    {
        RunInSta(() =>
        {
            var window = new MainWindow();
            const double scale = 1.25;
            for (int index = 0; index < 12; index++)
            {
                WidgetDisplayMode mode = index % 2 == 0
                    ? WidgetDisplayMode.Reduced
                    : WidgetDisplayMode.Standard;
                window.ApplyVisualSettings(new AppSettings
                {
                    DisplayMode = mode,
                    UiScalePercent = scale * 100,
                }.Normalized());
                Layout(window);

                double baseWidth = mode == WidgetDisplayMode.Reduced ? 150d : 280d;
                var transform = Assert.IsType<ScaleTransform>(window.WidgetRoot.LayoutTransform);
                Assert.Equal(baseWidth, window.WidgetRoot.Width);
                Assert.InRange(Math.Abs(window.Width - (baseWidth * scale)), 0, 0.5);
                Assert.Equal(scale, transform.ScaleX, 10);
                Assert.Equal(scale, transform.ScaleY, 10);
            }
        });
    }

    [Fact]
    public void reduced_network_value_corpus_fits_without_rx_tx_overlap_at_each_scale()
    {
        RunInSta(() =>
        {
            MetricViewModel viewModel = ReducedMaxContentViewModel();
            var window = new MainWindow { ViewModel = viewModel };
            foreach (string value in new[] { "--", "N/A", "999 GB/s", "10000000 Kb/s" })
            {
                viewModel.NetworkRx = value;
                viewModel.NetworkTx = value;
                foreach (double scalePercent in new[] { 75d, 100d, 125d, 150d, 200d })
                {
                    window.ApplyVisualSettings(new AppSettings
                    {
                        DisplayMode = WidgetDisplayMode.Reduced,
                        UiScalePercent = scalePercent,
                    }.Normalized());
                    Layout(window);

                    TextBlock rx = window.ReducedNetworkReceiveRate;
                    TextBlock tx = window.ReducedNetworkSendRate;
                    Rect rxBounds = rx.TransformToAncestor(window.ReducedNetworkRow).TransformBounds(new Rect(rx.RenderSize));
                    Rect txBounds = tx.TransformToAncestor(window.ReducedNetworkRow).TransformBounds(new Rect(tx.RenderSize));
                    Assert.Equal(TextTrimming.None, rx.TextTrimming);
                    Assert.Equal(TextTrimming.None, tx.TextTrimming);
                    Assert.True(rx.DesiredSize.Width <= rx.ActualWidth + 0.5, $"Rx '{rx.Text}' does not fit at {scalePercent}%.");
                    Assert.True(tx.DesiredSize.Width <= tx.ActualWidth + 0.5, $"Tx '{tx.Text}' does not fit at {scalePercent}%.");
                    Assert.True(rxBounds.Bottom <= txBounds.Top + 0.01, $"Rx {rxBounds} overlaps Tx {txBounds} at {scalePercent}%.");
                }
            }
        });
    }

    [Fact]
    public void reduced_network_direction_columns_stay_fixed_when_values_change()
    {
        RunInSta(() =>
        {
            MetricViewModel viewModel = ReducedMaxContentViewModel();
            viewModel.NetworkPeakRx = "183 KB/s";
            viewModel.NetworkPeakTx = "45.1 KB/s";
            viewModel.NetworkPeakLabel = "20s max";
            var window = new MainWindow { ViewModel = viewModel };
            window.ApplyVisualSettings(new AppSettings
            {
                DisplayMode = WidgetDisplayMode.Reduced,
                ShowNetworkPeaks = true,
            }.Normalized());
            Layout(window);

            FrameworkElement[] arrows =
            [
                window.ReducedNetworkReceiveArrow,
                window.ReducedNetworkSendArrow,
                window.ReducedNetworkPeakReceiveArrow,
                window.ReducedNetworkPeakSendArrow,
            ];
            double[] initialLefts = arrows
                .Select(arrow => arrow.TransformToAncestor(window).TransformBounds(new Rect(arrow.RenderSize)).Left)
                .ToArray();
            Rect peakLabelBounds = window.ReducedNetworkPeakLabel
                .TransformToAncestor(window)
                .TransformBounds(new Rect(window.ReducedNetworkPeakLabel.RenderSize));
            Rect peakReceiveArrowBounds = window.ReducedNetworkPeakReceiveArrow
                .TransformToAncestor(window)
                .TransformBounds(new Rect(window.ReducedNetworkPeakReceiveArrow.RenderSize));
            Assert.Equal(
                peakReceiveArrowBounds.Top + (peakReceiveArrowBounds.Height / 2d),
                peakLabelBounds.Top + (peakLabelBounds.Height / 2d),
                6);

            viewModel.NetworkRx = "10000000 Kb/s";
            viewModel.NetworkTx = "999 GB/s";
            viewModel.NetworkPeakRx = "10000000 Kb/s";
            viewModel.NetworkPeakTx = "999 GB/s";
            Layout(window);

            double[] updatedLefts = arrows
                .Select(arrow => arrow.TransformToAncestor(window).TransformBounds(new Rect(arrow.RenderSize)).Left)
                .ToArray();
            Assert.Equal(initialLefts, updatedLefts);
            Assert.Equal(initialLefts[0], initialLefts[1], 6);
            Assert.Equal(initialLefts[2], initialLefts[3], 6);
            Assert.InRange(initialLefts[2] - initialLefts[0], 10d, 10.5d);
            Assert.Single(
                Descendants(window.ReducedNetworkRow).OfType<TextBlock>(),
                text => text.Text == "20s max");
        });
    }

    [Fact]
    public void four_edge_fades_multiply_smoothly_at_each_corner()
    {
        var settings = new AppSettings
        {
            BackgroundEnabled = true,
            BackgroundColor = "#FF123456",
            BackgroundOpacity = 0.5,
            BackgroundFillMode = BackgroundFillMode.EdgeFade,
            BackgroundEdgeFadePercent = 25,
            ShowCpuMetrics = false,
            ShowGpuMetrics = false,
            ForegroundColor = "#00000000",
            MutedColor = "#00000000",
            AccentColor = "#00000000",
            RxAccentColor = "#00000000",
            TxAccentColor = "#00000000",
        }.Normalized();

        RenderedOverlay rendered = RenderOverlayAtNaturalSize(settings);
        int informationLeft = 70;
        int informationTop = 29;
        int informationRight = informationLeft + 280;
        int informationBottom = informationTop + (int)WidgetHeightCalculator.Calculate(2);
        int horizontalCenter = informationTop + ((informationBottom - informationTop) / 2);
        int verticalCenter = informationLeft + 200;
        int leftHalfFade = informationLeft / 2;
        int rightHalfFade = informationRight + ((rendered.Width - informationRight) / 2);
        int topHalfFade = informationTop / 2;
        int bottomHalfFade = informationBottom + ((rendered.Height - informationBottom) / 2);
        byte[] edgeAlphas =
        [
            AlphaAt(rendered.Pixels, rendered.Width, leftHalfFade, horizontalCenter),
            AlphaAt(rendered.Pixels, rendered.Width, rightHalfFade, horizontalCenter),
            AlphaAt(rendered.Pixels, rendered.Width, verticalCenter, topHalfFade),
            AlphaAt(rendered.Pixels, rendered.Width, verticalCenter, bottomHalfFade),
        ];
        byte[] cornerAlphas =
        [
            AlphaAt(rendered.Pixels, rendered.Width, leftHalfFade, topHalfFade),
            AlphaAt(rendered.Pixels, rendered.Width, rightHalfFade, topHalfFade),
            AlphaAt(rendered.Pixels, rendered.Width, leftHalfFade, bottomHalfFade),
            AlphaAt(rendered.Pixels, rendered.Width, rightHalfFade, bottomHalfFade),
        ];
        byte informationAlpha = AlphaAt(rendered.Pixels, rendered.Width, informationLeft + 8, informationTop + 20);

        Assert.Equal(420, rendered.Width);
        Assert.Equal(174, rendered.Height);
        Assert.All(edgeAlphas, alpha => Assert.InRange(alpha, 55, 75));
        Assert.All(cornerAlphas, alpha => Assert.InRange(alpha, 20, 45));
        Assert.InRange(informationAlpha, 127, 128);
        Assert.All(cornerAlphas, cornerAlpha => Assert.All(edgeAlphas, edgeAlpha => Assert.True(cornerAlpha < edgeAlpha)));
    }

    [Fact]
    public void network_peak_setting_independently_controls_the_window_field()
    {
        RunInSta(() =>
        {
            var window = new SettingsWindow(new AppSettings
            {
                ShowRecentPeaks = true,
                ShowNetworkPeaks = false,
            }.Normalized());

            Assert.True(window.ShowPeaksBox.IsChecked);
            Assert.False(window.ShowNetworkPeaksBox.IsChecked);
            Assert.False(window.NetworkPeakWindowBox.IsEnabled);

            window.ShowNetworkPeaksBox.IsChecked = true;

            Assert.True(window.NetworkPeakWindowBox.IsEnabled);
            Assert.True(window.ShowPeaksBox.IsChecked);
        });
    }

    [Fact]
    public void settings_window_selects_and_saves_reduced_display_mode()
    {
        RunInSta(() =>
        {
            var window = new SettingsWindow(new AppSettings { DisplayMode = WidgetDisplayMode.Reduced }.Normalized());

            Assert.Equal(["Standard", "Reduced"], window.DisplayModeBox.Items.Cast<ComboBoxItem>().Select(item => item.Tag).ToArray());
            Assert.Equal(["通常幅（280 DIP）", "縮小表示（150 DIP）"], window.DisplayModeBox.Items.Cast<ComboBoxItem>().Select(item => item.Content).ToArray());
            Assert.Equal("Reduced", ((ComboBoxItem)window.DisplayModeBox.SelectedItem).Tag);
            window.Loaded += (_, _) => window.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(window.ShowDialog());

            Assert.Equal(WidgetDisplayMode.Reduced, window.Result?.DisplayMode);
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void metric_markers_and_network_peak_row_have_independent_visibility(
        bool showMetricPeaks,
        bool showNetworkPeaks)
    {
        RunInSta(() =>
        {
            var window = new MainWindow();

            window.ApplyVisualSettings(new AppSettings
            {
                ShowRecentPeaks = showMetricPeaks,
                ShowNetworkPeaks = showNetworkPeaks,
            }.Normalized());

            Visibility metricVisibility = showMetricPeaks ? Visibility.Visible : Visibility.Collapsed;
            Assert.Equal(metricVisibility, window.CpuPeakMarker.Visibility);
            Assert.Equal(metricVisibility, window.MemoryPeakMarker.Visibility);
            Assert.Equal(metricVisibility, window.GpuPeakMarker.Visibility);
            Assert.Equal(metricVisibility, window.DiskPeakMarker.Visibility);
            Assert.Equal(
                showNetworkPeaks ? Visibility.Visible : Visibility.Collapsed,
                window.NetworkPeakRow.Visibility);
            Assert.Equal(Visibility.Collapsed, window.ReducedNetworkPeakReceiveRate.Visibility);
            Assert.Equal(Visibility.Collapsed, window.ReducedNetworkPeakSendRate.Visibility);

            window.ApplyVisualSettings(new AppSettings
            {
                DisplayMode = WidgetDisplayMode.Reduced,
                ShowNetworkPeaks = showNetworkPeaks,
            }.Normalized());

            Assert.Equal(
                showNetworkPeaks ? Visibility.Visible : Visibility.Collapsed,
                window.ReducedNetworkPeakReceiveRate.Visibility);
            Assert.Equal(
                showNetworkPeaks ? Visibility.Visible : Visibility.Collapsed,
                window.ReducedNetworkPeakSendRate.Visibility);
            Assert.Equal(showNetworkPeaks ? 64d : 48d, window.ReducedNetworkRow.Height);
            Assert.Equal(
                WidgetHeightCalculator.Calculate(4) + (showNetworkPeaks ? 16d : 0d),
                window.Height);
        });
    }

    [Theory]
    [InlineData(75)]
    [InlineData(100)]
    [InlineData(125)]
    [InlineData(150)]
    [InlineData(200)]
    public void reduced_network_peak_values_fit_at_each_scale(double scalePercent)
    {
        RunInSta(() =>
        {
            MetricViewModel viewModel = ReducedMaxContentViewModel();
            viewModel.NetworkPeakRx = "10000000 Kb/s";
            viewModel.NetworkPeakTx = "10000000 Kb/s";
            viewModel.NetworkPeakLabel = "60s max";
            var window = new MainWindow { ViewModel = viewModel };

            window.ApplyVisualSettings(new AppSettings
            {
                DisplayMode = WidgetDisplayMode.Reduced,
                UiScalePercent = scalePercent,
                ShowNetworkPeaks = true,
            }.Normalized());
            Layout(window);

            double scale = scalePercent / 100d;
            Assert.InRange(Math.Abs(window.Height - ((WidgetHeightCalculator.Calculate(4) + 16d) * scale)), 0, 0.5);
            foreach (TextBlock text in new[]
                     {
                         window.ReducedNetworkReceiveRate,
                         window.ReducedNetworkSendRate,
                         window.ReducedNetworkPeakReceiveRate,
                         window.ReducedNetworkPeakSendRate,
                     })
            {
                Rect bounds = text.TransformToAncestor(window).TransformBounds(new Rect(text.RenderSize));
                Assert.True(bounds.Right <= window.Width + 0.01, $"Text '{text.Text}' exceeds the window at {scalePercent}%.");
                Assert.True(bounds.Bottom <= window.Height + 0.01, $"Text '{text.Text}' exceeds the window at {scalePercent}%.");
                Assert.True(text.DesiredSize.Width <= text.ActualWidth + 0.5, $"Text '{text.Text}' does not fit at {scalePercent}%.");
            }
        });
    }

    [Theory]
    [InlineData(75)]
    [InlineData(100)]
    [InlineData(125)]
    [InlineData(150)]
    [InlineData(200)]
    public void disk_primary_and_rates_share_cpu_and_network_anchors(double scalePercent)
    {
        RunInSta(() =>
        {
            var window = new MainWindow { ViewModel = MaxContentViewModel() };
            window.ApplyVisualSettings(new AppSettings
            {
                UiScalePercent = scalePercent,
                ShowDiskMetrics = true,
            }.Normalized());
            Layout(window);

            double dpiScale = VisualTreeHelper.GetDpi(window).DpiScaleX;
            AssertDevicePixelAligned(window.CpuPrimaryValue, window.DiskPrimaryValue, window, dpiScale);
            AssertDevicePixelAligned(window.NetworkReceiveRate, window.DiskReadRate, window, dpiScale);
            AssertDevicePixelAligned(window.NetworkSendRate, window.DiskWriteRate, window, dpiScale);
        });
    }

    [Theory]
    [InlineData(false, false, false, false, false, 2)]
    [InlineData(false, true, true, false, false, 4)]
    [InlineData(true, false, false, true, true, 4)]
    [InlineData(true, true, true, true, true, 6)]
    public void metric_visibility_uses_collapsed_rows_dynamic_margins_and_height(
        bool showCpu,
        bool showGpu,
        bool showDisk,
        bool showBattery,
        bool batteryPresent,
        int expectedVisibleRows)
    {
        RunInSta(() =>
        {
            var window = new MainWindow();
            var settings = new AppSettings
            {
                ShowCpuMetrics = showCpu,
                ShowGpuMetrics = showGpu,
                ShowDiskMetrics = showDisk,
                ShowBatteryEstimate = showBattery,
            }.Normalized();

            foreach (WidgetDisplayMode mode in Enum.GetValues<WidgetDisplayMode>())
            {
                window.ApplyVisualSettings(settings with { DisplayMode = mode }, batteryPresent);

                FrameworkElement[] standardRows = [window.CpuRow, window.MemoryRow, window.GpuRow, window.DiskRow, window.NetworkRow, window.BatteryRow];
                FrameworkElement[] reducedRows = [window.ReducedCpuRow, window.ReducedMemoryRow, window.ReducedGpuRow, window.ReducedDiskRow, window.ReducedNetworkRow, window.ReducedBatteryRow];
                FrameworkElement[] activeRows = mode == WidgetDisplayMode.Standard ? standardRows : reducedRows;
                FrameworkElement[] visibleRows = activeRows.Where(row => row.Visibility == Visibility.Visible).ToArray();
                Assert.Equal(expectedVisibleRows, visibleRows.Length);
                Assert.Equal(WidgetHeightCalculator.Calculate(expectedVisibleRows), window.Height);
                Assert.Equal(showCpu ? Visibility.Visible : Visibility.Collapsed, standardRows[0].Visibility);
                Assert.Equal(showCpu ? Visibility.Visible : Visibility.Collapsed, reducedRows[0].Visibility);
                Assert.Equal(showGpu ? Visibility.Visible : Visibility.Collapsed, standardRows[2].Visibility);
                Assert.Equal(showGpu ? Visibility.Visible : Visibility.Collapsed, reducedRows[2].Visibility);
                Assert.Equal(showDisk ? Visibility.Visible : Visibility.Collapsed, standardRows[3].Visibility);
                Assert.Equal(showDisk ? Visibility.Visible : Visibility.Collapsed, reducedRows[3].Visibility);
                Visibility batteryVisibility = showBattery && batteryPresent ? Visibility.Visible : Visibility.Collapsed;
                Assert.Equal(batteryVisibility, standardRows[5].Visibility);
                Assert.Equal(batteryVisibility, reducedRows[5].Visibility);
                Assert.Equal(Visibility.Visible, standardRows[1].Visibility);
                Assert.Equal(Visibility.Visible, reducedRows[1].Visibility);
                Assert.Equal(Visibility.Visible, standardRows[4].Visibility);
                Assert.Equal(Visibility.Visible, reducedRows[4].Visibility);
                for (int index = 0; index < visibleRows.Length; index++)
                {
                    Assert.Equal(index == 0 ? 0 : WidgetHeightCalculator.RowGap, visibleRows[index].Margin.Top);
                }
            }
        });
    }

    [Fact]
    public void settings_window_uses_preloaded_disk_context_without_native_enumeration()
    {
        RunInSta(() =>
        {
            const string status = "Windowsボリュームが複数ディスクにまたがるため自動選択できません。";
            var context = new SettingsDialogContext(
                OptionalTelemetryCapabilities.Unknown,
                ImmutableArray.Create(0, 2),
                status);
            var window = new SettingsWindow(new AppSettings().Normalized(), context);

            Assert.Equal(3, window.PhysicalDiskBox.Items.Count);
            Assert.Equal(status, window.DiskStatusText.Text);
        });
    }

    [Fact]
    public void validation_moves_to_the_category_that_owns_the_invalid_field()
    {
        RunInSta(() =>
        {
            AssertValidation(window => window.IntervalBox.Text = "invalid", 0, "全体設定");
            AssertValidation(window => window.HorizontalMarginBox.Text = "invalid", 1, "表示位置");
            AssertValidation(window => window.ForegroundBox.Text = "invalid", 2, "外観");
            AssertValidation(window => window.BackgroundColorBox.Text = "#1234", 2, "外観");
            AssertValidation(window => window.GpuLuidBox.Text = "not-hex", 4, "GPU");
            AssertValidation(window => window.NetworkLuidsBox.Text = "not-a-luid", 6, "NET");
            AssertValidation(window =>
            {
                window.BatteryChargeModeBox.SelectedIndex = 1;
                window.BatteryChargeTargetBox.Text = "49";
            }, 7, "BAT");
        });

        static void AssertValidation(Action<SettingsWindow> makeInvalid, int expectedCategory, string messagePrefix)
        {
            var window = new SettingsWindow(new AppSettings().Normalized());
            window.CategoryTabs.SelectedIndex = 8;
            makeInvalid(window);

            window.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(expectedCategory, window.CategoryTabs.SelectedIndex);
            Assert.StartsWith(messagePrefix, window.ValidationMessage.Text, StringComparison.Ordinal);
        }
    }

    private static XElement LoadWindow()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "MainWindow.xaml");
        return XDocument.Load(path).Root!;
    }

    private static byte[] RenderOverlay(AppSettings settings) => RenderOverlay(settings, new MetricViewModel());

    private static byte[] RenderOverlay(AppSettings settings, MetricViewModel viewModel)
    {
        return RunInSta(() =>
        {
            var window = new MainWindow();
            window.ViewModel = viewModel;
            window.ApplyVisualSettings(settings.Normalized());
            Layout(window);
            Assert.Equal(1d, window.Opacity);

            var bitmap = new RenderTargetBitmap(280, 220, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);
            int stride = 280 * 4;
            var pixels = new byte[stride * 220];
            bitmap.CopyPixels(pixels, stride, 0);
            return pixels;
        });
    }

    private static RenderedOverlay RenderOverlayAtNaturalSize(AppSettings settings)
    {
        return RunInSta(() =>
        {
            var window = new MainWindow();
            window.ApplyVisualSettings(settings.Normalized());
            Layout(window);
            int width = (int)Math.Round(window.Width);
            int height = (int)Math.Round(window.Height);
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);
            int stride = width * 4;
            var pixels = new byte[stride * height];
            bitmap.CopyPixels(pixels, stride, 0);
            return new RenderedOverlay(pixels, width, height);
        });
    }

    private static MetricViewModel MaxContentViewModel() => new()
    {
        CpuValue = "100",
        CpuUnit = "%",
        CpuFrequency = "9.99 GHz*",
        CpuPower = "999.9 W",
        MemoryValue = "100",
        MemoryUnit = "%",
        MemoryUsage = "120/100 GiB",
        GpuValue = "100",
        GpuUnit = "%",
        GpuMemory = "120/100 GiB",
        GpuPower = "999.9 W",
        DiskValue = "100",
        DiskRead = "999 GB/s",
        DiskWrite = "999 GB/s",
        NetworkRx = "999 GB/s",
        NetworkTx = "999 GB/s",
        CpuBarFraction = 1d,
        MemoryBarFraction = 1d,
        GpuBarFraction = 1d,
    };

    private static MetricViewModel ReducedMaxContentViewModel() => new()
    {
        CpuValue = "100",
        CpuUnit = "%",
        MemoryValue = "100",
        MemoryUnit = "%",
        GpuValue = "100",
        GpuUnit = "%",
        DiskValue = "100",
        NetworkRx = "10000000 Kb/s",
        NetworkTx = "10000000 Kb/s",
        BatteryPrimary = "100%",
        CpuBarFraction = 1d,
        MemoryBarFraction = 1d,
        GpuBarFraction = 1d,
        DiskBarFraction = 1d,
    };

    private static byte AlphaAt(byte[] pixels, int x, int y) => pixels[((y * 280) + x) * 4 + 3];

    private static byte AlphaAt(byte[] pixels, int width, int x, int y) => pixels[((y * width) + x) * 4 + 3];

    private sealed record RenderedOverlay(byte[] Pixels, int Width, int Height);

    private static void AssertDevicePixelAligned(
        FrameworkElement expected,
        FrameworkElement actual,
        Visual ancestor,
        double dpiScale)
    {
        double expectedRight = expected.TransformToAncestor(ancestor).TransformBounds(new Rect(expected.RenderSize)).Right;
        double actualRight = actual.TransformToAncestor(ancestor).TransformBounds(new Rect(actual.RenderSize)).Right;
        double deltaPixels = (actualRight - expectedRight) * dpiScale;
        Assert.True(
            Math.Abs(deltaPixels) <= 1,
            $"Expected right {expectedRight:F3} DIP, actual {actualRight:F3} DIP, delta {deltaPixels:F3} device px.");
    }

    private static void Layout(Window window)
    {
        if (!window.IsVisible)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -10000;
            window.Top = -10000;
            window.ShowActivated = false;
            window.Show();
            (testWindows ??= []).Add(window);
        }
        window.Measure(new Size(window.Width, window.Height));
        window.Arrange(new Rect(0, 0, window.Width, window.Height));
        window.UpdateLayout();
    }

    private static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (DependencyObject descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static void RunInSta(Action action) => RunInSta(() =>
    {
        action();
        return true;
    });

    private static T RunInSta<T>(Func<T> action)
    {
        T result = default;
        Exception error = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                if (testWindows is not null)
                {
                    foreach (Window window in testWindows)
                    {
                        window.Close();
                    }
                    testWindows.Clear();
                }
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }
        return result!;
    }

    private static XElement FindOuterRowElement(XElement window, string label)
    {
        XNamespace presentation = window.Name.Namespace;
        XElement stack = FindStandardPanel(window);
        foreach (XElement row in stack.Elements(presentation + "Grid"))
        {
            if (row.Descendants(presentation + "TextBlock").Any(text => text.Attribute("Text")?.Value == label))
            {
                return row;
            }
        }
        throw new InvalidOperationException($"Label row was not found: {label}");
    }

    private static XElement FindStandardPanel(XElement window) =>
        FindNamedElement(window, "StackPanel", "StandardContentPanel");

    private static XElement FindNamedElement(XElement root, string localName, string name)
    {
        XNamespace presentation = root.Name.Namespace;
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        return root.Descendants(presentation + localName)
            .Single(element => element.Attribute(xaml + "Name")?.Value == name);
    }

    private static string[] FindMetricLabels(XElement window) =>
        new[] { "CPU", "MEM", "GPU", "DISK", "NET", "BAT" }
            .Where(label => FindOuterRowElement(window, label) is not null)
            .ToArray();
}
