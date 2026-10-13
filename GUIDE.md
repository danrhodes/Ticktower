# Ticktower: Project Guide

Source of truth for every Claude Code session. Read this file, then `TRACKER.md`, before any work.

## Session protocol

1. Read `GUIDE.md` and `TRACKER.md`.
2. Pick the first unchecked task in the lowest open phase. Do not skip phases.
3. Work one task or one vertical slice per session.
4. Write tests first for the state machine and UTC recompute logic.
5. Run `dotnet build` and `dotnet test`. Both must pass before you mark a task done.
6. Update `TRACKER.md`: tick tasks, add a session log line, log new decisions, log blockers.
7. Commit with a message that names the task ID, e.g. `P1-04: atomic JSON store`.
8. If this guide and the user's request conflict, stop and ask. Do not edit the spec silently.

## Rules

- Do not build outside the current phase.
- Do not add features not listed here. Log ideas under "Parking lot" in the tracker.
- Do not change the storage schema without bumping `version` and adding a migration.
- Never store tick counts. Store absolute UTC timestamps only.
- Never write JSON in place. Write a temp file, then atomic replace.
- No telemetry. No network calls except the P4 auto-update check.

## 1. Purpose

Multi-timer desktop tool for MSP engineers. It tracks callbacks, task time, and P1 update cadence. An air-traffic-style board shows every timer.

## 2. Scope

- Per-user install. Whole Service Desk can use it. No shared state.
- Internal tool. Public GitHub repo. MIT licence.
- Out of scope: 3CX integration, in-app reporting, PSA API push.

## 3. Stack and build

- C# / .NET 8 WPF.
- Single-file exe: `dotnet publish -p:PublishSingleFile=true`.
- GitHub Actions release build on tag.
- Namespace `Ticktower`. AUMID `Ticktower.App`, registered on first run for toasts.
- Single instance via named mutex. A second launch focuses the running instance.
- Tests: xUnit project `Ticktower.Tests`.

Suggested layout:

```
Ticktower/
  src/Ticktower/            WPF app
  src/Ticktower.Core/       Models, engine, state machine, storage (no UI references)
  tests/Ticktower.Tests/
  GUIDE.md
  TRACKER.md
  LICENSE
```

Keep the engine and storage in `Ticktower.Core`. The UI never holds logic.

## 4. Storage

