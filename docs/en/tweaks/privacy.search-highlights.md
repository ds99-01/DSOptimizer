<!-- source: docs/tweaks/privacy.search-highlights.md sha256: 833d5b2e85b99bedfecaef628112c8966abfb9f5c70a4be9179bbc247511feaf -->

# privacy.search-highlights – search highlights (EnableDynamicContentInWSB)

## Verdict for 26H2
Not ported: no effect to show. The search home page (Win+S) has no highlights even before the change, and Settings has no toggle.

## What it changes
The item isn't in the catalog and changes nothing. Entry considered: `HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Search`, `EnableDynamicContentInWSB`, DWord, `0`.

## Sources
- Test on 26H2 Home and Pro VMs, 2026-10-21 (build 26300.9457).
- Research: `docs/zrodla/2026-10-20-etap4a-zrodla.md` (in Polish), sections "4. Bing / wyszukiwanie w sieci w menu Start" (Bing / web search in the Start menu) and "Wyniki VM 26H2 (partia 1)" (26H2 VM results, round 1).

## Defaults on a fresh 26H2
Reading from the VMs, 2026-10-21: value `EnableDynamicContentInWSB`: none (Home and Pro).

## Detection and undo
Not applicable (the item isn't in the catalog).

## Risks and warnings
Not applicable. The item didn't make it into the catalog.

## VM test
Not applicable (not ported): the item isn't in the catalog, so there is no VM case.

## Observed effect
- **Home** (`DSO-26H2-Home`), 2026-10-21. Method: screenshot of the search home page (Win+S) and a check of Settings before and after writing the policy and restarting File Explorer. Result: **no effect to show**, there are no highlights either before or after; Settings has no toggle.
- **Pro** (`DSO-26H2-Pro`), 2026-10-21. Same method and same result.
