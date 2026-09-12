# Desktop System Monitor

Windows向けのDesktop System Monitorです。一般利用者は、このREADMEの隣にある
`ここから開始 - Desktop System Monitorを導入・更新.bat` を実行してください。

## 概要

Desktop System Monitorは、CPU・メモリ・GPU・ディスク・ネットワーク・温度・バッテリーの状況を、
デスクトップ上の透明なコンパクトウィンドウへ常時表示するWindows向けアプリです。
タスクトレイに常駐し、表示位置・外観・表示項目を設定画面から調整できます。

このrepositoryはsource-only previewであり、公式の署名済みbinaryは配布していません。
利用者自身のPC上でsourceからbuildして導入します。

repository metadata（`.git`、`.github`、`.gitignore`）とcanonical license（`LICENSE`）はrootに残し、
アプリ本体・source・tests・開発用script・配布資料は `project\` に集約しています。

## 動作要件

- Windows 11
- PowerShell 7（`pwsh.exe`）
- [`project/global.json`](project/global.json) に対応する .NET 10 SDK

初回のrestore/build/publishではNuGet packageと対象RIDのruntime packを取得するため、
インターネット接続が必要になる場合があります。生成されるアプリ自体はself-containedのため、
実行先へ.NET runtimeを別途導入する必要はありません。

## インストール

導入と更新は同じ手順です。

1. Desktop System Monitorをタスクトレイから終了します。
2. `ここから開始 - Desktop System Monitorを導入・更新.bat` をダブルクリックします。
3. BATが現在のWindows architectureを自動判定し、現在のsourceからRelease版を再ビルドしてから、導入・更新先を選ぶ画面を表示します。

PowerShellコマンドを手入力する必要はありません。既存インストールを更新する場合は、そのインストール先の親フォルダーを選択してください。親フォルダーをBATへドラッグ＆ドロップして直接指定することもできます。ビルドに必要なPowerShell 7と.NET SDKが見つからない場合は、BATが不足しているものを表示して停止します。

生成物はself-contained single-fileですが、README、LICENSE、THIRD-PARTY-NOTICES、`licenses`は法的・運用上必要です。
`DesktopSystemMonitor.exe`だけを取り出さず、`desktop-system-monitor-win-<architecture>`フォルダー全体を保持してください。

readinessが未完了の `local-only` payloadは、未検証プレビューであり公開禁止です。公開配布、Release、WinGet向けには使用しないでください。

手動ビルド手順とinstallerの詳細は [project/README.md](project/README.md) を参照してください。

## 使用方法

初回起動時は、透明なコンパクトウィンドウがプライマリモニターの右上に表示され、タスクトレイへ常駐します。

- タスクトレイアイコンの右クリックメニューから、終了・表示階層の切替・自動起動・クリック透過を操作します。
- 設定画面では、表示モニターと基準の四隅、余白、背景色と不透明度、フォント・配色、表示する項目を調整できます。
- クリック透過中は背後のウィンドウを操作できます。ウィジェットをドラッグする場合は、タスクトレイからクリック透過を解除してください。ドラッグ後の位置は自動保存されます。

各表示項目の意味と詳細な操作は [project/README.md](project/README.md) を参照してください。

## 設定

設定は `%LOCALAPPDATA%\DesktopSystemMonitor\settings.json` に保存されます（現在の `SchemaVersion` は 5）。
通常はタスクトレイの「設定」から変更し、ファイルを直接編集する必要はありません。

- サンプリング間隔、UIスケール、表示モード（通常／縮小）
- 表示階層（常に手前／デスクトップ上／通常）、クリック透過、ログオン自動起動
- 表示項目（CPU／GPU／ディスク／温度／バッテリー推定／高負荷プロセス）の個別ON/OFF
- 外観（フォント、前景色、アクセント色、背景色、背景の不透明度、四辺フェード）
- 診断ログ（既定は無効。有効時は `%LOCALAPPDATA%\DesktopSystemMonitor\logs` へ制限付きで記録）

設定ファイルが破損した場合は自動で `.bak` から復旧し、それも失敗した場合は既定値で起動します。
設定キーの完全な一覧は [project/README.md](project/README.md)、ローカルデータの取り扱いは [project/PRIVACY.md](project/PRIVACY.md) を参照してください。

## アンインストール

1. タスクトレイのメニューで自動起動を無効にします。
2. タスクトレイからDesktop System Monitorを終了します。
3. 配置したportableバイナリのディレクトリと、作成したshortcutを削除します。
4. 設定と診断ログも不要な場合だけ、`%LOCALAPPDATA%\DesktopSystemMonitor` を削除します。

installerは管理者権限、PATH変更、サービス登録、レジストリ変更、自動起動設定を行わないため、
アンインストールでシステム設定を戻す作業は不要です。portableバイナリを削除しても、設定と診断ログは自動削除されません。

## 既知の制限

- このBATはインターネットから最新版を取得するものではなく、手元のsourceを再ビルドして導入します。
- 通常の更新では `%LOCALAPPDATA%\DesktopSystemMonitor` の設定・ログを変更しません。
- ARM64は実機での受入れが完了するまで公開対応ではなく、validationまたはlocal-onlyに限定します。
- CPU速度・CPU/GPU電力・温度は、機種が公開するセンサーに依存するbest-effort値です。取得できない項目は `N/A` になります。
- 「デスクトップ上」表示はexperimental扱いです。Explorer再起動やWin+Dで最前面へ上がる環境が残る可能性があります。

センサー種別ごとの詳細な制限は [project/README.md](project/README.md) の「既知の制限」を参照してください。

## ライセンス

第一者コードは [MIT License](LICENSE) で提供します。
追加ライブラリにはそれぞれのライセンスが適用されるため、[project/THIRD-PARTY-NOTICES.md](project/THIRD-PARTY-NOTICES.md) も参照してください。

## 開発者向け

詳細な使い方、設計、検証、ライセンス、変更履歴は [project/README.md](project/README.md) と
[project/docs/](project/docs/) を参照してください。
