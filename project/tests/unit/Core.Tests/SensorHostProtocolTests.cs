using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Nodes;

using DesktopSystemMonitor.Core.Platform;

using Xunit;

namespace DesktopSystemMonitor.Core.Tests;

public sealed class SensorHostProtocolTests
{
    [Fact]
    public async Task RoundTrip_Preserves_PerMetric_Status_Values_And_Identities()
    {
        SensorHostMessage message = CreateResponse();
        await using var stream = new MemoryStream();

        await SensorHostProtocol.WriteAsync(stream, message, CancellationToken.None);
        stream.Position = 0;
        SensorHostMessage actual = (await SensorHostProtocol.ReadAsync(stream, CancellationToken.None))!;

        Assert.Equal(JsonSerializer.Serialize(message), JsonSerializer.Serialize(actual));
        Assert.Equal(SensorHostMetricStatus.Unavailable, actual.Metrics.CpuPowerWatts.Status);
        Assert.Null(actual.Metrics.CpuPowerWatts.Value);
        Assert.Equal("en0", actual.Metrics.NetworkInterfaces.Values[0].Name);
        Assert.Equal(10d, actual.Metrics.NetworkInterfaces.Values[0].ReceiveBytesPerSecond.Value);
    }

    [Fact]
    public async Task Read_Rejects_OverSized_Frame_Before_Allocating_Payload()
    {
        await using var stream = new MemoryStream();
        byte[] header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, SensorHostProtocol.MaxPayloadBytes + 1);
        await stream.WriteAsync(header);
        stream.Position = 0;

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await SensorHostProtocol.ReadAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task Read_Rejects_Truncated_And_Malformed_Frames()
    {
        await using var truncated = new MemoryStream();
        byte[] truncatedHeader = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(truncatedHeader, 4);
        await truncated.WriteAsync(truncatedHeader);
        await truncated.WriteAsync(new byte[] { (byte)'{' });
        truncated.Position = 0;

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await SensorHostProtocol.ReadAsync(truncated, CancellationToken.None));

