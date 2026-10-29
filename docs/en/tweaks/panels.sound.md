<!-- source: docs/tweaks/panels.sound.md sha256: 8d2182c3c8bd2c7d3780bc18dcb4f2af3bc4aba47b12e4c34853ae3fd93c3f31 -->

# panels.sound – Sound

## Verdict for 26H2
Port as `kind=panel`, `risk=safe`: on the 26H2 Home and Pro VMs the command opens the classic Sound window, not Settings. The window is confirmed by the VM reading; Dawid's manual Win+R got no response – to be confirmed: smoke test with the real DSOptimizer.exe in the dso session – to be confirmed by Dawid (R30).

## What it changes
Nothing: `kind=panel` opens a window and doesn't write to the registry, services or files. `launch` field: `file` = `control.exe`, `arguments` = `/name Microsoft.Sound`.
The launcher starts `C:\Windows\System32\control.exe /name Microsoft.Sound` through CreateProcess (the file only from System32, never from the current directory or `PATH`), outside the scripts' Job Object and without waiting for the window.

Rejected variants (not separate items; VM data in the "Sources" section):
- `mmsys.cpl` (WinUtil): the same window in the VM; the tie was resolved in favor of the MS canonical name.

## Sources
- WinUtil `config/feature.json` @ `144d516` (MIT, 2026-07-31): WPFPanelSound = "Sound Settings", `InvokeScript`: `mmsys.cpl`. A hint for the list and the command, not evidence of the effect – https://raw.githubusercontent.com/ChrisTitusTech/winutil/144d51675190ffcb2553b7ba5313f8d602c8ecf9/config/feature.json
- https://learn.microsoft.com/en-us/windows/win32/shell/controlpanel-canonical-names – MS "Canonical Names of Control Panel Items": `Microsoft.Sound` (`mmsys.cpl,-300`).
- Reading from 26H2 VMs (build 26300.9457), Home (`DSO-26H2-Home`) and Pro (`DSO-26H2-Pro`), 2026-10-19, K1 = PS Direct as Administrator (session 0), K2 = elevated interactive task in the session of user `dso`: the Sound window (`#32770`) in `rundll32.exe` (elevated); `control.exe` exits with code 1 even though the window is open. Discrepancy: with the manual Win+R, Dawid got no response – to be confirmed: smoke test with the real DSOptimizer.exe in the dso session – to be confirmed by Dawid (R30).
  - K1: after 10 s, `rundll32.exe` (`…\mmsys.cpl`) is running (session 0; K1 doesn't show the user's window, only that the process started).
  - K2: the Sound window (`#32770`). Home = Pro in all fields except the PIDs.
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
VM run `20261019-110405` (2026-10-19, HEAD `fd320ed`, build 26300.9457): Home (`DSO-26H2-Home`) and Pro (`DSO-26H2-Pro`), `tests/vm/Invoke-VmTests.ps1`. Reports are local, outside git. Whole run: Home 401 passed / 0 failed / 29 skipped, Pro 401 / 0 / 29 (`main` 313/0/29 + Reboot runs); `CleanupError` empty in the C5 cases.
- `panels-sound` (`Flow=Execute`, `Catalog=BuiltIn`, `RestorePoint=skip`): **Passed** on Home and Pro (`ExecuteExit=0`, `Executed`). `Check` polls in a loop: `rundll32.exe` with a command line containing `mmsys.cpl` is running; `Cleanup` closes only processes that match `Check`. **Check proves that the process from the command is running (K1, PS Direct as Administrator, session 0), not that the window appeared for the user.**
- K2 exploration (interactive task in the `dso` session, `Limited` token): the "Dźwięk" (Sound) window (`#32770`, in an elevated `rundll32.exe`). Home = Pro. Research and K2 exploration results: `docs/zrodla/2026-10-19-partia-c5-zrodla.md` (in Polish), sections "Wyniki VM" (VM results) and "Sonda uzupełniająca" (follow-up probe).
- Dawid's manual click (Pro VM, `dso` session, Win+R, 2026-10-19): `mmsys.cpl` got no response - Dawid skipped this item. So the "Dźwięk" (Sound) window is confirmed only by the VM reading (K2); the manual click neither confirms nor disproves this item. To be confirmed on first use from DSOptimizer.
- **Smoke test with the real `DSOptimizer.exe` in the `dso` session: to be confirmed by Dawid** (Ruling R30). Not done because Dawid was away; the item goes into `main` with this caveat (like D2 in round C4). A mismatch between "the process is running" (Check) and a window visible to the user will show up only on first use from the app.

## Reddit
To be added in part E.
