# 011 — SensorHostのプロセス分離

Status: Implemented for the local macOS candidate; final owner acceptance pending.
Date: 2026-09-28 (Asia/Tokyo)

D13(c)のowner決定に従い、CPU/メモリを含む全ネイティブメトリクスを専用Hostへ隔離する。案(a)UI同居はnative crashの波及、案(b)非公開APIだけ隔離は境界の二重化を持つ。事前PoCで追加CPU・RSS・IPCを測定し、総採取負荷を含む結果を踏まえて(c)を採用した。

本番は.app内の固定名の絶対パスだけを起動し、relative/symlink/bundle外Hostを拒否する。stdin/stdoutはversion 2・64KiB上限・little-endian長付きJSON。sequences/generation/monotonic ageと個々の指標を検証する。IPCは2秒timeout、最大5連続失敗と指数backoff。正常応答で失敗回数を戻す。Host stderrは親がdrainして保存しない。

テスト用factoryはinternalかつ既存test assemblyに限定し、本番CLIや環境変数へ公開しない。Mac結合テストは実Hostを起動し、終了注入・欠測・暖機・復旧を確認する。診断ログはUIの固定カテゴリのみ、既定無効で最大1MB×5。
