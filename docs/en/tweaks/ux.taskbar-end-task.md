<!-- source: docs/tweaks/ux.taskbar-end-task.md sha256: 1f4eac33349c71ae7fdf1e00df7e6a64432546cfcd046e5dd32a22498fadd31b -->

# ux.taskbar-end-task – Add "End task" to the taskbar menu

## Verdict for 26H2
Port: a test on a 26H2 VM (Home and Pro) showed a real effect. MS source for the toggle: MS Learn "Advanced Windows Settings" (System > Advanced, item Taskbar – End Task); the page doesn't give the registry value name, which the VM test confirms. The item is manual: `suggested=false`, never selected automatically.

## What it changes
- `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings`, value `TaskbarEndTask`, **DWord**, set to `1`; `scope=user` (the signed-in user's hive).
- On a fresh 26H2 neither the value nor the `TaskbarDeveloperSettings` key exists (only the toggle in Settings creates it). Apply creates the key, undo deletes the value and the empty key created by apply.
- Apply doesn't restart File Explorer (decision R15). The item appears in the menu after File Explorer restarts: use the `tools.restart-explorer` action or sign in again.

## Sources
- https://learn.microsoft.com/en-us/windows/advanced-settings/ – MS Learn "Advanced Windows Settings" (2026-01-22): the toggle "Enable end task in taskbar by right click" in System > Advanced (25H2 and later; previously For developers).
- Test on 26H2 Home and Pro VMs, 2026-10-21 (build 26300.9457): mechanism read from the toggle in Settings and the change made through the registry API.
- Research: `docs/zrodla/2026-10-20-etap4a-zrodla.md` (in Polish), sections "Wyniki VM 26H2 (partia 1)" (26H2 VM results (batch 1)) and "Pozycje do katalogu" (catalog items), Task 8.

## Defaults on a fresh 26H2
Reading from the DSO-26H2-Home and DSO-26H2-Pro VMs, 2026-10-21: `TaskbarEndTask` **value missing** (off), the `TaskbarDeveloperSettings` key **missing** (Home and Pro identical). The toggle in Settings, switched on and back off, leaves an explicit value `0` instead of no value.

## Detection and undo
- Detection: `TaskbarEndTask` = DWORD 1 -> `Applied`; missing value or another value -> `NotApplied`.
- Undo restores the snapshot from before the change from the journal (missing value = deleting the value and the `TaskbarDeveloperSettings` key, if it was created at apply); `original` = `<RemoveEntry>` is only the fallback.
- Observation from the exploration: after undo and a File Explorer restart, the item disappears from the button's menu. In the test the key had been created earlier by the Settings toggle, so the deletion of the key itself by undo wasn't observed; the VM case of 2026-10-21 on Home and Pro proved it (Task 9 of the batch, section "VM test").

## Risks and warnings
- `risk=safe`, `suggested=false`.
- Warnings in the catalog: "The "End task" entry appears after you sign in again (or after you restart File Explorer in "Tools")." and ""End task" ends the program's process; unsaved data in that program may be lost."
- Beyond the notes in the program (in the technical details or only here): DSOptimizer doesn't restart File Explorer by itself (restarting is the `tools.restart-explorer` item); the note about data loss describes ending a process, not a measurement.

## VM test
Case `ux-taskbar-end-task` (`Flow=Full`) in `tests/vm/cases.psd1`: `KeyAbsentAfterUndo` for `TaskbarDeveloperSettings` (in `HKU\{UserSid}\…`), `NotWrittenTo` for the Administrator's HKCU. VM run of 2026-10-21 (`Invoke-VmTests.ps1`, Task 9 of the batch): **DSO-26H2-Home and DSO-26H2-Pro – passed**, steps detect `NotApplied` → apply `Applied` → detect `Applied` → undo `Undone` → detect `NotApplied`, the state after apply confirmed by an independent reading, final state = state before (`SnapshotDiff` empty). The `TaskbarDeveloperSettings` key created by apply doesn't exist after undo (`KeyAbsentAfterUndo`), the Administrator's HKCU unchanged.

## Observed effect
- **Home** (`DSO-26H2-Home`, EditionID `Core`), 2026-10-21. Method: the menu of a program's button on the taskbar in the `dso` session after File Explorer restarted, reading the menu items. Result: with `TaskbarEndTask`=1 the button's menu has the item "Zakończ zadanie" (End task); after undo and a File Explorer restart the item isn't there.
- **Pro** (`DSO-26H2-Pro`, EditionID `Professional`), 2026-10-21. Same method and same result.

## Reddit
To be added in part E.
