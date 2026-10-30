<!-- source: docs/tweaks/repair.system-protection.md sha256: 4baa851e435bc5277e5f3bc5b72220f475f3d54bba1189f859692a8a8e4f8733 -->

# repair.system-protection – System Protection

## Verdict for 26H2
Port as `kind=tweak`, `risk=safe` ("Repairs" category, Stage 4b, round 1): on a fresh 26H2 Home and Pro, System Protection is turned off, so no restore point is created; `Enable-ComputerRestore -Drive 'C:\'` turns it on the same way as the "System Protection" window, and the effect can be read from the registry. Undo turns protection off, but doesn't delete existing points.

## What it changes
Script (`script.detect` / `apply` / `undo`), by command name (the tests mock them):
- `Enable-ComputerRestore -Drive 'C:\'` (apply) and `Disable-ComputerRestore -Drive 'C:\'` (undo). VSS sets the space limit itself (3% of the volume, the same value as when turned on from the window); **no `vssadmin resize`**, so `vssadmin` isn't called at all.
- Reading (read-only, helpers mocked in the tests): `Get-DsoSystemVolumeId` (CIM `Win32_Volume`, `DriveLetter='C:'`, `DeviceID` = `\\?\Volume{GUID}\`) and `Get-DsoProtectionClients` (the REG_MULTI_SZ value `{09F7EDC5-294E-4180-AF6A-FB0E6A0E9513}` in `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SPP\Clients`, 64-bit view).

## Sources
- Test on 26H2 Home and Pro VMs (build 26300.9457), 2026-10-22: `docs/zrodla/2026-10-22-etap4b-1-pomiary.md` (in Polish), decision table and section 7.
- The rule that links the entry to volume C: reproduces `src/DSOptimizer.Windows/Apps/SystemProtectionReader.cs` (Stage 4a, Task 2): the entry counts as turned on when any element of the value contains the GUID of volume C: from `Win32_Volume`.
- No MS documentation checked in this task; evidence: the VM test described below.

## Defaults on a fresh 26H2
Home and Pro (reading 2026-10-22): `SPP\Clients` without an entry, `vssadmin list shadowstorage` = "No items found", `SystemRestore\DiskPercent` = 15 (an old value that doesn't match the limit), `SystemRestorePointCreationFrequency` none.

## Detection and undo
- Detect: an entry for volume C: in `SPP\Clients` = `Applied`, no entry (or no key/value) = `NotApplied`, regardless of whether shadow copies (points) still exist. If volume C: can't be determined or the registry can't be read = script error = unknown state (not `NotApplied`).
- Apply counts as success only after verification (the C: entry exists); otherwise the error "System Protection wasn't turned on…". Already turned on = `AlreadyApplied` (engine, before the script).
- Undo: `Disable-ComputerRestore`, then verification (the entry is gone). It doesn't restore any snapshot (the state is a single entry), so `original` and the snapshot don't matter.

## Risks and warnings
- `risk=safe`.
- Warnings in the catalog: "Undo turns protection off: no new points are created, and you can't restore the system from existing ones (they stay on the disk)." and "Turning it on doesn't create a point – the first one is created before a change or with "Create a restore point now"."
- The first warning is a short form of a text changed from the spec (the measurement contradicts "deletes all points", Ruling R7). Full text of R7: "Turning off System Protection blocks the creation of new restore points and restoring the system from existing ones. Existing points stay on the disk (they take up space) and come back when protection is turned on again; they can be deleted in the 'System Protection' window (Configure → Delete)."
- Windows sets the space limit (3% of the C: drive on the measured machine); the program doesn't change it.
- Turning it on alone doesn't create a point. Turning protection off "with deletion of points" (`vssadmin delete shadows`) is a separate, irreversible operation that isn't here and wasn't measured.

## VM test
Case `repair-system-protection` (`Flow=Full`, `Snapshot`: the `{09F7EDC5-…}` value in `SPP\Clients`) added to `tests/vm/cases.psd1` in Task 7.

VM run (Task 10, 2026-10-22, full `tests/vm/Invoke-VmTests.ps1` on the `etap4b-1` branch; reports outside git): Home and Pro 722 passed / 0 failed / 25 skipped each (6 cases with `Skip` outside the Repairs category). `repair-system-protection`: **Passed** on Home and Pro – `NotApplied` → apply `Applied` → detection `Applied` → undo `Undone` → `NotApplied`, `CheckAfterApply`/`CheckAfterUndo` (the entry for volume C: in `SPP\Clients`) passed, the final state of the `SPP\Clients` value = the state before. The race when writing `SPP\Clients` after `Enable-ComputerRestore` (a minor finding from Task 7) wasn't observed.

## Observed effect
Method: reading `SPP\Clients`, `vssadmin list shadowstorage` and `Get-ComputerRestorePoint` before and after the change; a point created after turning it on; raw results outside git.
- **Home** (`DSO-26H2-Home`, 26300.9457), 2026-10-22: turning it on from the "System Protection" window (`SystemPropertiesProtection.exe` → "Konfiguruj" (Configure) → "Włącz ochronę systemu" (Turn on system protection)) and `Enable-ComputerRestore -Drive 'C:\'` (24–26 ms) give the same state: an entry in `SPP\Clients`; the shadow storage is created with the first point, with a limit of 2,052,367,196 B = 3.0% of the 68,412,239,872 B volume. `Disable-ComputerRestore -Drive 'C:\'` removes the entry, but the point stays (`Get-ComputerRestorePoint` and `vssadmin list shadows` see it after 3 s, 60 s and after a VM restart); after `Enable-ComputerRestore` again, the point is back in the list. With protection turned off, a point can't be created (WMI: the service is disabled).
- **Pro** (`DSO-26H2-Pro`, 26300.9457), 2026-10-22: exactly the same as on Home (runs P1 and A2).

## Reddit
To be added in part E.
