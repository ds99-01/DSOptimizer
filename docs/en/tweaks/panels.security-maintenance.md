<!-- source: docs/tweaks/panels.security-maintenance.md sha256: 7a6d3476b5ce7a334a985baa8fdd2fd7dccacd29d10358671f6edce5ae9d93ee -->

# panels.security-maintenance – Security and Maintenance

## Verdict for 26H2
Port as `kind=panel`, `risk=safe`: on the 26H2 Home and Pro VMs the command opens the classic Security and Maintenance window, not Settings. The item only starts a program and changes nothing.

## What it changes
Nothing: `kind=panel` opens a window and doesn't write to the registry, services or files. `launch` field: `file` = `control.exe`, `arguments` = `wscui.cpl`.
The launcher starts `C:\Windows\System32\control.exe wscui.cpl` through CreateProcess (the file only from System32, never from the current directory or `PATH`), outside the scripts' Job Object and without waiting for the window.

Rejected variants (not separate items; VM data in the "Sources" section):
- `wscui.cpl` through ShellExecute (WinUtil): the same window, but the launcher doesn't use ShellExecute. The full-path form (`control.exe "<System32>\wscui.cpl"`) wasn't measured – the catalog has the file name as the argument (measured).

## Sources
- WinUtil `config/feature.json` @ `144d516` (MIT, 2026-07-31): WPFPanelSecurity = "Security and Maintenance", `InvokeScript`: `wscui.cpl`. A hint for the list and the command, not evidence of the effect – https://raw.githubusercontent.com/ChrisTitusTech/winutil/144d51675190ffcb2553b7ba5313f8d602c8ecf9/config/feature.json
- Microsoft Learn: no page with a canonical name or command for this item in Windows 11 (research, sections 2.2 and 3.4: class B, WinUtil only); in general, MS only confirms that Control Panel exists for compatibility and that some settings are moving to Settings: https://support.microsoft.com/en-us/windows/system-configuration-tools-in-windows-f8a49657-b038-43b8-82d3-28bea0c5666b
- https://learn.microsoft.com/en-us/windows/win32/shell/controlpanel-canonical-names – MS "Canonical Names of Control Panel Items" lists only Action Center for this window (`Microsoft.ActionCenter`, "Windows 7, Windows 8, Windows 8.1"); it doesn't document a canonical name for Windows 11, so the description is based solely on the VM reading (research, the section with the table of canonical names).
- Reading from 26H2 VMs (build 26300.9457), Home (`DSO-26H2-Home`) and Pro (`DSO-26H2-Pro`), 2026-10-19, K1 = PS Direct as Administrator (session 0), K2 = elevated interactive task in the session of user `dso`: follow-up probe (CreateProcess `control.exe wscui.cpl`): the Security and Maintenance window (`CabinetWClass`) in a new `explorer.exe /factory` of the user; `control.exe` exits with code 1; in K1, `rundll32.exe` is running.
  - K1: after 10 s, `rundll32.exe` with the line `Shell32.dll,Control_RunDLL wscui.cpl` is running (session 0; K1 doesn't show the user's window, only that the process started).
  - K2: the Security and Maintenance window (`CabinetWClass`). Home = Pro in all fields except the PIDs.
- Research and results: `docs/zrodla/2026-10-19-partia-c5-zrodla.md` (in Polish), sections "Wyniki VM" (VM results) and "Sonda uzupełniająca" (follow-up probe).

## Defaults on a fresh 26H2
Not applicable: the item doesn't read or write any settings. The only system state after running it is the window's process (VM reading, 2026-10-19, above).

## Detection and undo
Not applicable: `kind=panel` has no detection and no undo. `Executed` only means that the process started; whether the window appeared is judged by the VM test (K2; smoke test with the real DSOptimizer.exe in the dso session – to be confirmed by Dawid (R30)).

## Risks and warnings
- `risk=safe`: the item changes nothing in the system; you close the window yourself (DSOptimizer doesn't wait for it and doesn't close it).
- Measured on a VM with Polish regional settings; the window title may differ in another culture.
- The scenario "DSOptimizer elevated with a different administrator account than the signed-in user" wasn't measured on a VM for this panel (the `control.exe`/`explorer.exe` host itself uses associations and COM in the elevated process; DSOptimizer controls only the path of the file it starts).

## VM test
VM run `20261019-110405` (2026-10-19, HEAD `fd320ed`, build 26300.9457): Home (`DSO-26H2-Home`) and Pro (`DSO-26H2-Pro`), `tests/vm/Invoke-VmTests.ps1`. Reports are local, outside git. Whole run: Home 401 passed / 0 errors / 29 skipped, Pro 401 / 0 / 29 (`main` 313/0/29 + Reboot runs); `CleanupError` empty in the C5 cases.
- `panels-security-maintenance` (`Flow=Execute`, `Catalog=BuiltIn`, `RestorePoint=skip`): **Passed** on Home and Pro (`ExecuteExit=0`, `Executed`). `Check` polls in a loop: `rundll32.exe` with a command line containing `wscui.cpl` is running; `Cleanup` closes only processes that match `Check`. **Check proves that the process from the command is running (K1, PS Direct as Administrator, session 0), not that the window appeared for the user.**
- K2 exploration (interactive task in the `dso` session, `Limited` token): the "Zabezpieczenia i konserwacja" (Security and Maintenance) window. Home = Pro. Research and K2 exploration results: `docs/zrodla/2026-10-19-partia-c5-zrodla.md` (in Polish), sections "Wyniki VM" (VM results) and "Sonda uzupełniająca" (follow-up probe).
- Dawid's manual click (Pro VM, `dso` session, Win+R, 2026-10-19): `wscui.cpl` opens the classic window.
- The clicked command (`wscui.cpl`) isn't the same as the item's `launch` (`control.exe wscui.cpl`): the click confirms the panel, while the item's `launch` is confirmed only by the K2 exploration.
- **Smoke test with the real `DSOptimizer.exe` in the `dso` session: to be confirmed by Dawid** (Ruling R30). Not done because Dawid was away; the item goes into `main` with this reservation (like D2 in batch C4). A mismatch between "the process is running" (Check) and a window visible to the user will show up only on first use from the app.

## Reddit
To be added in part E.
