using System.Collections.Immutable;

namespace DesktopSystemMonitor.Core.Metrics;

public sealed record NetworkAdapterSnapshot
{
    public required ulong InterfaceLuid { get; init; }
    public required string DisplayName { get; init; }
    public required MetricStatus Status { get; init; }
    public required double BytesReceivedPerSecond { get; init; }
    public required double BytesSentPerSecond { get; init; }
    public required long SessionBytesReceived { get; init; }
    public required long SessionBytesSent { get; init; }
    public required bool IsSelected { get; init; }
}

public sealed record NetworkSnapshot
{
    public required ImmutableArray<NetworkAdapterSnapshot> Adapters { get; init; }
    public required MetricStatus AggregateStatus { get; init; }
    public required double AggregateBytesReceivedPerSecond { get; init; }
    public required double AggregateBytesSentPerSecond { get; init; }

    public static NetworkSnapshot Warmup() => new()
    {
        Adapters = ImmutableArray<NetworkAdapterSnapshot>.Empty,
        AggregateStatus = MetricStatus.WarmingUp,
        AggregateBytesReceivedPerSecond = 0d,
        AggregateBytesSentPerSecond = 0d,
    };

    public static NetworkSnapshot Unavailable() => new()
    {
        Adapters = ImmutableArray<NetworkAdapterSnapshot>.Empty,
        AggregateStatus = MetricStatus.Unavailable,
        AggregateBytesReceivedPerSecond = 0d,
        AggregateBytesSentPerSecond = 0d,
    };
}
