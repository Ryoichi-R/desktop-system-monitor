# Usage

Version `0.1.0` is published as source only. There is no official validated
archive or prebuilt executable. Build locally using the commands in the root
README, keep the complete publish directory together, and start its
`DesktopSystemMonitor.exe`. The local executable is unsigned.

Use the tray icon to open Settings, disable click through while moving the
widget, change its layer, toggle Start with Windows, or exit. Start with Windows
is not recommended until DSM-1 is complete.

For a local portable update, exit the old process, build into a new directory,
start it once, then update any shortcut or startup registration. Keep the
previous directory until the new build passes a normal-use smoke test. To
uninstall, disable Start with Windows, exit, delete the local binary directory,
and optionally remove the local data described in
[PRIVACY.md](../../PRIVACY.md).
