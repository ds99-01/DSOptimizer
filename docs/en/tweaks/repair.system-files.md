<!-- source: docs/tweaks/repair.system-files.md sha256: 7583ab96852ed027e61e690eb57ab5ccaeb52a97048bc61dc728c62a832957d0 -->

# repair.system-files – Check and repair system files

## Verdict for 26H2
Port as `kind=action`, `risk=safe` ("Repairs" category, Stage 4b, round 2, a long repair with a progress window): `DISM /Online /Cleanup-Image /RestoreHealth`, followed by `sfc /scannow` – the order from the MS documentation. On a healthy 26H2 Home and Pro VM both tools end with code 0, and the result can be read honestly from `CBS.log` (measured). The verdicts "repaired", "couldn't repair some files" and "repair waiting for a restart" are **not measured** (Ruling R8: no deliberate damage to protected files on the VM) and are based on the MS documentation.

## What it changes
C# handler `system-files` (`SystemFilesHandler`), without a script:
1. The `ServicingGuard` guard: a running `dism`, `sfc` or `cleanmgr` process = `Refused` "The previous servicing operation is still in progress – try again in a moment.", nothing starts.
2. Step 1 of 2 "DISM": `%SystemRoot%\System32\dism.exe /Online /Cleanup-Image /RestoreHealth` through `IServicingToolRunner` (`ServicingToolRunner`: `CreateProcessW` with the full path, outside the Job Object, never killed, wait limit = `timeoutSeconds` = 2,700 s). The percentage from the bar (`[====  45.0%  ]`, OEM code page) goes to the progress window ("Step 1 of 2 · Next: SFC"). Before the start, the length of `%SystemRoot%\Logs\CBS\CBS.log` is read, and after the end, the segment from that offset (`ReadSharedFrom`, at most the last 16 MB).
3. DISM verdict (`DismOutcome`): code 0 + the "Checking System Update Readiness." block with `Total Detected Corruption` = 0 → healthy; > 0 and everything repaired → repaired; fewer repaired → error; code 0 without the block → "healthy or repaired". Code 3010 → restart. `0x800F081F`, `0x800F0906`, `0x800F0907` → "the repair files weren't found – an internet connection (Windows Update) or installation media is needed". Any other code (including `0x800F0950`, Ruling R13) → error with the code in hexadecimal and the path `%SystemRoot%\Logs\DISM\dism.log`. After a DISM failure, SFC starts anyway (spec 5.1).
4. Between the steps, "Stop" = a result with DISM only and the line "SFC skipped – stopped." (`NotConfirmed`, and after a DISM failure `Failed`; never `Executed` – Ruling R10).
5. Step 2 of 2 "SFC": `%SystemRoot%\System32\sfc.exe /scannow` the same way (UTF-16LE output without BOM, the percentage with a bare `\r`). The key `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending` is read before and after SFC.
6. SFC verdict (`SfcOutcome`) from the `[SR]` markers in the `CBS.log` segment of this run: `Cannot repair member file` → not everything repaired; `Repairing corrupted file … from store` / `Repaired file … by copying from backup` → repaired; a repair trace (these markers or `[SR] Repairing N components` with N > 0) together with a `RebootPending` key that didn't exist before SFC → repair after a restart; `[SR] Verify complete` without a repair trace and code 0 → no violations; anything else (no segment, no `[SR]`, code ≠ 0) → ambiguous.
7. Overall result: `Executed` only when DISM ∈ {healthy, repaired, healthy or repaired, restart} and SFC ∈ {no violations, repaired, repair after a restart}. DISM source missing / error or SFC "not everything" → `Failed`. SFC ambiguous → `NotConfirmed` with the `CBS.log` path. `RequiresReboot` with DISM 3010 or SFC "repair after a restart". Message: the DISM line, the SFC line and "Logs: %SystemRoot%\Logs\DISM\dism.log, %SystemRoot%\Logs\CBS\CBS.log.".
8. Time limit: DISM after 45 min → `NotConfirmed` "DISM is still running in the background after 45 min – the program stopped waiting. You can check the result in %SystemRoot%\Logs\DISM\dism.log.", SFC doesn't start (it would wait for DISM to finish anyway – measurement 3c). SFC after 45 min → `NotConfirmed` "SFC is still running in the background after 45 min – …" with the `CBS.log` path. The tool keeps working, the progress window moves on to the result, and the next long repair gets `Refused`.

The full sentences of the tools (without the progress bar lines) go to the program log, and all lines go to the action output.

## Sources
- Test on 26H2 Home and Pro VMs (build 26300.9457, pl-PL), 2026-10-22: `docs/zrodla/2026-10-22-etap4b-2-pomiary.md` (in Polish), items 1, 1z, 1b, 1n, 2, 2r, 2k, 3, 3c, 6 and sections 1, 2 and 6; samples of the output and of `CBS.log` segments in `tests/DSOptimizer.Tests.Unit/Repair/Samples/`.
- DISM, the DISM → SFC order and the SFC messages: https://support.microsoft.com/en-us/topic/use-the-system-file-checker-tool-to-repair-missing-or-corrupted-system-files-79aa86cb-ca52-166a-92a3-966e85d4094e (accessed 2026-10-22, MS Support).
- The `CBS.log` summary block and the source-missing codes: https://learn.microsoft.com/en-us/troubleshoot/windows-server/installing-updates-features-roles/fix-windows-update-errors (KB 947821), https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/configure-a-windows-repair-source (accessed 2026-10-22, MS Learn).
- `[SR]` markers: https://learn.microsoft.com/en-us/troubleshoot/windows-client/deployment/analyze-sfc-program-log-file-entries (KB 928228, accessed 2026-10-22, MS Learn).

