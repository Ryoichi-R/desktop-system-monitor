# DSM-4 ARM64 acceptance checklist

Complete this checklist against
[DSM-4 ARM64 hardware acceptance](DSM-4-arm64-acceptance.md). Store the
completed copy with owner-private evidence; do not add tester or device identity
to the public source tree.

## Candidate binding

- [ ] Product version recorded
- [ ] Candidate lineage ID recorded
- [ ] Source tree digest recorded
- [ ] Runtime is `win-arm64`
- [ ] Dependency fingerprint, .NET SDK, and publish contract recorded
- [ ] Artifact file name and byte count match the candidate manifest
- [ ] Recalculated artifact SHA-256 matches the candidate manifest
- [ ] PE machine is `0xAA64`
- [ ] Test start/end time and tester are recorded privately

## Native launch

- [ ] Windows 11 ARM64 build and device class are recorded privately
- [ ] Process is reported as native ARM64
- [ ] Launch does not require elevation
- [ ] Exactly one app instance and one tray icon are present

## Overlay, tray, settings, and startup

- [ ] Overlay is visible and legible
- [ ] Click-through on/off behavior matches the tray state
- [ ] Settings opens and a reversible change saves successfully
- [ ] Saved setting survives exit and restart
- [ ] Startup registration starts exactly one native ARM64 instance
- [ ] Startup registration is disabled again after verification
- [ ] Tray exit closes the overlay and process

## Metrics and fallbacks

- [ ] CPU updates under idle and representative load
- [ ] Memory updates under idle and representative load
- [ ] Network responds to disconnect, reconnect, and traffic
- [ ] GPU reports a supported value or documented `N/A`
- [ ] Enabled optional metrics report supported values or documented `N/A`
- [ ] No unavailable metric is replaced by an invented value
- [ ] Diagnostics do not grow continuously during fallback checks

## Recovery transitions

- [ ] Win+D and restore
- [ ] Explorer restart
- [ ] Session lock and unlock
- [ ] Sleep and resume
- [ ] Full-screen enter and exit
- [ ] Network disconnect and reconnect
- [ ] Sampling resumes within two normal sampling intervals
- [ ] No duplicate overlay, tray icon, or process remains

## DPI and displays

- [ ] Overlay is checked on every available display
- [ ] Mixed-DPI movement or scale change is checked
- [ ] Primary-display change is checked
- [ ] Secondary-display disconnect and reconnect is checked
- [ ] Overlay remains visible or uses the documented primary-display fallback

## Rotation round trip

- [ ] Landscape starting placement is recorded privately
- [ ] Landscape to portrait keeps the overlay visible
- [ ] Settings remains usable in portrait
- [ ] Restart in portrait restores or safely clamps placement
- [ ] Portrait to landscape keeps the overlay visible
- [ ] Layer, click-through, and saved-display intent remain consistent
- [ ] Sampling resumes without crash or duplicate instance

## Evidence review

- [ ] Private evidence name follows
      `DSM-4-<lineage12>-win-arm64-<UTC timestamp>.json`
- [ ] Every evidence item is bound to the same lineage and artifact SHA-256
- [ ] Public evidence is redacted and hashed
- [ ] Device, user, path, process, and network identifiers remain private
- [ ] Blockers and known limitations are recorded
- [ ] Final decision is `PASS`, `BLOCKED`, or `FAIL`
- [ ] ARM64 is excluded from release scope unless the final decision is `PASS`
