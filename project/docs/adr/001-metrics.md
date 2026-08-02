# ADR: Desktop System Monitor の指標選定と許容差

- Status: **Draft** (自動実測済み、タスク マネージャー同期比較未了)
- Date: 2026-07-11
- Context: migrated from the private parent workspace's historical implementation plan; that private planning path is not part of the standalone repository.

Draft期間中のportable成果物はExperimental扱いとし、DSM-1完了前にログオン自動起動へ投入しない。

## 決定 (暫定)

Phase 0 の実測に基づく最終決定は本 ADR を **Accepted** に更新する時点で確定する。実装時点で採用している候補と暫定パラメーターを以下に示す。

### CPU 利用率
- **採用**: `Processor Information(_Total)\% Processor Utility`
- **フォールバック**: `\% Processor Time` (raw が取得不能な場合)
- **前処理**: raw 値は診断用に保持しつつ、UI 表示・タスク マネージャー比較値は `Math.Clamp(0, 100)`。NaN / Infinity / invalid `CStatus` は欠測。

### CPU 現在周波数 (推定)
- **採用**: `\% Processor Performance × PROCESSOR_POWER_INFORMATION.MaxMhz / 100`
- **表示**: `x.xx GHz*` (`*` は「推定」を示す)
- **不採用理由**:
  - `Processor Frequency` 直接値は Hyper-V 有効時に既知の誤差事例あり
  - `CurrentMhz` は logical processor ごとに揃わず、集計方式が正解ではない

### GPU 代表利用率
- **採用**: `\GPU Engine(*)\Utilization Percentage` の adapter LUID + engine ごとに集計し、adapter 内の最大 engine 値
- **再展開**: `PdhExpandWildCardPathW(PDH_REFRESHCOUNTERS)` を **5 秒周期**で実行、driver reset / LUID 集合変化 / 連続 invalid で即時 query 再構築
- **新規 rate counter**: warming-up 1 tick

### GPU 専用メモリ
- **使用量**: `\GPU Adapter Memory(*)\Dedicated Usage`
- **上限**: DXGI `IDXGIFactory1::EnumAdapters1` + `IDXGIAdapter1::GetDesc1` の `DedicatedVideoMemory`
- **対応付け**: `AdapterLuid` で厳密照合
- **表示**: `usage / limit` を BINARY 単位 (KiB/MiB/GiB)
- **N/A 判定**: DXGI 未対応、LUID 一致なし、`usage > limit`
- **integrated GPU**: DXGI が正当に 0 を返す環境と取得失敗を区別 (前者は `Ok`)

### ネットワーク
- **採用**: `GetIfTable2` の `InOctets` / `OutOctets` を monotonic elapsed time で除算
- **識別**: `InterfaceLuid`
- **既定フィルタ**: `OperStatus = Up` かつ非 loopback をすべて合算
- **単位**: 1000 進 B/s / KB/s / MB/s / GB/s (bits/sec も選択可)

### 表示階層
- **既定**: `AlwaysOnTop`。WPF `Window.Topmost=true` とWin32 `WS_EX_TOPMOST=true` を同期し、観測不一致時は `SetWindowPos(HWND_TOPMOST)` で修復する。外部ウィンドウのforeground変更・移動完了・デスクトップ切り替え後は、フォーカスを奪わずTopMost帯内の順位も再適用する
- **デスクトップ上**: `OnDesktop` は `HWND_BOTTOM` を周期的に再適用するexperimental mode
- **通常**: `Normal` は `WS_EX_TOPMOST=false` を要求し、不一致時だけ `HWND_NOTOPMOST` を適用
- **フォールバック**: `OnDesktop` の同一HWND・同一希望状態で3回連続して適用に失敗し、`HWND_NOTOPMOST` の適用と観測に成功した場合だけ `Normal` へ移行

## 許容差 (暫定)

Phase 0 実測後に確定する。実装は以下の値を初期基準として組んでいる。

