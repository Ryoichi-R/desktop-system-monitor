using DesktopSystemMonitor.Windows.Pdh;
using System.Runtime.InteropServices;
using Xunit;

namespace DesktopSystemMonitor.Windows.Tests.Pdh;

public sealed class PdhQueryTests
{
    [Theory]
    [InlineData(@"\PhysicalDisk(*)\% Idle Time", @"\物理ディスク(*)\アイドル時間", "0 C:")]
    [InlineData(@"\Process V2(*)\Working Set - Private", @"\プロセス V2(*)\ワーキングセット", "app(1):123")]
    [InlineData(@"\Energy Meter(*)\Power", @"\電力計(*)\電力", "CPU_CLUSTER_0")]
    [InlineData(@"\GPU Engine(*)\Utilization Percentage", @"\GPU エンジン(*)\使用率", "pid_42_eng_0")]
    [InlineData(@"\GPU Adapter Memory(*)\Dedicated Usage", @"\GPU メモリ(*)\専用使用量", "luid_0x1_phys_0")]
    public void expansion_uses_localized_path_and_requeries_size_when_instances_change(string english, string local, string instance)
    {
        string path = @"\\machine" + local.Replace("*", instance, StringComparison.Ordinal);
        char[] list = (path + "\0\0").ToCharArray();
        int calls = 0;
        uint Expand(IntPtr source, string wildcard, IntPtr destination, ref uint size, uint flags)
        {
            Assert.Equal(local, wildcard);
            Assert.Equal(4u, flags);
            calls++;
            if (destination == IntPtr.Zero)
            {
                Assert.Equal(0u, size);
                size = calls == 1 ? 1 : (uint)list.Length;
                return PdhInterop.PDH_MORE_DATA;
            }
            if (size == 1) return PdhInterop.PDH_MORE_DATA;
            Marshal.Copy(list, 0, destination, list.Length);
            return 0;
        }
        var counter = Assert.Single(PdhQuery.ExpandInstances(english, local, Expand));
        Assert.Equal(4, calls);
        Assert.Equal(instance, counter.Instance);
        Assert.Equal(path, counter.LocalizedPath);
        if (instance == "app(1):123")
        {
            Assert.True(DesktopSystemMonitor.Windows.Processes.ProcessV2PrivateWorkingSetReader.TryParseInstanceId(counter.Instance, out int pid));
            Assert.Equal(123, pid);
        }
        Assert.Equal(english.Replace("*", instance, StringComparison.Ordinal), counter.EnglishPath);
    }

    [Fact]
    public void bool_apis_return_false_after_dispose()
    {
        var query = new PdhQuery();
        query.Dispose();

        Assert.False(query.Collect());
        Assert.False(query.TryAddCounter(@"\Processor(_Total)\% Processor Time"));
        Assert.False(query.TryGetDouble(@"\Processor(_Total)\% Processor Time", out double value));
        Assert.True(double.IsNaN(value));
        Assert.False(query.RemoveCounter(@"\Processor(_Total)\% Processor Time"));

        query.Dispose();
    }
}
