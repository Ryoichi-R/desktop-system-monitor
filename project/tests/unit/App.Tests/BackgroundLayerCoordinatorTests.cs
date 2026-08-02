using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using DesktopSystemMonitor.App.Startup;
using DesktopSystemMonitor.Core.Settings;
using DesktopSystemMonitor.Windows.Window;
using Xunit;

namespace DesktopSystemMonitor.App.Tests;

[Collection("WpfWindow")]
public sealed class BackgroundLayerCoordinatorTests
{
    [Fact]
    public void split_tracks_main_and_normal_mode_returns_to_inline_without_losing_preference()
    {
        RunInSta(() =>
        {
            var mainApi = new FakeWindowLayerApi();
            var backgroundApi = new FakeWindowLayerApi();
            var main = new MainWindow(mainApi)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000,
                Top = -9000,
                ShowActivated = false,
                Layer = LayerStrategy.TopMost,
            };
            using var coordinator = new BackgroundLayerCoordinator(main, (_, _) => { }, main.Dispatcher, backgroundApi);
            var settings = new AppSettings
            {
                BackgroundEnabled = true,
                BackgroundColor = "#FF102030",
                BackgroundOpacity = 0.6,
                BackgroundFillMode = BackgroundFillMode.EdgeFade,
                BackgroundEdgeFadePercent = 25,
                HideBackgroundBehindWindows = true,
                LayerMode = WindowLayerMode.AlwaysOnTop,
            }.Normalized();
            try
            {
                main.ApplyVisualSettings(settings);
                coordinator.Apply(settings, LayerRepairTrigger.SourceInitialized);
                main.Show();
                coordinator.SyncNow();

                Assert.Equal(BackgroundPresentationMode.Split, coordinator.Mode);
                Assert.True(coordinator.BackgroundWindow.IsVisible);
                Assert.Equal(main.Left, coordinator.BackgroundWindow.Left);
                Assert.Equal(main.Top, coordinator.BackgroundWindow.Top);
                Assert.Equal(main.Width, coordinator.BackgroundWindow.Width);
                Assert.Equal(main.Height, coordinator.BackgroundWindow.Height);
                Assert.InRange(main.Width, 419, 421);
                Assert.InRange(main.Height, 329, 331);
                Assert.Contains(WindowInterop.HWND_BOTTOM, backgroundApi.InsertAfterValues);
                Assert.Equal(Colors.Transparent, Assert.IsType<SolidColorBrush>(main.BackgroundSurface.Background).Color);

                coordinator.SetWidgetVisible(false);
                Assert.False(main.IsVisible);
                Assert.False(coordinator.BackgroundWindow.IsVisible);
                coordinator.SetWidgetVisible(true);
                Assert.True(main.IsVisible);
                Assert.True(coordinator.BackgroundWindow.IsVisible);

                coordinator.Apply(settings with { LayerMode = WindowLayerMode.Normal }, LayerRepairTrigger.TraySelection);

                Assert.Equal(BackgroundPresentationMode.Inline, coordinator.Mode);
                Assert.False(coordinator.BackgroundWindow.IsVisible);
                Assert.True(settings.HideBackgroundBehindWindows);
                Assert.NotEqual(Colors.Transparent, Assert.IsType<SolidColorBrush>(main.BackgroundSurface.Background).Color);
                Assert.IsType<LinearGradientBrush>(main.BackgroundFadeHost.OpacityMask);
                Assert.IsType<LinearGradientBrush>(main.BackgroundSurface.OpacityMask);
            }
            finally
            {
                main.Close();
            }
        });
    }

    [Theory]
    [InlineData(BackgroundFillMode.Solid, 150, 220)]
    [InlineData(BackgroundFillMode.EdgeFade, 225, 330)]
    public void split_background_tracks_reduced_solid_and_edge_fade_bounds(
        BackgroundFillMode fillMode,
        double expectedWidth,
        double expectedHeight)
    {
        RunInSta(() =>
        {
            var main = new MainWindow(new FakeWindowLayerApi())
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000,
                Top = -9000,
                ShowActivated = false,
                Layer = LayerStrategy.TopMost,
            };
            using var coordinator = new BackgroundLayerCoordinator(
                main,
                (_, _) => { },
                main.Dispatcher,
                new FakeWindowLayerApi());
            var settings = new AppSettings
            {
                DisplayMode = WidgetDisplayMode.Reduced,
                BackgroundEnabled = true,
                BackgroundFillMode = fillMode,
                BackgroundEdgeFadePercent = 25,
                HideBackgroundBehindWindows = true,
                LayerMode = WindowLayerMode.AlwaysOnTop,
            }.Normalized();
            try
            {
                main.ApplyVisualSettings(settings);
                coordinator.Apply(settings, LayerRepairTrigger.SettingsFlowCompleted);
                main.Show();
                coordinator.SyncNow();

                Assert.Equal(BackgroundPresentationMode.Split, coordinator.Mode);
                Assert.InRange(Math.Abs(main.Width - expectedWidth), 0, 0.5);
                Assert.InRange(Math.Abs(main.Height - expectedHeight), 0, 0.5);
                Assert.Equal(main.Left, coordinator.BackgroundWindow.Left);
                Assert.Equal(main.Top, coordinator.BackgroundWindow.Top);
                Assert.Equal(main.Width, coordinator.BackgroundWindow.Width);
                Assert.Equal(main.Height, coordinator.BackgroundWindow.Height);
                Assert.Equal(Colors.Transparent, Assert.IsType<SolidColorBrush>(main.BackgroundSurface.Background).Color);
            }
            finally
            {
                main.Close();
            }
        });
    }

    [Fact]
    public void repeated_bottommost_failure_suppresses_only_background_and_recovers()
    {
        RunInSta(() =>
        {
            var api = new FakeWindowLayerApi { FailBottomMost = true };
            var diagnostics = new List<string>();
            var window = new BackgroundWindow((category, _) => diagnostics.Add(category), api)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000,
                Top = -10000,
            };
            try
            {
                window.ApplyAppearance(new AppSettings
                {
                    BackgroundEnabled = true,
                    BackgroundOpacity = 0.7,
                }.Normalized());
                window.SetPresentationRequested(true, LayerRepairTrigger.SourceInitialized);
                _ = window.RunRepairTick();
                _ = window.RunRepairTick();
                _ = window.RunRepairTick();

                Assert.True(window.IsVisible);
                Assert.True(window.LayerFailureSuppressed);
                Assert.Equal(0, window.Opacity);
                Assert.Contains("background-layer-suppressed", diagnostics);

                api.FailBottomMost = false;
                _ = window.RunRepairTick();

                Assert.False(window.LayerFailureSuppressed);
                Assert.Equal(1, window.Opacity);
                Assert.Contains("background-layer-recovered", diagnostics);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void resume_after_suspend_reapplies_bottommost_exactly_once()
    {
        RunInSta(() =>
        {
            var api = new FakeWindowLayerApi();
            var window = new BackgroundWindow((_, _) => { }, api)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000,
                Top = -10000,
            };
            try
            {
                window.SetPresentationRequested(true, LayerRepairTrigger.SourceInitialized);
                window.SetRepairSuspended(true, LayerRepairTrigger.ExplicitReapply);
                int callsBeforeResume = api.InsertAfterValues.Count;

                // BottomMost never emits a RepairRecovered diagnostic (see
                // WindowLayerRepairEngine.RegisterSuccess), so the trigger
                // argument threading through SetRepairSuspended is verified by
                // call count here: exactly one native apply on resume, not the
                // zero (stuck-suspended) or two (double-reapply) regressions.
                window.SetRepairSuspended(false, LayerRepairTrigger.SettingsFlowCompleted);

                Assert.Equal(1, api.InsertAfterValues.Count - callsBeforeResume);
                Assert.Equal(WindowInterop.HWND_BOTTOM, api.InsertAfterValues[^1]);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void split_background_uses_the_configured_edge_fade_brush()
    {
        RunInSta(() =>
        {
            var window = new BackgroundWindow((_, _) => { }, new FakeWindowLayerApi());
            try
            {
                window.ApplyAppearance(new AppSettings
                {
                    BackgroundColor = "#FF102030",
                    BackgroundOpacity = 0.6,
                    BackgroundFillMode = BackgroundFillMode.EdgeFade,
                    BackgroundEdgeFadePercent = 40,
                }.Normalized());

                var horizontal = Assert.IsType<LinearGradientBrush>(window.BackgroundFadeHost.OpacityMask);
                var vertical = Assert.IsType<LinearGradientBrush>(window.BackgroundRoot.OpacityMask);
                Assert.Equal(2d / 9d, horizontal.GradientStops[1].Offset, 10);
                Assert.Equal(7d / 9d, horizontal.GradientStops[^2].Offset, 10);
                Assert.Equal(2d / 9d, vertical.GradientStops[1].Offset, 10);
                Assert.Equal(7d / 9d, vertical.GradientStops[^2].Offset, 10);
                Assert.Equal(0, horizontal.GradientStops[0].Color.A);
                Assert.Equal(0, horizontal.GradientStops[^1].Color.A);
                Assert.Equal(0, vertical.GradientStops[0].Color.A);
                Assert.Equal(0, vertical.GradientStops[^1].Color.A);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static void RunInSta(Action action)
    {
        Exception error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
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

    private sealed class FakeWindowLayerApi : IWindowLayerApi
    {
        public bool IsTopMost { get; private set; }
        public bool FailBottomMost { get; set; }
        public List<IntPtr> InsertAfterValues { get; } = [];

        public WindowStyleObservation GetExtendedStyle(IntPtr hWnd) => new(true, IsTopMost, 0);

        public WindowPositionCallResult SetWindowPosition(IntPtr hWnd, IntPtr hWndInsertAfter, uint flags)
        {
            InsertAfterValues.Add(hWndInsertAfter);
            if (FailBottomMost && hWndInsertAfter == WindowInterop.HWND_BOTTOM)
            {
                return new WindowPositionCallResult(false, 5);
            }
            IsTopMost = hWndInsertAfter == WindowInterop.HWND_TOPMOST;
            return new WindowPositionCallResult(true, 0);
        }
    }
}
