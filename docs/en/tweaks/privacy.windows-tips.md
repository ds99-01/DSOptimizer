<!-- source: docs/tweaks/privacy.windows-tips.md sha256: 61bcd50c8b64144727cd5d3659ea8f078b77f43add4f00967a43cd3e90978741 -->

# privacy.windows-tips – Turn off Windows tips and suggestions

## Verdict for 26H2
Port: the "Get tips and suggestions when using Windows" checkbox is changed by writing one value; a test on a 26H2 VM (Home and Pro) confirmed the checkbox state. Evidence: toggle state in Settings; the content itself wasn't observed.

## What it changes
- `HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager` (in the engine `HKU\<SID of the signed-in user>`), value `SubscribedContent-338389Enabled`, **DWord**, set to `0`; `scope=user`.
- The checkbox is in System > Notifications > Additional settings.
- The `DisableSoftLanding` policy had no effect (the checkbox stays selected), see `privacy.soft-landing.md`.

## Sources
- Test on 26H2 Home and Pro VMs, 2026-10-21 (build 26300.9457, Windows not activated, local account): write through the registry API (.NET `RegistryKey`) and observation in Settings (UI Automation, screenshots).
- Research: `docs/zrodla/2026-10-20-etap4a-zrodla.md` (in Polish), section "Wyniki VM 26H2 (partia 1)" (26H2 VM results, round 1).
- No Microsoft documentation for this value; evidence: VM test (observation below).

## Defaults on a fresh 26H2
Reading from the DSO-26H2-Home and DSO-26H2-Pro VMs, 2026-10-21: `SubscribedContent-338389Enabled` = **1** (checkbox selected); `original` in the catalog = 1.

## Detection and undo
- Detection: all values from "What it changes" equal to the written ones -> `Applied`; missing value or a different value -> `NotApplied`.
- Undo restores the snapshot from before the change from the journal (a missing value = deleting it); `original` from the catalog is only a fallback.
- The `ContentDeliveryManager` key existed before apply and stays.

## Risks and warnings
- `risk=safe`, `suggested=true`.
- No warnings in the catalog.

## VM test
Case `privacy-windows-tips` (`Flow=Full`) in `tests/vm/cases.psd1` (`NotWrittenTo` for the `HKCU` of the Administrator process, because the item is per user; `KeyPresentAfterUndo`, because the key exists on a fresh installation). VM run on 2026-10-21 (`Invoke-VmTests.ps1`, Task 9 of the round): **DSO-26H2-Home and DSO-26H2-Pro – passed**, steps detect `NotApplied` → apply `Applied` → detect `Applied` → undo `Undone` → detect `NotApplied`, state after apply confirmed by an independent reading, final state = state before (`SnapshotDiff` empty). The value 1 from before apply came back from the snapshot, the Administrator's HKCU unchanged. The exploration of 2026-10-21 passed on Home and Pro.

## Observed effect
- **Home** (`DSO-26H2-Home`), 2026-10-21. Method: the "Uzyskaj porady i sugestie podczas korzystania z systemu Windows" (Get tips and suggestions when using Windows) checkbox in Settings (UI Automation) after a write through the API. Result: checkbox cleared; after undo selected. Evidence: toggle state in Settings; the content itself wasn't observed.
- **Pro** (`DSO-26H2-Pro`), 2026-10-21. Same method and same result.

## Reddit
To be added in part E.
