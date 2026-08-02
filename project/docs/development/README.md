# Development and release gates

The solution, tests, scripts, ADRs, workflows, and schemas are repository-local.
Run build, format, unit/integration tests, and coverage before a release dry-run.

`scripts/new-desktop-system-monitor-release.ps1 -DryRun` validates version and
contract files without generating an asset. A real package additionally
requires the repository `LICENSE`, a matching candidate lineage, and a
non-blocked architecture decision in `docs/validation/readiness-summary.json`.

Signing is a project-local hook after publish and before ZIP creation. Signing
credentials must be supplied by managed identity or an external secret store,
never by a tracked PFX or password.

The signing hook must return a valid Authenticode signature whose certificate
subject exactly matches `ExpectedPublisher`. If a preview must remain unsigned,
the caller must pass `-AllowUnsignedPreview` together with a non-empty waiver
owner, reason, and future ISO-8601 expiry. The waiver is recorded in the release
manifest; omitting either signing or the complete waiver blocks packaging.
