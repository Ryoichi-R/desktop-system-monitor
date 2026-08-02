# DSM-1 metric acceptance checklist

Complete this checklist against
[DSM-1 metric acceptance](DSM-1-metric-acceptance.md). Store the completed copy
with owner-private evidence.

## Candidate binding

- [ ] Product version recorded
- [ ] Candidate lineage ID recorded
- [ ] Source tree digest recorded
- [ ] Runtime is `win-x64`
- [ ] Dependency fingerprint, .NET SDK, and publish contract recorded
- [ ] Artifact file name and byte count match the candidate manifest
- [ ] Recalculated artifact SHA-256 matches the candidate manifest
- [ ] PE machine is `0x8664`
- [ ] Test start/end time and tester are recorded privately

## Environment and method

- [ ] Windows build and hardware classes are recorded privately
- [ ] Task Manager update speed is **Normal**
- [ ] Test session is non-elevated
- [ ] Diagnostics CLI is built from the frozen candidate
- [ ] Timestamps permit one-second diagnostics and Task Manager readings to be aligned
- [ ] Units are normalized before comparison
- [ ] Raw value pairs and calculations are retained privately
- [ ] No competing DSM-2 or unrelated benchmark ran on the same resources

## Required workloads

- [ ] Idle, evaluated for at least 60 seconds after warm-up
- [ ] CPU-only load, evaluated for at least 60 seconds
- [ ] GPU 3D load, evaluated for at least 60 seconds
- [ ] Hardware video decode, evaluated for at least 60 seconds
- [ ] Large-file copy, evaluated for at least 60 seconds
- [ ] Composite load, evaluated for at least 60 seconds
- [ ] Network transfer includes at least one 30-second window at or above 10 MB/s

## Metric decisions

- [ ] CPU utilization median difference is at most 5 percentage points
- [ ] CPU utilization direction matches Task Manager
- [ ] CPU current-speed median relative difference is at most 10%
- [ ] CPU speed estimate marker/unavailable behavior is correct
- [ ] GPU 3D five-second-window median difference is at most 10 percentage points
- [ ] Video-decode five-second-window median difference is at most 10 percentage points
- [ ] Busiest-engine selection follows the dominant load or is explained
- [ ] Dedicated GPU usage never exceeds its limit
- [ ] Dedicated GPU limit matches the selected adapter after display rounding
- [ ] Dedicated GPU usage is within max(256 MiB, 10%) and follows load direction
- [ ] Network at or above 10 MB/s is within 10%
- [ ] Network below 10 MB/s is within 0.5 MB/s
- [ ] Network reset/warm-up produces no negative or wrapped rate

## GPU refresh intervals

- [ ] 1-second refresh run completed for 60 seconds
- [ ] 2-second refresh run completed for 60 seconds
- [ ] 5-second refresh run completed for 60 seconds
- [ ] 10-second refresh run completed for 60 seconds
- [ ] Each run has mean collector CPU below 1%
- [ ] Each run observes a new 15-second GPU workload within interval + 2 seconds
- [ ] No run hangs or continuously increases handle count

## Evidence review

- [ ] Private evidence name follows
      `DSM-1-<lineage12>-win-x64-<UTC timestamp>.json`
- [ ] Every evidence item is bound to the same lineage and artifact SHA-256
- [ ] Public evidence is redacted and hashed
- [ ] Device, user, path, process, and network identifiers remain private
- [ ] Blockers and known limitations are recorded
- [ ] Final decision is `PASS`, `BLOCKED`, or `FAIL`
- [ ] Metric ADR remains `Draft` unless the reviewed result is `PASS`
