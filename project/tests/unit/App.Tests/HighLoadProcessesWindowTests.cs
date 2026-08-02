using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using DesktopSystemMonitor.Windows.Window;
using Xunit;

namespace DesktopSystemMonitor.App.Tests;

[Collection("WpfWindow")]
public sealed class HighLoadProcessesWindowTests
{
    [Fact]
    public void window_is_unowned_centers_on_screen_and_keeps_a_taskbar_entry()
    {
        RunInSta(() =>
        {
            var window = new HighLoadProcessesWindow();
            try
            {
                Assert.Null(window.Owner);
                Assert.Equal(WindowStartupLocation.CenterScreen, window.WindowStartupLocation);
                Assert.True(window.ShowInTaskbar);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void shown_window_has_no_native_owner_handle()
    {
        RunInSta(() =>
        {
            var window = new HighLoadProcessesWindow
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000,
                Top = -10000,
                ShowActivated = false,
            };
            try
            {
                window.Show();

                IntPtr handle = new WindowInteropHelper(window).Handle;

                Assert.Equal(IntPtr.Zero, WindowInterop.GetWindow(handle, WindowInterop.GW_OWNER));
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
}
