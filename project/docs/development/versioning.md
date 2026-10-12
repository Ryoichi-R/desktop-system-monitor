# 製品バージョンとビルド情報

製品バージョンの唯一の定義元は `project/Directory.Build.props` の `Version`。
Windows WPFとmacOS Avalonia、x64とARM64は同じリリースで同じ番号を使う。
OS・アーキテクチャを変えてビルドし直すだけでは番号を上げない。
今回のバージョン情報機能追加は `0.1.0` → `0.2.0`。

- 機能追加はminor、修正はpatch、互換性を壊す変更はmajorを上げる。
- 0.xは開発期間で安定APIを保証しない。ただし通常の追加は0.(minor+1).0、
  修正は0.minor.(patch+1)とする。互換性を壊す場合は1.0.0へ進め、
  以後もmajorを上げる。破壊的変更をpatchや同じ番号に隠さない。
- minor/major更新時は下位桁を0へ戻す。設定SchemaVersionは別の契約として管理する。
- ビルドのたびに自動採番しない。バージョンの更新はリリース内容に基づく明示変更。
- Versionを上げる変更では、同じ変更で`docs/validation/readiness-summary.json`の
  `productVersion`も揃える。揃えないとWindows配布契約
  （`scripts/test-publication-contract.ps1`）とrelease scriptが停止する。
  旧版の結果は旧候補の記録と明記し、新しい版の合格証拠として扱わない。
  statusは新候補で再検証するまで`BLOCKED`のままにする。

AssemblyVersionとWindows FileVersionはVersionに`.0`を付けた4桁。
Windows ProductVersion/AssemblyInformationalVersionと設定画面は3桁のVersionそのもの。
Git IDをProductVersionへ付加しない。MacのCFBundleShortVersionStringとCFBundleVersionも
同じVersionから配布スクリプトが生成し、bundle契約で照合する。

`Directory.Build.targets`がアプリのコンパイル前に12文字のGit短縮ID、
未コミット変更状態（リポジトリ全体、未追跡・非ignoreファイルも含む）、UTCビルド時刻を
AssemblyMetadataへ埋め込む。未コミット変更にはステージ済み変更も含む。
Git欠落、HEAD未作成、取得失敗はそれぞれ取得できない項目を「不明」にする。
ソースアーカイブや別checkout内のコピーから周囲のGit情報を推測しない。
Git子プロセス内でGIT_DIR/GIT_WORK_TREE等のリポジトリ選択環境を除去し、
ビルド元がwork treeのルートであることをGit自身で確認する。親プロセスの環境・ユーザー設定は変更しない。
`--no-build`や既存バイナリの再配布では元のビルド情報を保持する。
時刻の埋め込みにより別ビルド間のバイナリhashは異なるが、製品番号は変わらない。

設定の「バージョン情報」は両UIで同じ6項目・順序を表示し、そのままコピーする。
実行版はOS名と`RuntimeInformation.ProcessArchitecture`で決める。
Windows ARM上のx64エミュレーションではx64、Mac Rosettaのx64版でもx64。
OSArchitectureやCPU機種名は使わない。日時はUTC (+00:00)を明記する。
起動後はGit・ソースツリー・ファイルの更新日時に依存しない。
個人名・メール・ホスト名・絶対パス・リモートURLは収集も埋め込みも行わない。

Macのローカルbundle生成（インストールや外部公開は行わない）:

```powershell
pwsh scripts/publish-desktop-system-monitor-macos.ps1 -OutputRoot <new-output-directory> -Runtime osx-arm64
# Intel Mac用は -Runtime osx-x64。両方とも共通のVersionを使う。
pwsh scripts/test-publication-contract-macos.ps1 -BundlePath <bundle> -Runtime osx-arm64
```

生成Macアプリの`Contents/MacOS/DesktopSystemMonitor --version-info`はUI・計測を
開始せず、埋め込み情報を出力する。互換アーキテクチャではbundle契約がこの出力も照合する。
既存Windows配布スクリプトのVersion読取・単一ファイル配布契約は維持する。
Windowsの`scripts/publish-desktop-system-monitor.ps1`はpublish直後に
`DesktopSystemMonitor.exe`のVERSIONINFOを読み、ProductVersionが`Version`、
FileVersionと固定数値版が`Version.0`であることを確認する。不一致なら配布を中止する。
この読取はPEヘッダとリソースだけを解析するため、macOSからのクロスビルドでも動く。

検証テスト:

- `tests/unit/BuildInformationTests.cs`: 表示項目・順序・コピー内容・「不明」の扱い。
- `tests/unit/BuildIdentityTargetsTests.cs`: 一時Gitリポジトリで実際の
  `Directory.Build.targets`を実行し、clean・変更・ステージ・未追跡・ignore・
  コミットなし・status失敗・Gitなし・別checkout内のソースコピーを確認する。
  上記2つはWPF側`App.Tests`とAvalonia側`App.Avalonia.Tests`の両方でコンパイルされ、
  `scripts/test-desktop-system-monitor.ps1`の`-Windows`／`-Avalonia`で実行される。
  Gitが必要。foreign Git環境による取り違えも検証する。
  対象targetsは両テストアセンブリのEmbeddedResourceに含めるため、
  製品外へ`--artifacts-path`を指定してもソースディレクトリ探索に依存しない。
- `tests/unit/installer/VersionResource.Tests.ps1`: VERSIONINFO読取と不一致検出。
正式配布のreadiness/gateは別途更新・検証が必要であり、この機能追加だけでは公開を承認しない。
オンライン最新版判定・自動更新は対象外。
