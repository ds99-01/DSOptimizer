<!-- source: docs/tweaks/privacy.advertising-id.md sha256: 60cdf10de32afad480571a229faa7f77a5d2b9fc6afc4464423705ff7ef95209 -->

# privacy.advertising-id – Turn off the advertising ID

## Verdict for 26H2
Port: the advertising ID toggle is changed with two values; a test on a 26H2 VM (Home and Pro) confirmed the toggle state (writing `1`/`1` turns it on, `0`/`0` turns it off). Evidence: toggle state in Settings; the content itself wasn't observed.

## What it changes
- `HKCU\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo` (in the engine `HKU\<SID of the signed-in user>`), value `Enabled`, **DWord**, `0`.
- `HKCU\Software\Microsoft\Windows\CurrentVersion\CPSS\Store\AdvertisingInfo`, value `Value`, **DWord**, `0`; `scope=user`.
- R18: the item writes only these two values and **doesn't delete** the `Id` value (REG_SZ) from `AdvertisingInfo\Id`. When turned off, the toggle in Settings additionally deletes `AdvertisingInfo\Id`. In the exploration the write included this deletion, so the 0/0 values alone, without deleting `Id`, weren't checked separately. Task 9 (2026-10-21, Home and Pro) checked this separately: with an existing `Id`, the toggle is off after apply of this item (0/0 only) and on after undo (details in "VM test").

### Working alternative (described, without its own catalog item)
`HKLM\SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo`, `DisabledByGroupPolicy` = DWORD 1: on Home and Pro it shows the banner "Some of these settings are managed by your organization" and grays out the toggle. It doesn't go into the catalog, because it takes away the user's ability to switch the toggle (spec 5.2: we don't take the choice away).

## Sources
- Test on 26H2 Home and Pro VMs, 2026-10-21 (build 26300.9457, Windows not activated, local account): write through the registry API (.NET `RegistryKey`) and observation in Settings (UI Automation, screenshots).
- Research: `docs/zrodla/2026-10-20-etap4a-zrodla.md` (in Polish), section "Wyniki VM 26H2 (partia 1)" (26H2 VM results, batch 1).
- No Microsoft documentation for this value; evidence: VM test (observation below).

## Defaults on a fresh 26H2
Reading from the DSO-26H2-Home and DSO-26H2-Pro VMs, 2026-10-21: `Enabled` = 0, `Value` = 0, **no** `Id` value (turned off after setup from the answer file); both keys exist. On a fresh installation the item can therefore be detected as `Applied` right away. `original` in the catalog = 0 and 0 (fallback only).

## Detection and undo
- Detection: all values from "What it changes" equal to the written ones -> `Applied`; missing value or a different value -> `NotApplied`.
- Undo restores the snapshot from before the change from the journal (a missing value = deleting it); `original` from the catalog is only a fallback.
- Both keys existed before apply and stay.

## Risks and warnings
- `risk=safe`, `suggested=true` (R13).
- No warnings in the catalog.

## VM test
Case `privacy-advertising-id` (`Flow=Full`) in `tests/vm/cases.psd1` (`NotWrittenTo` for the `HKCU` of the Administrator process, because the item is per user; `KeyPresentAfterUndo`, because the key exists on a fresh installation). VM run on 2026-10-21 (`Invoke-VmTests.ps1`, Task 9 of the batch): **DSO-26H2-Home and DSO-26H2-Pro – passed**, steps detect `NotApplied` → apply `Applied` → detect `Applied` → undo `Undone` → detect `NotApplied`, state after apply confirmed by an independent reading, final state = state before (`SnapshotDiff` empty). R18: the test `Id` value (String `dso-test`) existed after apply and after undo unchanged (snapshot before = after), `Enabled` and `Value` returned to 1 from the snapshot, both keys stayed (`KeyPresentAfterUndo`). Separate check of the toggle state (2026-10-21, Home and Pro, CLI `--apply`/`--undo` + UI Automation of the `ms-settings:privacy-general` page, `AutomationId` `SystemSettings_Privacy_AdvertisingIdEnabled_ToggleSwitch`, Settings closed before each reading): before apply (1/1, `Id`=`dso-test`) the toggle `On`, after apply (0/0) `Off`, after undo (1/1) `On`; detection after apply `Applied`, `user.isCertain`=true. On Pro, Windows itself replaced the test `Id` with a new ID (32 hex characters) between the reading before apply (with Settings open) and the reading after apply; DSOptimizer doesn't write `Id`, and in the `Invoke-VmTests` run (without opening Settings) `Id` stayed unchanged on both VMs. Raw result: outside git. The exploration of 2026-10-21 passed on Home and Pro.

## Observed effect
- **Home** (`DSO-26H2-Home`), 2026-10-21. Method: the advertising ID toggle in Settings (UI Automation) after a write through the API. Result: already off by default (0/0, no `Id`); writing 1/1 turns the toggle on, writing 0/0 turns it off. Evidence: toggle state in Settings; the content itself wasn't observed.
- **Pro** (`DSO-26H2-Pro`), 2026-10-21. Same method and same result.

## Reddit
To be added in part E.
