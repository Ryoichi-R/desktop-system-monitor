# Privacy

Desktop System Monitor runs locally. It does not include analytics, advertising,
accounts, or an application telemetry upload endpoint.

Settings are stored in `%LOCALAPPDATA%\DesktopSystemMonitor\settings.json`.
Diagnostic logging is off by default and can be enabled from Settings. When
enabled, logs are stored under `%LOCALAPPDATA%\DesktopSystemMonitor\logs`, rotate
at 1 MB, and retain at most five files. Diagnostics exclude metric values,
window titles, process names, PIDs, counter instance names, usernames, and
network identifiers by design.

The optional high-load process window displays process names and PIDs on screen
but does not write them to the diagnostic log. Screenshots and logs should still
be reviewed and redacted before sharing.

To remove local data, exit the application and delete the DesktopSystemMonitor
directory under `%LOCALAPPDATA%`. Portable binaries can be removed by deleting
their extraction directory after disabling Start with Windows.