        await using var malformed = new MemoryStream();
        byte[] malformedPayload = "{}"u8.ToArray();
        byte[] malformedHeader = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(malformedHeader, malformedPayload.Length);
        await malformed.WriteAsync(malformedHeader);
        await malformed.WriteAsync(malformedPayload);
        malformed.Position = 0;

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await SensorHostProtocol.ReadAsync(malformed, CancellationToken.None));
    }

    [Fact]
    public async Task Read_Returns_Null_At_Eof_And_Rejects_Truncated_Header()
    {
        await using var empty = new MemoryStream();
        Assert.Null(await SensorHostProtocol.ReadAsync(empty, CancellationToken.None));

        await using var truncatedHeader = new MemoryStream([1, 2]);
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await SensorHostProtocol.ReadAsync(truncatedHeader, CancellationToken.None));
    }

    [Fact]
    public async Task Write_Rejects_Oversized_Error_Code()
    {
        SensorHostMessage response = SensorHostProtocol.Unavailable(
            sequence: 1,
            hostGeneration: 1,
            errorCode: new string('x', SensorHostProtocol.MaxPayloadBytes));
        await using var stream = new MemoryStream();

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await SensorHostProtocol.WriteAsync(stream, response, CancellationToken.None));
        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public async Task Read_Rejects_Missing_Required_Metric_And_Unknown_Fields()
    {
        SensorHostMessage message = CreateResponse();
        JsonObject payload = JsonNode.Parse(JsonSerializer.Serialize(message, new JsonSerializerOptions(JsonSerializerDefaults.Web)))!.AsObject();
        payload["metrics"]!.AsObject().Remove("gpuUtilizationPercent");
        await AssertPayloadRejectedAsync(payload);

        payload = JsonNode.Parse(JsonSerializer.Serialize(message, new JsonSerializerOptions(JsonSerializerDefaults.Web)))!.AsObject();
        payload["unexpectedMetric"] = 1;
        await AssertPayloadRejectedAsync(payload);
    }

    [Fact]
    public void Validate_Allows_Partial_Success_Without_Defaulting_Missing_Data_To_Zero()
    {
        SensorHostMessage message = CreateResponse();
        SensorHostMetrics metrics = message.Metrics with
        {
            GpuUtilizationPercent = SensorHostMetricValue.Unavailable(),
        };
        message = message with { Metrics = metrics };

        SensorHostProtocol.Validate(message);

        Assert.Equal(SensorHostMetricStatus.Ok, message.Metrics.CpuUtilizationPercent.Status);
        Assert.Equal(25d, message.Metrics.CpuUtilizationPercent.Value);
        Assert.Equal(SensorHostMetricStatus.Unavailable, message.Metrics.GpuUtilizationPercent.Status);
        Assert.Null(message.Metrics.GpuUtilizationPercent.Value);
    }

    [Fact]
    public void Metric_Factories_Represent_Unavailable_And_Warming_Up_Values()
    {
        SensorHostMetricValue warming = SensorHostMetricValue.WarmingUp();
        SensorHostMetricGroup<SensorHostNetworkInterfaceReading> unavailableGroup =
            SensorHostMetricGroups.Unavailable<SensorHostNetworkInterfaceReading>();

        Assert.Equal(SensorHostMetricStatus.WarmingUp, warming.Status);
        Assert.Null(warming.Value);
        Assert.Equal(0, warming.SampledAtMonotonicTicks);
        Assert.Equal(SensorHostMetricStatus.Unavailable, unavailableGroup.Status);
        Assert.Empty(unavailableGroup.Values);
        Assert.Equal(SensorHostMetricStatus.Unavailable, SensorHostMetrics.Empty.CpuUtilizationPercent.Status);
    }

    [Fact]
    public void Unavailable_Response_Includes_Generation_And_Clock_Metadata()
    {
        SensorHostMessage response = SensorHostProtocol.Unavailable(9, 3, "timeout");

        Assert.Equal(SensorHostProtocol.CurrentVersion, response.Version);
        Assert.Equal("response", response.Kind);
        Assert.Equal(3, response.HostGeneration);
        Assert.Equal("unavailable", response.Status);
        Assert.Equal("timeout", response.ErrorCode);
        Assert.Equal(SensorHostMetrics.Empty, response.Metrics);
        Assert.True(response.MonotonicFrequency > 0);
        Assert.True(response.GeneratedAtMonotonicTicks > 0);
    }

    [Fact]
    public void ValidateResponse_Accepts_Fresh_Timestamps_Across_Metric_Groups()
    {
        SensorHostMessage response = CreateResponse();

        SensorHostProtocol.ValidateResponse(
            response,
            expectedSequence: 4,
            expectedHostGeneration: 1,
            nowMonotonicTicks: 110,
            expectedMonotonicFrequency: 1_000,
            maximumAge: TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Validate_Rejects_A_Value_When_Status_Does_Not_Allow_One()
    {
        SensorHostMessage message = CreateResponse();
        SensorHostMetrics metrics = message.Metrics with
        {
            CpuUtilizationPercent = message.Metrics.CpuUtilizationPercent with
            {
                Status = SensorHostMetricStatus.WarmingUp,
            },
        };

        Assert.Throws<InvalidDataException>(() => SensorHostProtocol.Validate(message with { Metrics = metrics }));
    }

    [Fact]
    public void ValidateResponse_Rejects_Old_Request_Generation_And_Stale_Response()
    {
        SensorHostMessage response = CreateResponse();

        Assert.Throws<InvalidDataException>(() =>
            SensorHostProtocol.ValidateResponse(response, expectedSequence: 4, expectedHostGeneration: 2, nowMonotonicTicks: 110, expectedMonotonicFrequency: 1_000, TimeSpan.FromSeconds(1)));
        Assert.Throws<InvalidDataException>(() =>
            SensorHostProtocol.ValidateResponse(response, expectedSequence: 5, expectedHostGeneration: 1, nowMonotonicTicks: 110, expectedMonotonicFrequency: 1_000, TimeSpan.FromSeconds(1)));
        Assert.Throws<InvalidDataException>(() =>
            SensorHostProtocol.ValidateResponse(response, expectedSequence: 4, expectedHostGeneration: 1, nowMonotonicTicks: 5_000, expectedMonotonicFrequency: 1_000, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void ValidateResponse_Rejects_Clock_Frequency_Mismatch_And_Old_Metric_Sample()
    {
        SensorHostMessage response = CreateResponse();

        Assert.Throws<InvalidDataException>(() =>
            SensorHostProtocol.ValidateResponse(response, 4, 1, 110, expectedMonotonicFrequency: 2_000, TimeSpan.FromSeconds(1)));

        SensorHostMetrics metrics = response.Metrics with
        {
            CpuUtilizationPercent = SensorHostMetricValue.Ok(25, sampledAtMonotonicTicks: 1),
        };
        response = response with { Metrics = metrics };

        Assert.Throws<InvalidDataException>(() =>
            SensorHostProtocol.ValidateResponse(response, 4, 1, 110, expectedMonotonicFrequency: 1_000, TimeSpan.FromMilliseconds(50)));
    }

    [Fact]
    public void Validate_Rejects_Metric_Samples_From_After_Response_Generation()
    {
        SensorHostMessage message = CreateResponse();
        SensorHostMetrics metrics = message.Metrics with
        {
            CpuUtilizationPercent = SensorHostMetricValue.Ok(25, sampledAtMonotonicTicks: 111),
        };

        Assert.Throws<InvalidDataException>(() => SensorHostProtocol.Validate(message with { Metrics = metrics }));
    }

    [Fact]
    public void Validate_Rejects_NonFinite_Or_OutOfRange_Metric()
    {
        SensorHostMessage message = CreateResponse();
        SensorHostMetrics metrics = message.Metrics with
        {
            CpuUtilizationPercent = SensorHostMetricValue.Ok(double.NaN, 100),
        };

        Assert.Throws<InvalidDataException>(() =>
            SensorHostProtocol.Validate(message with { Metrics = metrics }));
    }

    [Fact]
    public void Validate_Rejects_Collection_Limits_And_Overlong_Names()
    {
        SensorHostMessage message = CreateResponse();
        SensorHostNetworkInterfaceReading[] tooManyInterfaces = Enumerable.Range(0, SensorHostProtocol.MaxNetworkInterfaces + 1)
            .Select(index => CreateNetworkInterface((uint)(index + 1), "en0"))
            .ToArray();
        SensorHostMetrics metrics = message.Metrics with
        {
            NetworkInterfaces = SensorHostMetricGroups.Ok<SensorHostNetworkInterfaceReading>(tooManyInterfaces, 100),
        };
        Assert.Throws<InvalidDataException>(() => SensorHostProtocol.Validate(message with { Metrics = metrics }));

        SensorHostNetworkInterfaceReading[] longName = [CreateNetworkInterface(1, new string('x', SensorHostProtocol.MaxInterfaceNameLength + 1))];
        metrics = message.Metrics with
        {
            NetworkInterfaces = SensorHostMetricGroups.Ok<SensorHostNetworkInterfaceReading>(longName, 100),
        };
        Assert.Throws<InvalidDataException>(() => SensorHostProtocol.Validate(message with { Metrics = metrics }));
    }

    [Fact]
    public void Validate_Rejects_Version_Mismatch()
    {
        SensorHostMessage message = CreateResponse() with { Version = SensorHostProtocol.CurrentVersion - 1 };

        Assert.Throws<InvalidDataException>(() => SensorHostProtocol.Validate(message));
    }

    private static SensorHostMessage CreateResponse()
    {
        const long sampleTicks = 100;
        return new SensorHostMessage
        {
            Version = SensorHostProtocol.CurrentVersion,
            Kind = "response",
            Sequence = 4,
            HostGeneration = 1,
            Status = "ok",
            MonotonicFrequency = 1_000,
            GeneratedAtMonotonicTicks = 110,
            Metrics = new SensorHostMetrics
            {
                CpuUtilizationPercent = SensorHostMetricValue.Ok(25, sampleTicks),
                CpuFrequencyMhz = SensorHostMetricValue.Unavailable(),
                MemoryUtilizationPercent = SensorHostMetricValue.Ok(50, sampleTicks),
                MemoryUsedBytes = SensorHostMetricValue.Ok(512, sampleTicks),
                MemoryTotalBytes = SensorHostMetricValue.Ok(1_024, sampleTicks),
                DiskUtilizationPercent = SensorHostMetricValue.Unavailable(),
                DiskReadBytesPerSecond = SensorHostMetricValue.Ok(20, sampleTicks),
                DiskWriteBytesPerSecond = SensorHostMetricValue.Ok(30, sampleTicks),
                NetworkInterfaces = SensorHostMetricGroups.Ok<SensorHostNetworkInterfaceReading>(
                    [CreateNetworkInterface(1, "en0")], sampleTicks),
                GpuUtilizationPercent = SensorHostMetricValue.Ok(75, sampleTicks),
                GpuMemoryUsedBytes = SensorHostMetricValue.Unavailable(),
                GpuMemoryLimitBytes = SensorHostMetricValue.Unavailable(),
                CpuPowerWatts = SensorHostMetricValue.Unavailable(),
                GpuPowerWatts = SensorHostMetricValue.Ok(2.5, sampleTicks),
                CpuTemperatureCelsius = SensorHostMetricValue.Ok(55, sampleTicks),
                GpuTemperatureCelsius = SensorHostMetricValue.Ok(50, sampleTicks),
                HighLoadProcesses = SensorHostMetricGroups.Ok<SensorHostProcessReading>(
                    [new SensorHostProcessReading
                    {
                        ProcessId = 42,
                        Name = "example",
                        CpuUtilizationPercent = SensorHostMetricValue.Ok(10, sampleTicks),
                        PrivateBytes = SensorHostMetricValue.Ok(100, sampleTicks),
                        IoReadBytesPerSecond = SensorHostMetricValue.Ok(5, sampleTicks),
                        IoWriteBytesPerSecond = SensorHostMetricValue.Ok(6, sampleTicks),
                    }], sampleTicks),
            },
        };
    }

    private static SensorHostNetworkInterfaceReading CreateNetworkInterface(uint index, string name) => new()
    {
        InterfaceIndex = index,
        Name = name,
        ReceiveBytesPerSecond = SensorHostMetricValue.Ok(10, 100),
        SendBytesPerSecond = SensorHostMetricValue.Ok(12, 100),
    };

    private static async Task AssertPayloadRejectedAsync(JsonObject payload)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        await using var stream = new MemoryStream();
        byte[] header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await stream.WriteAsync(header);
        await stream.WriteAsync(bytes);
        stream.Position = 0;

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await SensorHostProtocol.ReadAsync(stream, CancellationToken.None));
    }
}
