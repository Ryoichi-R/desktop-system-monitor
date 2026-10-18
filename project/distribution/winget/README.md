# WinGet submission template

Do not submit this directory as-is. After public release, generate manifests
from the immutable asset URLs and hashes in the launch handoff, then run
`winget validate` and Windows Sandbox install/launch/upgrade/uninstall tests.

Required owner decisions: PackageIdentifier, Publisher, License, support/privacy
URLs, and whether both x64 and ARM64 passed readiness. Never copy a placeholder
URL or hash into `winget-pkgs`.

WinGet consumes the versioned portable ZIP artifact and its `InstallerSha256`.
The local one-click installer payload is a separate artifact role; both are
derived from the same canonical publish stage and are compared by content
digest and managed-file set, not by requiring identical archive hashes.
