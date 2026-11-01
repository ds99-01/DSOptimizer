<!-- source: docs/tweaks/panels.system-restore.md sha256: 9ffb8fbd5fa897c1f055336daefc7185f0dd489e013975955a96cc55195997ae -->

# panels.system-restore – System Restore

## Verdict for 26H2
Port as `kind=panel`, `risk=safe`: on the 26H2 Home and Pro VMs the command opens the classic System Restore window, not Settings. The item only starts a program and changes nothing.

## What it changes
Nothing: `kind=panel` opens a window and doesn't write to the registry, services or files. `launch` field: `file` = `rstrui.exe`, `arguments` = empty.
The launcher starts `C:\Windows\System32\rstrui.exe` through CreateProcess (the file only from System32, never from the current directory or `PATH`), outside the scripts' Job Object and without waiting for the window.
Side effect (measured on the Administrator account over PowerShell Direct (session 0), Home and Pro): starting `rstrui.exe` is accompanied by the start of the `wbengine.exe` service (Block Level Backup Engine). The wizard also opens when System Protection is off and there are no restore points.

## Sources
- WinUtil `config/feature.json` @ `144d516` (MIT, 2026-07-31): WPFPanelRestore = "Windows Restore", `InvokeScript`: `rstrui.exe`. A hint for the list and the command, not evidence of the effect – https://raw.githubusercontent.com/ChrisTitusTech/winutil/144d51675190ffcb2553b7ba5313f8d602c8ecf9/config/feature.json
- Microsoft Learn: no page with a canonical name or command for this item in Windows 11 (only WinUtil gives a name); in general, MS only confirms that Control Panel exists for compatibility and that some settings are moving to Settings: https://support.microsoft.com/en-us/windows/system-configuration-tools-in-windows-f8a49657-b038-43b8-82d3-28bea0c5666b
- Reading from 26H2 VMs (build 26300.9457), Home (`DSO-26H2-Home`) and Pro (`DSO-26H2-Pro`), 2026-10-19: the System Restore window (`#32770`) in `rstrui.exe` (elevated). The wizard also opens when System Protection is off: on the VM `RestorePointCount` = 0, `SRInitDone` = 1, `RPSessionInterval` = 0 (Home and Pro); consistent with a visual check. Side effect measured on the Administrator account over PowerShell Direct (session 0): the start of the `wbengine.exe` service.
  - The Administrator account over PowerShell Direct (session 0): after 10 s, `rstrui.exe` is running (this session doesn't show the user's window, only that the process started).
  - The `dso` user session (elevated task): the System Restore window (`#32770`). Home = Pro in all fields except the PIDs.

## Defaults on a fresh 26H2
Not applicable: the item doesn't read or write any settings. The only system state after running it is the window's process (VM reading, 2026-10-19, above).

## Detection and undo
Not applicable: `kind=panel` has no detection and no undo. `Executed` only means that the process started; whether the window appeared is judged by the VM test in the `dso` user session (elevated task; smoke test with the real DSOptimizer.exe in the dso session – not checked manually in the app).

## Risks and warnings
- `risk=safe`: the item changes nothing in the system; you close the window yourself (DSOptimizer doesn't wait for it and doesn't close it).
- Just starting the wizard also starts `wbengine.exe` (measured in the VM). The wizard can restore the system to a restore point – that is the user's decision in the open window, not this item's.
- Measured on a VM with Polish regional settings; the window title may differ in another culture.

## VM test
VM run `20261019-110405` (2026-10-19, build 26300.9457): Home (`DSO-26H2-Home`) and Pro (`DSO-26H2-Pro`), `tests/vm/Invoke-VmTests.ps1`. Reports are local, outside git. Whole run: Home 401 passed / 0 failed / 29 skipped, Pro 401 / 0 / 29 (`main` 313/0/29 + Reboot runs); `CleanupError` empty in each of the panel cases.
- `panels-system-restore` (`Flow=Execute`, `Catalog=BuiltIn`, `RestorePoint=skip`): **Passed** on Home and Pro (`ExecuteExit=0`, `Executed`). `Check` polls in a loop: `rstrui.exe` is running; `Cleanup` closes only processes that match `Check`. **Check proves that the process from the command is running (the Administrator account over PowerShell Direct, session 0), not that the window appeared for the user.**
- VM test in the `dso` user session (elevated task, `Limited` token): the "Przywracanie systemu" (System Restore) window; side effect: the start of the `wbengine.exe` service (it stays until the checkpoint is reverted). Home = Pro.
- The manual click (Pro VM, `dso` session, Win+R, 2026-10-19): `rstrui.exe` opens the restore wizard even though System Protection is off.
- `rstrui.exe` clicked from Win+R isn't exactly the same as the item's `launch` (CreateProcess with the full path from System32): the click confirms the panel, while the item's `launch` is confirmed only by the VM test in the `dso` user session (elevated task).
- **Smoke test with the real `DSOptimizer.exe` in the `dso` session: not checked manually in the app**. Not done; the item goes into `main` with this caveat. A mismatch between "the process is running" (Check) and a window visible to the user will show up only on first use from the app.
