#
| 2026-10-13 | Build | P1-06, P1-07 | `TimerEngine` and `IAlertSink` in Core. Event logging itself was built in P1-03; P1-07 added lifecycle-order and JSON round-trip tests for all 10 event types, plus engine-written alert-fired events. 62 tests passing, 0 warnings. | Ticktower: Tracker

Update at the end of every session. Spec lives in `GUIDE.md`.

**Current phase:** P1
**Next task:** P1-08
**Last updated:** 2026-10-13

## Status key

`[ ]` todo | `[~]` in progress | `[x]` done | `[!]` blocked

## P1: Core

- [x] P1-01 Repo scaffold: solution, `Ticktower`, `Ticktower.Core`, `Ticktower.Tests`, MIT `LICENSE`, `.gitignore`
- [x] P1-02 Models: timer, kind, state, event, priority, `version` field, `nextTimerId`
- [x] P1-03 State machine with unit tests (countdown, interval, stopwatch, snooze)
- [x] P1-04 Atomic JSON store: temp write, replace, `.bad` backup, defaults on failure
- [x] P1-05 Migration framework keyed on `version`
- [x] P1-06 Engine: UTC recompute on launch, missed-alert firing, tick loop (UI refresh only)
- [x] P1-07 Event log: start, stop, pause, resume, lap, bookmark, snooze, expire, ack, alert-fired
- [ ] P1-08 History writer: `history/YYYY-MM.jsonl` on timer close
- [ ] P1-09 Single-instance mutex, focus running instance
- [ ] P1-10 Launcher window: floating, always-on-top, button per type
- [ ] P1-11 Entry window: name, ticket, client, priority, type, notes
- [ ] P1-12 Board: lanes Active, Expired, Acknowledged; sort rules; card actions
- [ ] P1-13 Card display: remaining or elapsed, laps, bookmarks, time since last client update
- [ ] P1-14 Tray icon and menu, minimise to tray
- [ ] P1-15 Basic alert path: toast and sound on expire, ack, snooze (locked-screen test)
- [ ] P1-16 Built-in types and intervals: 5, 10, 15, 30, 60 min

**P1 exit test:** start 3 timers, sleep the PC for 5 min, wake, confirm correct expiry and alerts. Kill the app mid-run, relaunch, confirm state restored.

## P2: Alerts and customisation

- [ ] P2-01 Alert profile model and `alertprofiles.json`
- [ ] P2-02 Escalation ladder engine: repeat interval, intensity per missed repeat
- [ ] P2-03 Channels: taskbar flash, full-screen overlay, TTS
- [ ] P2-04 Alert profile editor UI
- [ ] P2-05 Per-timer alert override
- [ ] P2-06 Snooze presets and custom duration
- [ ] P2-07 AUMID registration on first run, clean-profile test
- [ ] P2-08 Ad hoc type creation, save to `types.json`
- [ ] P2-09 Global hotkeys via `RegisterHotKey`, duplicate rejection, UI failure messages
- [ ] P2-10 Presets: save, load, import, export JSON
- [ ] P2-11 Chain execution (`nextTimerId`)
- [ ] P2-12 Board colour modes: urgency, priority, type, client
- [ ] P2-13 Palette editor, save to `palettes.json`
- [ ] P2-14 Import and export for types, alert profiles, palettes

## P3: Export and polish

- [ ] P3-01 Export profile model and `exportprofiles.json`
- [ ] P3-02 CSV writer: delimiter, quoting, encoding, row mode
- [ ] P3-03 Column mapping: source field, header, order, enabled, format
- [ ] P3-04 Duration and date formats
- [ ] P3-05 Built-in profiles: Generic, PSA-ready
- [ ] P3-06 Export UI: date range, profile picker, profile editor
- [ ] P3-07 Profile import and export JSON
- [ ] P3-08 Window position and size memory per monitor
- [ ] P3-09 GitHub Actions release build on tag
- [ ] P3-10 README: install, usage, screenshots

## P4: Later

- [ ] P4-01 Auto-update

## Decisions log

| Date | Decision |
|---|---|
| 2026-10-10 | Name: Ticktower. Fallback: Tickwatch. |
| 2026-10-10 | Stack: C# / .NET 8 WPF, single-file exe, MIT, public GitHub. |
| 2026-10-10 | Interval timers restart on acknowledge. |
| 2026-10-10 | Escalation: repeat at set interval, intensity rises per missed repeat. Ladder editable. |
| 2026-10-10 | Stopwatches: unlimited concurrent. |
| 2026-10-10 | Chain timers in scope (P2). `nextTimerId` in schema from P1. |
| 2026-10-10 | Board colour modes: urgency, priority, type, client. Palettes editable. |
| 2026-10-10 | CSV columns customisable via export profiles. No in-app reporting. |
| 2026-10-10 | No 3CX integration. No PSA API push. |
| 2026-10-10 | Presets per user, JSON import and export. |
| 2026-10-13 | Added `Duration` (TimeSpan) to the timer model: countdown and interval length, needed for interval restart-on-ack. Not in the GUIDE field list; additive, schema version 1. |
| 2026-10-13 | Expire events are stamped with `EndUtc` (true expiry), not detection time, so sleep/relaunch does not skew the log. Stopwatch elapsed is derived from the event log. |
| 2026-10-13 | A file with a missing/invalid version, or a version newer than the build supports, is treated like a parse failure: moved to `.bad` and defaults loaded, so nothing is lost or overwritten. Migrated files are persisted on next save, not on load. |
| 2026-10-13 | Engine takes an `IAlertSink` and an injectable UTC clock, and raises `Changed` for persistence. `Recompute` (launch) and `Tick` share one sweep; alert-fired notes are `missed` or `expired`. Re-alerting an unacknowledged expired timer after relaunch is left to the P2 ladder. |
| 2026-10-13 | Only .NET SDK 10 installed (runtime 8 present). Projects target net8.0 / net8.0-windows, built with SDK 10. LICENSE and README already existed. |

## Blockers and open questions

- [ ] PSA CSV layout unknown. Generic profile ships first.
- [ ] Name checks: GitHub, NuGet, UK IPO classes 9 and 42 (unverified).

## Parking lot (not in scope, do not build)

- (empty)

## Session log

| Date | Session | Tasks | Notes |
|---|---|---|---|
| 2026-10-10 | Planning | n/a | Spec and tracker created. No code. |
| 2026-10-13 | Build | P1-01 | Scaffolded Ticktower.slnx with Ticktower (WPF), Ticktower.Core, Ticktower.Tests (xUnit). Build 0 warnings. No tests yet. |
| 2026-10-13 | Build | P1-02, P1-03 | Models and `TimerStateMachine` in Core, 25 tests passing, 0 warnings. Working tree had unrelated changes from outside the session (see notes); left uncommitted. |
| 2026-10-13 | Build | P1-04, P1-05 | `JsonFileStore` (temp write, atomic replace, `.bad` recovery) and `Migrator` in Core. 45 tests passing, 0 warnings. |
