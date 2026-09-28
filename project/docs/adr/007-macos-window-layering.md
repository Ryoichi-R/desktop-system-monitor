# 007 — macOSウィジェットの層と全画面

Status: Implemented for the local macOS candidate; final owner acceptance pending.
Date: 2026-09-28 (Asia/Tokyo)

NSWindowのlevel・ignoresMouseEvents・collectionBehaviorをMac側へ隔離する。Windows固有の周期的WndProc修復ループはMacへ移植しない。作業領域はAvalonia Screensが提供するNSScreen情報を利用し、変更通知で画面内へ補正する。

全画面判定は自ウィンドウの画面について、CGWindowListCopyWindowInfoの可視・非desktop要素を手前順に読み、自己PID・透明・非通常layerを除外した最初の候補が画面四辺を2 DIP以内で覆うかを見る。PoCの95%面積基準から、通常最大化の誤判定を避けるため四辺判定へ変更した。画面画像・window titleは読まず、追加権限を要求しない。失敗時はfail-openで表示へ戻す。

Hideで本体を閉じずタイマーを継続し、ShowActivated=falseでフォーカスを奪わず復帰する。メニューバーの手動表示は次に非全画面を観測するまで自動非表示を抑止する。borderless windowの意味判定はできず、複数画面とSpaceの実機確認は利用者指定でスキップ。
