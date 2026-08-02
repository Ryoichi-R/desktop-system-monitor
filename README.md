# Desktop System Monitor

Windows向けのDesktop System Monitorです。一般利用者は、このREADMEの隣にある
`ここから開始 - Desktop System Monitorを導入・更新.bat` を実行してください。

## 導入・更新

1. Desktop System Monitorをタスクトレイから終了します。
2. `ここから開始 - Desktop System Monitorを導入・更新.bat` をダブルクリックします。
3. BATが現在のWindows architectureを自動判定し、現在のsourceからRelease版を再ビルドしてから、導入・更新先を選ぶ画面を表示します。

PowerShellコマンドを手入力する必要はありません。既存インストールを更新する場合は、そのインストール先の親フォルダーを選択してください。親フォルダーをBATへドラッグ＆ドロップして直接指定することもできます。ビルドに必要なPowerShell 7と.NET SDKが見つからない場合は、BATが不足しているものを表示して停止します。

readinessが未完了の `local-only` payloadは、未検証プレビューであり公開禁止です。公開配布、Release、WinGet向けには使用しないでください。

## 開発者向け

詳細な使い方、設計、検証、ライセンス、変更履歴は [project/README.md](project/README.md) と
[project/docs/](project/docs/) を参照してください。

repository metadata（`.git`、`.github`、`.gitignore`）とcanonical license（`LICENSE`）はrootに残し、
アプリ本体・source・tests・開発用script・配布資料は `project\` に集約しています。

## 注意

- installerは管理者権限、PATH変更、サービス登録、レジストリ変更、自動起動設定を行いません。
- このBATはインターネットから最新版を取得するものではなく、手元のsourceを再ビルドして導入します。
- 通常の更新では `%LOCALAPPDATA%\DesktopSystemMonitor` の設定・ログを変更しません。
- ARM64は実機での受入れが完了するまで公開対応ではなく、validationまたはlocal-onlyに限定します。

ライセンスは [LICENSE](LICENSE) を参照してください。
