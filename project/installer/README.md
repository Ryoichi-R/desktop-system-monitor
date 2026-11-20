# Desktop System Monitor installer

`Install-DesktopSystemMonitor.ps1` is the Windows PowerShell 5.1-compatible
installer used by the root launcher. The script takes an exclusive
cross-session lock on the installer payload, validates the schema v2 manifest
and `single-file-v1` layout, copies the selected ZIP to a temporary directory,
and then validates archive SHA-256, ZIP entries, every managed file, PE
architecture, free space, and the existing install marker before changing the
destination. A pending payload transaction is fail-closed.

The package channel and per-runtime status are intentionally separate:

- `local-preview` may contain an explicitly approved `local-only` runtime. It
  always displays `未検証プレビュー・公開禁止` and records that status in the
  install marker.
- `validation` contains `validation-only` artifacts for acceptance workflows and
  is not accepted by the root launcher.
- `public-release` accepts only `public-release-approved` artifacts whose
  readiness identity and SHA-256 have passed the public promotion gate.

The installer does not require the repository, `.git`, `.github`, a solution, or
the .NET SDK. It resolves only its own `$PSScriptRoot`/`-PackageRoot` and the
selected existing parent directory. User settings and diagnostic logs under
`%LOCALAPPDATA%\DesktopSystemMonitor` are not installer-managed files.

The canonical payload is updated only by
`scripts/rebuild-desktop-system-monitor.ps1`: it publishes a fresh portable
stage, builds a local-preview payload in `dist\.payload-staging-*`, promotes
that payload under `installer\.payload-update.lock`, and only then replaces the
portable output. `-PortableOnly` (or `DSM_PORTABLE_ONLY=1` in the rebuild BAT)
skips payload promotion. Do not invoke the package builder directly against
`installer\payload`; it intentionally requires a staging output.

Exit codes: `0` success, `2` invalid input, `3` folder selection cancelled,
`4` unsupported runtime/status/package, `5` installation failure, and `6` failed rollback or pending recovery.

### Recovering an interrupted update (exit 6)

1. Close Desktop System Monitor. Open the destination-specific
   `*.recovery.json` under `%LOCALAPPDATA%\DesktopSystemMonitorInstaller\Backups`.
   Keep the journal and the entire directory named by `backupPath`.
2. Check the SHA-256 of the backed-up `.desktop-system-monitor-install.json`
   against `markerSha256` with `Get-FileHash -LiteralPath <file> -Algorithm SHA256`.
   Check every relative file in its `managedFiles` list against its recorded SHA-256.
3. Resolve the reported lock or access error. Preserve unrelated user files.
   Inspect any leftover paths in `createdFiles` and move the failed update's
   new files aside. Copy the verified files to the same relative paths under
   `installDirectory`, then restore the original marker byte for byte.
   For a failed first install the marker hash is null and there is no prior
   version to restore. Move only the listed installer-created files aside.
4. Run the installer again. It checks the restored marker and managed files
   before clearing the journal. A backup-rotation warning after a successful
   update names the retained backup locations and the new version remains installed.
