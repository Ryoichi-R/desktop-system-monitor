# 012 — macOSローカルbundle

Status: Implemented for the local macOS candidate; final owner acceptance pending.
Date: 2026-09-28 (Asia/Tokyo)

D3/D14に従い、macOS成果物はローカル評価用のosx-arm64 .appとする。Contents/MacOSにDesktopSystemMonitorとDesktopSystemMonitor.Mac.SensorHostを固定名で配置する。両方SelfContained=true、PublishSingleFile=true、IncludeNativeLibrariesForSelfExtract=true、PublishTrimmed=false、EnableCompressionInSingleFile=false、IncludeAllContentForSelfExtract=false。

署名順はHost→App→bundle、ad-hoc署名をstrict検証する。LSUIElement=trueでDockから隠す。Contents/Resourcesに依存inventory、third-party noticesとライセンスを同梱する。.NET noticeは解決済みosx-arm64 runtime packと同じ版から取得し、不在なら失敗させる。既存bundleは上書きせず新しい出力先を要求する。

読み取り専用のtest-publication-contract-macos.ps1でarm64、固定構成、plist、署名、Windows製品/runtime非混入、noticeを検査する。公開Release、notarization、installer、WinGetへの登録は対象外。clean環境での初回起動とログイン時自動起動は利用者受入を必要とする。
