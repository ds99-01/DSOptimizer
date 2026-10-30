<!-- source: docs/tweaks/privacy.start-recommended-section.md sha256: fa49665f12fb57e16651839d9da6527d71a154219826afe13fe636cd1318febc -->

# privacy.start-recommended-section – Hide the "Recommended" section in the Start menu

## Verdict for 26H2
Port: the `HideRecommendedSection` policy hides the "Recommended" section in Start; a test on a 26H2 VM (Home and Pro) showed a real effect. Non-suggested item (R12): the section also shows recently opened files, which can be useful every day.

## What it changes
- `HKCU\Software\Policies\Microsoft\Windows\Explorer` (in the engine `HKU\<SID of the signed-in user>`), value `HideRecommendedSection`, **DWord**, set to `1`; `scope=user`.
- The section disappears from the Start menu after you sign in again and after a computer restart (the observation on the VM was after restarting File Explorer and killing SearchHost and StartMenuExperienceHost); after undo it comes back. Apply doesn't restart anything (R15); whether `tools.restart-explorer` alone is enough wasn't checked (the warning says "restarting File Explorer alone may not be enough").
- The same value in `HKLM\SOFTWARE\Policies\Microsoft\Windows\Explorer` gives the same effect (described, without its own item); the item uses the user scope.
- `HideRecommendedPersonalizedSites` is included in this item (see `privacy.recommended-personalized-sites.md`).

## Sources
- Test on 26H2 Home and Pro VMs, 2026-10-21 (build 26300.9457, Windows not activated, local account): write through the registry API (.NET `RegistryKey`) and observation in Settings (UI Automation, screenshots).
- Research: `docs/zrodla/2026-10-20-etap4a-zrodla.md` (in Polish), section "Wyniki VM 26H2 (partia 1)" (26H2 VM results, round 1).
- No Microsoft documentation for this value; evidence: VM test (observation below).

## Defaults on a fresh 26H2
Reading from the DSO-26H2-Home and DSO-26H2-Pro VMs, 2026-10-21: value `HideRecommendedSection` **none** and the `...\Policies\Microsoft\Windows\Explorer` **key missing** (the parent key `...\Policies\Microsoft\Windows` exists).

## Detection and undo
- Detection: `HideRecommendedSection` = DWORD 1 -> `Applied`; missing value or a different value -> `NotApplied`.
- Undo restores the snapshot from before the change from the journal: the value and the `Explorer` key, which didn't exist before apply, again don't exist after undo (Review Focus 4); `original` = `<RemoveEntry>` is only a fallback.

## Risks and warnings
- `risk=safe`, `suggested=false` (R12).
- Warnings in the catalog: "Recently opened files disappear from the Start menu together with the section." and "The change takes effect after you sign in again; restarting File Explorer alone may not be enough."
- Beyond the notes in the program (in the technical details or only here): the effect of restarting File Explorer alone (`tools.restart-explorer`) wasn't checked.

## VM test
Case `privacy-start-recommended-section` (`Flow=Full`) in `tests/vm/cases.psd1` (`KeyAbsentAfterUndo` for the user's `Policies\...\Explorer` key, `NotWrittenTo` for the `HKCU` of the Administrator process). VM run on 2026-10-21 (`Invoke-VmTests.ps1`, Task 9 of the round): **DSO-26H2-Home and DSO-26H2-Pro – passed**, steps detect `NotApplied` → apply `Applied` → detect `Applied` → undo `Undone` → detect `NotApplied`, state after apply confirmed by an independent reading, final state = state before (`SnapshotDiff` empty). The user's `Policies\Microsoft\Windows\Explorer` key created by apply doesn't exist after undo (`KeyAbsentAfterUndo`), the Administrator's HKCU unchanged. The exploration of 2026-10-21 passed on Home and Pro.

## Observed effect
- **Home** (`DSO-26H2-Home`), 2026-10-21. Method: screenshot of the Start menu before and after writing the policy and restarting File Explorer, and also after restarting the VM. Result: the "Proponowane" (Recommended) section disappears from Start (after restarting File Explorer and after restarting the VM); after undo it comes back.
- **Pro** (`DSO-26H2-Pro`), 2026-10-21. Same method and same result.

## Reddit
To be added in part E.
