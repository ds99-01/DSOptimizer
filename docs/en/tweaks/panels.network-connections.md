<!-- source: docs/tweaks/panels.network-connections.md sha256: 63d09903b2575034a13ce287a10b33084cf28c032d3100e59d5d1be4be9a3aee -->

# panels.network-connections – Network Connections

## Verdict for 26H2
Port as `kind=panel`, `risk=safe` (v3.2 alias `tool_network`): on the 26H2 Home and Pro VMs the command opens the classic Network Connections window, not Settings. The item only starts a program and changes nothing.

## What it changes
Nothing: `kind=panel` opens a window and doesn't write to the registry, services or files. `launch` field: `file` = `control.exe`, `arguments` = `netconnections`.
The launcher starts `C:\Windows\System32\control.exe netconnections` through CreateProcess (the file only from System32, never from the current directory or `PATH`), outside the scripts' Job Object and without waiting for the window.

Rejected variants (not separate items; VM data in the "Sources" section):
- `ncpa.cpl` (WinUtil, v3.2 `tool_network`): the same Network Connections window in the VM; the tie was resolved in favor of the MS form. This is a different panel from "Network and Sharing Center" (`Microsoft.NetworkAndSharingCenter`).

## Sources
- WinUtil `config/feature.json` @ `144d516` (MIT, 2026-07-31): WPFPanelNetwork = "Network Connections", `InvokeScript`: `ncpa.cpl`. A hint for the list and the command, not evidence of the effect – https://raw.githubusercontent.com/ChrisTitusTech/winutil/144d51675190ffcb2553b7ba5313f8d602c8ecf9/config/feature.json
- https://learn.microsoft.com/en-us/windows/win32/shell/executing-control-panel-items – MS "Executing Control Panel Items": "control.exe netconnections" – "Displays the Network Connections folder".
- Reading from 26H2 VMs (build 26300.9457), Home (`DSO-26H2-Home`) and Pro (`DSO-26H2-Pro`), 2026-10-19, K1 = PS Direct as Administrator (session 0), K2 = elevated interactive task in the session of user `dso`: the Network Connections window (`CabinetWClass`) in the user's `explorer.exe` (`/factory`, `Limited` token).
  - K1: after 10 s, `control.exe` with the line `netconnections` is running (session 0; K1 doesn't show the user's window, only that the process started).
  - K2: the Network Connections window (`CabinetWClass`). Home = Pro in all fields except the PIDs.
- Research and results: `docs/zrodla/2026-10-19-partia-c5-zrodla.md` (in Polish), sections "Wyniki VM" (VM results) and "Sonda uzupełniająca" (follow-up probe).

## Defaults on a fresh 26H2
Not applicable: the item doesn't read or write any settings. The only system state after running it is the window's process (VM reading, 2026-10-19, above).

## Detection and undo
Not applicable: `kind=panel` has no detection and no undo. `Executed` only means that the process started; whether the window appeared is judged by the VM test (K2; smoke test with the real DSOptimizer.exe in the dso session – to be confirmed by the author).

## Risks and warnings
- `risk=safe`: the item changes nothing in the system; you close the window yourself (DSOptimizer doesn't wait for it and doesn't close it).
- Measured on a VM with Polish regional settings; the window title may differ in another culture.
- The scenario "DSOptimizer elevated with a different administrator account than the signed-in user" wasn't measured on a VM for this panel (the `control.exe`/`explorer.exe` host itself uses associations and COM in the elevated process; DSOptimizer controls only the path of the file it starts).

## VM test
VM run `20261019-110405` (2026-10-19, HEAD `fd320ed`, build 26300.9457): Home (`DSO-26H2-Home`) and Pro (`DSO-26H2-Pro`), `tests/vm/Invoke-VmTests.ps1`. Reports are local, outside git. Whole run: Home 401 passed / 0 failed / 29 skipped, Pro 401 / 0 / 29 (`main` 313/0/29 + Reboot runs); `CleanupError` empty in the C5 cases.
- `panels-network-connections` (`Flow=Execute`, `Catalog=BuiltIn`, `RestorePoint=skip`): **Passed** on Home and Pro (`ExecuteExit=0`, `Executed`). `Check` polls in a loop: `control.exe` with a command line containing `netconnections` is running; `Cleanup` closes only processes that match `Check`. **Check proves that the process from the command is running (K1, PS Direct as Administrator, session 0), not that the window appeared for the user.**
- K2 exploration (interactive task in the `dso` session, `Limited` token): the "Połączenia sieciowe" (Network Connections) window. Home = Pro. Research and K2 exploration results: `docs/zrodla/2026-10-19-partia-c5-zrodla.md` (in Polish), sections "Wyniki VM" (VM results) and "Sonda uzupełniająca" (follow-up probe).
- The author's manual click (Pro VM, `dso` session, Win+R, 2026-10-19): `ncpa.cpl` and `control.exe netconnections` open the classic window.
- The clicked commands (`ncpa.cpl`, `control.exe netconnections`) only partly match the item's `launch` (`control.exe netconnections`; `ncpa.cpl` alone is a different command): the click confirms the panel, while the item's `launch` is confirmed only by the K2 exploration.
- **Smoke test with the real `DSOptimizer.exe` in the `dso` session: to be confirmed by the author**. Not done because the author was away; the item goes into `main` with this caveat (like D2 in round C4). A mismatch between "the process is running" (Check) and a window visible to the user will show up only on first use from the app.
