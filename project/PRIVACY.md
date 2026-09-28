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

## macOS local preview

Settings: `~/Library/Application Support/DesktopSystemMonitor/mac-widget.json`.
Diagnostic logs: `~/Library/Logs/DesktopSystemMonitor/mac-monitor.log` and up to
four rotated files. Logging is disabled by default; enable it from the menu-bar
or context menu. A file is rotated before the next record would exceed 1 MB.
Records contain UTC time, a fixed event category, exception type and HRESULT
only. Exception messages and stack traces are not stored. Process names/PIDs,
window titles, screen identifiers, paths, SMC keys, channels and metric values
are not written to this diagnostic log. Native fatal signals or forced kill
may prevent a final record; a missing log is not proof of a clean exit.

Process names/PIDs and CPU/I/O values are displayed locally in the optional
process window. CoreGraphics window bounds/owner IDs are read only for
full-screen visibility decisions, not logged or exported. No window titles
or window images are read by the detector. No screen-recording permission is
requested. Restricted/missing metadata restores the widget.

Login startup uses `~/Library/LaunchAgents/local.desktop-system-monitor.plist`.
Enabling/disabling changes the next-login configuration without stopping the
current process. Agent stdout/stderr are redirected to `/dev/null`. Settings
and logs are not removed by quitting or replacing the app bundle.
