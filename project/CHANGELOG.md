# Changelog

## Unreleased

- Fixed the Windows network speed being reported several times too high.
  `GetIfTable2` also returns NDIS filter / intermediate-driver rows that carry
  the same traffic as the physical adapter, and the automatic mode summed every
  Up row; on the affected machine the total was exactly 7x the physical
  adapter. The automatic mode now sums only hardware interfaces that are not
  NDIS filters (`InterfaceAndOperStatusFlags`), decided on every sample, and
  falls back to all non-filter rows when no Up hardware interface exists.
  Rows that stay in the table keep their rate state when the selection
  switches. Explicitly selected adapter LUIDs are still summed as specified.
  Remaining limits (overlapping non-filter rows in the fallback mode,
  hardware-flag-less adapters such as Bluetooth PAN) are documented in the
  README and `docs/adr/001-metrics.md`.
- Excluded `tests/integration/Mac/` from the Windows integration test project
  so that it builds; that folder has its own project.
- Fixed the object-initializer formatting that made
  `dotnet format --verify-no-changes` fail in the Avalonia app and the Mac
  sensor host.
- Added headless tests for the Avalonia main window (context-menu actions,
  detail windows, live timer, screen changes, diagnostic logging) so that
  `App.Avalonia.Tests` meets its 90% line-coverage gate on Windows. The
  coverage script now excludes the Avalonia process entry point
  (`Program.cs`); the macOS-only branches are still measured only on macOS.
  `Program.Diagnostics` gained an internal setter so tests can redirect the
  log away from the user profile.
- Made two tests independent of the machine they run on: the PDH CPU counter
  integration test now collects again until PDH returns a valid sample (about
  one read in five was transiently invalid), and the Mac path provider test
  no longer assumes that a non-macOS host has no home directory.
- Fixed the settings window (and the high-load-processes window) sinking
  behind other applications when the widget's layer mode is "デスクトップ上"
  (`OnDesktop`/BottomMost). The settings window is owned by the widget, and
  the widget's periodic BottomMost repair was pulling its owned dialog down
  with it every two seconds. The settings dialog now suspends the widget's
  layer repair for the duration it is open — tracked by reason, so an
  overlapping session lock/unlock does not resume repair early — and the
  high-load-processes window is no longer owned by the widget at all.
- Rebuild now derives the portable output and installer payload from one
  single-file publish contract.
- Installer payloads use schema v2, include explicit layout and publish
  contract metadata, and reject loose runtime files.
- Canonical payload replacement is staged and transactional: a cross-session
  payload lock, pending transaction marker, payload-first promotion, and
  portable-output rollback prevent an old ZIP from being reused silently.
- The root launcher no longer performs an unprotected payload existence check;
  payload validation and copying are performed by the installer while holding
  the payload lock.
