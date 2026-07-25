using DesktopSystemMonitor.Core.Metrics;
using DesktopSystemMonitor.Windows.Disk;
using Xunit;

namespace DesktopSystemMonitor.Windows.Tests.Disk;

public sealed class DiskMetricSourceTests
{
    private static readonly string[] DiskZeroPaths = [@"\PhysicalDisk(0 C:)\% Idle Time"];

    [Fact]
    public async Task samples_selected_disk_after_warmup()
    {
        var query = new FakeQuery { Idle = 25, Read = 1_024, Write = 2_048 };
        using var source = CreateSource(() => query);
        source.SetEnabled(true, 0);

        DiskSnapshot warmup = await source.SampleAsync(default);
        DiskSnapshot result = await source.SampleAsync(default);

        Assert.Equal(MetricStatus.WarmingUp, warmup.Status);
        Assert.Equal(MetricStatus.Ok, result.Status);
        Assert.Equal(75, result.ActivePercent);
        Assert.Equal(1_024, result.ReadBytesPerSecond);
        Assert.Equal(2_048, result.WriteBytesPerSecond);
        Assert.Equal(3, query.AddedPaths.Count);
    }

    [Fact]
    public async Task rebuilds_query_after_collect_failure()
    {
        var failed = new FakeQuery { CollectResult = false };
        var recovered = new FakeQuery { Idle = 90, Read = 10, Write = 20 };
        var queries = new Queue<FakeQuery>([failed, recovered]);
        using var source = CreateSource(() => queries.Dequeue());
        source.SetEnabled(true, 0);

        DiskSnapshot unavailable = await source.SampleAsync(default);
        DiskSnapshot warmup = await source.SampleAsync(default);
        DiskSnapshot result = await source.SampleAsync(default);

        Assert.Equal(MetricStatus.Unavailable, unavailable.Status);
        Assert.Equal(DiskAvailabilityReason.CollectionFailed, unavailable.AvailabilityReason);
        Assert.True(failed.Disposed);
        Assert.Equal(MetricStatus.WarmingUp, warmup.Status);
        Assert.Equal(MetricStatus.Ok, result.Status);
        Assert.Equal(10, result.ActivePercent);
    }

    [Fact]
    public async Task retries_wildcard_expansion_after_delay()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        int expansions = 0;
        var query = new FakeQuery();
        using var source = new DiskMetricSource(
            new FakeResolver(SystemDiskResolution.Success(0)),
            () => ++expansions == 1 ? [] : DiskZeroPaths,
            () => query,
            () => now);
        source.SetEnabled(true, 0);

        await source.SampleAsync(default);
        now = now.AddSeconds(4);
        await source.SampleAsync(default);
        Assert.Equal(1, expansions);

        now = now.AddSeconds(1);
        DiskSnapshot result = await source.SampleAsync(default);

