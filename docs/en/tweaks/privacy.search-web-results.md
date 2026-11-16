<!-- source: docs/tweaks/privacy.search-web-results.md sha256: 243e86d19e0f03071280ee40b6c946653380d7f09af3a2907aa853f60caf52da -->

# privacy.search-web-results – Turn off web results in Windows search

## Verdict for 26H2
Port: a test on a 26H2 VM (Home and Pro) showed a real effect. No Microsoft documentation for either value; evidence: VM test (observation below).

Since 2026-11-16 (for the release after 4.0.8) the item sets two values. Up to 4.0.8 it set only `IsGlobalWebSearchProviderToggleEnabled`, which controls a toggle that Windows shows in the European Economic Area (EEA). On a VM with the region set to the United States it changed nothing, also after a restart. `BingSearchEnabled` = 0 turns off Bing results in the US and PL regions.

## What it changes
- `HKCU\Software\Microsoft\Windows\CurrentVersion\Search` (in the engine `HKU\<SID of the signed-in user>`), value `BingSearchEnabled`, **DWord**, set to `0`; `scope=user`. The main value: it works inside and outside the EEA. Side effect: search highlights disappear from the search start page (picture of the day, quiz, trending searches).
- `HKCU\Software\Microsoft\Windows\CurrentVersion\SearchSettings` (in the engine `HKU\<SID of the signed-in user>`), value `IsGlobalWebSearchProviderToggleEnabled`, **DWord**, set to `0`; `scope=user`. This is the state of the "Let search apps show results" toggle (Privacy & security > Search).
- `IsGlobalWebSearchProviderToggleEnabled` turns off all providers of web results in the EEA and stays in the item so that the toggle in Settings shows the same state. Neither value locks the toggle or touches the File Explorer search box.
- Apply doesn't restart File Explorer. The effect was observed after restarting File Explorer and killing SearchHost (StartMenuExperienceHost was also killed for the Start items). The catalog therefore gives the effect as "after you sign in again"; whether restarting File Explorer alone (`tools.restart-explorer`) is enough wasn't checked, and the item says explicitly "restarting File Explorer alone may not be enough".

### Working alternatives (described, without their own catalog items)
Both gave the same effect (Bing results disappear) on Home and Pro with the PL region, after restarting File Explorer:
- `...\SearchSettings\WebSearchProviders`, value `Microsoft.BingSearch_8wekyb3d8bbwe!App` = DWORD 0 (1 by default): narrower, only the Bing provider.
- `HKCU\Software\Policies\Microsoft\Windows\Explorer` `DisableSearchBoxSuggestions` = DWORD 1: a policy; according to Microsoft it also turns off the history of the File Explorer search box.

`BingSearchEnabled` was an alternative here up to 4.0.8; since 2026-11-16 it's a value of the item (above). On a VM with the US region (2026-11-16) `DisableSearchBoxSuggestions` took effect only after signing in again.

Not working (see the separate documents): `ConnectedSearchUseWeb` (`privacy.search-connected-web.md`), `DisableWebSearch` (`privacy.search-disable-web-search.md`).

## Sources
- Test on 26H2 Home and Pro VMs, 2026-10-21 (build 26300.9457): the toggle in Settings, write through the registry API, search results for the phrase "pogoda" (weather).
- Test on 26H2 VMs `DSO-EN-Home` with the US region and `DSO-PL-Home` with the PL region, 2026-11-16 (build 26300.9457): write through the registry API, search results before and after, also after a restart.

## Defaults on a fresh 26H2
Reading from the DSO-26H2-Home and DSO-26H2-Pro VMs, 2026-10-21: the `Search` key exists, `BingSearchEnabled` **no value**; the `SearchSettings` key exists, `IsGlobalWebSearchProviderToggleEnabled` **no value** (toggle on); `WebSearchProviders\Microsoft.BingSearch_8wekyb3d8bbwe!App` = 1.

## Detection and undo
- Detection: both values = DWORD 0 -> `Applied`; either value missing or different -> `NotApplied`. Anyone who applied the item in 4.0.8 will see it as not applied after the update, because `BingSearchEnabled` is missing.
- Undo restores the snapshot from before the change from the journal (a missing value = deleting it; the `Search` and `SearchSettings` keys existed before apply and stay); `original` = `<RemoveEntry>` is only a fallback.

## Risks and warnings
- `risk=safe`, `suggested=true`.
- Warnings in the catalog: "The change takes effect after you sign in again; restarting File Explorer alone may not be enough." and "Search highlights also disappear from the search screen: the picture of the day, the quiz and trending searches."
- Beyond the notes in the program (in the technical details or only here): the effect of restarting File Explorer alone (`tools.restart-explorer`) wasn't checked; in the US region the results disappeared right after writing `BingSearchEnabled`, but the warning stays cautious. In the EEA the item turns off all providers of web results. Not checked: a Microsoft account and the US region on the Pro edition.

## VM test
VM run of 2026-11-16 (build 26300.9457) after adding `BingSearchEnabled`: `DSO-EN-Pro`, `DSO-PL-Pro` and `DSO-EN-Home`, **Passed** on each (12 of 12): case `privacy-search-web-results` with both values, the `Search` and `SearchSettings` keys stay after undo, the Administrator's HKCU unchanged. The run covered only this case.

VM run of 2026-11-05 (build 26300.9457) on Windows 11 26H2 in English and Polish: `DSO-EN-Home` and `DSO-PL-Home` (Core), `DSO-EN-Pro` and `DSO-PL-Pro` (Professional), **Passed** on each: case `privacy-search-web-results`. Full run on each VM: 861 passed, 0 failed, 25 skipped. Earlier runs on Polish Windows below.

Case `privacy-search-web-results` (`Flow=Full`) in `tests/vm/cases.psd1` (`NotWrittenTo` for the `HKCU` of the Administrator process, because the item is per user). VM run on 2026-10-21 (`Invoke-VmTests.ps1`): **DSO-26H2-Home and DSO-26H2-Pro – passed**, steps detect `NotApplied` → apply `Applied` → detect `Applied` → undo `Undone` → detect `NotApplied`, state after apply confirmed by an independent reading, final state = state before (`SnapshotDiff` empty). The Administrator's HKCU unchanged (`NotWrittenTo`), the `SearchSettings` key stayed. A separate VM measurement of 2026-10-21 passed on Home and Pro.

## Observed effect
- **Home** (`DSO-26H2-Home`), 2026-10-21. Method: screenshots of the search results (Win+S) for the phrase "pogoda" (weather) in the `dso` session, after a write through the registry API and restarting File Explorer and SearchHost. Result: the "Microsoft Bing sugestie internetowe" (Microsoft Bing web suggestions) section and the "Microsoft Bing" card disappear from the results; after undo the section comes back.
- **Pro** (`DSO-26H2-Pro`), 2026-10-21. Same method and same result.
- **US region** (`DSO-EN-Home`), 2026-11-16. Method: screenshots of the search results before and after the write. Result: `IsGlobalWebSearchProviderToggleEnabled` = 0 changes nothing, also after a restart; after `BingSearchEnabled` = 0 the web results disappear right away and don't come back after a restart. Same result for `BingSearchEnabled` in the PL region (`DSO-PL-Home`).