| 指標 | 許容差 | 判定ウィンドウ |
| --- | --- | --- |
| CPU 利用率 | ±5 percentage points, 増減方向一致 | 主要負荷区間 |
| CPU 現在速度 | ±10% (`*` 表示) | 主要負荷区間 |
| GPU 代表利用率 | ±10 percentage points | 5 秒窓中央値 |
| GPU 専用メモリ | limitはTask Managerのadapter容量と表示丸めの範囲で一致。usageは増減方向が一致し、差がmax(256 MiB, Task Manager値の10%)以内。`usage > limit` は N/A | 主要GPU負荷区間 |
| ネットワーク | 10 MB/s 以上 = ±10%、未満 = ±0.5 MB/s | 30 秒 |

## Phase 0 実行手順

正式な試験前固定手順、観測値、計算方法、合否基準、非公開evidenceの
取り扱いは [DSM-1 metric acceptance](../validation/DSM-1-metric-acceptance.md)
とそのchecklistを使用する。本ADRが`Draft`の間も、DSM-1開始後に基準を
変更した場合は旧試験を無効として最初から再実施する。

1. `dotnet run --project desktop-system-monitor/src/DesktopSystemMonitor.Diagnostics -- --seconds 120 --output phase0-idle.csv`
2. タスク マネージャーを更新速度「標準」で並置し、以下を各 60 秒以上サンプリング:
   - idle
   - CPU 単独負荷 (例: `dotnet run` でループ)
   - GPU 3D 負荷 (例: 任意の 3D デモ)
   - video decode (H.264 4K 動画)
   - large copy (SSD 間コピー)
   - 複合負荷
3. GPU 再展開周期を 1s / 2s / 5s / 10s で比較し、`新規 process 捕捉遅延` と `アプリ CPU 使用率` を記録
4. 上記結果を本 ADR に貼り付け、Status を **Accepted** に更新

## 2026-07-11 実API smoke（精度判定には未使用）

対象PC上でDiagnosticsを3秒実行し、warming-up後の2 sampleで以下を確認した。

- CPU utility/raw: `16.8833%` → `7.32564%`
- CPU推定周波数: `4435.11 MHz` → `4353.1 MHz`
- GPU LUID `0x16A82`: utilization `17.3894%` → `13.3509%`、engine `3D`
- GPU dedicated usage/limit: `7,741,689,856 / 16,829,644,800 bytes`（次sampleでもusage取得）
- network rx/tx: warming-up後に双方の差分値を取得

これは各Windows API/PDH/DXGI経路が当該PCで実値を返すことだけを示す。タスク マネージャーとの同期比較、各60秒の負荷区間、GPU wildcard周期別CPU負荷、30秒local transferは未実施であり、許容差の根拠にはしない。`DSM-1` 完了まで本ADRはDraftを維持する。

## 2026-07-11 自動 Phase 0 実測（部分完了）

対象PCでDiagnosticsを各60秒実行した。計測中も他の処理が動作しており、`idle` ではなかったため、以下では「通常バックグラウンド時」と表記する。生データと集計は `result/desktop-system-monitor-phase0/20260711/` に保存した（`result/` はGit管理外）。

### GPU wildcard再展開周期

| 再展開周期 | sample数 | collector CPU 平均 / 最大 | working set 平均 / 最大 | 初期化10 sample後のhandle増分 |
| --- | ---: | ---: | ---: | ---: |
| 1秒 | 58 | 0.170% / 0.460% | 53.29 / 57.44 MiB | +3 |
| 2秒 | 59 | 0.150% / 0.450% | 52.69 / 56.34 MiB | +2 |
| 5秒 | 58 | 0.150% / 0.380% | 52.13 / 56.30 MiB | +3 |
| 10秒 | 58 | 0.140% / 0.450% | 51.82 / 55.85 MiB | +3 |

全候補が平均CPU 1%未満、working set 100 MiB未満を満たした。60秒では初期化後のhandle数は2～3増に留まり、継続増加は観測されなかった。ただし、これは24時間耐久試験の代替ではない。10秒周期は「新規process捕捉を10秒以内」という上限ぎりぎりであり、負荷差も小さいため、暫定採用の5秒周期を維持する。短命processの捕捉遅延比較は手動Phase 0に残す。

### CPU負荷追従

