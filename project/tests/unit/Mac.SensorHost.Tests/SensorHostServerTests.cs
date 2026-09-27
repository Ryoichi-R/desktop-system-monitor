using System.Diagnostics;

using DesktopSystemMonitor.Core.Platform;

using Xunit;

namespace DesktopSystemMonitor.Mac.SensorHost.Tests;

public sealed class SensorHostServerTests
{
    [Fact]
    public async Task Sample_Request_Returns_Unavailable_Response()
    {
        await using var input = new MemoryStream();
        await using var output = new MemoryStream();
        await SensorHostProtocol.WriteAsync(input, CreateRequest("sample", 12), CancellationToken.None);
        input.Position = 0;

        int exitCode = await SensorHostServer.RunAsync(input, output, CancellationToken.None);

        Assert.Equal(0, exitCode);
        output.Position = 0;
        SensorHostMessage response = (await SensorHostProtocol.ReadAsync(output, CancellationToken.None))!;
        Assert.Equal(12, response.Sequence);
        Assert.Equal(1, response.HostGeneration);
        Assert.Equal("unavailable", response.Status);
        Assert.Equal("native-metrics-not-implemented", response.ErrorCode);
        Assert.Equal(SensorHostMetricStatus.Unavailable, response.Metrics.CpuUtilizationPercent.Status);
    }

    [Fact]
    public async Task Unsupported_Request_Returns_Stable_Error()
    {
        await using var input = new MemoryStream();
        await using var output = new MemoryStream();
        await SensorHostProtocol.WriteAsync(input, CreateRequest("response", 13), CancellationToken.None);
        input.Position = 0;

        int exitCode = await SensorHostServer.RunAsync(input, output, CancellationToken.None);

        Assert.Equal(0, exitCode);
        output.Position = 0;
        SensorHostMessage response = (await SensorHostProtocol.ReadAsync(output, CancellationToken.None))!;
        Assert.Equal("unsupported-request", response.ErrorCode);
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
    public async Task Canceled_Request_Stops_Without_Writing()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await using var input = new MemoryStream();
        await using var output = new MemoryStream();

        int exitCode = await SensorHostServer.RunAsync(input, output, cancellation.Token);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public async Task Cancellation_During_Read_Exits_Gracefully()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
        await using var input = new CancellationAwareStream();
        await using var output = new MemoryStream();

        int exitCode = await Program.RunAsync(input, output, cancellation.Token);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public async Task Main_Uses_Standard_Stream_Factories_And_Runs_The_Server()
    {
        await using var input = new MemoryStream();
        await using var output = new MemoryStream();
        await SensorHostProtocol.WriteAsync(input, CreateRequest("sample", 20), CancellationToken.None);
        input.Position = 0;

        Func<Stream> previousInputFactory = Program.StandardInputFactory;
        Func<Stream> previousOutputFactory = Program.StandardOutputFactory;
        Program.StandardInputFactory = () => input;
        Program.StandardOutputFactory = () => output;
        try
        {
            Assert.Equal(0, await Program.Main());
        }
        finally
        {
            Program.StandardInputFactory = previousInputFactory;
            Program.StandardOutputFactory = previousOutputFactory;
        }

        output.Position = 0;
        SensorHostMessage response = (await SensorHostProtocol.ReadAsync(output, CancellationToken.None))!;
        Assert.Equal(20, response.Sequence);
        Assert.Equal("native-metrics-not-implemented", response.ErrorCode);
    }

    private static SensorHostMessage CreateRequest(string kind, long sequence) => new()
    {
        Version = SensorHostProtocol.CurrentVersion,
        Kind = kind,
        Sequence = sequence,
        HostGeneration = 1,
        Status = "ok",
        MonotonicFrequency = Stopwatch.Frequency,
        GeneratedAtMonotonicTicks = Stopwatch.GetTimestamp(),
        Metrics = SensorHostMetrics.Empty,
    };

    private sealed class CancellationAwareStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }
}
