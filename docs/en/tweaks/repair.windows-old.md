<!-- source: docs/tweaks/repair.windows-old.md sha256: fb655e905fe7b481793cb6a549fe23dc8e75265f81271cd7f98fdf9258b654b8 -->

# repair.windows-old – Remove the previous Windows installation

## Verdict for 26H2
Port as `kind=action`, `risk=caution` ("Repairs" category, Stage 4b batch 2, a long repair with a progress window). Mechanism
measured on 26H2 Home and Pro VMs (`docs/zrodla/2026-10-22-etap4b-2-pomiary.md` (in Polish), items 5, 5f and 6): the
"Previous Installations" handler of Disk Cleanup (`cleanmgr /sagerun`) recognizes a prepared `C:\Windows.old` folder and deletes it.
Only mechanism A – no taking ownership and no ACL changes in C# (Dawid's decision R8, Ruling R18). The prepared folder is ordinary
files owned by `BUILTIN\Administratorzy`; a real `Windows.old` after a feature update has files with a `TrustedInstaller` ACL and
thousands of items – this scenario is **to be confirmed on hardware**.

## What it changes
C# handler `windows-old` (`WindowsOldHandler`), without a catalog script:
1. The `ServicingGuard` guard: a running `dism`, `sfc` or `cleanmgr` process = `Refused` "The previous servicing operation is still
   in progress – try again in a moment.", nothing starts.
2. No `<SystemDrive>\Windows.old` (the drive with `%SystemRoot%`) = `NotSupported` "There's no previous Windows installation – nothing
   to remove.", without any step (the registry isn't even read). Whether the folder exists is checked by `IRepairFileSystem.ProbeDirectory`
   (`File.GetAttributes`), not by `Directory.Exists`: "doesn't exist" only with `ERROR_FILE_NOT_FOUND`/`ERROR_PATH_NOT_FOUND`, any other error
   (access denied, folder being deleted, drive not ready) = `Failed` "Couldn't check folder … – nothing was changed."
   (the result is certain: nothing was changed or started). Then the free space of the system drive (`IDiskSpace`).
3. Checking the Disk Cleanup settings before any write: the list of subkeys of
   `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches` (`IRegistry.SubKeyNames`, 64-bit view) and reading the
   `StateFlags7351` value under each one. An existing value (someone else's `cleanmgr /sageset:7351` or an earlier interrupted run), a read error
   or a missing `VolumeCaches\Previous Installations` key = `Failed` "… – nothing was changed."; we don't write or start
   anything. The number `WindowsOldHandler.SageRunId = 7351` comes from measurement 5f (on a fresh 26H2 there's no `StateFlags*` value at all
   under the 31 handlers; `cleanmgr` accepts 0–9999).
4. Once confirmed, the run can't be stopped (Ruling R30): both progress events have `ActionProgress.Stoppable = false`, so
   `ActionRun.CanStop` is `false` during the whole run and the window doesn't offer "Stop" (test `GuiModel_CanStopFalseThroughoutRun`).
   The handler doesn't read the cancellation token; a token canceled before the start is handled by the engine (`TweakEngine.RunHandler`: "Canceled
   before starting"), and the CLI has no stop. You can back out only in the confirmation window.
5. Step 1 of 2 "Disk Cleanup": `StateFlags7351` = DWORD `2` (selected) **only** under `VolumeCaches\Previous Installations`,
   then `%SystemRoot%\System32\cleanmgr.exe /sagerun:7351` through `IServicingToolRunner` (`CreateProcessW` with the full path, outside the Job
   Object, never killed, limit = `timeoutSeconds` = 2700 s). The program waits for the process to end (Task 2 decision: the folder disappears within a few
   seconds, but in the Task 2 measurement cleanmgr with `DismHost` kept working for about 11 more min; whether removing the flag before the process ends
   changes the result – not measured). In the handler runs (Task 8) cleanmgr ended on its own after 7 s – "Observed effect" section. In `finally`, its own `StateFlags7351` value is removed – also after a start error, an exception and the time limit. A failed
   selection = `Failed`, cleanmgr doesn't start. A failed removal = `Failed` with an entry in "Not restored" (otherwise the next
   run would refuse because of item 3).