        Assert.Equal(2, expansions);
        Assert.Equal(MetricStatus.WarmingUp, result.Status);
    }

    [Fact]
    public async Task preserves_multi_extent_resolution_reason()
    {
        using var source = new DiskMetricSource(
            new FakeResolver(SystemDiskResolution.Failed(DiskAvailabilityReason.SystemVolumeSpansMultipleDisks)),
            () => DiskZeroPaths,
            () => new FakeQuery(),
            () => DateTimeOffset.UtcNow);
        source.SetEnabled(true, null);

        DiskSnapshot result = await source.SampleAsync(default);

        Assert.Equal(MetricStatus.Unavailable, result.Status);
        Assert.Equal(DiskAvailabilityReason.SystemVolumeSpansMultipleDisks, result.AvailabilityReason);
    }

    [Fact]
    public async Task classifies_pdh_enumeration_failure()
    {
        using var source = new DiskMetricSource(
            new FakeResolver(SystemDiskResolution.Success(0)),
            () => throw new InvalidOperationException("PDH unavailable"),
            () => new FakeQuery(),
            () => DateTimeOffset.UtcNow);
        source.SetEnabled(true, null);

        DiskSnapshot result = await source.SampleAsync(default);

        Assert.Equal(DiskAvailabilityReason.PdhEnumerationFailed, result.AvailabilityReason);
    }

    [Fact]
    public async Task classifies_counter_open_and_read_failures()
    {
        using var openFailure = CreateSource(() => new FakeQuery { AddCounterResult = false });
        openFailure.SetEnabled(true, 0);
        DiskSnapshot openResult = await openFailure.SampleAsync(default);

        using var readFailure = CreateSource(() => new FakeQuery { ReadResult = false });
        readFailure.SetEnabled(true, 0);
        _ = await readFailure.SampleAsync(default);
        DiskSnapshot readResult = await readFailure.SampleAsync(default);

        Assert.Equal(DiskAvailabilityReason.CounterOpenFailed, openResult.AvailabilityReason);
        Assert.Equal(DiskAvailabilityReason.CounterReadFailed, readResult.AvailabilityReason);
    }

    [Fact]
    public async Task classifies_exceptions_at_resolver_counter_open_collect_and_read_boundaries()
    {
        using var resolverFailure = new DiskMetricSource(
            new ThrowingResolver(),
            () => DiskZeroPaths,
            () => new FakeQuery(),
            () => DateTimeOffset.UtcNow);
        resolverFailure.SetEnabled(true, null);
        DiskSnapshot resolverResult = await resolverFailure.SampleAsync(default);

        using var openFailure = CreateSource(() => throw new InvalidOperationException("open"));
        openFailure.SetEnabled(true, 0);
        DiskSnapshot openResult = await openFailure.SampleAsync(default);

        using var collectFailure = CreateSource(() => new FakeQuery { ThrowOnCollect = true });
        collectFailure.SetEnabled(true, 0);
        DiskSnapshot collectResult = await collectFailure.SampleAsync(default);

        using var readFailure = CreateSource(() => new FakeQuery { ThrowOnRead = true });
        readFailure.SetEnabled(true, 0);
        _ = await readFailure.SampleAsync(default);
        DiskSnapshot readResult = await readFailure.SampleAsync(default);

        Assert.Equal(DiskAvailabilityReason.SystemDiskResolveFailed, resolverResult.AvailabilityReason);
        Assert.Equal(DiskAvailabilityReason.CounterOpenFailed, openResult.AvailabilityReason);
        Assert.Equal(DiskAvailabilityReason.CollectionFailed, collectResult.AvailabilityReason);
        Assert.Equal(DiskAvailabilityReason.CounterReadFailed, readResult.AvailabilityReason);
    }

    [Fact]
    public async Task disabled_source_does_not_create_or_collect_a_query()
    {
        int created = 0;
        using var source = CreateSource(() =>
        {
            created++;
            return new FakeQuery();
        });

        DiskSnapshot result = await source.SampleAsync(default);

        Assert.Equal(MetricStatus.Unavailable, result.Status);
        Assert.Equal(0, created);
    }

    private static DiskMetricSource CreateSource(Func<IDiskCounterQuery> queryFactory) => new(
        new FakeResolver(SystemDiskResolution.Success(0)),
        () => DiskZeroPaths,
        queryFactory,
        () => DateTimeOffset.UtcNow);

    private sealed class FakeResolver(SystemDiskResolution result) : IVolumeDiskResolver
    {
        public SystemDiskResolution ResolveSystemDisk() => result;
    }

    private sealed class ThrowingResolver : IVolumeDiskResolver
    {
        public SystemDiskResolution ResolveSystemDisk() => throw new InvalidOperationException("resolve");
    }

    private sealed class FakeQuery : IDiskCounterQuery
    {
        public bool CollectResult { get; init; } = true;
        public bool AddCounterResult { get; init; } = true;
        public bool ReadResult { get; init; } = true;
        public bool ThrowOnCollect { get; init; }
        public bool ThrowOnRead { get; init; }
        public double Idle { get; init; }
        public double Read { get; init; }
        public double Write { get; init; }
        public List<string> AddedPaths { get; } = [];
        public bool Disposed { get; private set; }

        public bool TryAddCounter(string path)
        {
            AddedPaths.Add(path);
            return AddCounterResult;
        }

        public bool Collect() => ThrowOnCollect ? throw new InvalidOperationException("collect") : CollectResult;

        public bool TryGetDouble(string path, out double value)
        {
            if (ThrowOnRead)
            {
                throw new InvalidOperationException("read");
            }
            value = path.EndsWith("% Idle Time", StringComparison.Ordinal) ? Idle
                : path.EndsWith("Disk Read Bytes/sec", StringComparison.Ordinal) ? Read
                : Write;
            return ReadResult;
        }

        public void Dispose() => Disposed = true;
    }
}
