using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using DesktopSystemMonitor.Windows.Window;
using Xunit;

namespace DesktopSystemMonitor.App.Tests;

[CollectionDefinition("WpfWindow", DisableParallelization = true)]
public sealed class WpfWindowCollection;

[Collection("WpfWindow")]
public sealed class MainWindowLayerTests
{
    [Theory]
    [InlineData(RepairEntryPoint.LayerSetter)]
    [InlineData(RepairEntryPoint.SourceInitialized)]
    [InlineData(RepairEntryPoint.ExplicitReapply)]
    [InlineData(RepairEntryPoint.SessionUnlock)]
    [InlineData(RepairEntryPoint.SettingsFlowCompleted)]
    [InlineData(RepairEntryPoint.TraySelection)]
    [InlineData(RepairEntryPoint.Timer)]
    public void managed_native_exception_is_contained_deduplicated_and_recovers_for_each_entry(
        RepairEntryPoint entryPoint)
    {
        RunInSta(() =>
        {
            var api = new FakeWindowLayerApi();
            MainWindow window;
            if (entryPoint == RepairEntryPoint.SourceInitialized)
            {
                window = new MainWindow(api)
                {
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -10000,
                    Top = -10000,
                    ShowActivated = false,
                    Layer = LayerStrategy.TopMost,
                };
            }
            else
            {
                window = CreateVisibleWindow(api);
            }

            var diagnostics = new List<LayerDiagnosticEvent>();
            window.LayerDiagnosticOccurred += diagnostics.Add;
            try
            {
                if (entryPoint == RepairEntryPoint.SourceInitialized)
                {
                    api.ThrowOnSet = true;
                    window.Show();
                    window.ReapplyWindowStyles(LayerRepairTrigger.SourceInitialized);
                }
                else if (entryPoint == RepairEntryPoint.LayerSetter)
                {
                    api.ThrowOnSet = true;
                    window.SetLayer(LayerStrategy.TopMost, LayerRepairTrigger.LayerSetter);
                    window.SetLayer(LayerStrategy.TopMost, LayerRepairTrigger.LayerSetter);
                }
                else
                {
                    window.Layer = LayerStrategy.TopMost;
                    api.IsTopMost = false;
                    api.ThrowOnGet = true;
                    InvokeRepairEntry(window, entryPoint);
                    InvokeRepairEntry(window, entryPoint);
                }

                Assert.Equal(LayerStrategy.TopMost, window.Layer);
                Assert.True(window.Topmost);
                Assert.Single(
                    diagnostics,
                    item => item.Category == LayerDiagnosticCategory.ManagedException);

                api.ThrowOnSet = false;
                api.ThrowOnGet = false;
                api.IsTopMost = false;
                LayerRepairResult recovered = window.RunLayerRepairTick();

                Assert.Equal(LayerRepairOutcome.Healthy, recovered.Outcome);
                Assert.True(api.IsTopMost);
                Assert.Contains(
                    diagnostics,
                    item => item.Category == LayerDiagnosticCategory.RepairRecovered);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void topmost_set_before_show_does_not_create_or_call_native_handle()
    {
        RunInSta(() =>
        {
            var api = new FakeWindowLayerApi();
            var window = new MainWindow(api);
            try
            {
                window.Layer = LayerStrategy.TopMost;

                Assert.True(window.Topmost);
                Assert.Equal(IntPtr.Zero, new WindowInteropHelper(window).Handle);
                Assert.Equal(0, api.TotalCalls);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void real_hwnd_tracks_topmost_and_normal_styles()
    {
        RunInSta(() =>
        {
            var window = new MainWindow
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000,
                Top = -10000,
                ShowActivated = false,
                Layer = LayerStrategy.TopMost,
            };
            try
            {
                window.Show();
                WindowStyleObservation topMost = NativeWindowLayerApi.Instance.GetExtendedStyle(window.Handle);
                Assert.True(topMost.Succeeded);
                Assert.True(topMost.IsTopMost);

                window.Layer = LayerStrategy.Normal;
                WindowStyleObservation normal = NativeWindowLayerApi.Instance.GetExtendedStyle(window.Handle);
                Assert.True(normal.Succeeded);
                Assert.False(normal.IsTopMost);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void native_adapter_tracks_topmost_and_notopmost_on_plain_wpf_window()
    {
        RunInSta(() =>
        {
            var window = new Window
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000,
                Top = -10000,
                ShowActivated = false,
                ShowInTaskbar = false,
            };
            try
            {
                window.Show();
                IntPtr handle = new WindowInteropHelper(window).Handle;
                const uint flags = WindowInterop.SWP_NOMOVE
                    | WindowInterop.SWP_NOSIZE
                    | WindowInterop.SWP_NOACTIVATE;

                WindowPositionCallResult topMost = NativeWindowLayerApi.Instance.SetWindowPosition(
                    handle,
                    WindowInterop.HWND_TOPMOST,
                    flags);
                WindowStyleObservation topMostStyle = NativeWindowLayerApi.Instance.GetExtendedStyle(handle);
                WindowPositionCallResult normal = NativeWindowLayerApi.Instance.SetWindowPosition(
                    handle,
                    WindowInterop.HWND_NOTOPMOST,
                    flags);
                WindowStyleObservation normalStyle = NativeWindowLayerApi.Instance.GetExtendedStyle(handle);

                Assert.True(topMost.Succeeded, $"SetWindowPos(HWND_TOPMOST) failed: {topMost.ErrorCode}");
                Assert.True(topMostStyle.Succeeded);
                Assert.True(topMostStyle.IsTopMost);
                Assert.True(normal.Succeeded, $"SetWindowPos(HWND_NOTOPMOST) failed: {normal.ErrorCode}");
                Assert.True(normalStyle.Succeeded);
                Assert.False(normalStyle.IsTopMost);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void suspended_tick_makes_no_native_calls_and_unlock_reapplies_once()
    {
        RunInSta(() =>
        {
            var api = new FakeWindowLayerApi();
            var window = CreateVisibleWindow(api);
            try
            {
                api.ResetCalls();
                window.SetLayerRepairSuspended(true);

                LayerRepairResult result = window.RunLayerRepairTick();
                window.ReapplyWindowStyles();
                IntPtr windowPosPointer = CreateWindowPositionPointer(new IntPtr(44));
                try
                {
                    Assert.False(window.RewriteWindowPositionForLayer(windowPosPointer));
                }
                finally
                {
                    Marshal.FreeHGlobal(windowPosPointer);
                }

                Assert.Equal(LayerRepairOutcome.Skipped, result.Outcome);
                Assert.Equal(0, api.TotalCalls);

                window.SetLayerRepairSuspended(false);
                window.ReapplyWindowStyles(LayerRepairTrigger.SessionUnlock);
                Assert.Equal(1, api.SetCalls);
                Assert.Equal(1, api.GetStyleCalls);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void bottommost_to_normal_explicit_transition_applies_notopmost_once()
    {
        RunInSta(() =>
        {
            var api = new FakeWindowLayerApi();
            var window = CreateVisibleWindow(api);
            try
            {
                api.ResetCalls();

                window.Layer = LayerStrategy.Normal;

                Assert.Equal(LayerStrategy.Normal, window.Layer);
                Assert.False(window.Topmost);
                Assert.Equal([WindowInterop.HWND_NOTOPMOST], api.InsertAfterValues);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void failed_bottommost_to_normal_transition_retries_on_the_next_tick()
    {
        RunInSta(() =>
        {
            var api = new FakeWindowLayerApi();
            var window = CreateVisibleWindow(api);
            try
            {
                api.ResetCalls();
                api.NormalFailuresRemaining = 1;

                window.Layer = LayerStrategy.Normal;

                Assert.Equal(LayerStrategy.Normal, window.Layer);
                Assert.Equal([WindowInterop.HWND_NOTOPMOST], api.InsertAfterValues);

                api.ResetCalls();
                LayerRepairResult retry = window.RunLayerRepairTick();

                Assert.Equal(LayerRepairOutcome.Healthy, retry.Outcome);
                Assert.Equal([WindowInterop.HWND_NOTOPMOST], api.InsertAfterValues);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void invalid_hwnd_clears_forced_transition_without_retrying_set_window_pos()
    {
        RunInSta(() =>
        {
            var api = new FakeWindowLayerApi();
            var window = CreateVisibleWindow(api);
            try
            {
                api.ResetCalls();
                api.NormalSetErrorCode = WindowInterop.ERROR_INVALID_WINDOW_HANDLE;

                window.Layer = LayerStrategy.Normal;

                Assert.Equal([WindowInterop.HWND_NOTOPMOST], api.InsertAfterValues);
                api.NormalSetErrorCode = 0;
                api.ResetCalls();

                LayerRepairResult result = window.RunLayerRepairTick();

                Assert.Equal(LayerRepairOutcome.Healthy, result.Outcome);
                Assert.Equal(0, api.SetCalls);
                Assert.Equal(1, api.GetStyleCalls);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void failed_normal_transition_survives_hide_and_show_episode_reset()
    {
        RunInSta(() =>
        {
            var api = new FakeWindowLayerApi();
            var window = CreateVisibleWindow(api);
            try
            {
                api.NormalFailuresRemaining = 1;
                window.Layer = LayerStrategy.Normal;
                window.Hide();
                api.ResetCalls();

                window.Show();
                window.ReapplyWindowStyles();

                Assert.Equal([WindowInterop.HWND_NOTOPMOST], api.InsertAfterValues);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void failed_normal_transition_survives_session_suspension_reset()
    {
        RunInSta(() =>
        {
            var api = new FakeWindowLayerApi();
            var window = CreateVisibleWindow(api);
            try
            {
                api.NormalFailuresRemaining = 1;
                window.Layer = LayerStrategy.Normal;
                window.SetLayerRepairSuspended(true);
                api.ResetCalls();

                window.SetLayerRepairSuspended(false);
                window.ReapplyWindowStyles(LayerRepairTrigger.SessionUnlock);

                Assert.Equal([WindowInterop.HWND_NOTOPMOST], api.InsertAfterValues);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void hidden_strategy_change_is_forced_after_show()
    {
        RunInSta(() =>
        {
            var api = new FakeWindowLayerApi();
            var window = CreateVisibleWindow(api);
            try
            {
                window.Hide();
                api.ResetCalls();

                window.Layer = LayerStrategy.Normal;

                Assert.Equal(0, api.TotalCalls);
                window.Show();
                window.ReapplyWindowStyles();
                Assert.Equal([WindowInterop.HWND_NOTOPMOST], api.InsertAfterValues);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void hidden_window_skips_explicit_and_timer_repairs_until_shown_again()
    {
        RunInSta(() =>
        {
            var api = new FakeWindowLayerApi();
            var window = CreateVisibleWindow(api);
            try
            {
                window.Hide();
                api.ResetCalls();

                window.ReapplyWindowStyles();
                LayerRepairResult hiddenTick = window.RunLayerRepairTick();

                Assert.Equal(LayerRepairOutcome.Skipped, hiddenTick.Outcome);
                Assert.Equal(0, api.TotalCalls);

                window.Show();
                window.ReapplyWindowStyles();
                Assert.Equal(1, api.SetCalls);
                Assert.Equal(1, api.GetStyleCalls);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void bottommost_falls_back_only_after_three_failed_timer_ticks()
    {
        RunInSta(() =>
        {
            var api = new FakeWindowLayerApi { FailBottomMost = true };
            var window = CreateVisibleWindow(api);
            int fallbackEvents = 0;
            window.LayerFallbackOccurred += () => throw new InvalidOperationException("injected subscriber");
            window.LayerFallbackOccurred += () => fallbackEvents++;
            try
            {
                api.ResetCalls();
                _ = window.RunLayerRepairTick();
                _ = window.RunLayerRepairTick();
                Assert.Equal(LayerStrategy.BottomMost, window.Layer);
                Assert.Equal(0, fallbackEvents);

                _ = window.RunLayerRepairTick();

                Assert.Equal(LayerStrategy.Normal, window.Layer);
                Assert.False(window.Topmost);
                Assert.Equal(1, fallbackEvents);
                Assert.Contains(api.InsertAfterValues, value => value == WindowInterop.HWND_NOTOPMOST);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void fallback_native_exceptions_preserve_bottommost_until_a_later_success(
        bool throwOnSet,
        bool throwOnStyleRead)
    {
        RunInSta(() =>
        {
            var api = new FakeWindowLayerApi
            {
                FailBottomMost = true,
                ThrowOnNormalSet = throwOnSet,
                ThrowOnNormalStyleRead = throwOnStyleRead,
            };
            var window = CreateVisibleWindow(api);
            int fallbackEvents = 0;
            window.LayerFallbackOccurred += () => fallbackEvents++;
            try
            {
                _ = window.RunLayerRepairTick();
                _ = window.RunLayerRepairTick();
                _ = window.RunLayerRepairTick();

                Assert.Equal(LayerStrategy.BottomMost, window.Layer);
                Assert.Equal(0, fallbackEvents);

                api.ThrowOnNormalSet = false;
                api.ThrowOnNormalStyleRead = false;
                _ = window.RunLayerRepairTick();

                Assert.Equal(LayerStrategy.Normal, window.Layer);
                Assert.Equal(1, fallbackEvents);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void managed_native_exception_does_not_escape_and_next_tick_recovers()
    {
        RunInSta(() =>
        {
            var api = new FakeWindowLayerApi { ThrowOnGet = true };
            var window = new MainWindow(api)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000,
                Top = -10000,
                ShowActivated = false,
                Layer = LayerStrategy.TopMost,
            };
            try
            {
                window.Show();
                api.ThrowOnGet = false;
                api.IsTopMost = false;
                api.ResetCalls();

                LayerRepairResult result = window.RunLayerRepairTick();

                Assert.Equal(LayerRepairOutcome.Healthy, result.Outcome);
                Assert.True(api.IsTopMost);
                Assert.Equal(1, api.SetCalls);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void layer_setter_rejects_background_dispatcher_access()
    {
        RunInSta(() =>
        {
            var window = new MainWindow();
            Exception error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    window.Layer = LayerStrategy.TopMost;
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            });
            thread.Start();
            thread.Join();
            window.Close();

            Assert.IsType<InvalidOperationException>(error);
        });
    }

    [Fact]
    public void click_through_setter_applies_immediately_when_visible()
    {
        RunInSta(() =>
        {
            var applied = new List<bool>();
            var window = new MainWindow(new FakeWindowLayerApi(), (_, value) => applied.Add(value))
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000,
                Top = -10000,
                ShowActivated = false,
            };
            try
            {
                window.Show();
                applied.Clear();

                window.ClickThrough = false;

                Assert.Equal([false], applied);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void hidden_click_through_change_is_applied_when_shown()
    {
        RunInSta(() =>
        {
            var applied = new List<bool>();
            var window = new MainWindow(new FakeWindowLayerApi(), (_, value) => applied.Add(value))
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000,
                Top = -10000,
                ShowActivated = false,
            };
            try
            {
                window.Show();
                window.Hide();
                applied.Clear();

                window.ClickThrough = false;

                Assert.Empty(applied);
                window.Show();
                Assert.Equal([false], applied);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static MainWindow CreateVisibleWindow(FakeWindowLayerApi api)
    {
        var window = new MainWindow(api)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000,
            Top = -10000,
            ShowActivated = false,
            Layer = LayerStrategy.BottomMost,
        };
        window.Show();
        return window;
    }

    private static void InvokeRepairEntry(MainWindow window, RepairEntryPoint entryPoint)
    {
        switch (entryPoint)
        {
            case RepairEntryPoint.ExplicitReapply:
                window.ReapplyWindowStyles(LayerRepairTrigger.ExplicitReapply);
                break;
            case RepairEntryPoint.SessionUnlock:
                window.ReapplyWindowStyles(LayerRepairTrigger.SessionUnlock);
                break;
            case RepairEntryPoint.SettingsFlowCompleted:
                window.ReapplyWindowStyles(LayerRepairTrigger.SettingsFlowCompleted);
                break;
            case RepairEntryPoint.TraySelection:
                window.SetLayer(window.Layer, LayerRepairTrigger.TraySelection);
                break;
            case RepairEntryPoint.Timer:
                _ = window.RunLayerRepairTick();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(entryPoint));
        }
    }

    private static IntPtr CreateWindowPositionPointer(IntPtr insertAfter)
    {
        var position = new WindowInterop.WINDOWPOS
        {
            hwnd = new IntPtr(10),
            hwndInsertAfter = insertAfter,
            flags = WindowInterop.SWP_NOACTIVATE,
        };
        IntPtr pointer = Marshal.AllocHGlobal(Marshal.SizeOf<WindowInterop.WINDOWPOS>());
        Marshal.StructureToPtr(position, pointer, fDeleteOld: false);
        return pointer;
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
        public bool IsTopMost { get; set; }
        public bool FailBottomMost { get; set; }
        public bool ThrowOnGet { get; set; }
        public bool ThrowOnSet { get; set; }
        public bool ThrowOnNormalSet { get; set; }
        public bool ThrowOnNormalStyleRead { get; set; }
        public int NormalFailuresRemaining { get; set; }
        public int NormalSetErrorCode { get; set; }
        public int GetStyleCalls { get; private set; }
        public int SetCalls { get; private set; }
        public int TotalCalls => GetStyleCalls + SetCalls;
        public List<IntPtr> InsertAfterValues { get; } = [];
        private bool _normalStyleReadPending;

        public WindowStyleObservation GetExtendedStyle(IntPtr hWnd)
        {
            GetStyleCalls++;
            if (ThrowOnGet)
            {
                throw new InvalidOperationException("injected");
            }
            if (_normalStyleReadPending)
            {
                _normalStyleReadPending = false;
                if (ThrowOnNormalStyleRead)
                {
                    throw new InvalidOperationException("injected normal style read");
                }
            }
            return new WindowStyleObservation(true, IsTopMost, 0);
        }

        public WindowPositionCallResult SetWindowPosition(IntPtr hWnd, IntPtr hWndInsertAfter, uint flags)
        {
            SetCalls++;
            InsertAfterValues.Add(hWndInsertAfter);
            if (ThrowOnSet)
            {
                throw new InvalidOperationException("injected set");
            }
            if (FailBottomMost && hWndInsertAfter == WindowInterop.HWND_BOTTOM)
            {
                return new WindowPositionCallResult(false, 5);
            }
            if (hWndInsertAfter == WindowInterop.HWND_NOTOPMOST && ThrowOnNormalSet)
            {
                throw new InvalidOperationException("injected normal set");
            }
            if (hWndInsertAfter == WindowInterop.HWND_NOTOPMOST && NormalFailuresRemaining > 0)
            {
                NormalFailuresRemaining--;
                return new WindowPositionCallResult(false, 5);
            }
            if (hWndInsertAfter == WindowInterop.HWND_NOTOPMOST && NormalSetErrorCode != 0)
            {
                return new WindowPositionCallResult(false, NormalSetErrorCode);
            }
            IsTopMost = hWndInsertAfter == WindowInterop.HWND_TOPMOST;
            _normalStyleReadPending = hWndInsertAfter == WindowInterop.HWND_NOTOPMOST;
            return new WindowPositionCallResult(true, 0);
        }

        public void ResetCalls()
        {
            GetStyleCalls = 0;
            SetCalls = 0;
            InsertAfterValues.Clear();
        }
    }

    public enum RepairEntryPoint
    {
        LayerSetter,
        SourceInitialized,
        ExplicitReapply,
        SessionUnlock,
        SettingsFlowCompleted,
        TraySelection,
        Timer,
    }
}
