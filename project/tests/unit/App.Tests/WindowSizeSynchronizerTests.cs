using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DesktopSystemMonitor.App.Startup;
using DesktopSystemMonitor.Core.Settings;
using DesktopSystemMonitor.Windows.Window;
using Xunit;
using LayoutRect = DesktopSystemMonitor.Core.Layout.Rect;

namespace DesktopSystemMonitor.App.Tests;

/// <summary>
/// Reproduces the display-connect state on real WPF windows: a WM_DPICHANGED
/// whose suggested rectangle still has the previous DPI's pixel size leaves
/// WPF at the new DPI with an unchanged Width while the HWND keeps the old
/// pixel size, so the scaled content overflows the native frame.
/// </summary>
[Collection("WpfWindow")]
public sealed class WindowSizeSynchronizerTests
{
    private const int WM_DPICHANGED = 0x02E0;
    private static readonly IntPtr PerMonitorAwareV2 = new(-4);

    private static AppSettings ObservedSettings() => new AppSettings
    {
        UiScalePercent = 80,
        BackgroundEnabled = false,
    }.Normalized();

    [Fact]
    public void stale_dpi_change_rectangle_overflows_content_until_native_size_is_synchronized()
    {
        RunPerMonitorSta(() =>
        {
            MainWindow main = CreateMainWindow();
            main.ApplyVisualSettings(ObservedSettings());
            main.Show();
            Pump(main);
            Assert.Equal(new Size(224, 176), main.RequiredWindowSizeDip);
            Assert.True(main.SynchronizeWindowSize(apply: false).IsSynchronized);

            ForceStaleDpiFrame(main);

            uint dpi = WindowPlacementNativeApi.GetDpiForWindow(main.Handle);
            Assert.True(WindowPlacementNativeApi.TryGetBounds(main.Handle, out LayoutRect stale));
            Assert.NotEqual(Expected(224, dpi), stale.Width);
            Assert.Equal(224, main.Width);
            Assert.False(ContentFitsClient(main), "the stale frame clips the widget content");
            WindowSizeSyncResult observed = main.SynchronizeWindowSize(apply: false);
            Assert.Equal(WindowSizeSyncStatus.Mismatch, observed.Status);

            main.Width = 224;
            Pump(main);
            Assert.Equal(WindowSizeSyncStatus.Mismatch, main.SynchronizeWindowSize(apply: false).Status);

            WindowSizeSyncResult repaired = main.SynchronizeWindowSize(apply: true);
            Pump(main);

            Assert.Equal(WindowSizeSyncStatus.Corrected, repaired.Status);
            Assert.Contains("correctedBy=native", repaired.Detail, StringComparison.Ordinal);
            AssertNativeSize(main, 224, 176);
            Assert.True(ContentFitsClient(main));
            Assert.Equal(WindowSizeSyncStatus.Matched, main.SynchronizeWindowSize(apply: false).Status);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void layered_and_normal_windows_reach_the_same_stale_state_and_repair(bool allowsTransparency)
    {
        RunPerMonitorSta(() =>
        {
            var window = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = allowsTransparency,
                Background = Brushes.Transparent,
                ShowInTaskbar = false,
                ShowActivated = false,
                ResizeMode = ResizeMode.NoResize,
                UseLayoutRounding = true,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000,
                Top = -10000,
                Width = 252,
                Height = 198,
                Opacity = 0,
            };
            Track(window);
            window.Show();
            Pump(window);
            var required = new Size(252, 198);
            Assert.True(WindowSizeSynchronizer.Synchronize(window, required, apply: false).IsSynchronized);

            ForceStaleDpiFrame(window);

            Assert.Equal(WindowSizeSyncStatus.Mismatch, WindowSizeSynchronizer.Synchronize(window, required, apply: false).Status);
            Assert.Equal(WindowSizeSyncStatus.Corrected, WindowSizeSynchronizer.Synchronize(window, required, apply: true).Status);
            AssertNativeSize(window, 252, 198);
        });
    }

    [Fact]
    public void wpf_rewritten_width_is_repaired_through_the_wpf_size_path()
    {
        RunPerMonitorSta(() =>
        {
            MainWindow main = CreateMainWindow();
            main.ApplyVisualSettings(ObservedSettings());
            main.Show();
            Pump(main);
            IntPtr handle = main.Handle;
            Assert.True(WindowPlacementNativeApi.TryGetBounds(handle, out LayoutRect before));

            Assert.True(WindowPlacementNativeApi.TrySetSize(handle, (int)(before.Width / 2), (int)(before.Height / 2)));
            Pump(main);
            Assert.NotEqual(224, main.Width, 3);
            Assert.Equal(new Size(224, 176), main.RequiredWindowSizeDip);

            WindowSizeSyncResult repaired = main.SynchronizeWindowSize(apply: true);

            Assert.Equal(WindowSizeSyncStatus.Corrected, repaired.Status);
            Assert.Contains("correctedBy=wpf", repaired.Detail, StringComparison.Ordinal);
            Assert.Equal(224, main.Width, 6);
            AssertNativeSize(main, 224, 176);
        });
    }

    [Fact]
    public void repeated_synchronization_and_dpi_round_trips_do_not_accumulate_size()
    {
        RunPerMonitorSta(() =>
        {
            MainWindow main = CreateMainWindow();
            main.ApplyVisualSettings(ObservedSettings());
            main.Show();
            Pump(main);

            for (int round = 0; round < 3; round++)
            {
                ForceStaleDpiFrame(main);
                Assert.True(main.SynchronizeWindowSize(apply: true).IsSynchronized);
                Assert.Equal(WindowSizeSyncStatus.Matched, main.SynchronizeWindowSize(apply: true).Status);
                AssertNativeSize(main, 224, 176);
                Assert.Equal(224, main.Width, 6);
            }
        });
    }

    [Fact]
    public void placement_wave_repairs_stale_frame_before_verifying_position()
    {
        RunPerMonitorSta(() =>
        {
            AppSettings settings = ObservedSettings();
            int saves = 0;
            var diagnostics = new List<string>();
            MainWindow main = CreateMainWindow();
            main.ApplyVisualSettings(settings);
            main.Show();
            Pump(main);
            using var controller = new WindowPlacementController(
                main,
                () => settings,
                update => { settings = update(settings); saves++; },
                (category, _) => diagnostics.Add(category),
                main.Dispatcher);
            Assert.True(controller.PositionWindow());
            int savesBeforeWave = saves;

            ForceStaleDpiFrame(main);
            Assert.True(controller.ReflowPending);
            Assert.Contains("placement-trace", diagnostics);
            controller.CompletePendingDisplayReflow();
            Pump(main);

            Assert.False(controller.ReflowPending);
            AssertNativeSize(main, 224, 176);
            Assert.True(ContentFitsClient(main));
            Assert.True(WindowPlacementNativeApi.TryGetBounds(main.Handle, out LayoutRect bounds));
            LayoutRect work = MonitorEnumerator.Enumerate().Primary.WorkArea;
            Assert.True(bounds.Left >= work.Left - 1 && bounds.Right <= work.Right + 1);
            Assert.True(bounds.Top >= work.Top - 1 && bounds.Bottom <= work.Bottom + 1);
            Assert.Contains("placement-size-sync", diagnostics);
            Assert.DoesNotContain("placement-size-sync-failure", diagnostics);
            Assert.True(saves >= savesBeforeWave);
        });
    }

    [Fact]
    public void startup_placement_before_show_verifies_the_native_frame_without_layout()
    {
        RunPerMonitorSta(() =>
        {
            AppSettings settings = ObservedSettings();
            var diagnostics = new List<string>();
            MainWindow main = CreateMainWindow();
            main.ApplyVisualSettings(settings);
            using var controller = new WindowPlacementController(
                main,
                () => settings,
                update => settings = update(settings),
                (category, detail) => diagnostics.Add(category + " " + detail?.Message),
                main.Dispatcher);

            Assert.True(controller.PositionWindow(), string.Join(" | ", diagnostics));
            Assert.False(main.IsLoaded);
            Assert.DoesNotContain("placement-size-sync-failure", diagnostics);
            uint dpi = WindowPlacementNativeApi.GetDpiForWindow(main.Handle);
            Assert.True(WindowPlacementNativeApi.TryGetBounds(main.Handle, out LayoutRect bounds));
            Assert.InRange(bounds.Width, Expected(224, dpi) - 1, Expected(224, dpi) + 1);

            main.Show();
            Pump(main);
            Assert.Equal(WindowSizeSyncStatus.Matched, main.SynchronizeWindowSize(apply: false).Status);
            Assert.True(ContentFitsClient(main));
        });
    }

    [Fact]
    public void split_background_follows_information_window_native_pixels()
    {
        RunPerMonitorSta(() =>
        {
            AppSettings settings = new AppSettings
            {
                UiScalePercent = 80,
                BackgroundEnabled = true,
                BackgroundFillMode = BackgroundFillMode.Solid,
                HideBackgroundBehindWindows = true,
                LayerMode = WindowLayerMode.AlwaysOnTop,
            }.Normalized();
            MainWindow main = CreateMainWindow();
            using var coordinator = new BackgroundLayerCoordinator(main, (_, _) => { }, main.Dispatcher, new NoOpLayerApi());
            main.ApplyVisualSettings(settings);
            coordinator.Apply(settings, LayerRepairTrigger.SettingsFlowCompleted);
            main.Show();
            coordinator.SyncNow();
            Pump(main);
            Assert.Equal(BackgroundPresentationMode.Split, coordinator.Mode);
            AssertSameNativeBounds(main.Handle, coordinator.BackgroundWindow.Handle);

            ForceStaleDpiFrame(main);
            coordinator.SyncNow();
            Pump(main);

            AssertSameNativeBounds(main.Handle, coordinator.BackgroundWindow.Handle);

            Assert.True(main.SynchronizeWindowSize(apply: true).IsSynchronized);
            coordinator.SyncNow();
            Pump(main);

            AssertNativeSize(main, 224, 176);
            AssertSameNativeBounds(main.Handle, coordinator.BackgroundWindow.Handle);
        });
    }

    [Fact]
    public void native_size_api_rejects_invalid_input_and_position_api_keeps_size()
    {
        Assert.False(WindowPlacementNativeApi.TrySetSize(IntPtr.Zero, 10, 10));
        Assert.False(WindowPlacementNativeApi.TrySetBounds(IntPtr.Zero, new LayoutRect(0, 0, 10, 10)));
        RunPerMonitorSta(() =>
        {
            MainWindow main = CreateMainWindow();
            main.ApplyVisualSettings(ObservedSettings());
            main.Show();
            Pump(main);
            IntPtr handle = main.Handle;
            Assert.True(WindowPlacementNativeApi.TryGetBounds(handle, out LayoutRect before));

            Assert.False(WindowPlacementNativeApi.TrySetSize(handle, 0, 10));
            Assert.False(WindowPlacementNativeApi.TrySetSize(handle, 10, -1));
            Assert.False(WindowPlacementNativeApi.TrySetBounds(handle, new LayoutRect(double.NaN, 0, 10, 10)));
            Assert.False(WindowPlacementNativeApi.TrySetBounds(handle, new LayoutRect(0, 0, double.PositiveInfinity, 10)));
            Assert.False(WindowPlacementNativeApi.TrySetBounds(handle, new LayoutRect(0, 0, 1e12, 10)));
            Assert.False(WindowPlacementNativeApi.TrySetBounds(handle, new LayoutRect(0, 0, 0, 10)));
            Assert.True(WindowPlacementNativeApi.TrySetPosition(handle, before.Left + 3, before.Top + 2));
            Pump(main);

            Assert.True(WindowPlacementNativeApi.TryGetBounds(handle, out LayoutRect after));
            Assert.Equal(before.Width, after.Width);
            Assert.Equal(before.Height, after.Height);
            Assert.Equal(before.Left + 3, after.Left);
        });
    }

    private static MainWindow CreateMainWindow()
    {
        var main = new MainWindow(new NoOpLayerApi(), (_, _) => { })
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000,
            Top = -10000,
            ShowActivated = false,
            Layer = LayerStrategy.Normal,
            Opacity = 0,
        };
        Track(main);
        return main;
    }

    [Theory]
    [InlineData(WidgetDisplayMode.Standard, 75, false)]
    [InlineData(WidgetDisplayMode.Standard, 80, false)]
    [InlineData(WidgetDisplayMode.Standard, 90, false)]
    [InlineData(WidgetDisplayMode.Standard, 200, false)]
    [InlineData(WidgetDisplayMode.Reduced, 90, false)]
    [InlineData(WidgetDisplayMode.Standard, 90, true)]
    [InlineData(WidgetDisplayMode.Reduced, 90, true)]
    public void repaired_dpi_frame_renders_the_complete_baseline_bitmap(WidgetDisplayMode mode, int scalePercent, bool fade)
    {
        RunPerMonitorSta(() =>
        {
            MainWindow main = CreateMainWindow();
            main.ViewModel = new MetricViewModel
            {
                CpuValue = "73",
                CpuUnit = "%",
                CpuFrequency = "3.20 GHz",
                CpuPower = "12 W",
                MemoryValue = "65",
                MemoryUnit = "%",
                MemoryUsage = "8/16 GiB",
                GpuValue = "42",
                GpuUnit = "%",
                GpuMemory = "2/8 GiB",
                GpuPower = "9 W",
                NetworkRx = "12 MB/s",
                NetworkTx = "3 MB/s",
                CpuBarFraction = 0.73,
                MemoryBarFraction = 0.65,
                GpuBarFraction = 0.42,
            };
            main.ApplyVisualSettings(new AppSettings
            {
                DisplayMode = mode,
                UiScalePercent = scalePercent,
                BackgroundEnabled = fade,
                BackgroundFillMode = BackgroundFillMode.EdgeFade,
                BackgroundEdgeFadePercent = 22,
                HideBackgroundBehindWindows = false,
            }.Normalized());
            main.Show();
            Pump(main);
            NativeBitmap baseline = RenderNativeBitmap(main);
            Assert.True(CountVisiblePixels(baseline.Pixels) > 1000, "baseline must contain actual text, values and bars");

            ForceStaleDpiFrame(main);
            NativeBitmap clipped = RenderNativeBitmap(main);
            Assert.True(clipped.Width < baseline.Width && clipped.Height < baseline.Height);
            Assert.False(ContentFitsClient(main));

            Assert.True(main.SynchronizeWindowSize(apply: true).IsSynchronized);
            Pump(main);
            NativeBitmap repaired = RenderNativeBitmap(main);

            Assert.True(ContentFitsClient(main));
            Assert.Equal(baseline.Width, repaired.Width);
            Assert.Equal(baseline.Height, repaired.Height);
            // The healthy rendering, not another size formula, is the oracle.
            // Full pixel equality detects clipped glyphs and shortened bars.
            Assert.Equal(baseline.Pixels, repaired.Pixels);
        });
    }

    private sealed record NativeBitmap(int Width, int Height, byte[] Pixels);

    private static NativeBitmap RenderNativeBitmap(MainWindow main)
    {
        main.UpdateLayout();
        Assert.True(WindowPlacementNativeApi.TryGetBounds(main.Handle, out LayoutRect native));
        int width = (int)native.Width;
        int height = (int)native.Height;
        uint dpi = WindowPlacementNativeApi.GetDpiForWindow(main.Handle);
        var bitmap = new RenderTargetBitmap(width, height, dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(main);
        var pixels = new byte[checked(width * height * 4)];
        bitmap.CopyPixels(pixels, width * 4, 0);
        return new NativeBitmap(width, height, pixels);
    }

    private static int CountVisiblePixels(byte[] pixels)
    {
        int count = 0;
        for (int index = 3; index < pixels.Length; index += 4)
        {
            if (pixels[index] != 0)
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>
    /// Sends the DPI change pair observed after a display connect: first down
    /// to a lower DPI with a correctly scaled rectangle, then back up to the
    /// window's real DPI with the lower DPI's pixel size as the suggested
    /// rectangle (100% to 175% on the affected PC). Half the real DPI keeps
    /// the intermediate pixel size exact, so on any test machine DPI WPF keeps
    /// Width, reports the real DPI, and the frame is smaller than the content.
    /// </summary>
    private static void ForceStaleDpiFrame(Window window)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        uint dpi = WindowPlacementNativeApi.GetDpiForWindow(handle);
        uint other = dpi / 2;
        Assert.True(WindowPlacementNativeApi.TryGetBounds(handle, out LayoutRect current));
        var scaled = new NativeRect(current, current.Width * other / dpi, current.Height * other / dpi);
        SendDpiChanged(handle, other, scaled);
        Pump(window);
        Assert.True(WindowPlacementNativeApi.TryGetBounds(handle, out LayoutRect intermediate));
        var stale = new NativeRect(intermediate, intermediate.Width, intermediate.Height);
        SendDpiChanged(handle, dpi, stale);
        Pump(window);
        Assert.Equal(dpi, WindowPlacementNativeApi.GetDpiForWindow(handle));
        Matrix transform = PresentationSource.FromVisual(window)!.CompositionTarget!.TransformToDevice;
        Assert.Equal(dpi / 96d, transform.M11, 6);
    }

    private static void SendDpiChanged(IntPtr handle, uint dpi, NativeRect rect)
    {
        _ = SendMessage(handle, WM_DPICHANGED, new IntPtr((dpi << 16) | dpi), ref rect);
    }

    private static double Expected(double dip, uint dpi) => Math.Round(dip * dpi / 96d, MidpointRounding.AwayFromZero);

    private static void AssertNativeSize(Window window, double widthDip, double heightDip)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        uint dpi = WindowPlacementNativeApi.GetDpiForWindow(handle);
        Assert.True(WindowPlacementNativeApi.TryGetBounds(handle, out LayoutRect bounds));
        Assert.InRange(bounds.Width, Expected(widthDip, dpi) - 1, Expected(widthDip, dpi) + 1);
        Assert.InRange(bounds.Height, Expected(heightDip, dpi) - 1, Expected(heightDip, dpi) + 1);
        Assert.InRange(window.ActualWidth, widthDip - (96d / dpi), widthDip + (96d / dpi));
        Assert.InRange(window.ActualHeight, heightDip - (96d / dpi), heightDip + (96d / dpi));
    }

    private static void AssertSameNativeBounds(IntPtr main, IntPtr background)
    {
        Assert.NotEqual(IntPtr.Zero, background);
        Assert.True(WindowPlacementNativeApi.TryGetBounds(main, out LayoutRect mainBounds));
        Assert.True(WindowPlacementNativeApi.TryGetBounds(background, out LayoutRect backgroundBounds));
        Assert.InRange(Math.Abs(mainBounds.Left - backgroundBounds.Left), 0, 1);
        Assert.InRange(Math.Abs(mainBounds.Top - backgroundBounds.Top), 0, 1);
        Assert.InRange(Math.Abs(mainBounds.Width - backgroundBounds.Width), 0, 1);
        Assert.InRange(Math.Abs(mainBounds.Height - backgroundBounds.Height), 0, 1);
    }

    private static bool ContentFitsClient(MainWindow main)
    {
        main.UpdateLayout();
        System.Windows.Rect content = main.WidgetRoot
            .TransformToAncestor(main)
            .TransformBounds(new System.Windows.Rect(main.WidgetRoot.RenderSize));
        const double tolerance = 0.5;
        return content.Left >= -tolerance
            && content.Top >= -tolerance
            && content.Right <= main.ActualWidth + tolerance
            && content.Bottom <= main.ActualHeight + tolerance;
    }

    private static void Pump(Window window)
    {
        for (int index = 0; index < 3; index++)
        {
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        }
    }

    [ThreadStatic]
    private static List<Window> trackedWindows;

    private static void Track(Window window) => (trackedWindows ??= []).Add(window);

    /// <summary>
    /// Runs on an STA thread whose windows are created per-monitor-v2 aware,
    /// matching the product manifest even when the test host is not.
    /// </summary>
    private static void RunPerMonitorSta(Action action)
    {
        Exception error = null;
        var thread = new Thread(() =>
        {
            IntPtr previous = SetThreadDpiAwarenessContext(PerMonitorAwareV2);
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                if (trackedWindows is not null)
                {
                    foreach (Window window in trackedWindows)
                    {
                        window.Close();
                    }
                    trackedWindows.Clear();
                }
                if (previous != IntPtr.Zero)
                {
                    _ = SetThreadDpiAwarenessContext(previous);
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
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public NativeRect(LayoutRect origin, double width, double height)
        {
            Left = (int)origin.Left;
            Top = (int)origin.Top;
            Right = Left + (int)Math.Round(width, MidpointRounding.AwayFromZero);
            Bottom = Top + (int)Math.Round(height, MidpointRounding.AwayFromZero);
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, ref NativeRect lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

    private sealed class NoOpLayerApi : IWindowLayerApi
    {
        public WindowStyleObservation GetExtendedStyle(IntPtr hWnd) => new(true, false, 0);

        public WindowPositionCallResult SetWindowPosition(IntPtr hWnd, IntPtr hWndInsertAfter, uint flags) =>
            new(true, 0);
    }
}
