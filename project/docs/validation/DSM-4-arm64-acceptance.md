# DSM-4 ARM64 hardware acceptance

## Purpose

DSM-4 determines whether a specific `win-arm64` validation artifact may be
included in a release. It does not approve x64 artifacts and it cannot be
carried forward to a different candidate lineage or artifact SHA-256.

The test is performed on native Windows 11 ARM64 hardware. The Phase 3
validation artifact may be unsigned; Authenticode, timestamp, publisher, ZIP
extraction, and packaged-binary smoke checks are separate Phase 5 gates for the
formal release artifact.

## Required identity

Before testing, copy these values from the reviewed owner-private candidate
manifest into the private test record:

- product version
- candidate lineage ID
- source tree digest
- runtime: `win-arm64`
- unsigned artifact SHA-256
- artifact size and file name
- .NET SDK, dependency fingerprint, and publish contract
- test start time in UTC and tester identity

The artifact must have PE machine `0xAA64`. Recalculate its SHA-256 on the ARM64
device and require an exact match with the candidate manifest before launch.
Record the Windows build and device class privately; do not put device names,
user names, serial numbers, network identifiers, or absolute paths in the
public readiness summary.

## Test environment

- Native Windows 11 ARM64 device with current security updates.
- At least one display capable of landscape and portrait rotation.
- A second display for the multi-monitor checks when available. If unavailable,
  DSM-4 remains blocked unless the selected release scope explicitly removes
  the multi-monitor claim.
- A standard, non-elevated user session.
- Task Manager available for architecture and process-state observation.
- Settings and startup state backed up before the run. Startup registration
  created by the test must be disabled again at the end.

## Procedure and acceptance criteria

### 1. Artifact and native launch

1. Verify the candidate lineage, source digest, runtime, file size, and SHA-256.
2. Verify PE machine `0xAA64`.
3. Launch without elevation and confirm Task Manager reports a native ARM64
   process rather than an emulated x64 process.
4. Confirm that only one app instance and one tray icon are created.

Pass when all identity values match, the process is native ARM64, and startup
does not crash or request elevation.

### 2. Overlay, tray, settings, and startup

1. Confirm the overlay is visible and its text remains legible.
2. Toggle click-through from the tray and verify pointer input reaches the
   window below only while click-through is enabled.
3. Open settings from the tray, change a reversible display option, save, close,
   and reopen settings.
4. Exit from the tray, restart, and confirm the saved option is restored.
5. Enable Windows startup from the tray, sign out and back in or use an
   equivalent owner-approved startup verification, and confirm one native ARM64
   instance starts. Disable startup again before completing the run.

Pass when tray state matches behavior, settings survive restart, and startup
registration neither duplicates the process nor points to another artifact.

### 3. Metrics and unavailable-data behavior

Observe CPU, memory, GPU, network, and each enabled optional metric under idle
and representative load. CPU, memory, and network must update without freezing.
GPU and hardware-sensor metrics may report `N/A` when the ARM64 device or driver
does not expose a supported source; they must not invent a value, crash, or
continuously grow diagnostics. Record whether each metric returned a value or
the documented fallback.

DSM-4 verifies ARM64 behavior and fallback handling. Numerical tolerance against
Task Manager is decided by DSM-1 and is not redefined here.

### 4. Shell, session, sleep, and full-screen recovery

With the app running, perform each transition and allow at least two normal
sampling intervals after recovery:

- Win+D followed by desktop restore
- Explorer restart
- session lock and unlock
- sleep and resume
- entry to and exit from a full-screen application
- network disconnect and reconnect

Pass when the app remains responsive, does not duplicate its overlay or tray
icon, restores the requested layer behavior, and resumes sampling. A metric may
temporarily show `N/A` during a documented unavailable interval.

### 5. DPI and multi-monitor recovery

Move or anchor the overlay on each available display, including displays with
different scale factors. Change the scale factor or primary-display assignment
where the device permits it, then disconnect and reconnect the secondary
display.

Pass when the overlay stays inside a visible working area, remains usable, and
returns to the saved display or the documented primary-display fallback without
creating a duplicate window.

### 6. Rotation round trip

1. Start in landscape and record the selected display, anchor, and visible
   overlay position.
2. Rotate to portrait while the app is running.
3. Open and use the settings dialog in portrait.
4. Exit and restart while still in portrait.
5. Rotate back to landscape.

Pass when every orientation leaves the overlay and settings dialog visible and
interactive, saved placement is restored or safely clamped to the working area,
the requested layer and click-through state are retained, and sampling resumes
without a crash or duplicate instance.

## Evidence and decision

Use the private record name
`DSM-4-<lineage12>-win-arm64-<UTC timestamp>.json` and an adjacent redacted
checklist. Screenshots or logs containing device, user, process, path, or
network details remain owner-private.

DSM-4 is `PASS` only when every required checklist item passes against the same
lineage and artifact SHA-256. An identity mismatch, missing native ARM64 device,
missing required display transition, crash, data fabrication, unrecovered
overlay, or unexplained duplicate process makes the result `BLOCKED` or `FAIL`;
the ARM64 asset must then be excluded from the release.

After review, publish only the decision, candidate lineage, artifact SHA-256,
completed-test names, blockers or limitations, and hashes of approved redacted
evidence in `readiness-summary.json`.
