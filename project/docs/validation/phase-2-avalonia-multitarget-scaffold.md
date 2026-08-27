# Phase 2 — Avalonia多ターゲット移行骨格

実施日: 2026-08-28（Asia/Tokyo）
判定: **部分実施。Phase 2完了条件は未達**

## 実装した範囲

- `DesktopSystemMonitor.App`を`net10.0;net10.0-windows10.0.19041.0`へ多ターゲット化
- 既存WindowsターゲットではWPF / WinForms実装を維持し、既存利用者向け設定パスと回帰経路を変更しない
- `net10.0`ターゲットではAvalonia 11.3.20の共通App骨格を使用し、CoreとMac compositionだけを参照
- Avalonia側のHeadlessテストプロジェクトを追加し、レイアウト、N/A安全初期値、再確認イベントを検証
- ソリューションへMac、SensorHost、Avalonia側テストを追加

## 検証結果

| 検証 | 結果 |
| --- | --- |
| ソリューションrestore | 成功 |
| ソリューションRelease build | 0 warning / 0 error（Windows WPF + net10.0 Avalonia） |
| `App.Avalonia.Tests` | 2 / 2 green |
| 既存Windows回帰 | Core 234 + Windows 200 + WPF App 224 + Integration 9 = 667 green |
| Phase 0W PoC | 3 / 3 green |

## 方式と保留事項

これはD10の正式判定ではなく、案A（App多ターゲット + 条件付き参照）の実装骨格である。Windows / macOS双方の完全なrestore・build・test・publish、runtime assets一意選択、Windows publication contractを満たす比較結果はまだ揃っていない。

また、Avalonia側は現段階でネイティブSensorHostへ接続せず、取得不能な指標を`N/A`表示する安全な器に限定している。既存WPF UIの振る舞い等価、設定保存・復元、表示階層、クリック透過、トレイ、複数ディスプレイ、実機受入、旧WPF App削除は未完了である。
