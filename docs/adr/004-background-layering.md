# ADR 004: Background layering

- Status: Accepted
- Date: 2026-07-18

## Context

The widget is transparent by default. Text can become difficult to read over a
bright or detailed wallpaper, but applying opacity to the whole WPF window also
fades the metric text. A topmost opaque background would improve readability
while unnecessarily covering application content below the widget.

The application already supports three foreground layer modes:

- `AlwaysOnTop` maps to `LayerStrategy.TopMost`.
- `Normal` uses the normal non-topmost Z-order.
- `OnDesktop` maps the complete widget to `LayerStrategy.BottomMost`.

## Decision

Background opacity is independent from `Window.Opacity`; the foreground window
always keeps opacity 1. The runtime chooses one presentation:

- `None`: no background.
- `Inline`: the main widget border owns the background.
- `Split`: the main widget remains topmost and transparent while a separate,
  click-through, non-activating background window is kept BottomMost.

`Split` is effective only for `AlwaysOnTop`. `Normal` and `OnDesktop` always use
`Inline`, even when the saved split preference is true. The preference is not
discarded, so returning to `AlwaysOnTop` restores split presentation.

The background window observes the main window's DIP bounds and never writes
position state back. Location updates are coalesced at render priority, with an
exact synchronization after a user drag. Full-screen hide, session suspension,
display changes, taskbar recreation, and shutdown explicitly include both
windows.

Background fill is independent of presentation mode. `Solid` remains the
backward-compatible default. `EdgeFade` uses the configured color and opacity
over the original metric rectangle. It expands the window leftward by 5–50% of
the metric width and downward by the same percentage of the metric height, then
applies independent linear alpha masks only across those added areas. The metric
rectangle starts exactly where both masks become opaque. Nesting the masks
multiplies their alpha at the lower-left corner, producing a seamless two-axis
fade rather than a radial or hard-corner shape. The metric surface is a separate
sibling and is never masked. The same factory is used for `Inline` and `Split`,
so switching layer modes cannot change the selected appearance.

Each HWND owns a separate `WindowLayerRepairEngine` instance. The background
instance always requests `BottomMost`. After repeated repair failure, only the
background is suppressed; it is never promoted to Normal because that could
cover another application. A later successful repair restores it.

## Settings compatibility

The original additive v1 fields are `BackgroundEnabled`, nullable
`BackgroundOpacity`, and `HideBackgroundBehindWindows`. Schema v2 adds the
serialized `BackgroundFillMode` vocabulary plus `BackgroundEdgeFadePercent`.
Migration from v1 writes `Solid` and 22, so older settings retain their exact
solid appearance.

`BackgroundOpacity`, when present, is authoritative. If it is absent, the alpha
channel of a legacy `#AARRGGBB` `BackgroundColor` supplies the initial opacity;
a six-digit color implies full opacity. The settings UI saves canonical opaque
`#FFRRGGBB` plus an explicit `BackgroundOpacity`. The legacy whole-window
`Opacity` field remains round-tripped but is not rendered.

## Consequences

- Existing users remain transparent because `BackgroundEnabled` defaults to
  false.
- Metric text never fades with the configured background.
- Normal non-topmost windows can occlude only the split background.
- Other topmost windows may still cover the metric foreground; this is outside
  the guarantee.
- Split mode adds a second HWND and requires explicit lifecycle and DPI tests.
