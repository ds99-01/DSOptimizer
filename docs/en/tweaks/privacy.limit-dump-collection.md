<!-- source: docs/tweaks/privacy.limit-dump-collection.md sha256: 9e3f352ffc2a7c212022aa61df4a75b90442bdbc06f4fb06324d05e9f10e6179 -->

# privacy.limit-dump-collection – LimitDumpCollection

## Verdict for 26H2
Not ported: no effect to show.

## What it changes
The item isn't in the catalog and changes nothing. Entry considered: `HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection`, `LimitDumpCollection`, DWord, `1` (Microsoft policy: Windows Error Reporting sends only kernel mini dumps and user mode "triage" dumps; applies only to the optional diagnostic data path).

## Sources
- Microsoft documentation: the `System/LimitDumpCollection` policy (Windows 11). The registry value name in the `DataCollection` key was a hypothesis to be checked on the VM.
- Test on 26H2 Home and Pro VMs, 2026-10-21 (build 26300.9457, Windows not activated, local account): write through the registry API (.NET `RegistryKey`) and observation in Settings (UI Automation, screenshots).
- Research: `docs/zrodla/2026-10-20-etap4a-zrodla.md` (in Polish), sections "2c. Limit Diagnostic Log / Dump collection" and "Wyniki VM 26H2 (partia 1)" (26H2 VM results, round 1).

## Defaults on a fresh 26H2
Reading from the VMs, 2026-10-21: value `LimitDumpCollection`: none (Home and Pro).

## Detection and undo
Not applicable (the item isn't in the catalog).

## Risks and warnings
Not applicable. The item didn't make it into the catalog.

## VM test
Not applicable (not ported): the item isn't in the catalog, so there is no VM case.

## Observed effect
- **Home** (`DSO-26H2-Home`), 2026-10-21. Method: writing the policy and checking the Diagnostics & feedback page in Settings. Result: **no effect**.
- **Pro** (`DSO-26H2-Pro`), 2026-10-21. Same method and same result.
