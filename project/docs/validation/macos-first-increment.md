# Mac-first increment: CPU and memory

2026-09-27: the owner requested Mac implementation before further Windows migration work. Windows regression acceptance remains required before replacing the WPF implementation, but it no longer blocks this Mac increment.

## Implemented

- CPU utilization from Mach tick deltas; the first reading and intervals over ten seconds warm up the baseline. Counter resets and invalid intervals are not displayed as zero.
- Used memory = (internal pages - purgeable pages + wired pages + compressor pages) × page size. This is the selected Mac definition, not Windows committed memory.
- Native reads run exclusively in SensorHost. The UI consumes version-2 IPC with request/generation, monotonic timestamps, size/schema and freshness validation.
- Serialized polling, two-second timeout, capped exponential retry (five consecutive failures), and cancellation/child termination on close.
- One-second UI updates and an application-registered Avalonia tray menu (show, toggle topmost, exit).
- macOS NSWindow layer adapter for normal, always-on-top, desktop-level, click-through,
  all-Spaces and fullscreen-auxiliary behavior. Native failures are reported as a
  degraded health state without terminating the UI.
- Windows-compatible reduced layout at 150 DIP, plus standard/reduced display
  scaling choices of 50%, 75%, 100%, 125%, and 150%. The selected display form
  and scale are persisted with the Mac widget settings.
- LaunchAgent adapter and menu toggle for login startup. The plist is written atomically,
  and stdout/stderr are fixed to `/dev/null`; tests inject the launchctl boundary.
- Local self-contained arm64 `.app` generation with separate fixed-name Host and App executables, `LSUIElement`, and ad-hoc signatures. The bundle is still local-only; no install step is performed.

Run from the project directory on macOS:

```powershell
pwsh -NoProfile -File scripts/publish-desktop-system-monitor-macos.ps1 -OutputRoot <new-output-directory>
```

Open the resulting `DesktopSystemMonitor.app`. An existing output bundle is never overwritten. If repeated Host failures exhaust retries, close and reopen the application. GPU, disk, network, power, temperatures, process list, fullscreen auto-hide and the full coverage gate remain unfinished. The ordinary window is an intermediate functional UI, not the finished widget. Login startup is implemented but still requires manual real-device acceptance.

The owner should compare CPU and used-memory values with Activity Monitor after startup; CPU's existing acceptance tolerance is ±5 percentage points, allowing for different sampling windows. Memory values must be compared using the used-memory definition above. The Phase 3 interaction checklist also covers switching the 150 DIP reduced layout and testing 50%/75% standard scaling: [macOS Phase 3 manual acceptance](macos-phase3-manual-acceptance.md).

Native contracts: [XNU host_info.h](https://github.com/apple-oss-distributions/xnu/blob/main/osfmk/mach/host_info.h), [vm_statistics.h](https://github.com/apple-oss-distributions/xnu/blob/main/osfmk/mach/vm_statistics.h).
