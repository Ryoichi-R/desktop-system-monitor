using System.Diagnostics;
using Xunit;

namespace DesktopSystemMonitor.Mac.Tests;

public sealed class MacSingleInstanceGuardTests
{
    [MacFact]
    public async Task Separate_Process_Holds_The_Lock_Until_Normal_Exit_Or_Kill()
    {
        if (!OperatingSystem.IsMacOS()) return;
        string root = Path.Combine(Path.GetTempPath(), "dsm-instance-test-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(root, "instance.lock");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            using (var first = MacSingleInstanceGuard.TryAcquire(path))
            {
                Assert.NotNull(first);
                Assert.Null(MacSingleInstanceGuard.TryAcquire(path));
            }
            foreach (bool kill in new[] { false, true })
            {
                var start = new ProcessStartInfo("/usr/bin/perl")
                {
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                };
                start.ArgumentList.Add("-e");
                start.ArgumentList.Add("open(my $f, '<', $ARGV[0]) or die; flock($f, 2) or die; $|=1; print qq(ready\n); <STDIN>;");
                start.ArgumentList.Add(path);
                using var child = Process.Start(start)!;
                try
                {
                    Assert.Equal("ready", await child.StandardOutput.ReadLineAsync(timeout.Token));
                    Assert.Null(MacSingleInstanceGuard.TryAcquire(path));
                    if (kill) child.Kill();
                    else child.StandardInput.Close();
                    await child.WaitForExitAsync(timeout.Token);
                    using var after = MacSingleInstanceGuard.TryAcquire(path);
                    Assert.NotNull(after);
                }
                finally { if (!child.HasExited) child.Kill(); }
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
