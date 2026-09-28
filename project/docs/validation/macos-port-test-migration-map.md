# WPF to Avalonia test migration map

Updated: 2026-09-27. This is a source inventory, not evidence of equivalent product behavior.

The current Avalonia suite has five scaffold checks: initialization, initial N/A, unavailable refresh, available formatting, and source failure. Phase 0W tests validate the UI framework only. Keep Windows tests until their intent has equivalent coverage and the plan gates pass. Counts below are test method declarations, not expanded Theory cases.

| Windows source | Methods | Equivalent Avalonia tests | Status |
| --- | ---: | --- | --- |
| `tests/unit/App.Tests/BackgroundLayerCoordinatorTests.cs` | 5 | Pending | Phase 2 migration / OS adapter split |
| `tests/unit/App.Tests/BackgroundPresentationPolicyTests.cs` | 2 | Pending | Phase 2 migration / OS adapter split |
| `tests/unit/App.Tests/BatteryChargeStateCoordinatorTests.cs` | 3 | Pending | Phase 2 migration / OS adapter split |
| `tests/unit/App.Tests/DiagnosticLogTests.cs` | 7 | Pending | Phase 2 migration / OS adapter split |
| `tests/unit/App.Tests/DisplayModeChangeCoordinatorTests.cs` | 4 | Pending | Phase 2 migration / OS adapter split |
| `tests/unit/App.Tests/DisplayReflowPolicyTests.cs` | 14 | Pending | Phase 2 migration / OS adapter split |
| `tests/unit/App.Tests/ExecutablePathTests.cs` | 2 | Pending | Phase 2 migration / OS adapter split |
| `tests/unit/App.Tests/FullScreenAutoHideCoordinatorTests.cs` | 2 | Pending | Phase 2 migration / OS adapter split |
| `tests/unit/App.Tests/HighLoadProcessesWindowTests.cs` | 2 | Pending | Phase 2 migration / OS adapter split |
| `tests/unit/App.Tests/LayerLifecycleCoordinatorTests.cs` | 9 | Pending | Phase 2 migration / OS adapter split |
| `tests/unit/App.Tests/LayerRepairSuspensionCoordinatorTests.cs` | 11 | Pending | Phase 2 migration / OS adapter split |
| `tests/unit/App.Tests/MainWindowLayerTests.cs` | 18 | Pending | Phase 2 migration / OS adapter split |
| `tests/unit/App.Tests/MainWindowLayoutTests.cs` | 30 | Pending | Phase 2 migration / OS adapter split |
| `tests/unit/App.Tests/MetricViewModelTests.cs` | 26 | Pending | Phase 2 migration / OS adapter split |
| `tests/unit/App.Tests/OptionalTelemetryCapabilitiesTests.cs` | 2 | Pending | Phase 2 migration / OS adapter split |
| `tests/unit/App.Tests/SamplingSuspensionCoordinatorTests.cs` | 3 | Pending | Phase 2 migration / OS adapter split |
| `tests/unit/App.Tests/SettingsDialogContextTests.cs` | 2 | Pending | Phase 2 migration / OS adapter split |
| `tests/unit/App.Tests/SettingsEditMergeTests.cs` | 5 | Pending | Phase 2 migration / OS adapter split |
| `tests/unit/App.Tests/ShutdownGateTests.cs` | 2 | Pending | Phase 2 migration / OS adapter split |
| `tests/unit/App.Tests/StartupBatteryProbeTests.cs` | 3 | Pending | Phase 2 migration / OS adapter split |
| `tests/unit/App.Tests/TrayIconTests.cs` | 5 | Pending | Phase 2 migration / OS adapter split |
| `tests/unit/App.Tests/WindowPlacementControllerTests.cs` | 13 | Pending | Phase 2 migration / OS adapter split |

## Acceptance mapping

| Contract | Existing test families | Required evidence |
| --- | --- | --- |
| Layout and settings | MainWindowLayout, MetricViewModel, SettingsWindow | Fields, display modes, N/A, capabilities |
| Placement | WindowPlacementController, DisplayReflowPolicy | v5 ratios, DPI, rotation, restart restore |
| Layers and click-through | MainWindowLayer, BackgroundLayerCoordinator, LayerRepairSuspensionCoordinator | Three modes, repair suppression, lifecycle attach/detach |
| Settings compatibility | Core Settings tests and App placement tests | v4-to-v5 migration, v5 round-trip, actual placement restore |
| Tray, shutdown, startup | AppComposition and OS adapters | Native manual acceptance and lifecycle integration tests |

## Completion rules

1. Resolve D19/D10 and verify a restart snapshot before product migration.
2. Replace each Pending cell with exact test names, execution date, and result.
3. Rebaseline Windows coverage after removing broad logical-code exclusions.
4. Retain WPF until Windows acceptance, D10-M, Phase 0M GO, and rollback validation pass.
