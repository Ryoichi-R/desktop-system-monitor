# Desktop System Monitor

Desktop System Monitor is an experimental Windows 11 overlay that keeps CPU,
memory, GPU, and network activity visible without taking mouse input. Optional
disk, temperature, battery, peak, and process views are enabled only when the
user chooses them.

Settings and the tray menu provide two information widths: Standard (280 DIP)
and Reduced (150 DIP). Reduced keeps the main CPU, memory, GPU, disk, network,
and battery values while omitting secondary details such as temperatures,
frequencies, power, capacity breakdowns, disk rates, CPU/MEM/GPU/DISK peak
markers, and battery subtext. When network peaks are enabled, Reduced also
shows the configured receive and send maxima for the last x seconds.
Both widths use the configured 75–200% scale, and the choice persists across
restart. With EdgeFade, the background-only area extends around all four sides
of the information area, so the outer window is wider than the selected width.

[Bilingual home / 日本語](README.md) | [License](../LICENSE) | [Privacy](PRIVACY.md) | [Support](../.github/SUPPORT.md) | [Security](../.github/SECURITY.md)

## Release status

Version `0.1.0` is a **source-only experimental preview**. No official prebuilt
EXE, portable archive, or signed binary is distributed. Users may build the
source locally, but metric accuracy, 24-hour stability, hardware compatibility,
and response-time support are not guaranteed.

The checked-in [readiness summary](docs/validation/readiness-summary.json) is
the future binary architecture gate. Do not publish or redistribute a prebuilt
architecture while it is marked `BLOCKED`.

## Highlights

- Transparent-by-default, click-through WPF overlay with optional background
  color, background-only opacity, and configurable four-edge fades outside
  the fully opaque metric area.
- In always-on-top mode, the background can stay on the desktop plane while
  metric text remains topmost, so normal windows occlude only the background.
- CPU, memory, GPU, and network metrics; unavailable sensors display `N/A`.
- Standard (280 DIP) and Reduced (150 DIP) information widths from Settings or
  the tray menu; the configured UI scale applies to both.
- Full-screen auto-hide and sampling suspension while the session is locked.
- Self-contained portable builds for validated Windows architectures.
- Opt-in local diagnostics with rotation and deliberately limited fields.

## Build and test

Windows 11, PowerShell 7, and the .NET SDK selected by `global.json` are required.
These commands produce local, unsigned builds; they do not download an official
binary release.

```powershell
dotnet build .\DesktopSystemMonitor.slnx -c Release
pwsh .\scripts\test-desktop-system-monitor.ps1 -All
pwsh .\scripts\coverage-desktop-system-monitor.ps1
pwsh .\scripts\publish-desktop-system-monitor.ps1 -Runtime win-x64
# For a reproducibility check, use distinct empty output and artifacts roots:
# pwsh .\scripts\publish-desktop-system-monitor.ps1 -Runtime win-x64 -OutputDir dist\repro-x64-a -ArtifactsPath dist\.artifacts\repro-x64-a
```

See the Japanese README for the complete metric behavior and current known
limitations. English and Japanese release facts must be updated together using
[`docs/publication/language-parity.md`](docs/publication/language-parity.md).

The portable publish is self-contained single-file. Runtime DLLs and satellite
directories are bundled into `DesktopSystemMonitor.exe`; native components may
be extracted under `%TEMP%\.net` on first launch. Keep the README, license,
third-party notices, and `licenses` directory beside the executable. Direct
publish requires an empty output directory; use the rebuild script for managed
replacement or choose a new empty `-OutputDir`. `-ArtifactsPath` is an optional,
separate empty SDK bin/obj root used for reproducibility checks.

## License

Desktop System Monitor's first-party code is available under the
[MIT License](../LICENSE). Third-party components remain subject to their own
licenses; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