4つのCPU workerを追加した60秒区間では、システムCPU平均が通常バックグラウンド時の24.86%から53.64%へ上昇した。Diagnostics自身は平均0.182%、最大0.451%、working set最大56.43 MiB、handle数は初期化10 sample後290から終了時293だった。CPU値の増減方向と低オーバーヘッドは確認できたが、タスク マネージャーとの差分は未測定である。

### GPU engine選択

FFmpegでH.264 1080p60を4本、D3D11VAにより実時間デコードしながら60秒計測した。59 sample中、最繁忙engineは`3D`が52回、`VideoDecode`が6回（初回warming-up 1回）となり、固定3Dではなく実負荷に応じてbusiest engineが切り替わることを実機で確認した。GPU利用率は平均26.02%、最大49.59%。Diagnostics自身は平均0.159%、最大0.456%、working set最大57.39 MiBだった。タスク マネージャーとの同期値比較およびGPU 3D専用負荷は未実施である。

### 未完了の受け入れ項目

- タスク マネージャー更新速度「標準」と同期したCPU、CPU速度、GPU、VRAM、networkの許容差判定
- GPU 3D専用負荷、30秒以上の実network transfer、large copy、複合負荷
- 24時間耐久、DPI / multi-monitor、Win+D、Explorer再起動、lock / sleep / network切替

したがって`DSM-1`と`DSM-2`は未完了のままとし、本ADRはDraftを維持する。

### portable版の短時間常駐smoke

公開済みself-contained版を約218秒起動し、5秒間隔で36 sampleを記録した。プロセスは応答状態を維持し、handleは646から627へ減少、総working setは146.90～156.12 MiB、`Process(DesktopSystemMonitor)\Working Set - Private`は53.23～63.50 MiBだった。別の20秒区間でプロセスCPU平均は0.422%だった。

タスク マネージャーの既定メモリ表示に近いPrivate Working Setは100 MiB未満だが、`Process.WorkingSet64`による共有ページ込みの総working setは100 MiBを超える。後続のDSM-2仕様ではPrivate Working Setを100 MiBの主判定、total working setを別の増加傾向判定として固定した。この短時間smokeのみで`DSM-2`を合格にせず、将来のbinary受入では24時間にわたり両指標を併記する。

## Open Questions

- Hyper-V 有効環境で `MaxMhz` が誤って報告される場合の代替: `Hyper-V Hypervisor Logical Processor(*)\Frequency`
- integrated + discrete 併用時の代表 GPU 選択: 現状は最大 utilization、後で「pinned adapter」で固定できるように済み

## 任意テレメトリ（2026-07-15）

- 新規機能はすべてopt-inとし、既定のCPU / MEM / GPU / NET表示と220 DIP高を維持する。
- DISKはphysical diskを1台だけ選び、`PhysicalDisk(<number> ... )`の`100 - % Idle Time`を0..100へclampした稼働率とread/write bytes/secを使用する。system volumeが複数disk extentへまたがる場合は自動選択せず、利用者へ明示選択を求める。
- CPU/GPU温度は既存LibreHardwareMonitor poll cycleから電力と同時取得し、別のhardware treeやworkerを作らない。対応センサーがない場合とGPU対応が曖昧な場合は推定せず`N/A`とする。
- BATは`GetSystemPowerStatus`のpercentと`CallNtPowerInformation(SystemBatteryState)`のsystem aggregate capacity/rate/timeを合成する。放電中のvalid rate 5件以上について直近60秒medianを取り、capacity/rateから概算する。状態遷移、長いsample gap、pause/resumeでは履歴を破棄する。
- CPU / MEM / GPU / DISKとNETの直近peakはメモリ内の60秒窓だけに保持し、永続化しない。
- 高負荷プロセス詳細のread/writeは`GetProcessIoCounters`の全I/O accountingであり、disk専用値ではない。画面表示時だけ列挙し、process identityにはPIDとstart timeを用いる。

## 決定履歴

- 2026-07-11 Draft 作成 (実装コミットと同時)
- 2026-07-11 実API 3秒smokeを追記（Phase 0比較の代替にはしない）
- 2026-07-11 GPU再展開周期、CPU負荷、D3D11VA動画負荷の自動実測を追記（手動同期比較は未了）
- 2026-07-15 opt-in DISK / temperature / BAT / recent peak / process detailsの採用契約を追記
