<!-- source: docs/tweaks/privacy.spotlight.md sha256: d71e84effca7565e839a3ba067c608b216cf4d7df5af9a3a3c5b622a6e11bf29 -->

# privacy.spotlight – Turn off Windows spotlight (pictures of the day)

## Verdict for 26H2
Port as a **non-suggested** item with a warning (Dawid's decision 2026-10-21, question 1 after batch 1). There is an effect: the desktop background stops being a picture of the day. Undo removes the policy, but doesn't bring back the "Windows spotlight" background (which is why the item was dropped in batch 1, R9). The warning in the catalog says that the pictures of the day have to be chosen again in Settings > Personalization > Background.

## What it changes
- `HKCU\Software\Policies\Microsoft\Windows\CloudContent` (in the engine `HKU\<SID of the signed-in user>`), value `DisableWindowsSpotlightFeatures`, **DWord**, set to `1`; `scope=user`.
- The effect is visible right away, without restarting File Explorer and without signing in again.

## Sources
- Test on 26H2 Home and Pro VMs, 2026-10-21 (build 26300.9457, Windows not activated, local account): write through the registry API (.NET `RegistryKey`) and observation in Settings (UI Automation, screenshots).
- Research: `docs/zrodla/2026-10-20-etap4a-zrodla.md` (in Polish), section "Wyniki VM 26H2 (partia 1)" (26H2 VM results, batch 1), row "Spotlight, wszystko" (Spotlight, everything).
- Microsoft (ADMX/CSP `Experience/AllowWindowsSpotlight`) lists this policy as ❌ Pro, ✅ Enterprise/Education; on the 26H2 VM, however, the effect on the desktop was the same on Home and Pro, so the observation counts (Stage 4a rule).

## Defaults on a fresh 26H2
Reading from the VMs, 2026-10-21: neither the user's `...\Policies\Microsoft\Windows\CloudContent` key nor the value exists (Home and Pro); desktop background = "W centrum uwagi Windows" (Windows spotlight).

## Detection and undo
- Detection: `DisableWindowsSpotlightFeatures` = DWORD 1 -> `Applied`; missing value or a different value -> `NotApplied`.
- Undo restores the snapshot from before the change from the journal (a missing value = deleting it) and deletes the `CloudContent` key if apply created it (empty).
- **Undo doesn't restore the background**: after undo the background stays "Picture". The registry returns to the state from before the change, but the background choice in Settings doesn't.

## Risks and warnings
- `risk=caution` (undo doesn't restore the whole effect), `suggested=false`.
- Warning in the catalog: "Undo doesn't bring back the pictures of the day: choose them again in Settings > Personalization > Background."
- Beyond the notes in the program (in the technical details or only here): the effect on the lock screen wasn't checked.
- Choosing "Windows spotlight" again after undo wasn't checked: Windows not activated on the VM blocks personalization.

## VM test
Case `privacy-spotlight` (`Flow=Full`) in `tests/vm/cases.psd1` (`NotWrittenTo` for the `HKCU` of the Administrator process, because the item is per user; `KeyAbsentAfterUndo`, because the user's `CloudContent` key doesn't exist on a fresh installation). VM run on 2026-10-21 (`Invoke-VmTests.ps1` with only this case): **DSO-26H2-Home and DSO-26H2-Pro – passed (12/0)**, steps detect `NotApplied` → apply `Applied` → detect `Applied` → undo `Undone` → detect `NotApplied`, state after apply confirmed by an independent reading, the write went to the signed-in user's HKU, not to the process's HKCU, the `CloudContent` key doesn't exist after undo, final state = state before. The case checks the registry, not the background (background: observation below).

## Observed effect
- **Home** (`DSO-26H2-Home`), 2026-10-21. Method: screenshots of the desktop background and Settings before writing the policy, after writing it and after undo. Result: the background switches from "W centrum uwagi Windows" (Windows spotlight) to "Obraz" (Picture) (the "Dowiedz się więcej o tym zdjęciu" (Learn about this picture) icon disappears); **after undo the background stays "Obraz" (Picture)**. The lock screen couldn't be checked (Windows not activated blocks personalization).
- **Pro** (`DSO-26H2-Pro`), 2026-10-21. Same method and same result.

## Reddit
To be added in part E.
