using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using DesktopSystemMonitor.App.Tray;
using DesktopSystemMonitor.Core.Settings;
using DesktopSystemMonitor.Windows.Window;
using Xunit;

namespace DesktopSystemMonitor.App.Tests;

public sealed class TrayIconTests
{
    [Fact]
    public void generated_icon_contains_required_high_dpi_frames()
    {
        byte[] icon = NotifyIconController.CreateDefaultIconData();
        using var stream = new MemoryStream(icon);
        using var reader = new BinaryReader(stream);

        Assert.Equal(0, reader.ReadUInt16());
        Assert.Equal(1, reader.ReadUInt16());
        ushort count = reader.ReadUInt16();
        Assert.Equal(8, count);
        var sizes = new List<int>();
        for (int index = 0; index < count; index++)
        {
            sizes.Add(reader.ReadByte());
            Assert.Equal(sizes[index], reader.ReadByte());
            stream.Position += 14;
        }
        Assert.Equal([0, 128, 64, 48, 32, 24, 20, 16], sizes);
    }

    [Fact]
    public void display_mode_submenu_has_exactly_two_expected_items()
    {
        RunInSta(() =>
        {
            using var tray = new NotifyIconController();

            Assert.Equal(2, tray.DisplayModeHeader.DropDownItems.Count);
            Assert.Same(tray.StandardDisplayModeItem, tray.DisplayModeHeader.DropDownItems[0]);
            Assert.Same(tray.ReducedDisplayModeItem, tray.DisplayModeHeader.DropDownItems[1]);
            Assert.Equal("通常幅（280 DIP）", tray.StandardDisplayModeItem.Text);
            Assert.Equal("縮小表示（150 DIP）", tray.ReducedDisplayModeItem.Text);
        });
    }

    [Fact]
    public void display_mode_checks_are_always_exclusive()
    {
        RunInSta(() =>
        {
            using var tray = new NotifyIconController();

            tray.SetDisplayMode(WidgetDisplayMode.Standard);
            Assert.True(tray.StandardDisplayModeItem.Checked);
            Assert.False(tray.ReducedDisplayModeItem.Checked);

            tray.SetDisplayMode(WidgetDisplayMode.Reduced);
            Assert.False(tray.StandardDisplayModeItem.Checked);
            Assert.True(tray.ReducedDisplayModeItem.Checked);

            tray.SetDisplayMode((WidgetDisplayMode)999);
            Assert.True(tray.StandardDisplayModeItem.Checked);
            Assert.False(tray.ReducedDisplayModeItem.Checked);
        });
    }

    [Fact]
    public void each_display_mode_click_raises_one_request()
    {
        RunInSta(() =>
        {
            using var tray = new NotifyIconController();
            var requested = new List<WidgetDisplayMode>();
            tray.DisplayModeRequested += requested.Add;

            tray.StandardDisplayModeItem.PerformClick();
            tray.ReducedDisplayModeItem.PerformClick();

            Assert.Equal([WidgetDisplayMode.Standard, WidgetDisplayMode.Reduced], requested);
        });
    }

    [Fact]
    public void editing_state_disables_display_mode_and_set_state_contract_remains_compatible()
    {
        RunInSta(() =>
        {
            using var tray = new NotifyIconController();

            tray.SetEditingEnabled(false);
            Assert.False(tray.DisplayModeHeader.Enabled);
            tray.SetEditingEnabled(true);
            Assert.True(tray.DisplayModeHeader.Enabled);

            tray.SetState(clickThrough: true, startup: false, LayerStrategy.TopMost);
            Assert.NotNull(typeof(NotifyIconController).GetMethod(
                nameof(NotifyIconController.SetState),
                [typeof(bool), typeof(bool), typeof(LayerStrategy)]));
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
