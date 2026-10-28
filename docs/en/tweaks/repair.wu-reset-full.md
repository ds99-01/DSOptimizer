<!-- source: docs/tweaks/repair.wu-reset-full.md sha256: 6a135a4dd12bc74d79d34b55a90ecbff2aa52057d60cb2d52bf87cdac46aed65 -->

# repair.wu-reset-full – Reset Windows Update and remove blocks

## Verdict for 26H2
Port as `kind=action`, `risk=experimental` ("Repairs" category, Stage 4b batch 1, C# handler `wu-reset-full`): everything from `repair.wu-reset` plus exporting and removing the Windows Update policies and restoring the startup types of the WU services to the values of a fresh 26H2. On Pro, the full set of test policies makes Settings say that your organization manages the Windows Update settings, with an inactive button, and after the key is removed the message disappears.

## What it changes
C# handler (`src/DSOptimizer.Core/Repair/WindowsUpdateReset.cs`, `full` variant). Steps on top of `repair.wu-reset`:
1. First the refusal: `IDomainInfo.IsDomainJoined()` (`Win32_ComputerSystem.PartOfDomain`) = `true` → `Refused` "The computer is joined to a domain: Windows Update policies are set by the domain administrator."; `null` → `Refused` "Couldn't check whether the computer is joined to a domain." Nothing is changed.
2. Reading the services also covers `UsoSvc` (startup type for step 5; `UsoSvc` isn't stopped).
3. Backup of the policies, before any change (R13): when the key `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate` exists – `%SystemRoot%\System32\reg.exe export "<key>" "<backups>\wu-policies-<yyyyMMdd-HHmmss>.reg" /y` (`System32ToolRunner`, the export includes the `AU` subkey) and a check that the file size is > 0. `<backups>` = `%ProgramData%\DSOptimizer\backups`, created with a trusted ACL at the first backup (`SecureDirectory.Ensure`), not at program startup. An unreadable key, an export with code ≠ 0, an empty file or an inaccessible backup folder → `Failed` "… the policies stay unchanged. Nothing was changed." (services and folders untouched). Key missing → no file, the line "No Windows Update policies."
4. Stopping the services and moving the folders as in `repair.wu-reset` (all or nothing). When this stage fails, the policies stay, and the `.reg` file stays in `<backups>` (path in the result).
5. Removing the policies, after the folders are moved and before the services start: `IRegistry.DeleteKeyTree` of the key with its subkeys (only when the backup was created), with verification (the key is gone). A deletion failure or the key still existing → `Failed` (after the services are restored) with the backup path and the hint `reg import "<file>"`.
6. Starting the services as in `repair.wu-reset`.
7. Startup types (only when everything above succeeded): writing `<backups>\wu-services-<yyyyMMdd-HHmmss>.txt` (UTF-8 with BOM, trusted ACL; the name and the previous startup type, and the `sc.exe config` hint), then `IServiceManager.SetStartType` only where the type differs from the table below, with verification. A failed write of the backup = a warning and the startup types unchanged; a refused write of a type or a mismatch on reading = a warning with the service name, without failing the whole repair.
8. A fixed warning in the result: "On a computer managed by Intune/MDM, the removed policies may come back at the next sync." (there's no MDM detection in this batch).

When the run stops after step 5 but before step 7 – there was no folder to move, or the services couldn't be restarted – the result is `Failed`, and the heading ends with the sentences "Windows Update policies have already been removed (backup: <file>; to restore: reg import "<file>")." (only when there were policies and they were removed) and "Service startup types weren't changed."; the MDM warning is in the result then too.

Table of startup types (the same on Home and Pro, R10):

| Service | Startup type after the reset |
|---|---|
| `wuauserv` | Manual |
| `bits` | AutomaticDelayed |
| `cryptsvc` | Automatic |
| `UsoSvc` | AutomaticDelayed |

`msiserver`, `WaaSMedicSvc` and `DoSvc` are never changed: writing the startup type ends with refusal 5 even for the Administrator (measurement), and `DoSvc` has different values on Home and Pro. The result mentions one of them only when it's disabled (`Disabled`). No DLL registration (`regsvr32`), no modules from outside the system, no touching `WaaSMedicSvc`.

## Sources
- Test on 26H2 Home and Pro VMs (build 26300.9457), 2026-10-22: `docs/zrodla/2026-10-22-etap4b-1-pomiary.md` (in Polish), decision table items 3, 3h and 4, sections 3 and 4 (policies: test write, the Settings view with all of them and with each one separately, `reg export`/`delete`/`import`).
- No MS documentation checked in this task (the Windows Update policy pages weren't fetched); evidence: the VM test described below. The effect of the policies on the scan itself (offline) wasn't measured, so the diagnosis and the description don't promise that every value blocks updates.

## Defaults on a fresh 26H2
Reading 2026-10-22: the key `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate` – none (Home and Pro). Startup types: as in the table above (Home and Pro), `msiserver` Manual, `WaaSMedicSvc` Manual, `DoSvc` Home Manual / Pro AutomaticDelayed. `PartOfDomain` = False on both VMs.

## Detection and undo
Not applicable: `kind=action` without detection and undo (no journal). Going back manually: `reg import "<backups>\wu-policies-<marker>.reg"` (measurement: restores both keys 1:1) and the startup types from `wu-services-<marker>.txt` (`sc.exe config <name> start= …`). The paths of both files are in the result.

## Risks and warnings
- `risk=experimental`: removes settings that block or delay updates, which the owner or another "optimizing" tool may have set on purpose; `.reg` backup in `%ProgramData%\DSOptimizer\backups`.
- Changes the startup types of `wuauserv`, `bits`, `cryptsvc`, `UsoSvc` to the values of a fresh 26H2; the previous ones are in a text file next to the backup.
- Refuses in a domain; on a computer with Intune/MDM the policies may come back at the sync.
- All warnings of `repair.wu-reset` (update history, services stopped for a moment with their dependents, `.dso-*` folder copies).
- Update history in Settings will be cleared (only Microsoft Defender definition entries may stay); updates will download again.

## VM test
Case `repair-wu-reset-full` (`Flow=Execute`): `Setup` writes 6 test policies from the measurement (`WUServer`/`WUStatusServer` = `http://dso-wsus.invalid:8530`, `DisableWindowsUpdateAccess`/`SetDisableUXWUAccess` = 1, `AU\NoAutoUpdate` = 1, `AU\UseWUServer` = 1) and sets `UsoSvc` to `Disabled`; `Check` requires: the policy key is gone, the `wu-policies-*.reg` file in `C:\ProgramData\DSOptimizer\backups` contains `WUServer`, the startup types = the table above, `wuauserv`/`bits`/`cryptsvc` are running. `Cleanup` removes the policy key and the backup folder (the VM goes back to the checkpoint anyway). Added to `tests/vm/cases.psd1` in Task 6.

VM run (Task 10, 2026-10-22, full `tests/vm/Invoke-VmTests.ps1` on the `etap4b-1` branch; reports outside git): Home and Pro 722 passed / 0 failed / 25 skipped each (6 cases with `Skip` outside the Repairs category). `repair-wu-reset-full`: **Passed** on Home and Pro (`Executed`; `wu-policies-*.reg` backup with `WUServer`, `wu-services-*.txt`, the policy key removed, the startup types as on a fresh 26H2, the services running). Note: in the first run of the Repairs cases only (`repair-1`), on Home `bits` once didn't start again (`Failed` "Nie udało się ponownie uruchomić usług: bits." (Couldn't restart services: bits.), `NotRestored`: "Nie można uruchomić usługi bits" (Cannot start service bits)) – an honest result, but the cause wasn't found: in 5 further attempts (also right after `repair.wu-reset`, as in that run) and in 2 further runs `bits` started; to be watched.

## Observed effect
Method: an exploration probe (`tests/vm/explore/Invoke-E4B1Exploration.ps1`): writing the 6 policies together and each one separately, a screenshot of the Windows Update page in Settings (the organization message, the state of the "Sprawdź aktualizacje" (Check for updates) button), `reg export` / `reg delete` / `reg import` with a comparison of the values, `ChangeServiceConfig` for each WU service; raw results outside git.
- **Home** (`DSO-26H2-Home`, 26300.9457), 2026-10-22: 6 policies together and each one separately – no message in Settings, "Sprawdź aktualizacje" (Check for updates) active (no effect visible in the UI; the effect on an offline scan can't be measured). `reg export` of the key also writes `AU` (one file), `reg delete` removes everything, `reg import` restores 1:1. `ChangeServiceConfig`: `wuauserv`, `bits`, `cryptsvc`, `UsoSvc` OK; `msiserver`, `WaaSMedicSvc`, `DoSvc` refusal 5. Reset of the services and folders as in `repair.wu-reset`.
- **Pro** (`DSO-26H2-Pro`, 26300.9457), 2026-10-22: 6 policies together → "Ustawieniami usługi Windows Update zarządza Twoja organizacja." (Your organization manages the Windows Update settings.) and an inactive button; separately, only `SetDisableUXWUAccess` gives the message and the block, the other 5 have no visible effect. After the key is removed the message disappears, `reg import` restores the state 1:1. `ChangeServiceConfig` as on Home (`wuauserv` Disabled and back – WaaSMedic doesn't restore it within 10 s). Reset of the services and folders and the update history as in `repair.wu-reset` (N1).

## Reddit
To be added in part E.
