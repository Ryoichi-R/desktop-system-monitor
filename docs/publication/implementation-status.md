# Public release implementation status

Updated: 2026-07-26 JST

## Selected public scope

Version `0.1.0` is publicly available as a source-only experimental preview.
Prebuilt executables, portable archives, signed assets, release-candidate media,
and distribution submissions are outside this scope. Two redacted evaluation UI
screenshots are included in the README as interface examples, not acceptance
evidence. DSM-1, DSM-2, DSM-4, publisher identity, and signing remain future
binary-release gates.

## Implemented and validated

- Repository-local solution, unit/integration tests, coverage scripts, ADR index,
  and standalone GitHub Actions candidates.
- DSM-3 full-screen/session sampling transition seam and regression tests.
- Redacted readiness schema and fail-closed architecture summary.
- Deterministic release orchestration, checksum/manifest schemas, signing hook,
  immutable asset rules, and WinGet preparation template.
- Japanese/English entry points, privacy/security/support/contribution documents,
  Issue Forms, GitHub settings change-set, incident response, and launch drafts.
- Build, 581 tests, coverage thresholds, format, JSON/schema, relative links,
  PC-specific path scan, and release dry-run verification.

## Source publication status

- Public repository: `Ryoichi-R/desktop-system-monitor`.
- Clean source-only snapshot, history, personal-data, and secret scans completed.
- Public visibility and the initial GitHub Actions run were confirmed on
  2026-07-26 JST.

Binary publication remains blocked until its separate hardware, signing,
packaging, and architecture gates pass.
