using DesktopSystemMonitor.Windows.Gpu;
using Xunit;

namespace DesktopSystemMonitor.Windows.Tests.Gpu;

public class GpuInstanceNameTests
{
    [Fact]
    public void parses_gpu_adapter_memory_instance()
    {
        Assert.True(GpuInstanceName.TryParseMemory(
            "luid_0x00000001_0xABCDEF02_phys_3",
            out GpuMemoryInstance parsed));
        Assert.Equal(0x00000001ABCDEF02UL, parsed.Luid);
        Assert.Equal(3U, parsed.PhysicalAdapter);
    }

    [Theory]
    [InlineData("_Total")]
    [InlineData("pid_1_luid_0x0_0x1_phys_0_eng_0_engtype_3D")]
    [InlineData("luid_0xZZ_0x1_phys_0")]
    public void rejects_non_memory_instances(string instance)
    {
        Assert.False(GpuInstanceName.TryParseMemory(instance, out _));
    }
    [Fact]
    public void parses_a_real_instance_name()
    {
        bool ok = GpuInstanceName.TryParse(
            "pid_1234_luid_0x00000000_0x0000A78F_phys_0_eng_0_engtype_3D",
            out var parsed);
        Assert.True(ok);
        Assert.Equal(1234u, parsed.Pid);
        Assert.Equal(0xA78Ful, parsed.Luid);
        Assert.Equal(0u, parsed.PhysicalAdapter);
        Assert.Equal(0u, parsed.EngineIndex);
        Assert.Equal("3D", parsed.EngineType);
    }

    [Fact]
    public void combines_high_and_low_luid_parts()
    {
        bool ok = GpuInstanceName.TryParse(
            "pid_1_luid_0x00000001_0x00000002_phys_0_eng_1_engtype_VideoDecode",
            out var parsed);
        Assert.True(ok);
        Assert.Equal(((ulong)1 << 32) | 2, parsed.Luid);
        Assert.Equal("VideoDecode", parsed.EngineType);
    }

    [Fact]
    public void _Total_pseudo_instance_is_rejected()
    {
        Assert.False(GpuInstanceName.TryParse("_Total", out _));
    }

    [Fact]
    public void engine_type_with_underscores_is_captured_whole()
    {
        bool ok = GpuInstanceName.TryParse(
            "pid_0_luid_0x00000000_0x00000001_phys_0_eng_3_engtype_Video_Processing",
            out var parsed);
        Assert.True(ok);
        Assert.Equal("Video_Processing", parsed.EngineType);
    }
}
