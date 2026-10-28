<!-- source: docs/tweaks/privacy.limit-diagnostic-log-collection.md sha256: 9337777dc135a18472ae3d640dc535225a776b81203ed0bc0db4eb0fbdde3d53 -->

# privacy.limit-diagnostic-log-collection – LimitDiagnosticLogCollection

## Verdict for 26H2
Not ported: no repeatable effect.

## What it changes
The item isn't in the catalog and changes nothing. Entry considered: `HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection`, `LimitDiagnosticLogCollection`, DWord, `1` (the Microsoft policy applies only to the optional diagnostic data path).

## Sources
- Microsoft documentation: the `System/LimitDiagnosticLogCollection` policy (Windows 11 21H2+, Pro). The registry value name in the `DataCollection` key was a hypothesis to be checked on the VM.
- Test on 26H2 Home and Pro VMs, 2026-10-21 (build 26300.9457, Windows not activated, local account): write through the registry API (.NET `RegistryKey`) and observation in Settings (UI Automation, screenshots).
- Research: `docs/zrodla/2026-10-20-etap4a-zrodla.md` (in Polish), sections "2c. Limit Diagnostic Log / Dump collection" and "Wyniki VM 26H2 (partia 1)" (26H2 VM results, batch 1).

## Defaults on a fresh 26H2
Reading from the VMs, 2026-10-21: value `LimitDiagnosticLogCollection` none (Home and Pro).

## Detection and undo
Not applicable (the item isn't in the catalog).

## Risks and warnings
Not applicable. The item didn't make it into the catalog.

## VM test
Not applicable (not ported): the item isn't in the catalog, so there is no VM case.

## Observed effect
- **Home** (`DSO-26H2-Home`), 2026-10-21. Method: writing the policy and checking the Diagnostics & feedback page in Settings. Result: **no repeatable effect** (nothing in the "after" reading; one ambiguous reading after undo: a grayed-out toggle).
- **Pro** (`DSO-26H2-Pro`), 2026-10-21. Same method. Result: **no effect**.

## Reddit
To be added in part E.
