<!-- source: docs/tweaks/panels.system-properties.md sha256: 44ffd887b2929d59c7ec582deb3d778e6ca593534afdbd4e1a6ceb6da9326ccd -->

# panels.system-properties – System Properties

## Verdict for 26H2
Port as `kind=panel`, `risk=safe`: on the 26H2 Home and Pro VMs the command opens the classic System Properties window, not Settings. The tab shown in the window is an inference from the process name (`SystemPropertiesComputerName.exe`), not a reading of the tab title.

## What it changes
Nothing: `kind=panel` opens a window and doesn't write to the registry, services or files. `launch` field: `file` = `control.exe`, `arguments` = `sysdm.cpl`.
The launcher starts `C:\Windows\System32\control.exe sysdm.cpl` through CreateProcess (the file only from System32, never from the current directory or `PATH`), outside the scripts' Job Object and without waiting for the window.

Rejected variants (not separate items; VM data in the "Sources" section):
- `sysdm.cpl` through ShellExecute (WinUtil): in the main probe, no new process and no window – inconclusive, because the earlier variant `control.exe sysdm.cpl,,3` had left `SystemPropertiesAdvanced.exe` open (a single-instance window – a hypothesis, without a direct reading). The follow-up probe in a clean state confirmed the window for `control.exe sysdm.cpl`.
- `control.exe sysdm.cpl,,3`: opens `SystemPropertiesAdvanced.exe` (the Advanced tab, judging by the process name); an unnecessary variant.
- `control.exe /name Microsoft.System`: in the `dso` user session (elevated task) only a new `explorer.exe /factory` without a window appears – rejected.

## Sources
- WinUtil `config/feature.json` @ `144d516` (MIT, 2026-07-31): WPFPanelSystem = "System Properties", `InvokeScript`: `sysdm.cpl`. A hint for the list and the command, not evidence of the effect – https://raw.githubusercontent.com/ChrisTitusTech/winutil/144d51675190ffcb2553b7ba5313f8d602c8ecf9/config/feature.json
- https://learn.microsoft.com/en-us/windows/win32/shell/executing-control-panel-items – MS "Executing Control Panel Items": the example `control.exe sysdm.cpl,,3` (tabs; the numbering changed between versions).
- Reading from 26H2 VMs (build 26300.9457), Home (`DSO-26H2-Home`) and Pro (`DSO-26H2-Pro`), 2026-10-19: follow-up probe (CreateProcess `control.exe sysdm.cpl`, clean state): the System Properties window (`#32770`) in the `SystemPropertiesComputerName.exe` process (user session). Which tab it shows is an inference from the process name (default: "Computer Name"); the window title doesn't settle it, and there is no MS quote about this file.
  - The Administrator account over PowerShell Direct (session 0): after 10 s, `SystemPropertiesComputerName.exe` is running (this session doesn't show the user's window, only that the process started).
  - The `dso` user session (elevated task): the System Properties window (`#32770`). Home = Pro in all fields except the PIDs.

## Defaults on a fresh 26H2
Not applicable: the item doesn't read or write any settings. The only system state after running it is the window's process (VM reading, 2026-10-19, above).

## Detection and undo
Not applicable: `kind=panel` has no detection and no undo. `Executed` only means that the process started; whether the window appeared is judged by the VM test in the `dso` user session (elevated task; smoke test with the real DSOptimizer.exe in the dso session – not checked manually in the app).

## Risks and warnings
- `risk=safe`: the item changes nothing in the system; you close the window yourself (DSOptimizer doesn't wait for it and doesn't close it).
- The System Properties window is probably single-instance (a hypothesis): if another window of this type is already open, running it again may only activate that window.
- Measured on a VM with Polish regional settings; the window title may differ in another culture.
- The scenario "DSOptimizer elevated with a different administrator account than the signed-in user" wasn't measured on a VM for this panel (the `control.exe`/`explorer.exe` host itself uses associations and COM in the elevated process; DSOptimizer controls only the path of the file it starts).

## VM test
VM run `20261019-110405` (2026-10-19, build 26300.9457): Home (`DSO-26H2-Home`) and Pro (`DSO-26H2-Pro`), `tests/vm/Invoke-VmTests.ps1`. Reports are local, outside git. Whole run: Home 401 passed / 0 failed / 29 skipped, Pro 401 / 0 / 29 (`main` 313/0/29 + Reboot runs); `CleanupError` empty in these cases.
- `panels-system-properties` (`Flow=Execute`, `Catalog=BuiltIn`, `RestorePoint=skip`): **Passed** on Home and Pro (`ExecuteExit=0`, `Executed`). `Check` polls in a loop: `SystemPropertiesComputerName.exe` is running (before the start, Setup closes all `SystemProperties*.exe`, so that an old process can't pass Check); `Cleanup` closes only processes that match `Check`. **Check proves that the process from the command is running (the Administrator account over PowerShell Direct, session 0), not that the window appeared for the user.**
- Exploration in the `dso` user session (elevated task, `Limited` token): the "Właściwości systemu" (System Properties) window. Home = Pro.
- The manual click (Pro VM, `dso` session, Win+R, 2026-10-19): `sysdm.cpl` opens the classic "Właściwości systemu" (System Properties) window.
- The clicked command (`sysdm.cpl`) isn't the same as the item's `launch` (`control.exe sysdm.cpl`): the click confirms the panel, while the item's `launch` is confirmed only by the exploration in the `dso` user session (elevated task).
- **Smoke test with the real `DSOptimizer.exe` in the `dso` session: not checked manually in the app**. Not done; the item goes into `main` with this caveat. A mismatch between "the process is running" (Check) and a window visible to the user will show up only on first use from the app.
