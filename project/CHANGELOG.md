# Changelog

## Unreleased

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
