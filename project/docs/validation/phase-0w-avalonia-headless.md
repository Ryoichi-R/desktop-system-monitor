# Phase 0W — Avalonia / Headless 技術ゲート

実施日: 2026-08-28（Asia/Tokyo）
対象: Windows 11 / .NET SDK 10.0.303 / `net10.0`
判定: **GO（Phase 2のWindows側着手条件を満たす）**

## 実施内容

Phase 0Wの判定は本番Appと分離した`tests/phase0w` と `src/DesktopSystemMonitor.Avalonia.PoC` の最小PoCで行った。

- Avalonia 11.3.20 のデスクトップテンプレート相当を `net10.0` でrestore/build
- Avalonia.Headless.XUnit 11.3.20 と FluentTheme を使用
- レイアウトの最小サイズとコントロール探索
- 設定入力とボタンイベントによる表示更新
- ウィンドウ状態の Normal / Maximized 切り替え

HeadlessはAvaloniaのコントロールツリー、レイアウト、スタイル、データバインディングを実行できるため、UIの自動テスト基盤として採用する。実ウィンドウ、DPI、クリック透過、最前面・最背面、トレイ、複数ディスプレイはHeadlessの責務外としてWindowsまたはmacOS実機受入へ分離する。

## 検証結果

```text
dotnet restore .\tests\phase0w\DesktopSystemMonitor.Avalonia.PoC.csproj --nologo
  成功

dotnet test .\tests\phase0w\DesktopSystemMonitor.Avalonia.PoC.csproj --no-restore --nologo --configuration Release --logger "console;verbosity=minimal"
  成功: 3 / 3、失敗 0、スキップ 0
```

使用した主要パッケージはAvalonia公式配布のMITライセンス版で、既存のxUnit v2系と互換する11.3.20を固定した。Avalonia 12系はHeadless.XUnitがxUnit v3系を要求するため、このPoCでは既存テスト基盤との移行範囲を増やさない選択をした。

## 既存WPF UIテストの分類

行数は2026-08-28時点の各`.cs`ファイルの物理行数である。合計は5,289行で、計画記載の実測値と一致する。

| 分類 | ファイル | 行数 | Phase 2の扱い |
| --- | --- | ---: | --- |
| Headless controls | `MainWindowLayoutTests.cs`, `BackgroundLayerCoordinatorTests.cs`, `HighLoadProcessesWindowTests.cs` | 1,659 | Avaloniaの実コントロール、XAML、データバインディング、レイアウトへ移植 |
| 純粋ロジック / ViewModel | `BackgroundPresentationPolicyTests.cs`, `BatteryChargeStateCoordinatorTests.cs`, `DiagnosticLogTests.cs`, `DisplayModeChangeCoordinatorTests.cs`, `DisplayReflowPolicyTests.cs`, `ExecutablePathTests.cs`, `FullScreenAutoHideCoordinatorTests.cs`, `LayerLifecycleCoordinatorTests.cs`, `LayerRepairSuspensionCoordinatorTests.cs`, `MetricViewModelTests.cs`, `OptionalTelemetryCapabilitiesTests.cs`, `SamplingSuspensionCoordinatorTests.cs`, `SettingsDialogContextTests.cs`, `SettingsEditMergeTests.cs`, `ShutdownGateTests.cs`, `StartupBatteryProbeTests.cs` | 2,409 | Avaloniaから独立した通常のxUnitテストとして維持・移設 |
| Windows実機 / OS依存受入 | `MainWindowLayerTests.cs`, `TrayIconTests.cs`, `WindowPlacementControllerTests.cs` | 1,221 | HWND、WndProc、トレイ、DPI、実ディスプレイを使う受入へ分離。判定ロジックはHeadlessまたは通常の単体テストへ抽出 |

## D4の決定

**Avalonia.Headless + xUnitを採用する。**

1. Avaloniaのコントロールを含むレイアウト・バインディング・設定画面・ウィンドウ状態は`[AvaloniaFact]`で検証する。
2. `MetricViewModel`、設定マージ、表示ポリシー、ライフサイクル状態機械、停止・再開判断は、UIスレッドを必要としない通常のxUnitで検証する。
3. HWND/WndProc、クリック透過、ウィンドウ層の実効値、トレイ、DPI、複数ディスプレイ、フルスクリーン連携はプラットフォームアダプターと実機受入へ分離する。これらをHeadlessの成功で代替しない。

## App ≥90%への到達可能性と不足テスト

PoCはHeadless基盤の成立を示すものであり、現行WPF Appの90%カバレッジを証明するものではない。Phase 2では以下を満たす構造へ分解することで到達可能と判定する。

- ViewModel、設定、表示ポリシー、ライフサイクル、背景レイヤの判定をUI・OS APIから分離する
- Avaloniaの実コントロールを使う1,659行相当のテストをHeadlessへ移植し、XAMLロード、レイアウト、表示状態、入力イベントをカバーする
- WPF固有のWndProc、Win32、トレイ、DPI呼び出しは宣言専用ファイルと状態・判定ロジックへ分離する。宣言専用ファイルだけをカバレッジ除外し、ロジックを除外しない
- 移植時に追加するDispatcher、ウィンドウattach/detach、設定保存・復元、閉じる操作、表示モード遷移、ディスプレイ再配置、Headlessでのイベント伝播のテストを不足分として追加する
- 最終的にApp単体のline coverageを90%以上として計測し、未到達行を一覧化する。90%未満の場合も閾値は下げず、Phase 2内で不足テストを追加する

## Phase 2で実機受入へ送る項目

- 常に手前、デスクトップ最背面、通常の3表示階層
- クリック透過、ドラッグ位置保存、再起動後の位置復元
- トレイアイコンとメニュー操作
- DPI、複数ディスプレイ、解像度・スケール変更への追従
- フルスクリーン時の自動非表示
- Windows既存設定パスと`SchemaVersion` 4の互換性

このPoCはPhase 0MのmacOS実機検証を代替しない。また、SensorHost、macOSネイティブメトリクス、macOS bundle publish、commit/pushは本記録の対象外である。
