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

## 2026-09-27 IPC契約追補

計画のPhase 0M作業として、SensorHost契約をversion 2へ更新した。これはIPC契約の実装・検証であり、SensorHostの実プロセスlifecycleやnative collectorを実装した記録ではない。

- 各指標は`Ok`／`Unavailable`／`WarmingUp`／`Stale`、nullable値、単調時計の採取時刻を持つ。欠落fieldと未知fieldは拒否し、取得不可を0へ既定化しない。
- 要求IDとHost世代を照合し、応答・各採取値の鮮度を検証する。親子の単調時計周波数不一致、応答生成より未来の採取時刻、古い値は拒否する。
- ネットワークinterfaceは最大64件・名前128文字、高負荷processは最大32件・名前256文字。payloadは64 KiBで上限を設ける。
- 検証: Core.Tests 261/261、Mac.Tests 13/13、Mac.SensorHost.Tests 11/11。coverage gateはCore 94.11%、App.Avalonia 95.38%、Mac 97.43%、Mac.SensorHost 93.33%。Windows.TestsはmacOS上のため未実行。

SensorHostは現時点で`native-metrics-not-implemented`を返し、`MacMetricSourceFactory`も`Unavailable`のままである。実プロセス起動・timeout・再起動/backoff・signal crash分離・ネイティブ指標取得は未完了であり、Phase 0MのGOおよびD10-Mの後に実装する。
