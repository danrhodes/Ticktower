# Ticktower: Tracker

Update at the end of every session. Spec lives in `GUIDE.md`.

**Current phase:** P2
**Next task:** P1 exit test (manual), then P2-03 / P2-07 manual checks, then P3-01
**Last updated:** 2026-10-14

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
- [x] P1-08 History writer: `history/YYYY-MM.jsonl` on timer close
- [x] P1-09 Single-instance mutex, focus running instance
- [x] P1-10 Launcher window: floating, always-on-top, button per type
- [x] P1-11 Entry window: name, ticket, client, priority, type, notes
- [x] P1-12 Board: lanes Active, Expired, Acknowledged; sort rules; card actions
- [x] P1-13 Card display: remaining or elapsed, laps, bookmarks, time since last client update
- [x] P1-14 Tray icon and menu, minimise to tray
- [x] P1-15 Basic alert path: toast and sound on expire, ack, snooze (locked-screen test)
- [x] P1-16 Built-in types and intervals: 5, 10, 15, 30, 60 min

**P1 exit test:** start 3 timers, sleep the PC for 5 min, wake, confirm correct expiry and alerts. Kill the app mid-run, relaunch, confirm state restored.

## P2: Alerts and customisation

- [x] P2-01 Alert profile model and `alertprofiles.json`
- [x] P2-02 Escalation ladder engine: repeat interval, intensity per missed repeat
- [~] P2-03 Channels: taskbar flash, full-screen overlay, TTS
- [x] P2-04 Alert profile editor UI
- [x] P2-05 Per-timer alert override
- [x] P2-06 Snooze presets and custom duration
- [~] P2-07 AUMID registration on first run, clean-profile test
- [x] P2-08 Ad hoc type creation, save to `types.json`
- [x] P2-09 Global hotkeys via `RegisterHotKey`, duplicate rejection, UI failure messages
- [x] P2-10 Presets: save, load, import, export JSON
- [x] P2-11 Chain execution (`nextTimerId`)
- [x] P2-12 Board colour modes: urgency, priority, type, client
- [x] P2-13 Palette editor, save to `palettes.json`
- [x] P2-14 Import and export for types, alert profiles, palettes

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
| 2026-10-14 | "Closed" means removed from the live set (`TimerEngine.Remove`), which raises `Closed(timer, utc)`. `HistoryWriter` appends a versioned `HistoryRecord` line to `history/YYYY-MM.jsonl`, filed by the UTC close month. Wiring Closed to the writer happens at app startup (UI tasks). Corrupt lines are skipped on read. |
| 2026-10-14 | User asked for P1-09 to P1-16 in one session, overriding the one-task-per-session protocol. Committed per task or slice. |
| 2026-10-14 | `Closed` means removed from the live set (`TimerEngine.Remove`). `HistoryWriter` appends a versioned `HistoryRecord` per line to `history/YYYY-MM.jsonl`, filed by UTC close month. |
| 2026-10-14 | Built-in types are the five intervals plus a 15 min Countdown and a Stopwatch, so every kind has a launcher button. |
| 2026-10-14 | Board lane and sort rules, card text and "last client update" (since last ack, else start; intervals only) live in Core `BoardLogic`. WPF code only binds. |
| 2026-10-14 | P1 snooze is a fixed 5 min button and toast action. Presets and custom durations stay in P2-06. Launcher right-click type edit is deferred to P2-08. Tray "settings" item omitted until a settings window exists. |
| 2026-10-14 | P1 alerts: toast (Microsoft.Toolkit.Uwp.Notifications, buttons Acknowledge and Snooze) plus system sound; failures are swallowed. App targets net8.0-windows10.0.17763.0 and enables WinForms for `NotifyIcon` only. AUMID registration stays P2-07. |
| 2026-10-14 | Added `TICKTOWER_DATA` env var to override the data folder, for sandboxed runs and tests. |
| 2026-10-14 | User started P2 with the P1 exit test still unrun (it is manual). Proceeded; the test stays open. |
| 2026-10-14 | `state.json` bumped to version 2 (migration `StateV1ToV2`): adds `alertOverride` and the `Pending` state. History records keep their own version 1; the timer inside is read tolerantly. |
| 2026-10-14 | Ladder: `IAlertSink.Alert(AlertNotice)` replaces `Expired`. A rung's `RepeatInterval` is the wait before the next rung; past the last rung it repeats. Rung due-ness is derived from alert-fired events since the latest expiry, so it survives relaunch and a snooze restarts the ladder. Relaunch continues at the next rung (one alert per sweep, no catch-up burst). Repeat floor 1 s. Alert-fired notes: `expired`, `missed`, `repeat N`. This settles the P1 note about re-alerting after relaunch. |
| 2026-10-14 | Built-in profiles: Default (the guide's ladder, 60 s per rung) and Urgent. Per-timer `AlertOverride` layers any of ladder, sound file, volume, speech text over the profile. |
| 2026-10-14 | Chain: the successor is created `Pending` (new state, shown in Active as "Queued") by `AddChain` and referenced by `NextTimerId`. It starts when the predecessor is acknowledged (countdown, interval) or stopped (stopwatch). The entry window's "Then start" picks a type for it. |
| 2026-10-14 | `settings.json` v1 holds snooze presets (minutes), board colour mode and palette id. Window positions and board mode memory arrive in P3-08. |
| 2026-10-14 | Colour modes: urgency is green until 50% left, blends to amber at 25%, red at 0%; expired is red; acknowledged is grey; stopwatch and queued are neutral. Client colour is an FNV-1a hash of the lower-cased client name, so it is stable across runs. Palettes: Default and High contrast built in. |
| 2026-10-14 | Hotkeys: need at least one modifier; keys A-Z, 0-9, F1-F24. Duplicates rejected inside Ticktower before Windows is asked; Windows refusals are shown as the Win32 message. Type hotkey starts a new timer (a stopwatch type toggles its live stopwatch). Timer hotkey starts a queued timer or toggles a stopwatch. The type's hotkey is not copied onto timers made from it. |
| 2026-10-14 | Import/export: one envelope (`version`, `kind`, `items`) for types, alert profiles, presets and palettes. Only custom items export. Import never overwrites built-ins (a clash becomes a copy), never marks items built-in, and strips hotkeys from imported types and presets to avoid silent clashes. A bad import file is never moved to `.bad`. |
| 2026-10-14 | AUMID `Ticktower.App` is set on the process and its HKCU key created on every start (idempotent); the toast library adopts it (verified: its activator was written under that key). |
| 2026-10-14 | Overlay covers the primary screen only. Taskbar flash shows a hidden board (without focus) so there is a button to flash. TTS uses System.Speech (NuGet 8.0.0). The Windows default alert sound ignores profile volume; volume applies to sound files and speech. |
| 2026-10-14 | Entry window gained a Length field (needed for presets such as "15 min interval" and ad hoc lengths), Alerts, Hotkey and Then-start rows. Board gained an always-on-top tick (the guide lists it as optional). A Settings window (tray, launcher) hosts types, profiles, presets, palettes and snooze presets. |

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
| 2026-10-13 | Build | P1-06, P1-07 | `TimerEngine` and `IAlertSink` in Core. Event logging itself was built in P1-03; P1-07 added lifecycle-order and JSON round-trip tests for all 10 event types, plus engine-written alert-fired events. 62 tests passing, 0 warnings. |
| 2026-10-14 | Build | P1-08 | `HistoryWriter`, `HistoryRecord`, `TimerEngine.Closed`. 68 tests passing, 0 warnings. Pre-existing uncommitted changes (Tests csproj package versions, `TicktowerCoreTests.cs`) left untouched. |
| 2026-10-14 | Build | P1-09 to P1-16 | Core: `SingleInstance`, `TimerType`/`BuiltInTypes`, `BoardLogic`, `AppPaths`, `TimerEngine.Edit`. App: `AppHost`, `AlertService`, launcher, entry window, board, tray. 81 tests passing, 0 warnings. Smoke run: relaunch recomputes an expired timer with a `missed` alert, second launch exits. **Not verified:** UI appearance, toast delivery, locked-screen alerts, sleep/wake. See P1 exit test. |
| 2026-10-14 | Build | P2-01 to P2-14 | Core: alert profiles, ladder, override, chain/Pending, colours, palettes, hotkeys, catalog import/export, presets, settings, state v2 migration. App: channels (toast, sound, flash, overlay, TTS), editors, settings window, hotkey service, AUMID. 145 tests passing, 0 warnings. Smoke runs: ladder repeated every 2 s with `repeat N` notes; a real global hotkey created a timer; AUMID key written with toast activator. **Not verified:** how the new windows look, audible sound/TTS, overlay appearance and locked screen, toast on a clean profile. |
