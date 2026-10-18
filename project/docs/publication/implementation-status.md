# Public release implementation status

Updated: 2026-08-03 JST

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
- Release build, 649 tests, coverage thresholds (Core 92.1% / Windows 74.79%),
  format, JSON/schema, relative links, PC-specific path scan, publication
  contract, installer contracts on PowerShell 7 and Windows PowerShell 5.1,
  and release dry-run verification.

## Local candidate verification (not public distribution)

- The current local candidate uses a new source/dependency-bound lineage and
  has clean, reproducible `win-x64` and `win-arm64` single-file publishes.
- Schema-v2 `single-file-v1` installer payloads were generated outside the
  canonical payload directory and passed ZIP, managed-file, PE, and runtime
  contract checks. The generated payloads are `local-preview` only and are not
  public release assets.
- PowerShell 7 and Windows PowerShell 5.1 installer tests passed for the
  normal, migrated, and real distribution-layout contracts.

## Source publication status

- Public repository: `Ryoichi-R/desktop-system-monitor`.
- Clean source-only snapshot, history, personal-data, and secret scans completed.
- Public visibility and the initial GitHub Actions run were confirmed on
  2026-07-26 JST.

Binary publication remains blocked until its separate DSM-1 metric, DSM-2
endurance, DSM-4 ARM64 hardware, signing, packaging, and architecture gates
pass. The checked-in readiness summary remains `BLOCKED`; local candidate
verification does not change that decision.
