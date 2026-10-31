<!-- source: docs/tweaks/privacy.spotlight-third-party.md sha256: 5ab224dfa5fe2dda3704e76d761a4932e24905411d816420f71c8d619a7aa1b5 -->

# privacy.spotlight-third-party – Third-party Spotlight suggestions (DisableThirdPartySuggestions)

## Verdict for 26H2
Not ported: no effect to show.

## What it changes
The item isn't in the catalog and changes nothing. Entry considered: `HKCU\Software\Policies\Microsoft\Windows\CloudContent`, `DisableThirdPartySuggestions`, DWord, `1`.

## Sources
- Test on 26H2 Home and Pro VMs, 2026-10-21 (build 26300.9457, Windows not activated, local account): write through the registry API (.NET `RegistryKey`) and observation in Settings (UI Automation, screenshots).
- Research: `docs/zrodla/2026-10-20-etap4a-zrodla.md` (in Polish), section "Wyniki VM 26H2 (partia 1)" (26H2 VM results, round 1).
- No Microsoft documentation for this value; evidence: VM test (observation below).

## Defaults on a fresh 26H2
Reading from the VMs, 2026-10-21: value `DisableThirdPartySuggestions`: none (Home and Pro).

## Detection and undo
Not applicable (the item isn't in the catalog).

## Risks and warnings
Not applicable. The item didn't make it into the catalog.

## VM test
Not applicable (not ported): the item isn't in the catalog, so there is no VM case.

## Observed effect
- **Home** (`DSO-26H2-Home`), 2026-10-21. Method: writing the policy, checking Start and Settings. Result: **no effect**.
- **Pro** (`DSO-26H2-Pro`), 2026-10-21. Same method and same result.
