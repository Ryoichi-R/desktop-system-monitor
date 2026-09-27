# macOS Display / Full-screen PoC

This disposable probe supports the remaining Phase 0M P0-2b checks. It opens one movable, all-Spaces overlay panel per active display and a dashboard with JSONL state-change records.

## Build and run

Run these commands from the desktop-system-monitor repository root on macOS. The executable is written to /tmp.

    swiftc -swift-version 5 -parse-as-library -framework AppKit -framework CoreGraphics project/tools/macos-display-poc/main.swift -o /tmp/desktop-system-monitor-display-poc
    /tmp/desktop-system-monitor-display-poc

Stop it with Ctrl+C.

## What it observes

- Active display IDs, AppKit point frames, visible frames, Quartz bounds, backing scale, and primary-display status.
- Display-configuration notifications and active-Space changes.
- A foreground layer-0 window from the frontmost application that covers at least 95% of a display's Quartz bounds. The overlay on that display hides while this heuristic is true and returns when it becomes false.
- Overlay position as normalized fractions of each display's visible frame. The position is retained in memory by display ID and reapplied after topology or scale changes.

The window-list query reads only owner PID, layer, and bounds to make the detection decision. JSONL and dashboard output never include app names, window titles, or PIDs. No window images or screenshots are captured.

## Manual P0-2b checks

1. On each connected display, confirm the overlay panel reports that display's ID, backing scale, and primary status.
2. Start and exit a full-screen app on each display. Confirm the matching panel hides and returns, and the dashboard records the transition for that display.
3. Switch Spaces while panels are visible and while a full-screen app is active. Confirm panels remain on all Spaces and visibility recovers after returning.
4. Move a panel, disconnect and reconnect its display, and note whether macOS reuses the display ID. Confirm the normalized placement is reapplied when the ID is reused.
5. Change a display's scaling and confirm the panel stays within its visible frame at the same normalized position.

The full-screen rule is an experimental 95%-coverage heuristic, not a macOS full-screen API. A maximized ordinary window may also match it. The observation is not a Phase 0M acceptance result until it is exercised with multiple displays and Spaces on the target Mac.
