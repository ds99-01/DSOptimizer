<!-- source: docs/tweaks/privacy.settings-suggestions.md sha256: a804060897fa78c0c8eda653bd720f15e1af7328a93dd4c190e63ef90091787f -->

# privacy.settings-suggestions – Turn off recommendations and offers in Settings

## Verdict for 26H2
Port: the "Recommendations and offers in Settings" toggle (Privacy & security) is switched with three values; a test on a 26H2 VM (Home and Pro) confirmed the toggle state. Evidence: toggle state in Settings; the content itself wasn't observed.

## What it changes
- `HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager` (in the engine `HKU\<SID of the signed-in user>`), three **DWord** values set to `0`: `SubscribedContent-338393Enabled`, `SubscribedContent-353694Enabled`, `SubscribedContent-353696Enabled`; `scope=user`.
- After the three values are written the toggle shows "Off"; writing `1` = "On".
- Apply doesn't restart anything and doesn't require restarting anything.

## Sources
- Test on 26H2 Home and Pro VMs, 2026-10-21 (build 26300.9457, Windows not activated, local account): write through the registry API (.NET `RegistryKey`) and observation in Settings (UI Automation, screenshots).
- Research: `docs/zrodla/2026-10-20-etap4a-zrodla.md` (in Polish), section "Wyniki VM 26H2 (partia 1)" (26H2 VM results, round 1).
- No Microsoft documentation for this value; evidence: VM test (observation below).

## Defaults on a fresh 26H2
Reading from the DSO-26H2-Home and DSO-26H2-Pro VMs, 2026-10-21: the `ContentDeliveryManager` key exists, all three values **none** (toggle on).

## Detection and undo
- Detection: all values from "What it changes" equal to the written ones -> `Applied`; missing value or a different value -> `NotApplied`.
- Undo restores the snapshot from before the change from the journal (a missing value = deleting it); `original` from the catalog is only a fallback.
- The `ContentDeliveryManager` key existed before apply and stays.

## Risks and warnings
- `risk=safe`, `suggested=true`.
- No warnings in the catalog. The item doesn't lock the toggle (it writes the toggle's values, not a policy).

## VM test
Case `privacy-settings-suggestions` (`Flow=Full`) in `tests/vm/cases.psd1` (`NotWrittenTo` for the `HKCU` of the Administrator process, because the item is per user; `KeyPresentAfterUndo`, because the key exists on a fresh installation). VM run on 2026-10-21 (`Invoke-VmTests.ps1`, Task 9 of the round): **DSO-26H2-Home and DSO-26H2-Pro – passed**, steps detect `NotApplied` → apply `Applied` → detect `Applied` → undo `Undone` → detect `NotApplied`, state after apply confirmed by an independent reading, final state = state before (`SnapshotDiff` empty). The Administrator's HKCU unchanged, the `ContentDeliveryManager` key stayed (`KeyPresentAfterUndo`). The exploration of 2026-10-21 passed on Home and Pro.

## Observed effect
- **Home** (`DSO-26H2-Home`), 2026-10-21. Method: the "Rekomendacje i oferty w ustawieniach" (Recommendations and offers in Settings) toggle in Settings (UI Automation) after writing the three values through the API. Result: toggle "wyłączone" (Off); writing `1` = "włączone" (On). Evidence: toggle state in Settings; the content itself wasn't observed.
- **Pro** (`DSO-26H2-Pro`), 2026-10-21. Same method and same result.

## Reddit
To be added in part E.
