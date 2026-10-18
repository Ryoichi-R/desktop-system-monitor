namespace DesktopSystemMonitor.App;

internal enum MacDiagnosticEvent { Started, Stopped, MainWindowClosed, UnhandledException, LoggingEnabled, LoggingDisabled, FullScreenHidden, FullScreenRestored }

/// <summary>Opt-in category-only log: no exception messages, stack traces, metrics, or identities.</summary>
internal sealed class MacDiagnosticLog(string directory, long maxBytes = 1_000_000)
{
    private readonly object _gate = new();
    internal bool Enabled { get; set; }

    internal void Record(MacDiagnosticEvent category, Exception? error = null)
    {
        if (!Enabled) return;
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "mac-monitor.log");
                string line = $"{DateTimeOffset.UtcNow:O} category={category}"
                    + (error is null ? "" : $" exception={error.GetType().Name} hresult=0x{error.HResult:X8}") + Environment.NewLine;
                if (File.Exists(path) && new FileInfo(path).Length + System.Text.Encoding.UTF8.GetByteCount(line) > maxBytes)
                {
                    for (int i = 3; i >= 0; i--)
                    {
                        string source = i == 0 ? path : path + $".{i}";
                        if (File.Exists(source)) File.Move(source, path + $".{i + 1}", overwrite: true);
                    }
                }
                File.AppendAllText(path, line);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // Logging is best-effort and must never take down the monitor.
            }
        }
    }
}
