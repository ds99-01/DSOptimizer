<!-- source: docs/tweaks/repair.wu-reset.md sha256: 5bfc6fc1678d8da3745e2d81167e2ab1739207847f1e3e3303e6cded1a1c4a64 -->

# repair.wu-reset – Reset Windows Update

## Verdict for 26H2
Port as `kind=action`, `risk=caution` ("Repairs" category, Stage 4b batch 1, C# handler `wu-reset`): on 26H2 Home and Pro the Windows Update services can be stopped and restored, both working folders (`SoftwareDistribution`, `catroot2`) can be moved, and Windows creates new ones right away. The effect is visible in Settings (update history cleared, updates download again and install).

## What it changes
C# handler (`src/DSOptimizer.Core/Repair/WindowsUpdateReset.cs`), without a PowerShell script. Steps:
1. Reading the services `wuauserv`, `bits`, `cryptsvc` (`IServiceManager.Query`). A missing service = skipped with a warning; access denied or a read error = `Failed` "… Nothing was changed." before any changes.
2. Stopping the running ones, in this order, with a 30 s limit each: `wuauserv` and `bits` through `StopService`, `cryptsvc` through `StopServiceWithDependents` together with the running dependent services read from the system (`GetRunningDependents` = `EnumDependentServices`; on a fresh 26H2: `AppIDSvc` and the `applockerfltr` driver). The program remembers which ones were running. `UsoSvc` and `msiserver` aren't stopped (Restart Manager: they don't hold either folder, and stopping them changes nothing). A stop failure = the stopped services are started again and `Failed` "Couldn't stop service <name>: … Nothing was changed."
3. Moving `%SystemRoot%\SoftwareDistribution`, then `%SystemRoot%\System32\catroot2` to `<folder>.dso-<yyyyMMdd-HHmmss>` (local time from `IClock`; a name collision = `-2`, `-3`…). A missing folder = step skipped with a note (on a fresh Home, `SoftwareDistribution` doesn't exist until `wuauserv` starts). A failure of the second move = the first move is undone, the services are restored, `Failed` with the name of the blocking folder; a failure to undo goes to `NotRestored` ("Folder … is now named …. Windows may have already created a new folder … – move or delete it first, then restore the name. The next reset in DSOptimizer will delete the copy … as an older one.").
4. Starting the stopped services in reverse order: `cryptsvc`, its dependents (in start order), `bits`, `wuauserv`. Whether a service counts as restored is decided by reading its state (`Query`), not by the `Start` code: a driver that started on its own together with `AppIDSvc` counts as restored. A service that isn't running after the start = `NotRestored` and `Failed`.
5. Policy diagnosis (read-only): the values `WUServer`, `WUStatusServer`, `DisableWindowsUpdateAccess`, `SetDisableUXWUAccess` in `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate` and `NoAutoUpdate`, `UseWUServer` in the `AU` subkey, if they exist, go into the result as "Policy: <name> = <value>" + "They may be the cause of the problem; the item "Reset Windows Update and remove blocks" removes them (with a backup)." Nothing is changed.
6. Verification: the services that were running before the reset are running, the copies from this run exist. A mismatch = `Failed` with a list. Success = `Executed` with the list of steps, the paths of the copies and a warning about the update history.
7. Only after success: deleting older copies `<folder>.dso-*` next to the original (`ListSiblingDirectories`, only names matching the pattern `<folder>.dso-yyyyMMdd-HHmmss[-N]`, without the copies from this run); the newest one stays. A deletion failure = a warning.

Services protected by the system: `msiserver`, `WaaSMedicSvc`, `DoSvc` are never changed; the result mentions one of them only when it's disabled (`Disabled`).

## Sources
- Test on 26H2 Home and Pro VMs (build 26300.9457), 2026-10-22, runs H1, N1, P1: `docs/zrodla/2026-10-22-etap4b-1-pomiary.md` (in Polish), section 3 (3a, 3b, 3h), decision table items 3 and 3h.
- No MS documentation checked in this task (the pages about resetting Windows Update components weren't fetched); evidence: the VM test described below.

## Defaults on a fresh 26H2
Reading 2026-10-22 (Home and Pro): `wuauserv` Manual, `bits` AutomaticDelayed, `cryptsvc` Automatic, `UsoSvc` AutomaticDelayed, `msiserver` Manual, `WaaSMedicSvc` Manual; `DoSvc` Home Manual, Pro AutomaticDelayed. Dependents of `cryptsvc`: running `AppIDSvc` and the `applockerfltr` driver, not running `IsolationSession`, `IsoEnvBroker`. `SoftwareDistribution` on a fresh Home: none (created when `wuauserv` starts), on Pro it exists. Windows Update policies: key missing.

## Detection and undo
Not applicable: `kind=action` has no detection or undo (no journal). Trace: the log file and the result with the list of steps. The old folders stay as `.dso-*` copies (paths in the result), so going back manually is possible with the services stopped.

## Risks and warnings
- `risk=caution`: for a few seconds it stops the Windows Update and BITS services and Cryptographic Services (with their dependents); for the duration of the reset (about 1 s) AppLocker doesn't enforce rules – irrelevant on a private computer without AppLocker.
- Update history in Settings will be cleared (only Microsoft Defender definition entries may stay); updates will download again.
- The folder copies stay next to the original (the newest one); paths in the result.
- Windows Update policies are only listed, not changed (`repair.wu-reset-full` removes them).

## VM test
Case `repair-wu-reset` (`Flow=Execute`): `Setup` creates an old copy `C:\Windows\SoftwareDistribution.dso-20000101-000000`; `Check` requires that `wuauserv`, `bits`, `cryptsvc` are running, that exactly one `SoftwareDistribution.dso-*` copy and exactly one `catroot2.dso-*` copy exist, and that the old copy is gone. Added to `tests/vm/cases.psd1` in Task 6. In Task 10, `Check` also compares the state of `AppIDSvc` (`Win32_Service`) and the `applockerfltr` driver (`Win32_SystemDriver`) with the state saved in `Setup` (R8: the dependents return to their previous state).

VM run (Task 10, 2026-10-22, full `tests/vm/Invoke-VmTests.ps1` on the `etap4b-1` branch; reports outside git): Home and Pro 722 passed / 0 failed / 25 skipped each (6 cases with `Skip` outside the Repairs category). `repair-wu-reset`: **Passed** on Home and Pro (`Executed`; `wuauserv`, `bits`, `cryptsvc` stopped together with `applockerfltr` and `AppIDSvc`, folders moved to `.dso-<marker>`, the old copy deleted, the services and dependents back in their previous state).

## Observed effect
Method: an exploration probe (`tests/vm/explore/Invoke-E4B1Exploration.ps1`), stopping services through `ControlService` and `ServiceController.Stop()`, Restart Manager for both folders, moving the folders, reading the files after the services start, the update history API and the Settings page (screenshots in the raw results outside git).
- **Home** (`DSO-26H2-Home`, 26300.9457, no network), 2026-10-22: `cryptsvc` through `ControlService(STOP)` = code 1051 (`AppIDSvc` and `applockerfltr` are running); `ServiceController.Stop()` stops all three in about 0.5 s, after which `catroot2` can be moved. `SoftwareDistribution` doesn't exist until `wuauserv` starts. After the services start, the new `catroot2` (39 `edb*` database files) is there at second 0; all services return to their previous state (`applockerfltr` starts on its own together with `AppIDSvc`, and starting it explicitly reports "już działa" (already running)). History without a network: empty before and after.
- **Pro** (`DSO-26H2-Pro`, 26300.9457), 2026-10-22: stopping and moving the same as Home (P1, N1); the new `SoftwareDistribution` (DataStore, Download, ReportingEvents.log) is there at second 0 after `wuauserv` starts. With a network (N1): before the reset 2 history entries (a driver + Defender definitions); after the reset the history API returns 0, the driver entry disappears in Settings, "Aktualizacje definicji (1)" (Definition updates (1)) stays. After the network was connected, WU installed 2 updates on its own (result Succeeded), the Windows Update page without an error.

## Reddit
To be added in part E.
