# Readiness evidence contract

`readiness-summary.json` is the public, redacted release gate. Raw CSV files,
machine names, process names, user names, absolute paths, and tester details must
remain outside Git under `result/desktop-system-monitor-<run-id>/`.

Version `0.1.0` uses this document only as the future **prebuilt binary** gate.
The public preview is source-only, so an architecture may remain `BLOCKED`
without blocking publication of the source. No prebuilt artifact may be
published or inferred from the source-only decision.

The checked-in summary intentionally starts in `BLOCKED`. Replace its null
lineage and artifact hashes only after a candidate is generated and DSM-1 to
DSM-4 evidence is reviewed. A release workflow must reject an architecture
unless its status is `PASS` or an explicitly approved `CONDITIONAL` decision.

The candidate lineage is SHA-256 over the production source tree digest,
dependency fingerprint, .NET SDK, product version, and publish contract. It is
independent of the selected RID or RID set, so x64 and ARM64 artifacts built
from the same source candidate share one lineage.

RID-specific identity remains fail-closed under `architectures.<rid>` through
the artifact SHA-256, readiness status, completed tests, blockers, and reviewed
evidence hashes. Adding a RID does not change the source lineage, but its
artifact, automated checks, hardware acceptance, and readiness decision cannot
be inherited from another RID.

The x64 acceptance procedures are:

- [DSM-1 metric acceptance](DSM-1-metric-acceptance.md) with its
  [owner-private completion checklist](DSM-1-metric-acceptance-checklist.md)
- [DSM-2 endurance acceptance](DSM-2-endurance-acceptance.md) with its
  [owner-private completion checklist](DSM-2-endurance-acceptance-checklist.md)

ARM64 hardware acceptance uses
[the DSM-4 specification](DSM-4-arm64-acceptance.md) and its
[owner-private completion checklist](DSM-4-arm64-acceptance-checklist.md).
