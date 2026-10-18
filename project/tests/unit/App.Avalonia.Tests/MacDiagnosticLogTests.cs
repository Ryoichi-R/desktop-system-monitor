using DesktopSystemMonitor.App;
using Xunit;

namespace DesktopSystemMonitor.App.Avalonia.Tests;

public sealed class MacDiagnosticLogTests
{
    [Fact]
    public void Disabled_Log_Does_Not_Write_And_Enabled_Log_Is_Bounded_And_Redacted()
    {
        string directory = Path.Combine(Path.GetTempPath(), "dsm-log-" + Guid.NewGuid().ToString("N"));
        var log = new MacDiagnosticLog(directory, 350);
        log.Record(MacDiagnosticEvent.Started);
        Assert.False(Directory.Exists(directory));
        try
        {
            log.Enabled = true;
            for (int i = 0; i < 50; i++) log.Record(MacDiagnosticEvent.UnhandledException, new IOException("PRIVATE-MESSAGE-PATH"));
            var files = Directory.GetFiles(directory);
            Assert.Equal(5, files.Length);
            foreach (string file in files)
            {
                Assert.True(new FileInfo(file).Length <= 350);
                string text = File.ReadAllText(file);
                Assert.DoesNotContain("PRIVATE-MESSAGE", text);
                Assert.Contains("exception=IOException", text);
            }
            log.Enabled = false;
            string before = File.ReadAllText(Path.Combine(directory, "mac-monitor.log"));
            log.Record(MacDiagnosticEvent.Stopped);
            Assert.Equal(before, File.ReadAllText(Path.Combine(directory, "mac-monitor.log")));
            // A non-directory destination must not make diagnostics throw.
            new MacDiagnosticLog(files[0]) { Enabled = true }.Record(MacDiagnosticEvent.Started);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }
}
