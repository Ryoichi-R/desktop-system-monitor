using System;
using System.Collections.Generic;
using System.IO;
using DesktopSystemMonitor.App.Tray;
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
}
