# ADR 005: Reduced display width

Status: Accepted

## Decision

The widget exposes a persistent `WidgetDisplayMode` setting with `Standard`
(280 DIP) and `Reduced` (150 DIP) values. Existing users and schema 0–3
migrations use Standard. The setting is available in the Settings window and
the tray menu, and is saved before a tray change is applied to the runtime.

Reduced uses a separate WPF content panel so Standard bindings and geometry stay
unchanged. Both panels share row visibility, 48 DIP row height, 4 DIP gaps,
and the same background, scaling, and placement pipeline. Reduced keeps the
primary values and bars for CPU, MEM, GPU, DISK, NET, and BAT. NET is rendered
as two vertical rows so fixed 10 GbE-equivalent `Kb/s` values remain readable;
secondary telemetry and CPU/MEM/GPU/DISK peak markers are omitted. When the
network peak setting is enabled, Reduced adds receive and send peak rows using
the configured time window, increasing only the network row height.

The Core `WidgetWidthCalculator` owns pure width constants and calculation.
WPF remains responsible for panel visibility, scale, EdgeFade expansion,
background synchronization, and window placement. A display change reuses the
existing preset anchor or custom right-edge placement intent. If display
reflow is pending during a user move, the pending timer remains responsible for
the next wave rather than taking the user's position.

## Alternatives rejected

- Mutating one panel through many runtime width and font properties would make
  Standard regressions and hidden secondary bindings difficult to audit.
- A horizontal two-column NET layout does not provide enough width for the
  explicit fixed-Kb/s acceptance case.
- A generic transaction rewrite for every settings path is broader than this
  feature. Only the tray display-width path uses save-before-apply; the existing
  settings-dialog flow remains unchanged.
- Adding a new public parameter to `SetState` would unnecessarily break its
  existing callers. `SetDisplayMode` is an independent synchronization API.