## Defaults on a fresh 26H2
Component store healthy (`Total Detected Corruption: 0`), SFC without violations, the `RebootPending` key none – reading 2026-10-22, Home and Pro. The `dism`, `sfc`, `cleanmgr` processes don't run on an idle VM (baseline 4 × 36 samples).

## Detection and undo
Not applicable: `kind=action` without detection and undo. The verification of the effect is the exit codes and the `CBS.log` segments from this run (the DISM summary block and the SFC `[SR]` markers); without them the result is `NotConfirmed`, never "Done". The system file repair can't be undone – it restores the state from the component store.

## Risks and warnings
- `risk=safe`: DISM and SFC repair only from the component store and (DISM) Windows Update; they don't change settings.
- Time: usually 2–3 min on a VM, with Windows Update active DISM about 10 min, with real damage longer (not measured). Wait limit 45 min per tool; after it, the tool keeps working in the background.
- "Stop" works only between steps.
- Without an internet connection, DISM may report missing source files (not measured – a healthy store doesn't reach for a source).
- Known limitation of the guard: it doesn't detect updates being installed by Windows Update (`TiWorker` is excluded – false alarms); a second servicing tool in that situation waits for the first one to finish (measurement 3c), so the repair takes longer, but doesn't break.
- Closing the program after the time limit doesn't interrupt DISM or SFC (measured, R17 – "VM test" section); not measured: DSOptimizer started by another program inside a Job Object with `KILL_ON_JOB_CLOSE`.

## VM test
Case `repair-system-files` in `tests/vm/cases.psd1` (Task 6): `Flow='Execute'`, built-in catalog, `CliTimeoutSeconds = 1200`, `Expect`: `Executed`, code 0, `RequiresReboot = $false`; `Check`: `CBS.log` has an `[SR]` segment from this run (after the timestamp from before the start) and neither `dism.exe` nor `sfc.exe` is running in the VM. The case `repair-system-files-repaired` from the plan was dropped (R8: no damage to protected files).

VM run (Task 6, 2026-10-23, `tests/vm/Invoke-VmTests.ps1 -CasesPath <only this case>` on the `etap4b-2` branch; reports outside git): **Passed** on Home and Pro (7 passed / 0 failed each). Home: `Executed`, code 0, `requiresReboot=false`, CLI 146.5 s (DISM 87 s, SFC 60 s); Pro: the same, 149 s (DISM 85 s, SFC 63 s). Message on both: "DISM: magazyn składników jest zdrowy – nie wykryto uszkodzeń." (DISM: the component store is healthy – no corruption detected.) / "SFC: nie znaleziono naruszeń integralności plików systemowych." (SFC: didn't find any integrity violations in system files.) The run takes under 30 min, so the case is in the default run (without `Skip`).

R17 (2026-10-23, Home and Pro, `docs/zrodla/2026-10-22-etap4b-2-pomiary.md` (in Polish) section 7): after the wait limit, the CLI ends (`NotConfirmed`, code 4), and DISM and SFC with the closed pipe keep working and end with code 0 with the full result in `CBS.log` – both from PS Direct and from a Task Scheduler task of the `dso` account.

## Observed effect
Method: running both tools from the full System32 path with redirected output (like `ServicingToolRunner`), saving the output bytes and the `CBS.log` and `dism.log` segments from the offset before the start, process samples every 5 s (`tests/vm/explore/Invoke-E4B2Exploration.ps1`).
- **Home** (`DSO-26H2-Home`, 26300.9457), 2026-10-22: DISM `/RestoreHealth` without a network code 0 in 74.5 s, `CBS.log`: `Total Detected Corruption: 0`, `Operation result: 0x0`; SFC `/scannow` code 0 in 52.4 s, "Windows Resource Protection did not find any integrity violations.", 498 `[SR]` lines without repair markers (`Repairing 0 components`). No restart needed (`RebootPending` none).
- **Pro** (`DSO-26H2-Pro`, 26300.9457), 2026-10-22: DISM code 0 in 72.1 s, the same summary block; SFC code 0 in 53.9 s, 525 `[SR]` lines, the same templates.
- The handler itself (2026-10-23, Home and Pro): `DSOptimizer.exe --execute repair.system-files` = `Executed`, and an independent reading in the VM confirms the DISM summary block and the `[SR]` markers of this run in `CBS.log`, and that no `dism.exe`/`sfc.exe` is running after the CLI returns (case `repair-system-files`).
- Repair of a corrupted file, "couldn't repair" and "repair waiting for a restart": **not measured** (R8) – markers from KB 928228, codes from KB 947821.

## Reddit
To be added in part E.
