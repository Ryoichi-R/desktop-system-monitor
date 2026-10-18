# 008 — macOSの指標と意味

Status: Implemented for the local macOS candidate; final owner acceptance pending.
Date: 2026-09-28 (Asia/Tokyo)

CPUはMachの時間差分、メモリはinternal−purgeable＋wired＋compressorを使う。ネットワークはupな物理en系を合算し、32bit counterの特性と256B粒度を前提に折返し・reset・長時間gapを検査する。ディスクはIOBlockStorageDriverのBytes (Read)/(Write)をregistry entry IDごとに照合して合算する。稼働率へI/O量を流用しない。

高負荷プロセスはlibprocのrusage_info_v2からuser/systemのMach時間、開始時刻、ディスクI/Oを取得する。mach_timebase_infoで秒へ変換し、全論理CPU容量を100%とする。開始時刻・名前変化・counter減少・gapで基準を捨てる。PID列挙8192件、表示10件を上限とし、権限不足と終了は除外する。physical footprintはWindows private bytesではないため、そのDTO欄をN/Aとする。プロセス名とPIDは画面専用で診断ログへ残さない。

CPUの正式許容差はtop比±5pt（owner決定）。他の指標の最終精度・他監視アプリとの競合受入は未完了。CPU時間の単位は実機で.NET TotalProcessorTimeとも照合した。カーネル実装の一次参照: [Apple XNU fill_task_rusage](https://github.com/apple-oss-distributions/xnu/blob/main/osfmk/kern/bsd_kern.c)。実装layoutはローカルmacOS SDKのsys/resource.h・libproc.hに照合している。
