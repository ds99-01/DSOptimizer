<!-- source: docs/tweaks/repair.restore-point.md sha256: 4c8745b51064d46917feefd72bc6df82637f07d0e1046aeff5b9fd5b61d0378e -->

# repair.restore-point – Create a restore point now

## Verdict for 26H2
Port as `kind=action`, `risk=safe` ("Repairs" category, Stage 4b batch 1): an on-demand point through the existing `IRestorePointService` (the same one that creates points before changes) works on 26H2 Home and Pro when System Protection is turned on. No undo.

## What it changes
C# handler `restore-point` (`RestorePointHandler`), without a script: `IRestorePointService.Create("DSOptimizer – manual restore point")` (the name follows the program language; in Polish it is "DSOptimizer – punkt ręczny"). For the duration of the call, the service sets `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore\SystemRestorePointCreationFrequency` (the 24-hour limit) to zero, calls `SystemRestore.CreateRestorePoint`, waits (up to 10 s) until the point appears in the list, and restores the previous value of the limit. Results:
- `Created` → `Executed`: "Created restore point "DSOptimizer – manual restore point" (<local time from IClock>)."
- `ProtectionDisabled` → `Failed`: "System Protection is turned off for drive C: – turn it on with the "System Protection" item in this category." The handler doesn't silently turn anything on.
- `Failed` → `Failed` with the service message and code (`(code N)`), when the service provided it.

## Sources
- Test on 26H2 Home and Pro VMs (build 26300.9457), 2026-10-22: `docs/zrodla/2026-10-22-etap4b-1-pomiary.md` (in Polish), section 7 (a point created by `DSOptimizer.exe --apply vmtest.hklm-value --restore-point required` = `Created`; with protection turned off, `CreateRestorePoint` returns "Nie można uruchomić określonej usługi, ponieważ jest ona wyłączona…" (the specified service can't be started because it is disabled…)).
- No MS documentation checked in this task; evidence: the VM test described below.

## Defaults on a fresh 26H2
System Protection turned off (`SPP\Clients` without an entry), `SystemRestorePointCreationFrequency` none (reading 2026-10-22, Home and Pro), so on a fresh computer the action ends with `Failed` and points to the "System Protection" item.

## Detection and undo
Not applicable: `kind=action` has no detection or undo. The evidence of the effect is the list of points (the service returns `Created` only when the new point is in the list). The created point is deleted manually in the "System Protection" window (Configure → Delete).

## Risks and warnings
- `risk=safe`: a restore point doesn't change anything in the system apart from taking up space within the System Protection limit.
- Requires System Protection to be turned on for C:.
- The point doesn't cover personal files; Windows may delete the oldest points when the allotted space runs out.

## VM test
Cases `repair-restore-point-disabled` (protection turned off: `Failed` with a message pointing to "System Protection") and `repair-restore-point-created` (protection turned on in `Setup` with `Enable-ComputerRestore -Drive 'C:\'`, `Check`: the point "DSOptimizer – punkt ręczny" (DSOptimizer – manual restore point) in the list) added to `tests/vm/cases.psd1` in Task 7.

VM run (Task 10, 2026-10-22, full `tests/vm/Invoke-VmTests.ps1` on the `etap4b-1` branch; reports outside git): Home and Pro 722 passed / 0 failed / 25 skipped each (6 cases with `Skip` outside the Repairs category). `repair-restore-point-disabled`: **Passed** on Home and Pro (`Failed`, code 1, "Ochrona systemu na dysku C: jest wyłączona – włącz ją pozycją „Ochrona systemu” w tej kategorii." (System Protection is turned off for drive C: – turn it on with the "System Protection" item in this category.), no point was created). `repair-restore-point-created`: **Passed** on Home and Pro (`Executed`, "Utworzono punkt przywracania „DSOptimizer – punkt ręczny” (…)" (Created restore point "DSOptimizer – manual restore point" (…)), the point is in the `Get-ComputerRestorePoint` list).

## Observed effect
Method: creating a point through the same service (`IRestorePointService`) and reading `Get-ComputerRestorePoint` and `vssadmin list shadows`; raw results outside git. The manual action calls the same service with a different description, so the measurement covers the service, not the handler itself.
- **Home** (`DSO-26H2-Home`, 26300.9457), 2026-10-22: after protection is turned on, the point is created and is visible in the list and as a shadow copy in `vssadmin list shadows`; with protection turned off, a point can't be created (WMI returns a disabled-service error, which the service maps to `ProtectionDisabled`).
- **Pro** (`DSO-26H2-Pro`, 26300.9457), 2026-10-22: a point through `DSOptimizer.exe --apply vmtest.hklm-value --restore-point required` = `Created` ("DSOptimizer – przed vmtest.hklm-value" (DSOptimizer – before vmtest.hklm-value)); the behavior with protection turned off is the same as on Home.

## Reddit
To be added in part E.
