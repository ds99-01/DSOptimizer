<!-- source: docs/tweaks/tools.restart-explorer.md sha256: 316eb539d0d51195cce962648c7d9fce658c184ad5318bef2a39b35d5225f5de -->

# tools.restart-explorer – Restart the taskbar and File Explorer

## Verdict for 26H2
Port as `kind=action`, `risk=safe` (v3.2 alias `tool_restart_explorer`): on 26H2 Home and Pro VMs, stopping the shell with `Stop-Process -Force` ends with winlogon restarting it in no more than 1.42 s, with the user's token (`Limited`), so the item doesn't repeat the v3.2 bug (the program started `explorer.exe` itself and got an elevated File Explorer).

## What it changes
It doesn't write the registry, services or files; it stops all File Explorer shells of the signed-in user (SID `$UserSid`) in that user's session and checks whether a new one has come back. The script (`script.apply`) works only through three helpers defined in the script (the tests mock them):
1. `Get-DsoAutoRestartShell` – only READS `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon`, value `AutoRestartShell` (64-bit registry view). A value other than 1 (including 0) or a missing value: `Failed` ("AutoRestartShell = … – winlogon won't restart File Explorer; nothing was stopped"), without stopping anything. The action never writes this value.
2. `Get-DsoUserShellProcess` – `explorer.exe` processes (Win32_Process) whose owner has a SID equal to `$UserSid` (the SID of the signed-in user passed to the script: the owner of `explorer.exe` in the active console session, according to `InteractiveUserContext`; this doesn't have to be the account that started DSOptimizer, when the program was elevated with another admin account) and the `CommandLine` field. The script keeps only the shell: a command line WITHOUT `/factory`. The `explorer.exe /factory,{…} -Embedding` processes (parent `svchost.exe`) are window hosts, not the shell: stopping them doesn't bring anything back, so they are never stopped. No shell: `Failed` ("no File Explorer of user …; nothing was stopped" + the Task Manager instruction). The `AutoRestartShell` value must be a DWORD equal to 1 (REG_SZ "1", QWORD and BINARY are rejected, with the type given).
3. `Stop-DsoShellProcess` – `Stop-Process -Id <PID> -Force` (measured on the VM). `taskkill` isn't used (it wasn't measured on the shell).

Flow: read `AutoRestartShell` → select the `$UserSid` shells without `/factory` → determine the session (all shells found must be in one session, otherwise `Failed` without stopping anything: it's unclear which one to restart) → stop all shells of this account found in this session (usually one; if several were found, "the right one" can't be identified reliably, so stopping all of them is a safeguard, not a measured scenario) → a loop every 0.5 s, with a 10 s limit measured by a clock (Stopwatch), not by the number of attempts; an error reading processes while waiting (for example the process disappeared during the CIM query) means "not there yet" and goes into the `Failed` message: success = a shell of the same SID in the same session (without `/factory`) with a PID different from the stopped ones → `Executed` "File Explorer restarted by winlogon: PID a → b". No new shell within 10 s: `Failed` with Microsoft's instruction "Task Manager > Run new task > explorer.exe" (this is a manual step; the program doesn't start anything). The item's `timeoutSeconds` = 30 (longer than the 10 s limit).

The script NEVER starts `explorer.exe` (neither `Start-Process` nor `&`): the elevated File Explorer in v3.2 came precisely from the program's process. The harness test guards this with tripwires (`Start-Process`, `explorer`, `taskkill` throw on an unmocked call).

## Sources
- MS archive (Windows Server 2003 Technical Reference, "AutoRestartShell: Core Services", ms.date 2009-10-08, `is_archived`), accessed 2026-10-19: https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-server-2003/cc776814(v=ws.10) – key `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon`, `REG_DWORD`, range 0 or 1, default 1: "Specifies whether the Windows user interface (typically, Explorer.exe) restarts automatically if it stops unexpectedly."; 0 – the user has to start the interface themselves (Task Manager > Run new task). This applies to Windows Server 2003; there is no MS Learn page for Windows 11/26H2 (Ruling R7), so the effect on 26H2 relies on the VM reading below.
- Reading from 26H2 VMs (build 26300.9457), Home and Pro, 2026-10-19 (C5 exploration, `tests/vm/explore/Invoke-C5Exploration.ps1`; research: `docs/zrodla/2026-10-19-partia-c5-zrodla.md` (in Polish), "Wyniki VM" (VM results), questions 5–7):
  - `AutoRestartShell` = DWORD 1 (Home and Pro); the "missing value" case didn't occur, and the behavior with a missing value wasn't measured (hence the conservative `Failed`).
  - `Stop-Process -Force` on the shell (`C:\Windows\Explorer.EXE`) of user `dso`: new shell after 136–1,416 ms (loop resolution 500 ms), parent `userinit.exe`, same session (1), owner `dso`, token `Limited` (not elevated).
  - Besides the shell, the `dso` account has several `C:\Windows\explorer.exe /factory,{5BD95610-9434-43C2-886C-57852CC8A120} -Embedding` processes (parent `svchost.exe`). Choosing "the oldest process" failed in a run, and stopping a `/factory` process didn't bring the shell back – that's why the criterion is a command line without `/factory`.
  - v3.2 (`Start-Process C:\Windows\explorer.exe` from an admin process): in session 0 a separate, elevated File Explorer of the Administrator appeared; in the user's session the started process ends with code 1 and hands the window over to the existing shell.
