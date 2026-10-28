<!-- source: docs/tweaks/privacy.recommended-personalized-sites.md sha256: e271c42c544124e26d293000c4b7438191a8132ce02e39c62afd18065bc39f10 -->

# privacy.recommended-personalized-sites – Personalized websites in Recommended (HideRecommendedPersonalizedSites)

## Verdict for 26H2
Not ported: can't be observed (local account, no websites in the section), and the effect is included in the `privacy.start-recommended-section` item, which hides the whole "Recommended" section.

## What it changes
The item isn't in the catalog and changes nothing. Entry considered: `HKLM\SOFTWARE\Policies\Microsoft\Windows\Explorer`, `HideRecommendedPersonalizedSites`, DWord, `1`.

## Sources
- Test on 26H2 Home and Pro VMs, 2026-10-21 (build 26300.9457, Windows not activated, local account): write through the registry API (.NET `RegistryKey`) and observation in Settings (UI Automation, screenshots).
- Research: `docs/zrodla/2026-10-20-etap4a-zrodla.md` (in Polish), section "Wyniki VM 26H2 (partia 1)" (26H2 VM results, batch 1).
- No Microsoft documentation for this value; evidence: VM test (observation below).

## Defaults on a fresh 26H2
Reading from the VMs, 2026-10-21: value `HideRecommendedPersonalizedSites` none (Home and Pro).

## Detection and undo
Not applicable (the item isn't in the catalog).

## Risks and warnings
Not applicable. The item didn't make it into the catalog.

## VM test
Not applicable (not ported): the item isn't in the catalog, so there is no VM case.

## Observed effect
- **Home** (`DSO-26H2-Home`), 2026-10-21. Method: writing the policy, checking the Recommended section after restarting File Explorer. Result: **can't be observed**, because the local account has no websites in the section.
- **Pro** (`DSO-26H2-Pro`), 2026-10-21. Same method and same result.

## Reddit
To be added in part E.
