<!-- source: docs/tweaks/privacy.tailored-experiences.md sha256: c29e1f4a26f71483354e708b027c7019a271ffa711bc8280c8adb917e5e1a470 -->

# privacy.tailored-experiences – Turn off personalized offers

## Verdict for 26H2
Port: the "Personalized offers" toggle is changed by writing one value; a test on a 26H2 VM (Home and Pro) confirmed that the toggle matches the value. Evidence: toggle state in Settings; the content itself wasn't observed.

## What it changes
- `HKCU\Software\Microsoft\Windows\CurrentVersion\Privacy` (in the engine `HKU\<SID of the signed-in user>`), value `PersonalizedOffersEnabled`, **DWord**, set to `0`; `scope=user`.
- Turning the toggle on writes `1`.

### Working alternative (described, without its own catalog item)
`HKCU\Software\Policies\Microsoft\Windows\CloudContent`, `DisableTailoredExperiencesWithDiagnosticData` = DWORD 1: on Home and Pro it shows a banner saying the settings are managed by your organization and grays out the "Personalized offers" toggle. It doesn't go into the catalog, because it takes away the user's ability to switch the toggle (spec 5.2: we don't take the choice away).

## Sources
- Test on 26H2 Home and Pro VMs, 2026-10-21 (build 26300.9457, Windows not activated, local account): write through the registry API (.NET `RegistryKey`) and observation in Settings (UI Automation, screenshots).
- Research: `docs/zrodla/2026-10-20-etap4a-zrodla.md` (in Polish), section "Wyniki VM 26H2 (partia 1)" (26H2 VM results, batch 1).
- No Microsoft documentation for this value; evidence: VM test (observation below).

## Defaults on a fresh 26H2
Reading from the DSO-26H2-Home and DSO-26H2-Pro VMs, 2026-10-21: the `Privacy` key exists, `PersonalizedOffersEnabled` **none** (toggle off). Detection (value = DWORD 0) will therefore show `NotApplied`, even though the toggle is already off; apply writes an explicit `0`.

## Detection and undo
- Detection: all values from "What it changes" equal to the written ones -> `Applied`; missing value or a different value -> `NotApplied`.
- Undo restores the snapshot from before the change from the journal (a missing value = deleting it); `original` from the catalog is only a fallback.
- The `Privacy` key existed before apply and stays.

## Risks and warnings
- `risk=safe`, `suggested=true` (R13).
- No warnings in the catalog.

## VM test
Case `privacy-tailored-experiences` (`Flow=Full`) in `tests/vm/cases.psd1` (`NotWrittenTo` for the `HKCU` of the Administrator process, because the item is per user; `KeyPresentAfterUndo`, because the key exists on a fresh installation). VM run on 2026-10-21 (`Invoke-VmTests.ps1`, Task 9 of the batch): **DSO-26H2-Home and DSO-26H2-Pro – passed**, steps detect `NotApplied` → apply `Applied` → detect `Applied` → undo `Undone` → detect `NotApplied`, state after apply confirmed by an independent reading, final state = state before (`SnapshotDiff` empty). After undo the `PersonalizedOffersEnabled` value again doesn't exist, the `Privacy` key stayed. The exploration of 2026-10-21 passed on Home and Pro.

## Observed effect
- **Home** (`DSO-26H2-Home`), 2026-10-21. Method: the "Spersonalizowane oferty" (Personalized offers) toggle in Settings (UI Automation) after a write through the API. Result: the toggle matches the written value (no value = off, `1` = on). Evidence: toggle state in Settings; the content itself wasn't observed.
- **Pro** (`DSO-26H2-Pro`), 2026-10-21. Same method and same result.

## Reddit
To be added in part E.
