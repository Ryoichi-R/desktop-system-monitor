# 009 — 非公開センサーAPIの隔離

Status: Implemented for the local macOS candidate; final owner acceptance pending.
Date: 2026-09-28 (Asia/Tokyo)

ownerのD2に従い、SMC／IOReportをSensorHost内でのみ利用する。温度はMac17,14／macOS 27.0に固定したCPU 26キー・GPU 108キーの有効値最大。推定値には*を付ける。5〜120℃・finiteを検査し、10%超の欠落またはOS/機種不一致でN/Aへ落とす。

GPU電力はGPU Energyのみ購読し2サンプル平均。CPU電力・CPU現在周波数・GPUメモリは対応値として提供しない。CPU電力は事前較正で対応キーを決められず、現在周波数は本体非対応、GPUメモリは意味未確定である。「未対応」と「技術的に取得不能」は同一視しない。

型・件数・buffer・version・range・ageを検査する。取得例外は欠測、native signalはHost終了として親から隔離し、UIに古い値を残さない。主要キーは互換性資料だけへ記録し、診断ログには書かない。
