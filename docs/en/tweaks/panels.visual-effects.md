<!-- source: docs/tweaks/panels.visual-effects.md sha256: 4bcb92174d255e807e6a0ed33aa3c31ec6f6e96c8c836044bd584b69f16c2a5e -->

# panels.visual-effects – Visual effects (Performance Options)

## Verdict for 26H2
Port as `kind=panel`, `risk=safe` (v3.2 alias `tool_vis`): on the 26H2 Home and Pro VMs the command opens the classic Performance Options window, not Settings. The item only starts a program and changes nothing.

## What it changes
Nothing: `kind=panel` opens a window and doesn't write to the registry, services or files. `launch` field: `file` = `SystemPropertiesPerformance.exe`, `arguments` = empty.
The launcher starts `C:\Windows\System32\SystemPropertiesPerformance.exe` through CreateProcess (the file only from System32, never from the current directory or `PATH`), outside the scripts' Job Object and without waiting for the window.

Rejected variants (not separate items; VM data in the "Sources" section):
- `control.exe sysdm.cpl,,3` (v3.2 `tool_vis`): `control.exe` exits with code 1, and the window belongs to `SystemPropertiesAdvanced.exe` (title "System Properties", `#32770`) – this is the System Properties window, not Performance Options. The Advanced tab is an inference from the process name (MS calls `SystemPropertiesAdvanced.exe` "Advanced system properties"). In this variant, visual effects are one click further away, so the variant is dropped for this item.

## Sources
- WinUtil: no row for this item (v3.2 `core_models.py`: `sysdm.cpl,,3`, rejected, see above).
- https://learn.microsoft.com/en-us/windows/win32/shell/executing-control-panel-items – MS "Executing Control Panel Items" (accessed 2026-10-19): "Performance: %windir%\system32\SystemPropertiesPerformance.exe".
- Reading from 26H2 VMs (build 26300.9457), Home (`DSO-26H2-Home`) and Pro (`DSO-26H2-Pro`), 2026-10-19, K1 = PS Direct as Administrator (session 0), K2 = elevated interactive task in the session of user `dso`: the Performance Options window from the `SystemPropertiesPerformance.exe` process (user session, elevated). The automation didn't read the effect switches; according to a visual check in the VM (Dawid, 2026-10-19), the window shows "Visual Effects".
  - K1: after 10 s, `SystemPropertiesPerformance.exe` is running (session 0; K1 doesn't show the user's window, only that the process started).
  - K2: the Performance Options window (`#32770`). Home = Pro in all fields except the PIDs.
- Research and results: `docs/zrodla/2026-10-19-partia-c5-zrodla.md` (in Polish), sections "Wyniki VM" (VM results) and "Sonda uzupełniająca" (follow-up probe).

## Defaults on a fresh 26H2
Not applicable: the item doesn't read or write any settings. The only system state after running it is the window's process (VM reading, 2026-10-19, above).

## Detection and undo
Not applicable: `kind=panel` has no detection and no undo. `Executed` only means that the process started; whether the window appeared is judged by the VM test (K2; smoke test with the real DSOptimizer.exe in the dso session – to be confirmed by Dawid (R30)).

## Risks and warnings
- `risk=safe`: the item changes nothing in the system; you close the window yourself (DSOptimizer doesn't wait for it and doesn't close it).
- Measured on a VM with Polish regional settings; the window title may differ in another culture.

## VM test
VM run `20261019-110405` (2026-10-19, HEAD `fd320ed`, build 26300.9457): Home (`DSO-26H2-Home`) and Pro (`DSO-26H2-Pro`), `tests/vm/Invoke-VmTests.ps1`. Reports are local, outside git. Whole run: Home 401 passed / 0 failed / 29 skipped, Pro 401 / 0 / 29 (`main` 313/0/29 + Reboot runs); `CleanupError` empty in the C5 cases.
- `panels-visual-effects` (`Flow=Execute`, `Catalog=BuiltIn`, `RestorePoint=skip`): **Passed** on Home and Pro (`ExecuteExit=0`, `Executed`). `Check` polls in a loop: within 15 s (polling every 0.5 s) the `SystemPropertiesPerformance.exe` process is running; `Cleanup` closes only processes that match `Check`. **Check proves that the process from the command is running (K1, PS Direct as Administrator, session 0), not that the window appeared for the user.**
- K2 exploration (interactive task in the `dso` session, `Limited` token): the "Opcje wydajności" (Performance Options) window ("Efekty wizualne" (Visual Effects) tab). Home = Pro. Research and K2 exploration results: `docs/zrodla/2026-10-19-partia-c5-zrodla.md` (in Polish), sections "Wyniki VM" (VM results) and "Sonda uzupełniająca" (follow-up probe).
- Dawid's manual click (Pro VM, `dso` session, Win+R, 2026-10-19): `SystemPropertiesPerformance.exe` opens the window with the "Efekty wizualne" (Visual Effects) tab. Win+R with just `sysdm.cpl,,3` gives the error "nie można odnaleźć pliku" (cannot find the file) (v3.2 called `control.exe sysdm.cpl,,3`, not `sysdm.cpl,,3` alone).
- The `control.exe sysdm.cpl,,3` variant was rejected based on the VM exploration (K1/K2, not Dawid's click): it opens System Properties (`SystemPropertiesAdvanced.exe`), not visual effects.
- `SystemPropertiesPerformance.exe` clicked from Win+R isn't exactly the same as the item's `launch` (CreateProcess with the full path from System32): the click confirms the panel, while the item's `launch` is confirmed only by the K2 exploration.
- **Smoke test with the real `DSOptimizer.exe` in the `dso` session: to be confirmed by Dawid** (Ruling R30). Not done because Dawid was away; the item goes into `main` with this caveat (like D2 in round C4). A mismatch between "the process is running" (Check) and a window visible to the user will show up only on first use from the app.

## Reddit
To be added in part E.
