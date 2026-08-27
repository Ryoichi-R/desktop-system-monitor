# Phase 3 — macOS安全骨格（実機前の部分実装）

実施日: 2026-08-28（Asia/Tokyo）
判定: **部分実施。Phase 3完了条件は未達**

## 実装した境界

- `DesktopSystemMonitor.Mac` を `net10.0` で新設
- `DesktopSystemMonitor.Mac.SensorHost` を `net10.0`の子プロセス骨格として新設
- Coreへバージョン付きSensorHost固定長フレーム契約を追加
  - 4バイトlittle-endian長 + JSON固定schema
  - 最大payload 64 KiB
  - version / kind / sequence / status の検証
  - `ok`値のfinite・範囲・非負検証
  - oversized / truncated / malformed入力のfail-closed拒否
- bundle内の固定Hostパス検証を追加
  - macOS形式の絶対パスのみ
  - `Contents/MacOS/DesktopSystemMonitor.Mac.SensorHost`との完全一致
  - symlink・非regular file・bundle外を拒否
- `~/Library/Application Support/DesktopSystemMonitor/settings.json` と
  `~/Library/Logs/DesktopSystemMonitor` の明示パスプロバイダーを追加
- Mac Studioのバッテリーを`BatterySnapshot.Absent()`へ固定
- 実機スパイク完了前の未実装ネイティブメトリクスは、推測値を返さず`Unavailable`へ落とすfactoryを追加

## 検証結果

| 対象 | 結果 |
| --- | --- |
| `Mac.Tests` | 8 / 8 green |
| `Mac.SensorHost.Tests` | 5 / 5 green |
| ソリューションRelease build | 0 warning / 0 error |
| 既存Core / Windows / App / Integration回帰 | 234 + 200 + 224 + 9 = 667 green |
| `dotnet format --verify-no-changes --severity error` | 成功 |

## 未完了

この記録はPhase 3の完了記録ではない。次の項目はMac Studio実機とPhase 0M / Phase 2の完了後に実施する。

- Avalonia本体AppへのUI移植と既存WPF Appの置換
- objc interopによるwindow layer / display / fullscreen / tray / launchd実装
- SensorHostの実プロセスlifecycle、timeout、再起動上限、backoff、signal crash統合
- `host_processor_info`、`host_statistics64`、IOKit、`sysctl`、`libproc`のネイティブ実装
- IOReport / SMCの温度・電力取得と実機キー検証
- `.app` bundle、self-contained single-file、ad-hoc署名、clean環境での展開cache検証

未実装のネイティブ指標を0や推定値で表示しないことが、本部分実装の重要な安全条件である。
