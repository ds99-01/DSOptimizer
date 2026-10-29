<!-- source: docs/tweaks/privacy.search-web-results.md sha256: f4b9cc3b353f4e41466adbd20c26c71c2c1a695014ef2d55542847707d906d68 -->

# privacy.search-web-results – Turn off web results in Windows search

## Verdict for 26H2
Port: a test on a 26H2 VM (Home and Pro) showed a real effect. No Microsoft documentation for this value; evidence: VM test (observation below).

## What it changes
- `HKCU\Software\Microsoft\Windows\CurrentVersion\SearchSettings` (in the engine `HKU\<SID of the signed-in user>`), value `IsGlobalWebSearchProviderToggleEnabled`, **DWord**, set to `0`; `scope=user`. This is the state of the "Let search apps show results" toggle (Privacy & security > Search).
- It turns off all providers of web results, doesn't lock the toggle and doesn't touch the File Explorer search box.
- Apply doesn't restart File Explorer (R15). The effect was observed after restarting File Explorer and killing SearchHost (StartMenuExperienceHost was also killed for the Start items). The catalog therefore gives the effect as "after you sign in again"; whether restarting File Explorer alone (`tools.restart-explorer`) is enough wasn't checked, and the item says explicitly that it "may not be enough".

### Working alternatives (described, without their own catalog items)
All three gave the same effect (Bing results disappear) on Home and Pro, after restarting File Explorer:
- `...\SearchSettings\WebSearchProviders`, value `Microsoft.BingSearch_8wekyb3d8bbwe!App` = DWORD 0 (1 by default): narrower, only the Bing provider.
- `HKCU\Software\Policies\Microsoft\Windows\Explorer` `DisableSearchBoxSuggestions` = DWORD 1: a policy; according to Microsoft it also turns off the history of the File Explorer search box.
- `HKCU\Software\Microsoft\Windows\CurrentVersion\Search` `BingSearchEnabled` = DWORD 0 (no value by default): works, no Microsoft documentation.

Not working (see the separate documents): `ConnectedSearchUseWeb` (`privacy.search-connected-web.md`), `DisableWebSearch` (`privacy.search-disable-web-search.md`).

## Sources
- Test on 26H2 Home and Pro VMs, 2026-10-21 (build 26300.9457): the toggle in Settings, write through the registry API, search results for the phrase "pogoda" (weather).
- Research: `docs/zrodla/2026-10-20-etap4a-zrodla.md` (in Polish), sections "4. Bing / wyszukiwanie w sieci w menu Start" (Bing / web search in the Start menu) and "Wyniki VM 26H2 (partia 1)" (26H2 VM results, round 1).

## Defaults on a fresh 26H2
Reading from the DSO-26H2-Home and DSO-26H2-Pro VMs, 2026-10-21: the `SearchSettings` key exists, `IsGlobalWebSearchProviderToggleEnabled` **no value** (toggle on); `WebSearchProviders\Microsoft.BingSearch_8wekyb3d8bbwe!App` = 1.

## Detection and undo
- Detection: value = DWORD 0 -> `Applied`; missing value or a different value -> `NotApplied`.
- Undo restores the snapshot from before the change from the journal (a missing value = deleting it; the `SearchSettings` key existed before apply and stays); `original` = `<RemoveEntry>` is only a fallback.

## Risks and warnings
- `risk=safe`, `suggested=true`.
- Warning in the catalog: "The change takes effect after you sign in again; restarting File Explorer alone may not be enough."
- Beyond the notes in the program (in the technical details or only here): the effect of restarting File Explorer alone (`tools.restart-explorer`) wasn't checked; the item turns off all providers of web results.

## VM test
Case `privacy-search-web-results` (`Flow=Full`) in `tests/vm/cases.psd1` (`NotWrittenTo` for the `HKCU` of the Administrator process, because the item is per user). VM run on 2026-10-21 (`Invoke-VmTests.ps1`, Task 9 of the round): **DSO-26H2-Home and DSO-26H2-Pro – passed**, steps detect `NotApplied` → apply `Applied` → detect `Applied` → undo `Undone` → detect `NotApplied`, state after apply confirmed by an independent reading, final state = state before (`SnapshotDiff` empty). The Administrator's HKCU unchanged (`NotWrittenTo`), the `SearchSettings` key stayed. The exploration of 2026-10-21 passed on Home and Pro.

## Observed effect
- **Home** (`DSO-26H2-Home`), 2026-10-21. Method: screenshots of the search results (Win+S) for the phrase "pogoda" (weather) in the `dso` session, after a write through the registry API and restarting File Explorer and SearchHost. Result: the "Microsoft Bing sugestie internetowe" (Microsoft Bing web suggestions) section and the "Microsoft Bing" card disappear from the results; after undo the section comes back.
- **Pro** (`DSO-26H2-Pro`), 2026-10-21. Same method and same result.

## Reddit
To be added in part E.
