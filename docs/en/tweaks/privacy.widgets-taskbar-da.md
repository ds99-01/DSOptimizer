<!-- source: docs/tweaks/privacy.widgets-taskbar-da.md sha256: 1a0bb73427e28cbef845d1072d6c74731c6286e6ea8413e21e1faaf64954c6f9 -->

# privacy.widgets-taskbar-da – Widgets button through TaskbarDa (user)

## Verdict for 26H2
Not ported: the system blocks writing `TaskbarDa` (UCPD driver) for every one of our processes. Widgets are turned off by the `privacy.widgets` item (machine policy).

## What it changes
The item isn't in the catalog and changes nothing. Entry considered: `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced`, `TaskbarDa`, DWord, `0` (`scope=user`). The toggle in Settings writes this value (0/1) and hides the button right away, but our write ends with `UnauthorizedAccessException`.

## Sources
- Test on 26H2 Home and Pro VMs, 2026-10-21 (build 26300.9457): write from `powershell.exe` and from a separate exe named `DSOptimizer.exe`.
- Research: `docs/zrodla/2026-10-20-etap4a-zrodla.md` (in Polish), section "Wyniki VM 26H2 (partia 1)" (26H2 VM results, batch 1), note on `TaskbarDa`.

## Defaults on a fresh 26H2
Reading from the VMs, 2026-10-21: `Explorer\Advanced\TaskbarDa` no value (button visible), Home and Pro.

## Detection and undo
Not applicable (the item isn't in the catalog).

## Risks and warnings
Not applicable. The item didn't make it into the catalog.

## VM test
Not applicable (not ported): the item isn't in the catalog, so there is no VM case.

## Observed effect
- **Home** (`DSO-26H2-Home`), 2026-10-21. Method: write through .NET `RegistryKey` (like the app's API) and from a separate exe. Result: `UnauthorizedAccessException`, the UCPD driver is active; the toggle in Settings hides the button, but that is a system write that we can't repeat.
- **Pro** (`DSO-26H2-Pro`), 2026-10-21. Same method and same result.

## Reddit
To be added in part E.
