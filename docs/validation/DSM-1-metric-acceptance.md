# DSM-1 metric acceptance

## Purpose

DSM-1 determines whether a specific `win-x64` validation artifact reports CPU,
GPU, dedicated GPU memory, and network metrics closely enough to Windows Task
Manager for the documented preview behavior. The result is bound to one
candidate lineage and one unsigned artifact SHA-256.

Passing DSM-1 permits the metric ADR to be reviewed for `Accepted` status. It
does not approve signing, packaging, or publication and does not make DSM-2
optional.

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
Verify PE machine `0x8664`. Record the Windows build, CPU class, GPU/driver
class, and active network-adapter class privately. Do not put device names,
user names, process names, network identifiers, or absolute paths in the
public readiness summary.

## Test environment

- A Windows 11 x64 device with a supported discrete or integrated GPU.
- A standard, non-elevated user session.
- Task Manager update speed set to **Normal** for the entire comparison.
- Task Manager and the overlay visible at the same time.
- A local monotonic timestamp source used to align observations.
- The diagnostics CLI built from the same frozen source candidate.
- No concurrent DSM-2 run or unrelated benchmark that competes for the same
  CPU, GPU, storage, or network resource.

Store raw CSV, screenshots, workload commands, and manually transcribed Task
Manager readings only in the owner-private evidence directory.

## Sampling method

For each scenario, run the diagnostics collector for at least 70 seconds and
use the final 60 seconds after warm-up:

```text
DesktopSystemMonitor.Diagnostics --seconds 70 --gpu-refresh-seconds 5 --output <private-csv>
```

Record Task Manager values at least every five seconds. Align each observation
with the nearest diagnostics sample. Normalize percent, decimal/binary byte
units, and bits/bytes per second before comparing them. Use the median absolute
difference over the 60-second evaluation window unless a criterion below
explicitly states otherwise.

The test record must preserve the raw value pairs and the calculation method.
A screenshot alone is not sufficient evidence.

## Workload scenarios

Run every scenario for at least 60 evaluated seconds:

1. idle after system activity has settled
2. CPU-only sustained load
3. GPU 3D sustained load
4. hardware-accelerated video decode
5. large-file copy
6. composite CPU, GPU, and network/storage load

The large-file copy should use a network share when practical so the same
interval exercises network throughput. Otherwise, add a separate sustained
network transfer of at least 60 seconds. At least one evaluated network window
must sustain 10 MB/s or more.

## Metric acceptance criteria

### CPU utilization

- Median absolute difference from Task Manager is at most 5 percentage points.
- The reported value moves in the same direction as Task Manager between idle
  and the CPU-load interval.
- No finite displayed value is below 0% or above 100%.

### CPU current speed

- The display retains the estimate marker documented by the metric ADR.
- During the CPU-load interval, median relative difference from Task Manager is
  at most 10%.
- An unavailable source is shown as unavailable, not as zero or a fabricated
  frequency.

### GPU utilization

- Compare the app's busiest-engine value with the corresponding active adapter
  and busiest-engine view in Task Manager.
- The median of aligned five-second windows differs by at most 10 percentage
  points during both 3D and video-decode load.
- The selected engine changes when the dominant workload changes, or the
  private evidence explains why one engine remained dominant.

### Dedicated GPU memory

- Usage never exceeds the reported dedicated-memory limit; an inconsistent
  pair is shown as unavailable.
- The limit matches the selected adapter's dedicated-memory capacity after
  allowing only normal display-unit rounding.
- During GPU load, the usage direction agrees with Task Manager and the median
  difference is no more than the greater of 256 MiB or 10% of Task Manager's
  observed dedicated usage.

An adapter that legitimately exposes no dedicated-memory capacity may report a
documented unavailable or zero-capacity state, but that state and the Task
Manager comparison must be recorded.

### Network

- For a 30-second window at or above 10 MB/s, the normalized receive/send rate
  differs from Task Manager by at most 10%.
- Below 10 MB/s, the absolute difference is at most 0.5 MB/s.
- Disconnect/reconnect or a counter reset may produce a documented warm-up
  sample but must not produce a negative or implausibly wrapped rate.

## GPU wildcard refresh comparison

Repeat a 60-second GPU-load capture for each supported refresh interval:

```text
--gpu-refresh-seconds 1
--gpu-refresh-seconds 2
--gpu-refresh-seconds 5
--gpu-refresh-seconds 10
```

For every interval:

- the collector's mean CPU use is below 1% on the test device
- a newly started GPU workload lasting at least 15 seconds is observed no later
  than the configured interval plus two seconds
- sampling remains responsive and does not continuously increase handle count

The release default remains five seconds unless the reviewed evidence changes
the metric ADR and the source candidate.

## Evidence and decision

Use the owner-private evidence name:

`DSM-1-<lineage12>-win-x64-<UTC timestamp>.json`

Keep adjacent CSV files and unredacted screenshots owner-private. DSM-1 is
`PASS` only when every required scenario and metric criterion passes against
the same lineage and artifact SHA-256. Unsupported required hardware, identity
mismatch, missing raw comparisons, an unexplained unavailable metric, or a
tolerance failure makes the result `BLOCKED` or `FAIL`.

After review, publish only the decision, candidate lineage, artifact SHA-256,
completed-test name, limitations, and hashes of approved redacted evidence in
`readiness-summary.json`. Update the metric ADR from `Draft` to `Accepted` only
after the reviewed DSM-1 result is `PASS`.
