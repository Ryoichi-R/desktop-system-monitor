using System.Diagnostics;
using DesktopSystemMonitor.Windows.Processes;
using Xunit;

namespace DesktopSystemMonitor.Windows.Tests.Processes;

public sealed class PrivateWorkingSetReaderTests
{
    [Theory]
    [InlineData(@"\Process V2(app:123)\Working Set - Private", 123)]
    [InlineData(@"\Process V2(name:with:colon:456)\Working Set - Private", 456)]
    public void process_v2_instance_parser_uses_the_terminal_pid(string path, int expected)
    {
        Assert.True(ProcessV2PrivateWorkingSetReader.TryParseProcessId(path, out int actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(@"\Process(app:123)\Working Set - Private")]
    [InlineData(@"\Process V2(app)\Working Set - Private")]
    [InlineData(@"\Process V2(app:0)\Working Set - Private")]
    [InlineData(@"\Process V2(app:123)\Private Bytes")]
    public void process_v2_instance_parser_rejects_ambiguous_paths(string path)
    {
        Assert.False(ProcessV2PrivateWorkingSetReader.TryParseProcessId(path, out _));
    }

    [Fact]
    public void ex2_reads_the_current_private_working_set_when_available()
    {
        using Process process = Process.GetCurrentProcess();

        bool available = ProcessMemoryInterop.TryGetPrivateWorkingSetBytes(process.SafeHandle, out long bytes);

        if (ProcessMemoryInterop.IsEx2Available())
        {
            Assert.True(available);
            Assert.True(bytes > 0);
        }
    }
}