- Design decision R18 (2026-10-19).

## Defaults on a fresh 26H2
`AutoRestartShell` = 1 (DWORD) in `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon` on 26H2 Home and Pro VMs (read 2026-10-19). User shell: one `C:\Windows\Explorer.EXE` process in the user's session, with `/factory` processes beside it.

## Detection and undo
Not applicable: `kind=action` has no detection or undo (nothing is written, there is no journal). The evidence of the effect is a new shell (different PID) of the same account in the same session, checked in the script; exit code 0 without it isn't possible.

## Risks and warnings
- `risk=safe`: it writes nothing; the side effects are that File Explorer windows hosted in the shell may close (copy operations in them interrupted; `CabinetWClass` windows are sometimes in `/factory` processes, which the action doesn't stop – VM data) and that the taskbar and desktop disappear for a few seconds (measured at up to about 1.4 s).
- The action doesn't change `AutoRestartShell`; with value 0 or a missing value it refuses to stop anything (an empty desktop with no automatic return would be worse than no action).
- Token: the VM measurement showed that the shell restarted by winlogon has the user account's `Limited` token, so the item doesn't need a warning about an elevated token (unlike v3.2).
- A manual "End task" in Task Manager on "Windows Explorer" did NOT bring the desktop back (Dawid's click, 2026-10-19, Pro VM). This is a different mechanism from the `Stop-Process -Force` used in the action; the data doesn't explain why, so this document doesn't promise a return after ending it by hand from Task Manager. When the shell doesn't come back (also after the action): Task Manager > Run new task > `explorer.exe`.
- On slower hardware the return may take longer than the 10 s limit: the action then reports `Failed`, although the shell may come back a moment later (the message with the instruction softens this).

## VM test
VM run `20261019-110405` (2026-10-19, HEAD `fd320ed`, build 26300.9457): Home (`DSO-26H2-Home`) and Pro (`DSO-26H2-Pro`), `tests/vm/Invoke-VmTests.ps1`. Reports are local, outside git. Whole run: Home 401 passed / 0 failed / 29 skipped, Pro 401 / 0 / 29 (`main` 313/0/29 + Reboot runs); in the C5 cases `CleanupError` is empty.
- `tools-restart-explorer` (`Flow=Execute`, `Catalog=BuiltIn`): **Passed** on Home and Pro (`Executed`). `Setup` writes the PID of the only `dso` shell (without `/factory`) and the session to `C:\dso\restart-explorer-pid.txt` (it throws when the number of shells isn't 1); `Check` in a loop (15 s, every 0.5 s) looks for a `dso` shell without `/factory` with a different PID in the same session; `Cleanup` deletes the file.
- K2 exploration (R18): `Stop-Process -Force` on the `dso` shell → new shell within max. 1.42 s, parent `userinit.exe`, session 1, token `Limited`; `AutoRestartShell` = DWORD 1. The runner can't see the desktop, so the visual return of the taskbar and desktop isn't part of the Check. Research and K2 exploration results: `docs/zrodla/2026-10-19-partia-c5-zrodla.md` (in Polish) (sections "Wyniki VM" (VM results), "Sonda uzupełniająca" (follow-up probe)).
- Dawid's manual click (Pro VM, `dso` session, 2026-10-19): Task Manager > "Eksplorator Windows" (Windows Explorer) > "Zakończ zadanie" (End task) **didn't bring back** the taskbar or the desktop ("Uruchom nowe zadanie" (Run new task) > `explorer.exe` was needed). This is a different mechanism from the `Stop-Process -Force` in the action; this document doesn't promise a return after ending it by hand from Task Manager (see "Risks and warnings").
- **Smoke test with the real `DSOptimizer.exe` in the `dso` session: to be confirmed by Dawid** (Ruling R30). Not done, because Dawid was away; the item goes into `main` with this caveat (like D2 in round C4).
- The smoke test also includes a visual check that the taskbar and desktop come back after the action.

## Reddit
To be added in part E.
