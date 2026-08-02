# Correctness API and performance gate decisions

Date: 2026-07-17

## Private working set API spike

Environment: Windows 11 `10.0.26200.0`, ARM64, .NET SDK 10.0.301.

- `GetProcessMemoryInfo` with `PROCESS_MEMORY_COUNTERS_EX2` succeeded for the current process. The returned private working set was distinct from both total working set and Private Bytes.
- The `Process V2` counter set was present. Enumerated instances used the observed form `process-name:pid`, including duplicate process names with different PIDs.
- Decision: use EX2 as the normal per-process path and Process V2 as the runtime fallback. Do not add the classic `ID Process` plus `Working Set - Private` two-array join.
- Capability is probed against the current process rather than inferred from an OS build number.

## Access-denied PID cache gate

Screening used the Release assemblies and a normal user token. Each run executed 300 complete `ProcessLoadSampler` cycles. This is a concentrated screening run rather than a 10-minute UI cadence run; it is sufficient to reject the optimization because every observed p95 was about one tenth of the 50 ms GO threshold and no 250 ms backoff occurred.

| Run | p50 | p95 | Maximum | 5-second backoff cycles |
|---:|---:|---:|---:|---:|
| 1 | 3.99 ms | 5.15 ms | 18.26 ms | 0 |
| 2 | 4.19 ms | 5.57 ms | 9.21 ms | 0 |
| 3 | 3.79 ms | 4.72 ms | 5.41 ms | 0 |

Decision: **NO-GO**. Do not add a denied-PID cache. The cache would introduce PID-reuse and retry-TTL behavior without measurable budget pressure.

Reopen when any of the following occurs:

- cycle p95 reaches 50 ms in two representative runs;
- `BudgetExceeded` produces a 5-second backoff;
- the process enumeration or security context changes materially.

## Paused timer gate

The implementation still prevents metric source sampling while paused. A 30-minute locked-session WPR/WPA experiment was not performed because it would lock the active development session and the required DC/AC controlled environment was not established in this run. Absence of that evidence does not justify adding the more complex cancel/resume state machine.

Decision: **NO-GO for this implementation**. Keep the existing `PeriodicTimer` wait and do not claim a power improvement.

Reopen only after the documented protocol can be run: Release build, 0.5-second interval, diagnostics off, 5-minute warm-up, 30-minute session lock, three DC runs and one AC control using WPR `CPU Usage (Precise)` plus WPA. The implementation threshold remains at least 0.5 timer-attributed wakeups/second or 0.1% of one logical core in every DC run.
