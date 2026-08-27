# Third-party notices

Desktop System Monitor's first-party code is licensed under the MIT License;
see `LICENSE`. The self-contained distribution includes the following
third-party components. Package versions are the resolved `win-x64` graph for
Desktop System Monitor 0.1.0. The Avalonia entries are currently used by the
new `net10.0` migration target; their final self-contained macOS bundle
inventory is a Phase 8 task and must be completed before any macOS publish.

| Component | Version | License | Project/source | Bundled license |
| --- | --- | --- | --- | --- |
| LibreHardwareMonitorLib | 0.9.6 | MPL-2.0 | <https://github.com/LibreHardwareMonitor/LibreHardwareMonitor> | `licenses/LibreHardwareMonitorLib-0.9.6/LICENSE` |
| BlackSharp.Core | 1.0.7 | MPL-2.0 | <https://github.com/Blacktempel/BlackSharp> | `licenses/LibreHardwareMonitorLib-0.9.6/LICENSE` |
| DiskInfoToolkit | 1.1.2 | MPL-2.0 | <https://github.com/Blacktempel/DiskInfoToolkit> | `licenses/LibreHardwareMonitorLib-0.9.6/LICENSE` |
| RAMSPDToolkit-NDD | 1.4.2 | MPL-2.0 | <https://github.com/Blacktempel/RAMSPDToolkit> | `licenses/LibreHardwareMonitorLib-0.9.6/LICENSE` |
| HidSharp | 2.6.4 | Apache-2.0 | <https://software.seekye.com/hidsharp> | `licenses/HidSharp-2.6.4/LICENSE.txt` |
| Mono.Posix.NETStandard | 1.0.0 | BSD-3-Clause | <https://www.nuget.org/packages/Mono.Posix.NETStandard/1.0.0> | `licenses/Mono.Posix.NETStandard-1.0.0/LICENSE.txt` |
| Avalonia | 11.3.20 | MIT | <https://github.com/AvaloniaUI/Avalonia> | To be vendored before macOS publish |
| Avalonia.Desktop | 11.3.20 | MIT | <https://github.com/AvaloniaUI/Avalonia> | To be vendored before macOS publish |
| Avalonia.Themes.Fluent | 11.3.20 | MIT | <https://github.com/AvaloniaUI/Avalonia> | To be vendored before macOS publish |
| Avalonia.Headless.XUnit (test-only) | 11.3.20 | MIT | <https://github.com/AvaloniaUI/Avalonia> | Test dependency; not published |
| Microsoft .NET runtime and runtime packages | Resolved at publish time (currently 10.0.8); see `licenses/dotnet-runtime-*/` | MIT and bundled third-party terms | <https://github.com/dotnet/dotnet> | `licenses/dotnet-runtime-<resolved-version>/` |

The MPL-2.0 license text is identical for each MPL component and is bundled
once at the path shown above. The listed project and NuGet package pages provide
the corresponding source for the exact package versions. Desktop System
Monitor does not modify these package source files.

Desktop System Monitor uses `LibreHardwareMonitorLib` to read CPU package and
GPU package/board power sensors and CPU/GPU temperature sensors.

- Project: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor
- NuGet package: https://www.nuget.org/packages/LibreHardwareMonitorLib/0.9.6
- Source revision: `3d331e3370efb858411f19511373eff65a218701`
- License: Mozilla Public License 2.0 (MPL-2.0)
- License text: https://www.mozilla.org/MPL/2.0/

The exact upstream MPL text and LibreHardwareMonitor third-party notices for
the pinned source revision are bundled in
`licenses/LibreHardwareMonitorLib-0.9.6/`.

The Microsoft .NET runtime license and its complete third-party notices are
copied from the .NET runtime pack version resolved by the SDK at publish time.
The build scripts use an exact-version `licenses/dotnet-runtime-<version>/`
snapshot when one is vendored, and otherwise fall back to the newest vendored
snapshot that shares the same major.minor version (the upstream license text
is stable within a minor version line) — see
`Resolve-DesktopSystemMonitorRuntimeLicenseDirectory` in
`scripts/resolve-desktop-system-monitor-runtime-assets.ps1`. If the runtime
pack moves to a new major.minor version, the dependency fingerprint, this
inventory, and the bundled .NET legal files must be reviewed and a matching
`licenses/dotnet-runtime-<version>/` snapshot added before a new candidate is
accepted.
