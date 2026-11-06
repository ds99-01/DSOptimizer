<!-- source: docs/tweaks/privacy.widgets.md sha256: a9b68b18c535133cf1b695e4f96c5d778de89c4962d257fc2e2e93725882fad5 -->

# privacy.widgets – Turn off Widgets

## Verdict for 26H2
Port: a test on a 26H2 VM (Home and Pro) showed a real effect. Documented by Microsoft in Policy CSP (Pro and higher, Home isn't listed); on Home the evidence is the VM test.

## What it changes
- `HKLM\SOFTWARE\Policies\Microsoft\Dsh`, value `AllowNewsAndInterests`, **DWord**, set to `0`; `scope=machine`.
- On a fresh 26H2 neither the value nor the `Dsh` key exists. Apply creates the key, undo deletes the value and the empty `Dsh` key created by apply.
- Apply doesn't restart File Explorer. The Widgets button disappears from the taskbar after File Explorer restarts: use the `tools.restart-explorer` action or sign in again. The board is blocked right away.
- The user variant considered, `HKCU\...\Explorer\Advanced\TaskbarDa`, isn't ported, because the system blocks the write (see `privacy.widgets-taskbar-da.md`).

## Sources
- Test on 26H2 Home and Pro VMs, 2026-10-21 (build 26300.9457): mechanism read from the toggle in Settings and from changing the policy through the registry API (.NET `RegistryKey`).

## Defaults on a fresh 26H2
Reading from the DSO-26H2-Home and DSO-26H2-Pro VMs, 2026-10-21: `HKLM\SOFTWARE\Policies\Microsoft\Dsh` **key missing**, `AllowNewsAndInterests` **no value** (Home and Pro identical).

## Detection and undo
- Detection: `AllowNewsAndInterests` = DWORD 0 -> `Applied`; missing value or a different value -> `NotApplied`.
- Undo restores the snapshot from before the change from the journal (a missing value = deleting the value and the `Dsh` key that was created during apply); `original` = `<RemoveEntry>` is only a fallback.

## Risks and warnings
- `risk=safe`, `suggested=true`.
- Warning in the catalog: "The button disappears after you sign in again (or after you restart File Explorer in "Repairs and tools")."
- Beyond the notes in the program (in the technical details or only here): restarting File Explorer is the `tools.restart-explorer` item; after the change the "Widgets" toggle in Settings is grayed out (the policy locks it and this can't be avoided).

## VM test
VM run of 2026-11-05 (build 26300.9457) on Windows 11 26H2 in English and Polish: `DSO-EN-Home` and `DSO-PL-Home` (Core), `DSO-EN-Pro` and `DSO-PL-Pro` (Professional), **Passed** on each: case `privacy-widgets`. Full run on each VM: 861 passed, 0 failed, 25 skipped. Earlier runs on Polish Windows below.

Case `privacy-widgets` (`Flow=Full`) in `tests/vm/cases.psd1`: `KeyAbsentAfterUndo` for `HKLM\SOFTWARE\Policies\Microsoft\Dsh`. VM run on 2026-10-21 (`Invoke-VmTests.ps1`): **DSO-26H2-Home and DSO-26H2-Pro – passed**, steps detect `NotApplied` → apply `Applied` → detect `Applied` → undo `Undone` → detect `NotApplied`, state after apply confirmed by an independent reading, final state = state before (`SnapshotDiff` empty). The `Dsh` key created by apply doesn't exist after undo (`KeyAbsentAfterUndo`). A separate VM measurement of 2026-10-21 (write through the API and undo) passed on Home and Pro.

## Observed effect
- **Home** (`DSO-26H2-Home`, EditionID `Core`), 2026-10-21. Method: UI Automation of Settings by `AutomationId`, screenshots, the Win+W keys in the `dso` session. Result: after writing `AllowNewsAndInterests`=0 the Win+W shortcut doesn't open the board; the "Widżety" (Widgets) toggle in Settings is grayed out and off; the taskbar button disappears after File Explorer restarts (without a restart the board is blocked, the button is still visible). After undo (value and key deleted) Win+W opens the board again, the toggle is active, the button comes back after File Explorer restarts.
- **Pro** (`DSO-26H2-Pro`, EditionID `Professional`), 2026-10-21. Same method and same result.
