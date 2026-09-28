# macOS Phase 3 manual acceptance

> Historical Phase 3 increment checklist. The current Mac candidate, including full-screen auto-hide, process details and 1% scale input, is assessed in [macos-final-acceptance.md](macos-final-acceptance.md). Unchecked items below are not an assertion that the current source lacks those features.

This is the stopping point for the automated implementation increment. The
commands below are read-only except for the explicit per-user login-startup
toggle in the application menu. No installation into `~/Applications` is
required for this acceptance.

## Candidate preparation

Run on the target Apple Silicon Mac from the project directory:

```powershell
pwsh -NoProfile -File scripts/publish-desktop-system-monitor-macos.ps1 -OutputRoot <new-output-directory>
pwsh -NoProfile -File scripts/test-publication-contract-macos.ps1 -BundlePath <new-output-directory>/DesktopSystemMonitor.app
open <new-output-directory>/DesktopSystemMonitor.app
```

The second command verifies the exact bundle opened by the third command:
`Info.plist`, fixed App/Host executable names, executable permissions, arm64
Mach-O format, Host inclusion, and ad-hoc signatures. A failed contract is a
candidate failure and should be reported before UI checks.

## Acceptance checklist

- [ ] The app starts without a terminal window and shows CPU and used-memory values.
- [ ] CPU is within approximately ±5 percentage points of Activity Monitor when
      the sampling windows are comparable. Memory is compared using the selected
      definition: internal minus purgeable plus wired plus compressor pages.
- [ ] The widget can be dragged and remains at the expected position after quit
      and restart. Scale and topmost settings also survive restart.
- [ ] The display-form menu switches between the normal 280 DIP layout and the
      Windows-compatible reduced 150 DIP layout, and the selected form survives
      restart.
- [ ] In the normal layout, 50% and 75% scaling produce the intended smaller
      widget without clipping or losing the context/menu-bar controls. 100%,
      125%, and 150% remain available.
- [ ] The menu-bar menu can switch normal, always-on-top, desktop-level, and
      click-through modes. Click-through is restored from the menu-bar item, not
      by clicking the widget itself.
- [ ] The widget remains visible over a full-screen application when the
      full-screen application permits auxiliary windows. If the target desktop
      policy requires auto-hide instead, record that as a product decision;
      automatic full-screen hiding is not implemented in this increment.
- [ ] Enabling login startup creates the expected per-user LaunchAgent behavior;
      disabling it removes that behavior. The owner decides whether this
      per-user setting should remain enabled after the test.
- [ ] Quitting or terminating only `DesktopSystemMonitor.Mac.SensorHost` leaves
      the UI alive and exposes `N/A` or reconnect behavior rather than crashing
      the app. Reopening the app recovers from exhausted retries.
- [ ] If a second display is available, dragging the widget there and back does
      not corrupt the saved position. This is an optional observation for the
      current single-display implementation, not a Phase 3 pass condition.

## Required owner decision

Please report the result of the checklist and choose one of these next steps:

1. Accept the current Phase 3 behavior and continue implementing the remaining
   metric collectors (disk, network, GPU, power, temperature, and process list).
2. Hold on a UI/lifecycle issue and provide the failed checklist item, observed
   behavior, and the relevant app/Host log or screenshot.
3. Decide that full-screen auto-hide, multi-display/Space behavior, or login
   startup policy needs a product change before metric work continues.

GPU, disk, network, power, temperature, high-load process collection, and
full-screen auto-hide remain outside this increment. Their absence is not an
acceptance failure for the checklist above, but they must not be described as
implemented until their own contracts and validation are complete.