Path: `%AppData%\Ticktower\`

| File | Content |
|---|---|
| `types.json` | Timer types, built-in and custom |
| `presets.json` | User presets |
| `alertprofiles.json` | Alert profiles and escalation ladders |
| `palettes.json` | Board colour palettes |
| `exportprofiles.json` | CSV export profiles |
| `settings.json` | Hotkeys, window positions per monitor, board mode |
| `state.json` | Live timers |
| `history/YYYY-MM.jsonl` | Closed timers with full event logs |

Rules:
- Every file carries a `version` field.
- Write to a temp file, then atomic replace.
- On parse failure, back up the file as `.bad` and load defaults.
- Presets, types, alert profiles, palettes, and export profiles import and export as JSON.
- Migrations keyed on `version`.

## 5. Timer model

Fields: `id`, `name`, `ticket`, `client`, `priority` (P1-P4), `typeId`, `kind`, `notes`, `alertProfileId`, `hotkey`, `nextTimerId` (chain), `state`, `endUtc`, `startedUtc`, `lastAckUtc`, `events[]`.

Kinds:
- **Countdown.** Fires once at `endUtc`.
- **Stopwatch.** Counts up. Unlimited concurrent instances.
- **Interval.** Repeats at a fixed length. Built-ins: 5, 10, 15, 30, 60 min.

Rules:
- Absolute UTC timestamps only. Sleep and reboot cannot drift timers.
- On launch, recompute every timer from `state.json`. Fire missed alerts immediately.
- Chain: when timer A completes, timer B starts. "Complete" = acknowledge for countdown and interval, stop for stopwatch.

## 6. State machine

- Countdown: Running -> Expired -> Acknowledged.
- Interval: Running -> Expired -> Acknowledged -> Running (restart on acknowledge).
- Stopwatch: Running <-> Paused -> Stopped.
- Snooze: Expired -> Running with a new end time. Preset and custom durations.

Event log, all UTC-stamped: start, stop, pause, resume, lap, bookmark (optional note), snooze, expire, ack, alert-fired.

Interval cards show "time since last client update". Restart-on-acknowledge stretches the real gap by the acknowledge delay.

## 7. Alerts

Channels: sound, toast, taskbar flash, full-screen overlay, TTS.

Profile fields:
- Channels enabled per step.
- Escalation ladder: ordered steps, each with a channel set and repeat interval.
- Intensity rises on each missed repeat. Default ladder: toast -> toast + sound -> overlay + sound -> overlay + TTS.
- Sound file, volume, TTS text template.
- Per-timer override of any profile field.

Rules:
- Alerts fire while minimised, in the tray, or on a locked screen.
- Repeat until acknowledged or snoozed.
- Ladder, repeat interval, and channels are editable per profile.

## 8. Windows

- **Launcher.** Small, floating, always-on-top. One button per timer type. Right-click opens type edit.
- **Entry window.** Floating. Fields: name, ticket, client, priority, type, notes, alert profile, hotkey. Opens from the launcher.
- **Board.** Resizable. Dockable to a second monitor. Optional always-on-top.
- **Tray icon.** Menu: show board, show launcher, new timer, mute all, settings, exit. Minimise sends the app to tray.
- Remember window position and size per monitor.

## 9. Board

- Lanes: Active, Expired, Acknowledged. Cards drop between lanes on state change.
- Active sorts by time remaining. Expired sorts by time overdue.
- Card shows: name, ticket, client, priority badge, remaining or elapsed time, lap count, bookmark count, last client update.
- Card actions: start, stop, pause, lap, bookmark, snooze, acknowledge, edit, delete.
- Colour modes: urgency (green -> amber -> red), priority, type, client.
- Palettes edit in-app and save to `palettes.json`.

## 10. Types and presets

- Types: add ad hoc from the launcher or entry window. Save to `types.json`.
- Type fields: name, kind, default duration, default alert profile, default priority, colour, hotkey.
- Presets: saved timer configurations per user. Example: "Client X P1 = 15 min interval, escalation ladder 2".
- Presets import and export as JSON.

## 11. Hotkeys

- Per-timer and per-type global hotkeys via Win32 `RegisterHotKey`.
- Reject duplicates at assignment. Show registration failures in the UI.
- Default action: start the timer. Stopwatch hotkey toggles start and pause.

## 12. CSV export

- Export by date range from history files.
- Export profile fields: name, delimiter, quote style, encoding (UTF-8 with BOM default), row mode (one row per timer or per event).
- Per column: source field, header text, order, enabled flag, format.
- Source fields: name, ticket, client, priority, type, start UTC, end UTC, duration, laps, bookmarks, notes, snoozes, acknowledgements.
- Duration formats: `hh:mm:ss`, decimal hours, minutes.
- Date formats: ISO 8601 or custom pattern.
- Built-in profiles: Generic, PSA-ready (remap once the PSA is known).
- Laps and bookmarks (timestamp plus note) export in their own columns or rows.

## 13. Phases

- **P1: Core.** Engine, state machine, event log, atomic storage, launcher, entry window, board, tray.
- **P2: Alerts and customisation.** Alert profiles, ladder, snooze, AUMID, types, hotkeys, presets, chain execution, colour modes.
- **P3: Export and polish.** CSV profiles and editor, monitor position memory.
- **P4: Later.** Auto-update.

Task-level breakdown lives in `TRACKER.md`.

## 14. Risks

- Hotkey conflicts with other apps. Surface failures in the UI.
- Locked-screen alerts. Test the overlay and sound path in P1.
- Schema drift breaks old files. Write migrations keyed on `version`.
- Toasts fail without AUMID registration. Test on a clean profile.
- Restart-on-acknowledge stretches P1 update gaps. Show "time since last client update".

## 15. Definition of done (per task)

- Code builds with zero warnings in `Ticktower.Core`.
- Unit tests cover new engine or storage logic.
- Tracker updated.
- No spec deviation, or the deviation is logged in the tracker decisions log and approved.

## 16. Open items

- PSA CSV layout unknown. Ship the Generic profile. Remap later.
- Name checks pending: GitHub, NuGet, UK IPO (classes 9 and 42). Unverified.
