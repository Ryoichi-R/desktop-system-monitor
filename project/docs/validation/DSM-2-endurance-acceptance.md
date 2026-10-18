# DSM-2 endurance acceptance

## Purpose

DSM-2 determines whether a specific `win-x64` validation artifact can remain
usable for 24 hours without a crash, hang, duplicate instance, unrecovered
Windows-state transition, or sustained resource growth. It is bound to one
candidate lineage and one unsigned artifact SHA-256.

DSM-2 does not establish numerical metric accuracy; DSM-1 owns that decision.

## Required identity

Before testing, copy these values from the reviewed owner-private candidate
manifest into the private test record:

- product version
- candidate lineage ID
- source tree digest
- runtime: `win-x64`
- unsigned artifact file name, byte count, and SHA-256
- .NET SDK, dependency fingerprint, and publish contract
- test start time in UTC and tester identity

Recalculate the artifact SHA-256 on the test device and require an exact match.
Verify PE machine `0x8664`. Record the Windows build, display topology, and
resource-sampling method privately.

## Fixed resource definitions

The primary memory gate is **Private Working Set**, measured for the exact
process identity using PID plus process start time. The acceptance ceiling is
100 MiB. Each five-minute median after the first ten minutes must be below that
ceiling.

**Total Working Set** (`Process.WorkingSet64` or an equivalent documented
counter) is recorded separately and is not compared with the 100 MiB ceiling.
It passes the growth check when:

- the final one-hour median is no more than 20 MiB above the first one-hour
  median after warm-up, and
- the fitted 24-hour slope is no greater than 1 MiB per hour.

Handle count is sampled with the same process identity. It passes when the
final one-hour median is no more than 25 handles above the first one-hour
median and no six consecutive hourly medians are strictly increasing.

The private record must state the exact counters or APIs used. If Private
Working Set, Total Working Set, or handle count cannot be measured reliably,
DSM-2 is `BLOCKED`; one metric must not be silently substituted for another.

## Duration and sampling

- Run for at least 24 hours of wall-clock time from candidate launch.
- Retain at least 23 hours of valid one-minute resource samples.
- The planned sleep interval may account for the missing coverage.
- Any other sample gap longer than five minutes must be explained and reviewed;
  an unexplained gap makes DSM-2 `BLOCKED`.
- Mark every state-transition timestamp in the private sample stream.

The resource sampler must not change the candidate executable or inject code
into it. Keep raw samples, screenshots, event details, and machine identifiers
owner-private.

## Reliability acceptance criteria

Across the full run:

- no unexpected process exit or unhandled exception
- no unresponsive interval lasting more than two normal sampling intervals
  after a Windows transition completes
- exactly one app process, overlay, and tray icon
- no continuous diagnostic-log growth caused by a repeating failure
- CPU use returns to its pre-transition idle range after recovery; a sustained
  unexplained value above 5% for ten minutes fails the run
- metrics resume or show the documented unavailable state rather than a stale
  or fabricated value

An application restart invalidates the run unless the procedure explicitly
requires restart for a settings/startup check. If a required restart is tested,
perform it before starting the 24-hour endurance clock.

## Required Windows transitions

Perform and timestamp every transition while the endurance clock is running.
Allow at most two normal sampling intervals after the transition completes:

1. Win+D followed by desktop restore
2. Explorer restart
3. session lock and unlock
4. sleep and resume
5. network disconnect and reconnect
6. full-screen application enter and exit

Pass when the requested window layer is restored, sampling resumes, the tray
remains usable, and no duplicate app instance or overlay remains.

## DPI and multi-monitor checks

At least one secondary display is required unless the selected release scope
explicitly removes multi-monitor support. During the run:

1. move or anchor the overlay on each display
2. move it between different scale factors or change a scale factor
3. change the primary display
4. disconnect and reconnect the secondary display
5. open and use the settings dialog on each tested scale factor

Pass when the overlay and settings remain visible and interactive, placement
is restored or safely clamped to a visible working area, and the app creates no
duplicate window.

## Scheduled observations

Record a manual usability checkpoint near launch and at approximately 1, 6,
12, 18, and 24 hours. Each checkpoint confirms:

- overlay and tray responsiveness
- current window-layer and click-through behavior
- metric updates or documented unavailable states
- process identity and single-instance state
- Private Working Set, Total Working Set, handle count, and process CPU
- diagnostic-log size and absence of a repeating error loop

## Evidence and decision

Use the owner-private evidence name:

`DSM-2-<lineage12>-win-x64-<UTC timestamp>.json`

DSM-2 is `PASS` only when the identity matches, the duration and sample
coverage are met, every memory/handle criterion passes, every required Windows
and display transition recovers, and all manual checkpoints pass. A crash,
hang, restart during the endurance clock, duplicate instance, resource-limit
failure, missing required display, or unexplained evidence gap makes the result
`BLOCKED` or `FAIL`.

After review, publish only the decision, candidate lineage, artifact SHA-256,
completed-test name, limitations, and hashes of approved redacted evidence in
`readiness-summary.json`.
