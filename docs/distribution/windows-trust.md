# Windows trust and distribution decision record

Status: **NOT APPLICABLE TO THE 0.1.0 SOURCE-ONLY PREVIEW**

Version `0.1.0` distributes no first-party executable or portable archive.
Code-signing certificate and publisher identity therefore do not block this
source publication. Locally built executables are unsigned and are not official
release assets.

For a future binary release, the preferred portable path is a versioned
immutable GitHub Release whose
first-party executable is Authenticode-signed and RFC 3161 timestamped before
ZIP creation. The release manifest records both architecture and signature
status. Users are never instructed to disable SmartScreen.

The release script accepts a project-local signing hook and exact expected
certificate subject; substring matches are rejected.
Credentials, PFX files, passwords, identity documents, and certificate material
must remain outside the repository and logs. An unsigned preview requires a
recorded owner, version, reason, and future expiry; it is not an implicit success.

MSIX/Store remains a feasibility spike. Tray behavior, single instance,
startup, settings/log paths, Win32 metrics, install/upgrade/uninstall, and WACK
must pass before choosing that path. WinGet submission is post-publication only
and must use the immutable public asset URL and SHA-256 from the release manifest.