6. Step 2 of 2 "Check" (verification): the free space after, and whether the folder exists.
   - the folder doesn't exist = `Executed` "Removed the previous Windows installation. Freed: <DiskSpaceText.Freed>." (a cleanmgr code ≠ 0
     is added as a note; MS doesn't document the cleanmgr codes);
   - the folder exists = `Failed` "The Windows.old folder still exists (freed: …).";
   - an error checking the folder (`ProbeDirectory` throws) = `NotConfirmed` "Disk Cleanup finished, but checking whether folder … is gone
     failed (<error>) – check it manually"; the note about the cleanmgr code stays, and a flag that wasn't removed still gives
     `Failed` with an entry in "Not restored" (Ruling R27);
   - after the limit = `NotConfirmed` "Cleanup is still running in the background – the program stopped waiting after 45 min." + the folder state ("still
     exists – check it later", "no longer exists (freed: …)" or "couldn't check folder … (<error>)"); cleanmgr
     keeps working.

The handler doesn't delete anything itself, doesn't take ownership and doesn't change ACLs (test `NeverDeletesFilesItself`).

## Sources
- Test on 26H2 Home and Pro VMs (build 26300.9457, pl-PL), 2026-10-22: `docs/zrodla/2026-10-22-etap4b-2-pomiary.md` (in Polish),
  items 5, 5f and 6, section 5 (script `tests/vm/explore/Invoke-E4B2Exploration.ps1`, `Invoke-E4B2WindowsOldProbe`).
- `cleanmgr` (`/sageset:n`, `/sagerun:n`, n = 0–9999): https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/cleanmgr
  (MS Learn, as cited in the measurement of 2026-10-22).
- `StateFlagsNNNN` under `VolumeCaches\<handler>` (0 = not selected, 2 = selected):
  https://learn.microsoft.com/en-us/troubleshoot/windows-server/backup-and-storage/automating-disk-cleanup-tool (MS Learn, as cited in the measurement).
- Windows.old and no way back after deletion ("this can't be undone (you won't be able to go back to your previous version of
  Windows)"; the system itself deletes the previous version 10 days after the update):
  https://support.microsoft.com/en-us/windows/delete-your-previous-version-of-windows-f8b26680-e083-c710-b757-7567d69dbb74
  (Microsoft Support, accessed 2026-10-23).

## Defaults on a fresh 26H2
`C:\Windows.old` doesn't exist (the item gives `NotSupported`). `VolumeCaches`: 31 handlers, including `Previous Installations`,
no `StateFlags*` value at all – reading 2026-10-22 (Home and Pro).

## Detection and undo
Not applicable: `kind=action` without detection and undo (`reversible` doesn't apply to actions). The verification is whether the folder exists after
cleanmgr finishes, and the free space before/after. A deleted previous installation can't be restored.

## Risks and warnings
- `risk=caution`: after deletion you can't go back to the previous version of Windows (Microsoft Support).
- Time: the folder disappears within a few seconds; Disk Cleanup finished after 7 s (handler runs, Task 8) or after about 11 min
  (measurement K2 of Task 2); with a real, large Windows.old – not measured. Wait limit 45 min, after it cleanmgr keeps working in the background.
- Once confirmed, it can't be stopped: "Stop" is inactive during the whole run (cleanmgr isn't interrupted).
- The `StateFlags7351` setting exists only for the duration of the run and concerns a single handler; someone else's value with this number =
  refusal without changes.
- The cleanmgr window in the user session: **not measured** (measurement K2 without a screenshot).
- From session 0 (PowerShell Direct, K1) cleanmgr deletes the folder but doesn't finish within 1200 s – a program started from the user session
  (GUI after UAC or a Task Scheduler task) doesn't have this problem (measurement K2). Running the handler from session 0 – not measured; according to
  measurement K1, the expected result is `NotConfirmed` after 45 min, with the folder already deleted.

## VM test
Cases in `tests/vm/cases.psd1` (Task 8), `Flow='Execute'`, built-in catalog, no network:
- `repair-windows-old-absent` (without `RunAs`): a fresh VM without `Windows.old` → `NotSupported`, code 1, message "nie ma czego usuwać" (nothing to remove);
  `Check`: the folder doesn't exist, no `StateFlags7351` value under `VolumeCaches`, cleanmgr isn't running.
- `repair-windows-old` (`RunAs='dso'`, CLI from a Task Scheduler task of the `dso` account – from PS Direct cleanmgr doesn't finish, measurement 5,
  Ruling R18), `CliTimeoutSeconds = 3000` (above the handler limit of 2700 s, so that a `NotConfirmed` result also reaches the report).
  `Setup`: an ordinary `C:\Windows.old` folder as in measurement 5 (test files in `Windows\System32`, `Windows\SysWOW64`,
  `Users\Public\Documents`, `Program Files\DsoTest`, `ProgramData\DsoTest`, owner Administratorzy (Administrators), no ACL change).
  `Check`: `C:\Windows.old` doesn't exist, no `StateFlags7351` under any `VolumeCaches\*`, cleanmgr isn't running.
  `Expect`: `Executed`, code 0, message "Usunięto poprzednią instalację Windows." (Removed the previous Windows installation.).

Results of the run: "Observed effect" section.

## Observed effect
Method: measurement of the mechanism (Task 2): a prepared 200 MB folder, `StateFlags7351` only under `Previous Installations`,
`cleanmgr /sagerun:7351`, samples every 5 s (folder exists, free space, processes), after the run a reading of all `VolumeCaches`.
- **Home** (`DSO-26H2-Home`, 26300.9457), 2026-10-22: K1 (PS Direct, session 0) – the folder disappears in ≤ 5 s, the space comes back (+208.8 MB),
  cleanmgr with `DismHost` lives until the 1200 s limit (killed); K2 (`dso` task, session 1) – the folder disappears in ≤ 5 s, cleanmgr ends on its own
  after 641 s with the result 0x0; after each run the flag is absent under all 31 keys.
- **Pro** (`DSO-26H2-Pro`, 26300.9457), 2026-10-22: K1 the same as Home; K2 – the folder disappears in ≤ 5 s, ends after 665 s, result 0x0.
- What `DismHost` does for about 11 min after the folder is deleted – not measured (the free space changes by less than 1 MB during that time).
- The handler itself (Task 8, 2026-10-23, `tests/vm/Invoke-VmTests.ps1 -CasesPath <only two cases>`, branch `etap4b-2`, no network,
  reports outside git): **Passed** on Home and Pro (12 passed / 0 failed each). `repair-windows-old-absent`: `NotSupported`, code 1,
  "Nie ma poprzedniej instalacji Windows – nie ma czego usuwać." (There's no previous Windows installation – nothing to remove.). `repair-windows-old` (CLI from a task of the `dso` account):
  `cleanmgr.exe /sagerun:7351` ended **with code 0 after 7 s** (Home and Pro), `Executed` "Usunięto poprzednią instalację Windows.
  Zwolniono: 158 MB." (Home) / "… 193 MB." (Pro) (Removed the previous Windows installation. Freed: 158 MB. / … 193 MB.); independent `Check` in the VM: the folder doesn't exist, no `StateFlags7351` under any
  `VolumeCaches\*`, cleanmgr isn't running. A second, diagnostic run (the same case, with `Check` additionally requiring no running
  `DismHost`): Passed on both, again code 0 after 7 s, freed 192 MB / 198 MB, and after cleanmgr exits `DismHost` isn't running – after
  it finishes, nothing keeps working in the background. Why the Task 2 measurement (K2: `Process` with `CreateNoWindow` in a Task Scheduler task) took
  641/665 s, while the handler run (`CreateProcessW` from DSOptimizer in a task of the `dso` account) took 7 s – **not measured**.
- A real `Windows.old` after a feature update (`TrustedInstaller` files, from a few to a dozen or so GB, the go-back option in Settings):
  **to be confirmed on hardware** – on a VM it can't be honestly recreated without changing ACLs (R8).

## Reddit
To be added in part E.
