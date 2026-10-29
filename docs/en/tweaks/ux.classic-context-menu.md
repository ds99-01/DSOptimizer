<!-- source: docs/tweaks/ux.classic-context-menu.md sha256: eaeeda17e54ea1c3c98e7bc6815460ed04b91788ac91a6782a5e6eb993183f33 -->

# ux.classic-context-menu – Bring back the classic right-click menu

## Verdict for 26H2
Port: a test on a 26H2 VM (Home and Pro) showed a real effect. There is no MS documentation for this key in our source; the evidence is the VM test (observation below). The item is manual: `suggested=false`, never selected automatically.

## What it changes
- `HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32`, **default value** (empty name), **String** (REG_SZ), empty string; `scope=user` (the signed-in user's hive, not the Administrator account's).
- On a fresh 26H2 neither the `{86ca…}` key nor its `InprocServer32` subkey exists. Apply creates both, undo deletes both (the engine saves the highest missing key in the snapshot and, after restoring the missing value, deletes the empty keys from `InprocServer32` up to `{86ca…}`).
- Apply doesn't restart File Explorer (ruling R15). The menu changes after File Explorer restarts: use the `tools.restart-explorer` action or sign in again.

## Sources
- Test on 26H2 Home and Pro VMs, 2026-10-21 (build 26300.9457): write and undo through the registry API (.NET `RegistryKey`), observation of the menu window.
- Research: `docs/zrodla/2026-10-20-etap4a-zrodla.md` (in Polish), sections "Wyniki VM 26H2 (partia 1)" (26H2 VM results, round 1) and "Pozycje do katalogu" (catalog items), Task 8.

## Defaults on a fresh 26H2
Reading from the DSO-26H2-Home and DSO-26H2-Pro VMs, 2026-10-21: `HKU\<SID>\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}` **key missing**, `...\InprocServer32` **key missing** (Home and Pro identical).

## Detection and undo
- Detection: default value of `InprocServer32` = empty REG_SZ -> `Applied`; missing key or another value -> `NotApplied`.
- Undo restores the snapshot from before the change from the journal (missing value = deleting the value, and then both keys created by apply); `original` = `<RemoveEntry>` is only the fallback.
- Observation from the exploration: after undo both keys were deleted, and after File Explorer restarted the Windows 11 menu came back. The deletion of both keys by the engine was confirmed by the VM case of 2026-10-21 on Home and Pro (Task 9 of the round, section "VM test").

## Risks and warnings
- `risk=caution`, `suggested=false`.
- Warning in the catalog: "The menu changes after you sign in again (or after you restart File Explorer in "Tools")."
- Beyond the notes in the program (in the technical details or only here): DSOptimizer doesn't restart File Explorer by itself (restarting is the `tools.restart-explorer` item); the entry in HKCU overrides the registration of the Windows 11 menu for this account, and undo restores the state from before the change.

## VM test
Case `ux-classic-context-menu` (`Flow=Full`) in `tests/vm/cases.psd1`: `KeyAbsentAfterUndo` for `{86ca…}` and `{86ca…}\InprocServer32` (in `HKU\{UserSid}\…`), `NotWrittenTo` for the Administrator's HKCU. VM run on 2026-10-21 (`Invoke-VmTests.ps1`, Task 9 of the round): **DSO-26H2-Home and DSO-26H2-Pro – passed**, steps detect `NotApplied` → apply `Applied` → detect `Applied` → undo `Undone` → detect `NotApplied`, the state after apply confirmed by an independent reading, final state = state before (`SnapshotDiff` empty). After undo the engine deleted both keys created by apply: `{86ca…}\InprocServer32` and `{86ca…}` (`KeyAbsentAfterUndo`). After apply the independent reading saw an empty default value (`Exists`, `String`, empty data) and detection `Applied`; after undo the key was missing and detection `NotApplied` (an empty default value and a missing key are distinguished). The Administrator's HKCU unchanged.

## Observed effect
- **Home** (`DSO-26H2-Home`, EditionID `Core`), 2026-10-21. Method: right-click on the desktop in the `dso` session after File Explorer restarted, reading the class of the opened menu window. Result: with the key, a right-click on the desktop opens a window of class `#32768` (Win32 menu); without the key, `Microsoft.UI.Content.PopupWindowSiteBridge` opens (Windows 11 menu). After undo (both keys deleted) and a File Explorer restart, the Windows 11 menu comes back.
- **Pro** (`DSO-26H2-Pro`, EditionID `Professional`), 2026-10-21. Same method and same result.

## Reddit
To be added in part E.
