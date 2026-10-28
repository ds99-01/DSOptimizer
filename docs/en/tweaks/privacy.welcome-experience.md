<!-- source: docs/tweaks/privacy.welcome-experience.md sha256: 852fde5dba01e2ed4c66d08648dec1e707fdd709c6e803ff4a47f98c731a1945 -->

# privacy.welcome-experience – "Show the Windows welcome experience" and "Suggest ways…" (removed settings)

## Verdict for 26H2
Not ported: these settings don't exist in 26H2: "Additional settings" (System > Notifications) has only the tips checkbox (`privacy.windows-tips`). So there is nothing to switch and nothing to observe.

## What it changes
The item isn't in the catalog and changes nothing. Entry considered: the "Show the Windows welcome experience" and "Suggest ways…" settings (they don't exist in 26H2, so there is no value to write).

## Sources
- Test on 26H2 Home and Pro VMs, 2026-10-21 (build 26300.9457, Windows not activated, local account): write through the registry API (.NET `RegistryKey`) and observation in Settings (UI Automation, screenshots).
- Research: `docs/zrodla/2026-10-20-etap4a-zrodla.md` (in Polish), section "Wyniki VM 26H2 (partia 1)" (26H2 VM results, batch 1).
- No Microsoft documentation for this value; evidence: VM test (observation below).

## Defaults on a fresh 26H2
Reading from the VMs, 2026-10-21: neither checkbox is in Settings (Home and Pro).

## Detection and undo
Not applicable (the item isn't in the catalog).

## Risks and warnings
Not applicable. The item didn't make it into the catalog.

## VM test
Not applicable (not ported): the item isn't in the catalog, so there is no VM case.

## Observed effect
- **Home** (`DSO-26H2-Home`), 2026-10-21. Method: checking System > Notifications > Additional settings (UI Automation). Result: there is only the tips and suggestions checkbox; these two settings are missing.
- **Pro** (`DSO-26H2-Pro`), 2026-10-21. Same method and same result.

## Reddit
To be added in part E.
