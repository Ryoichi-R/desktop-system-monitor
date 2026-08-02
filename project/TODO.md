# TODO

## Unstarted

- **BottomMost の冪等チェック追加**（`WindowLayerRepairEngine` / `BottomMostStrategy`）
  - 着手トリガー: 次回レイヤ修復のパフォーマンス最適化に着手するとき、または定期的な `SetWindowPos(HWND_BOTTOM, ...)` が他ウィンドウ・プロファイリング・省電力に悪影響を与えたと確認されたとき
  - 期限: 未定（上記トリガー発生まで着手しない）
  - 影響範囲: `src/DesktopSystemMonitor.Windows/Window/WindowLayerRepairEngine.cs`（`Repair()` の高速パス条件）、`src/DesktopSystemMonitor.Windows/Window/BottomMostStrategy.cs`、`tests/unit/Windows.Tests/*`、`tests/unit/App.Tests/MainWindowLayerTests.cs`
  - 背景: `WindowLayerRepairEngine.Repair()` の冪等スキップ判定（既に望む状態なら `SetWindowPos` を呼ばない高速パス）は `TopMost` / `Normal` のみが対象で、`BottomMost` は対象外のため、2秒周期のタイマーで毎回 `SetWindowPos(HWND_BOTTOM, ...)` が発行される。設定ウィンドウが背面に沈む不具合（[docs/adr/004-background-layering.md](docs/adr/004-background-layering.md) 参照）の根本原因の一つであり、`LayerRepairSuspensionCoordinator` による一時停止で症状自体は解消済みだが、定常時の無駄な `SetWindowPos` 呼び出しは残っている

## Completed
