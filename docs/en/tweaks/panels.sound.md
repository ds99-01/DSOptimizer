<!-- source: docs/tweaks/panels.sound.md sha256: 7ab1e159036d452adbdb2f5229c5474a1bebe6fdb62585decc8b1e5fed3cca8c -->

# panels.sound – Sound

## Verdict for 26H2
Port as `kind=panel`, `risk=safe`: on the 26H2 Home and Pro VMs the command opens the classic Sound window, not Settings. The window is confirmed by the VM reading; the manual Win+R got no response – to be confirmed: smoke test with the real DSOptimizer.exe in the dso session (not checked manually in the app).

## What it changes
Nothing: `kind=panel` opens a window and doesn't write to the registry, services or files. `launch` field: `file` = `control.exe`, `arguments` = `/name Microsoft.Sound`.
The launcher starts `C:\Windows\System32\control.exe /name Microsoft.Sound` through CreateProcess (the file only from System32, never from the current directory or `PATH`), outside the scripts' Job Object and without waiting for the window.

Rejected variants (not separate items; VM data in the "Sources" section):
- `mmsys.cpl` (WinUtil): the same window in the VM; the tie was resolved in favor of the MS canonical name.

## Sources
- WinUtil `config/feature.json` @ `144d516` (MIT, 2026-07-31): WPFPanelSound = "Sound Settings", `InvokeScript`: `mmsys.cpl`. A hint for the list and the command, not evidence of the effect – https://raw.githubusercontent.com/ChrisTitusTech/winutil/144d51675190ffcb2553b7ba5313f8d602c8ecf9/config/feature.json
- https://learn.microsoft.com/en-us/windows/win32/shell/controlpanel-canonical-names – MS "Canonical Names of Control Panel Items": `Microsoft.Sound` (`mmsys.cpl,-300`).
- Reading from 26H2 VMs (build 26300.9457), Home (`DSO-26H2-Home`) and Pro (`DSO-26H2-Pro`), 2026-10-19: the Sound window (`#32770`) in `rundll32.exe` (elevated); `control.exe` exits with code 1 even though the window is open. Discrepancy: with the manual Win+R, there was no response – to be confirmed: smoke test with the real DSOptimizer.exe in the dso session (not checked manually in the app).
  - The Administrator account over PowerShell Direct (session 0): after 10 s, `rundll32.exe` (`…\mmsys.cpl`) is running (this session doesn't show the user's window, only that the process started).
  - The `dso` user session (elevated task): the Sound window (`#32770`). Home = Pro in all fields except the PIDs.

## Defaults on a fresh 26H2
Not applicable: the item doesn't read or write any settings. The only system state after running it is the window's process (VM reading, 2026-10-19, above).

## Detection and undo
Not applicable: `kind=panel` has no detection and no undo. `Executed` only means that the process started; whether the window appeared is judged by the VM measurement in the `dso` user session (elevated task; smoke test with the real DSOptimizer.exe in the dso session – not checked manually in the app).

## Risks and warnings
- `risk=safe`: the item changes nothing in the system; you close the window yourself (DSOptimizer doesn't wait for it and doesn't close it).
- Measured on a VM with Polish regional settings; the window title may differ in another culture.
- The scenario "DSOptimizer elevated with a different administrator account than the signed-in user" wasn't measured on a VM for this panel (the `control.exe`/`explorer.exe` host itself uses associations and COM in the elevated process; DSOptimizer controls only the path of the file it starts).

## VM test
VM run of 2026-11-05 (build 26300.9457) on Windows 11 26H2 in English and Polish: `DSO-EN-Home` and `DSO-PL-Home` (Core), `DSO-EN-Pro` and `DSO-PL-Pro` (Professional), **Passed** on each: case `panels-sound`. Full run on each VM: 861 passed, 0 failed, 25 skipped. Earlier runs on Polish Windows below.

VM run `20261019-110405` (2026-10-19, build 26300.9457): Home (`DSO-26H2-Home`) and Pro (`DSO-26H2-Pro`), `tests/vm/Invoke-VmTests.ps1`. Reports are local, outside git. Whole run: Home 401 passed / 0 failed / 29 skipped, Pro 401 / 0 / 29 (`main` 313/0/29 + Reboot runs); `CleanupError` empty in each of the panel cases.
- `panels-sound` (`Flow=Execute`, `Catalog=BuiltIn`, `RestorePoint=skip`): **Passed** on Home and Pro (`ExecuteExit=0`, `Executed`). `Check` polls in a loop: `rundll32.exe` with a command line containing `mmsys.cpl` is running; `Cleanup` closes only processes that match `Check`. **Check proves that the process from the command is running (the Administrator account over PowerShell Direct, session 0), not that the window appeared for the user.**
- VM measurement in the `dso` user session (elevated task, `Limited` token): the "Dźwięk" (Sound) window (`#32770`, in an elevated `rundll32.exe`). Home = Pro.
- The manual click (Pro VM, `dso` session, Win+R, 2026-10-19): `mmsys.cpl` got no response - this item was skipped. So the "Dźwięk" (Sound) window is confirmed only by the VM reading in the `dso` user session (elevated task); the manual click neither confirms nor disproves this item. To be confirmed on first use from DSOptimizer.
- **Smoke test with the real `DSOptimizer.exe` in the `dso` session: not checked manually in the app**. Not done; the item goes into `main` with this caveat. A mismatch between "the process is running" (Check) and a window visible to the user will show up only on first use from the app.
