<!-- source: docs/tweaks/privacy.search-connected-web.md sha256: 622bc0797f8bdfec2767340d4f95f0feed9ca6a2934bd5a236d1c6c293d7bc58 -->

# privacy.search-connected-web – ConnectedSearchUseWeb policy

## Verdict for 26H2
Not ported: no effect on 26H2. After setting `ConnectedSearchUseWeb` = 0, Bing is still in the search results. The working mechanisms are described in `privacy.search-web-results.md`.

## What it changes
The item isn't in the catalog and changes nothing. Entry considered: `HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Search`, `ConnectedSearchUseWeb`, DWord, `0`.

## Sources
- Test on 26H2 Home and Pro VMs, 2026-10-21 (build 26300.9457).

## Defaults on a fresh 26H2
Reading from the VMs, 2026-10-21: value `ConnectedSearchUseWeb`: none (Home and Pro).

## Detection and undo
Not applicable (the item isn't in the catalog).

## Risks and warnings
Not applicable. The item didn't make it into the catalog.

## VM test
Not applicable (not ported): the item isn't in the catalog, so there is no VM case.

## Observed effect
- **Home** (`DSO-26H2-Home`), 2026-10-21. Method: writing the policy through the registry API, restarting File Explorer and SearchHost, search results for the phrase "pogoda" (weather). Result: **no effect**, Bing still in the results.
- **Pro** (`DSO-26H2-Pro`), 2026-10-21. Same method and same result.
