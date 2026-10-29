<!-- source: docs/tweaks/privacy.start-recommendations.md sha256: 3a8e5aa01574844bc6b50b3c01b63e7ee40e2c04022fee84b6146ca831085b53 -->

# privacy.start-recommendations – Turn off recommendations in the Start menu

## Verdict for 26H2
Port: the Start recommendations toggle is changed by writing one value; a test on a 26H2 VM (Home and Pro) confirmed the toggle state. Evidence: toggle state in Settings; the content itself wasn't observed (the Start menu had no content of this type, so no change is visible in the menu).

## What it changes
- `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced` (in the engine `HKU\<SID of the signed-in user>`), value `Start_IrisRecommendations`, **DWord**, set to `0`; `scope=user`.
- Settings shows the new state right away; the catalog describes the Start menu view (based on the observation on the VM after restarting File Explorer and killing SearchHost and StartMenuExperienceHost) as visible after you sign in again. Apply doesn't restart anything (R15); whether `tools.restart-explorer` alone is enough wasn't checked (warning: "may not be enough, not checked").
- Note for UI tests: on Home, after the first restart with the network on, a new version of the Start menu turned on (in Settings the control's `AutomationId` is `SystemSettings_Start_IrisRecommendations_CheckBox` instead of `_ToggleSwitch`); the registry value doesn't change.

## Sources
- Test on 26H2 Home and Pro VMs, 2026-10-21 (build 26300.9457, Windows not activated, local account): write through the registry API (.NET `RegistryKey`) and observation in Settings (UI Automation, screenshots).
- Research: `docs/zrodla/2026-10-20-etap4a-zrodla.md` (in Polish), section "Wyniki VM 26H2 (partia 1)" (26H2 VM results, batch 1).
- No Microsoft documentation for this value; evidence: VM test (observation below).

## Defaults on a fresh 26H2
Reading from the DSO-26H2-Home and DSO-26H2-Pro VMs, 2026-10-21: the `Explorer\Advanced` key exists, `Start_IrisRecommendations` **none** (toggle on).

## Detection and undo
- Detection: all values from "What it changes" equal to the written ones -> `Applied`; missing value or a different value -> `NotApplied`.
- Undo restores the snapshot from before the change from the journal (a missing value = deleting it); `original` from the catalog is only a fallback.
- The `Explorer\Advanced` key existed before apply and stays.

## Risks and warnings
- `risk=safe`, `suggested=true` (R12: stays suggested).
- Warning in the catalog: "The change takes effect after you sign in again; restarting File Explorer alone may not be enough."
- Beyond the notes in the program (in the technical details or only here): the effect of restarting File Explorer alone (`tools.restart-explorer`) wasn't checked.

## VM test
Case `privacy-start-recommendations` (`Flow=Full`) in `tests/vm/cases.psd1` (`NotWrittenTo` for the `HKCU` of the Administrator process, because the item is per user; `KeyPresentAfterUndo`, because the key exists on a fresh installation). VM run on 2026-10-21 (`Invoke-VmTests.ps1`, Task 9 of the batch): **DSO-26H2-Home and DSO-26H2-Pro – passed**, steps detect `NotApplied` → apply `Applied` → detect `Applied` → undo `Undone` → detect `NotApplied`, state after apply confirmed by an independent reading, final state = state before (`SnapshotDiff` empty). The Administrator's HKCU unchanged, the `Explorer\Advanced` key stayed (`KeyPresentAfterUndo`). The exploration of 2026-10-21 passed on Home and Pro.

## Observed effect
- **Home** (`DSO-26H2-Home`), 2026-10-21. Method: the recommendations toggle in Settings (Personalization > Start; UI Automation) after a write through the API and restarting File Explorer. Result: toggle "wyłączone" (Off); the Start menu had no content of this type, so no change is visible in the menu. Evidence: toggle state in Settings; the content itself wasn't observed.
- **Pro** (`DSO-26H2-Pro`), 2026-10-21. Same method and same result.

## Reddit
To be added in part E.
