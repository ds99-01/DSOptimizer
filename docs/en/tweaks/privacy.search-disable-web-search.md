<!-- source: docs/tweaks/privacy.search-disable-web-search.md sha256: aea20881291e765728b05c7b98c2c76290d19fe4a977dd2103eadf3f0d1c0ed6 -->

# privacy.search-disable-web-search – DisableWebSearch policy

## Verdict for 26H2
Not ported: no effect on 26H2. After setting `DisableWebSearch` = 1, Bing is still in the search results. The working mechanisms are described in `privacy.search-web-results.md`.

## What it changes
The item isn't in the catalog and changes nothing. Entry considered: `HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Search`, `DisableWebSearch`, DWord, `1`.

## Sources
- Test on 26H2 Home and Pro VMs, 2026-10-21 (build 26300.9457).
- Research: `docs/zrodla/2026-10-20-etap4a-zrodla.md` (in Polish), sections "4. Bing / wyszukiwanie w sieci w menu Start" (Bing / web search in the Start menu) and "Wyniki VM 26H2 (partia 1)" (26H2 VM results, round 1).

## Defaults on a fresh 26H2
Reading from the VMs, 2026-10-21: value `DisableWebSearch`: none (Home and Pro).

## Detection and undo
Not applicable (the item isn't in the catalog).

## Risks and warnings
Not applicable. The item didn't make it into the catalog.

## VM test
Not applicable (not ported): the item isn't in the catalog, so there is no VM case.

## Observed effect
- **Home** (`DSO-26H2-Home`), 2026-10-21. Method: writing the policy through the registry API, restarting File Explorer and SearchHost, search results for the phrase "pogoda" (weather). Result: **no effect**, Bing still in the results.
- **Pro** (`DSO-26H2-Pro`), 2026-10-21. Same method and same result.
