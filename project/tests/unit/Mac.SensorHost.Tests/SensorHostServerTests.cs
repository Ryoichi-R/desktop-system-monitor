using DesktopSystemMonitor.Core.Platform;
using DesktopSystemMonitor.Mac.SensorHost;

using Xunit;

namespace DesktopSystemMonitor.Mac.SensorHost.Tests;

public sealed class SensorHostServerTests
{
    [Fact]
    public async Task Sample_Request_Returns_Unavailable_Response()
    {
        await using var input = new MemoryStream();
        await using var output = new MemoryStream();
        await SensorHostProtocol.WriteAsync(input, new SensorHostMessage
        {
            Version = SensorHostProtocol.CurrentVersion,
            Kind = "sample",
            Sequence = 12,
            Status = "unavailable",
            Metrics = SensorHostMetrics.Empty,
        }, CancellationToken.None);
        input.Position = 0;

        int exitCode = await Program.RunAsync(input, output, CancellationToken.None);

        Assert.Equal(0, exitCode);
        output.Position = 0;
        SensorHostMessage response = (await SensorHostProtocol.ReadAsync(output, CancellationToken.None))!;
        Assert.Equal(12, response.Sequence);
        Assert.Equal("unavailable", response.Status);
        Assert.Equal("native-metrics-not-implemented", response.ErrorCode);
    }

    [Fact]
    public async Task Invalid_Request_Returns_Failure_Exit_Code()
    {
        await using var input = new MemoryStream("not-json"u8.ToArray());
        await using var output = new MemoryStream();

        int exitCode = await SensorHostServer.RunAsync(input, output, CancellationToken.None);

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task Unsupported_Request_Returns_Unavailable_Response()
    {
        await using var input = new MemoryStream();
        await using var output = new MemoryStream();
        await SensorHostProtocol.WriteAsync(input, new SensorHostMessage
        {
            Version = SensorHostProtocol.CurrentVersion,
            Kind = "response",
            Sequence = 13,
            Status = "unavailable",
            Metrics = SensorHostMetrics.Empty,
        }, CancellationToken.None);
        input.Position = 0;

        int exitCode = await Program.RunAsync(input, output, CancellationToken.None);

        Assert.Equal(0, exitCode);
        output.Position = 0;
        SensorHostMessage response = (await SensorHostProtocol.ReadAsync(output, CancellationToken.None))!;
        Assert.Equal("unsupported-request", response.ErrorCode);
    }

    [Fact]
    public async Task Canceled_Request_Stops_Without_Writing()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await using var input = new MemoryStream();
        await using var output = new MemoryStream();

        int exitCode = await Program.RunAsync(input, output, cancellation.Token);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, output.Length);
    }
}
