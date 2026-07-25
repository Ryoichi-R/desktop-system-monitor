using System.Collections.Concurrent;
using DesktopSystemMonitor.Windows.Gpu;
using Xunit;

namespace DesktopSystemMonitor.Windows.Tests.Gpu;

public sealed class PreferredAdapterSelectionStoreTests
{
    [Fact]
    public void set_atomically_replaces_the_complete_nullable_value()
    {
        var store = new PreferredAdapterSelectionStore();

        Assert.Null(store.Value);
        store.Set(ulong.MaxValue);
        Assert.Equal(ulong.MaxValue, store.Value);
        store.Set(null);
        Assert.Null(store.Value);
    }

    [Fact]
    public void bounded_concurrent_reads_observe_only_complete_values()
    {
        const ulong first = 0xAAAAAAAAAAAAAAAA;
        const ulong second = 0x5555555555555555;
        var store = new PreferredAdapterSelectionStore();
        var invalid = new ConcurrentQueue<ulong?>();

        Parallel.For(0, 2_000, index =>
        {
            ulong? next = (index % 3) switch
            {
                0 => first,
                1 => second,
                _ => (ulong?)null,
            };
            store.Set(next);
            ulong? observed = store.Value;
            if (observed is not null && observed != first && observed != second)
            {
                invalid.Enqueue(observed);
            }
        });

        Assert.Empty(invalid);
    }
}
