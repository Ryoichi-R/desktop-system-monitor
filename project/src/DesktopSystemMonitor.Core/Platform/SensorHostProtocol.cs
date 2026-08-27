using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopSystemMonitor.Core.Platform;

/// <summary>
/// SensorHostとの通信契約。JSON本文を4バイトlittle-endian長で包み、stdoutには
/// プロトコルフレーム以外を出さない。native値はこの境界の両側で再検証する。
/// </summary>
public static class SensorHostProtocol
{
    public const int CurrentVersion = 1;
    public const int MaxPayloadBytes = 64 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.Strict,
        PropertyNameCaseInsensitive = false,
    };

    public static async ValueTask<SensorHostMessage?> ReadAsync(
        Stream input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        byte[] header = new byte[sizeof(int)];
        int headerBytes = await ReadAtMostAsync(input, header, cancellationToken).ConfigureAwait(false);
        if (headerBytes == 0)
        {
            return null;
        }
        if (headerBytes != header.Length)
        {
            throw new InvalidDataException("SensorHost frame header is truncated.");
        }

        int payloadLength = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (payloadLength is <= 0 or > MaxPayloadBytes)
        {
            throw new InvalidDataException("SensorHost frame length is outside the allowed range.");
        }

        byte[] payload = new byte[payloadLength];
        int payloadBytes = await ReadAtMostAsync(input, payload, cancellationToken).ConfigureAwait(false);
        if (payloadBytes != payloadLength)
        {
            throw new InvalidDataException("SensorHost frame payload is truncated.");
        }

        SensorHostMessage message;
        try
        {
            message = JsonSerializer.Deserialize<SensorHostMessage>(payload, JsonOptions)
                ?? throw new InvalidDataException("SensorHost frame is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("SensorHost frame is not valid JSON.", exception);
        }
        Validate(message);
        return message;
    }

    public static async ValueTask WriteAsync(
        Stream output,
        SensorHostMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);
        Validate(message);
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        if (payload.Length > MaxPayloadBytes)
        {
            throw new InvalidDataException("SensorHost frame payload is too large.");
        }

        byte[] header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await output.WriteAsync(header.AsMemory(), cancellationToken).ConfigureAwait(false);
        await output.WriteAsync(payload.AsMemory(), cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static void Validate(SensorHostMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Version != CurrentVersion)
        {
            throw new InvalidDataException("SensorHost protocol version is not supported.");
        }
        if (message.Sequence < 0)
        {
            throw new InvalidDataException("SensorHost sequence must not be negative.");
        }
        if (message.Kind is not ("sample" or "response"))
        {
            throw new InvalidDataException("SensorHost message kind is not supported.");
        }
        if (message.Status is not ("ok" or "unavailable" or "error"))
        {
            throw new InvalidDataException("SensorHost message status is not supported.");
        }
        if (message.Metrics is null)
        {
            throw new InvalidDataException("SensorHost message metrics are missing.");
        }

        if (message.Status == "ok")
        {
            ValidateMetricRange(message.Metrics.CpuUtilizationPercent, 0d, 100d, "CPU utilization");
            ValidateMetricRange(message.Metrics.MemoryUtilizationPercent, 0d, 100d, "memory utilization");
            ValidateMetricRange(message.Metrics.GpuUtilizationPercent, 0d, 100d, "GPU utilization");
            ValidateFiniteNonNegative(message.Metrics.DiskReadBytesPerSecond, "disk read rate");
            ValidateFiniteNonNegative(message.Metrics.DiskWriteBytesPerSecond, "disk write rate");
            ValidateFiniteNonNegative(message.Metrics.NetworkReceiveBytesPerSecond, "network receive rate");
            ValidateFiniteNonNegative(message.Metrics.NetworkSendBytesPerSecond, "network send rate");
        }
    }

    public static SensorHostMessage Unavailable(long sequence, string errorCode) => new()
    {
        Version = CurrentVersion,
        Kind = "response",
        Sequence = sequence,
        Status = "unavailable",
        ErrorCode = errorCode,
        Metrics = SensorHostMetrics.Empty,
    };

    private static void ValidateMetricRange(double value, double minimum, double maximum, string name)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
        {
            throw new InvalidDataException($"SensorHost {name} is outside the allowed range.");
        }
    }

    private static void ValidateFiniteNonNegative(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0d)
        {
            throw new InvalidDataException($"SensorHost {name} is invalid.");
        }
    }

    private static async ValueTask<int> ReadAtMostAsync(
        Stream input,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = await input.ReadAsync(buffer[total..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }
            total += read;
        }
        return total;
    }
}

public sealed record SensorHostMessage
{
    public required int Version { get; init; }
    public required string Kind { get; init; }
    public required long Sequence { get; init; }
    public required string Status { get; init; }
    public required SensorHostMetrics Metrics { get; init; }
    public string? ErrorCode { get; init; }
}

public sealed record SensorHostMetrics
{
    public double CpuUtilizationPercent { get; init; }
    public double MemoryUtilizationPercent { get; init; }
    public double GpuUtilizationPercent { get; init; }
    public double DiskReadBytesPerSecond { get; init; }
    public double DiskWriteBytesPerSecond { get; init; }
    public double NetworkReceiveBytesPerSecond { get; init; }
    public double NetworkSendBytesPerSecond { get; init; }

    public static SensorHostMetrics Empty { get; } = new();
}
