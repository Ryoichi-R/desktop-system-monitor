using System;
using System.IO;
using DesktopSystemMonitor.App.Startup;
using Xunit;

namespace DesktopSystemMonitor.App.Tests;

public sealed class ExecutablePathTests
{
    [Fact]
    public void executable_path_is_absolute_and_existing_for_the_test_host()
    {
        string path = AppComposition.GetExecutablePath();

        Assert.True(Path.IsPathFullyQualified(path));
        Assert.True(File.Exists(path));
        Assert.Equal(Environment.ProcessPath, path);
    }

    [Fact]
    public void fallback_executable_path_uses_the_single_file_name()
    {
        string path = AppComposition.GetFallbackExecutablePath();

        Assert.Equal(
            Path.Combine(AppContext.BaseDirectory, "DesktopSystemMonitor.exe"),
            path);
    }
}
