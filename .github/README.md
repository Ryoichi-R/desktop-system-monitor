# Desktop System Monitor

日本語の概要です。英語版は [README.en.md](../project/README.en.md) を参照してください。

Desktop System Monitor は Windows 11 向けのクリック透過システムメトリクス・オーバーレイです。利用者はルートの `ここから開始 - Desktop System Monitorを導入・更新.bat` から、書き込み可能な既存親フォルダーへ導入または更新できます。

## 配布状態

- `local-only`: readiness未検証のowner-local preview。`未検証プレビュー・公開禁止` を表示し、公開成果物には使用しません。
- `validation-only`: DSM受入試験専用。root launcherからは選択できません。
- `public-release-approved`: readiness identity、artifact SHA-256、必要な受入試験が揃った公開候補。

一般公開時のdownload導線、privacy、security、changelogは [project documentation](../project/docs/usage/README.md) から確認できます。公開用portable ZIPとinstaller payload ZIPは別artifact roleで管理し、共通のcontent digestで内容を照合します。
