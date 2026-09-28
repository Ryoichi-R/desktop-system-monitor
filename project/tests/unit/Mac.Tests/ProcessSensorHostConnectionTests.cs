using System.Diagnostics;
using DesktopSystemMonitor.Core.Platform;
using Xunit;

namespace DesktopSystemMonitor.Mac.Tests;

public sealed class ProcessSensorHostConnectionTests
{
    [MacFact]
    public async Task Redirected_Process_Roundtrips_A_Frame_And_Is_Reaped()
    {
        // cat echoes the frame; response validation belongs to MacSensorSession tests.
        await using var connection = new ProcessSensorHostConnection("/bin/cat");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var request = SensorHostProtocol.Unavailable(27, 3, "test");
        var response = await connection.ExchangeAsync(request, deadline.Token);
        Assert.Equal(27, response!.Sequence);
        Assert.Equal(3, response.HostGeneration);
        connection.Abort();
    }

    [MacFact]
    public async Task Immediate_Exit_Is_Detected_And_Disposed()
    {
        await using var connection = new ProcessSensorHostConnection("/usr/bin/true");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            Assert.Null(await connection.ExchangeAsync(SensorHostProtocol.Unavailable(1, 1, "test"), deadline.Token));
        }
        catch (IOException) { /* A broken stdin pipe is also an expected failure. */ }
    }

    [MacFact]
    public void Missing_Executable_Does_Not_Leave_A_Connection()
    {
        Assert.ThrowsAny<System.ComponentModel.Win32Exception>(() => new ProcessSensorHostConnection("/nonexistent-dsm-host"));
    }
}

internal sealed class MacFactAttribute : FactAttribute
{
    public MacFactAttribute()
    {
        if (!OperatingSystem.IsMacOS()) Skip = "macOS process integration test";
    }
}
