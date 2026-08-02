# ADR: Desktop System Monitor のプロセス別NET取得

- Status: **NO-GO for default asInvoker**
- Date: 2026-07-15
- Context: migrated from the private parent workspace's historical process-network plan; that private planning path is not part of the standalone repository.

## 要求

高負荷プロセス画面へ、個別プロセス単位のreceive＋send合計速度を追加する。全I/O accounting、接続数、endpoint数をnetwork throughputとして代用せず、通常のアプリ起動と同じ`asInvoker`トークンで取得できることをproduction実装の前提とする。

## 2026-07-15 ARM64実機probe

対象PCの非昇格トークンとWindows登録providerを確認した。

- OS/architecture: Windows 11 ARM64
- App execution level: `asInvoker`
- Administrators token: false
- Performance Log Users (`S-1-5-32-559`): false
- Performance Monitor Users (`S-1-5-32-558`): false
- Provider: `Microsoft-Windows-TCPIP`
- Provider GUID: `{2F07E2EE-15DB-40F1-90EF-9D7BA282188A}`
- 必要候補keyword: `ut:ProcessIdHint`、`ut:Transfer`、`ut:SendPath`、`ut:ReceivePath`

同じトークンからproviderを指定した一時real-time sessionの開始を試みた結果、`logman`は`-2147024891 (0x80070005, Access is denied)`を返した。sessionは開始されず、同名sessionが残っていないことを確認した。

この結果はproviderが登録されていても、現在の標準user tokenではsystem-wide sessionを制御できないことを示す。管理者PowerShellで成功する可能性は、`asInvoker`でのGO条件を満たさない。

## decoder/package評価

`Microsoft.Diagnostics.Tracing.TraceEvent`は公式NuGet feedで3.2.4まで公開されていることを確認した。ただしsession開始前に権限gateでNO-GOとなったため、production projectへのPackageReference追加、license同梱、event schema decoder実装には進まない。利用しない依存を配布物へ増やさない。

## 決定

標準user tokenを公開時の既定契約とする限り、プロセス別NETのproduction実装は開始しない。高負荷プロセス画面の既存CPU/MEM/全I/O表示を維持し、全I/OをNET値として偽装しない。

次のいずれかを利用者が明示的に選択した場合に限り、別変更として再開する。

1. 利用者自身が対象アカウントをPerformance Log Usersへ追加することを動作前提にする。
2. UAC付きの分離helper/serviceを設計し、IPC、権限境界、install/update、署名、停止処理を別途security reviewする。
3. プロセス別NETを保留し、既存機能だけを公開する。

アプリ全体のmanifestを`requireAdministrator`へ変更する案、自動UAC昇格、自動group追加、全I/Oや接続数による近似表示は採用しない。

## 再開条件

- 選択肢1の場合: group追加後にログオンし直した非昇格トークンで`StartTrace`/real-time consume、TCP/UDPのPID/size、cleanupを再検証する。
- 選択肢2の場合: helperの脅威モデルとIPC契約を先にAccepted ADRへする。
- いずれの場合もARM64/x64、lost event、PID reuse、close/suspend後2秒以内のsession解放を受入条件とする。
