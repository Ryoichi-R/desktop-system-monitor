# 006 — Avalonia macOS UI

Status: Implemented for the local macOS candidate; final owner acceptance pending.
Date: 2026-09-28 (Asia/Tokyo)

Mac先行の利用者決定に従い、MacのUIにAvalonia 12.1.3を採用する。Windowsは既存WPFを保持し、WindowsでのAvalonia等価性・回帰受入が済むまで削除しない。通常280 DIP／縮小150 DIPの表示に、1%単位の50〜150%倍率を適用する。DockはShowInDock=falseとLSUIElementで非表示にし、メニューバーと右クリックを操作入口とする。

倍率入力・ディスク・プロセスの補助ウィンドウは本体と別に開閉する。本体終了時に閉じる。Headless testsで倍率変更後の本体継続と子画面終了を検証する。macOS 27の実機確認は必須で、依存版採用は全OS受入完了を意味しない。
