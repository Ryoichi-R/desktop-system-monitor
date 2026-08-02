# DSM-2 endurance acceptance checklist

Complete this checklist against
[DSM-2 endurance acceptance](DSM-2-endurance-acceptance.md). Store the
completed copy with owner-private evidence.

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

## Resource method fixed before launch

- [ ] Exact process is identified by PID plus process start time
- [ ] Private Working Set counter/API is recorded
- [ ] Total Working Set counter/API is recorded separately
- [ ] Handle-count counter/API is recorded
- [ ] Sampling interval is one minute
- [ ] State transitions are timestamped in the same private evidence stream

## Duration and coverage

- [ ] Wall-clock run is at least 24 hours
- [ ] At least 23 hours of valid one-minute samples are retained
- [ ] Planned sleep interval is marked
- [ ] Every other gap longer than five minutes is explained and reviewed
- [ ] No restart occurred during the endurance clock

## Resource decisions

- [ ] Every post-warm-up five-minute Private Working Set median is below 100 MiB
- [ ] Final Total Working Set one-hour median is at most 20 MiB above the first
- [ ] Total Working Set fitted slope is at most 1 MiB/hour
- [ ] Final handle-count one-hour median is at most 25 above the first
- [ ] No six consecutive hourly handle medians are strictly increasing
- [ ] No unexplained process CPU above 5% persists for ten minutes
- [ ] Diagnostic log has no continuous repeating-failure growth

## Reliability

- [ ] No unexpected exit or unhandled exception
- [ ] No hang persists beyond two normal sampling intervals after recovery
- [ ] Exactly one process, overlay, and tray icon remain
- [ ] Metrics resume or show a documented unavailable state
- [ ] Tray, click-through, and requested window layer remain usable

## Windows transitions

- [ ] Win+D and restore
- [ ] Explorer restart
- [ ] Session lock and unlock
- [ ] Sleep and resume
- [ ] Network disconnect and reconnect
- [ ] Full-screen enter and exit
- [ ] Each transition recovers within two normal sampling intervals
- [ ] No transition leaves a duplicate process, overlay, or tray icon

## DPI and displays

- [ ] Secondary display is available or the release scope excludes the claim
- [ ] Overlay is checked on every available display
- [ ] Mixed-DPI movement or scale change is checked
- [ ] Primary-display change is checked
- [ ] Secondary-display disconnect and reconnect is checked
- [ ] Settings remains usable at each tested scale factor
- [ ] Windows remain visible or use the documented safe fallback

## Scheduled observations

- [ ] Launch checkpoint
- [ ] Approximately 1-hour checkpoint
- [ ] Approximately 6-hour checkpoint
- [ ] Approximately 12-hour checkpoint
- [ ] Approximately 18-hour checkpoint
- [ ] Approximately 24-hour checkpoint
- [ ] Every checkpoint records usability, single-instance state, resources, and logs

## Evidence review

- [ ] Private evidence name follows
      `DSM-2-<lineage12>-win-x64-<UTC timestamp>.json`
- [ ] Every evidence item is bound to the same lineage and artifact SHA-256
- [ ] Public evidence is redacted and hashed
- [ ] Device, user, path, process, and network identifiers remain private
- [ ] Blockers and known limitations are recorded
- [ ] Final decision is `PASS`, `BLOCKED`, or `FAIL`
