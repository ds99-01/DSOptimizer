<!-- source: docs/tweaks/privacy.settings-account-notifications.md sha256: 5cfa9d825158513a6d82d6debaee04b2232965f29fff1afe1e63687bfd293b6f -->

# privacy.settings-account-notifications – Turn off account notifications in Settings

## Verdict for 26H2
Port: the "Show notifications in Settings" toggle is changed by writing one value; a test on a 26H2 VM (Home and Pro) confirmed the toggle state. Evidence: toggle state in Settings; the content itself wasn't observed.

## What it changes
- `HKCU\Software\Microsoft\Windows\CurrentVersion\SystemSettings\AccountNotifications` (in the engine `HKU\<SID of the signed-in user>`), value `EnableAccountNotifications`, **DWord**, set to `0`; `scope=user`.

## Sources
- Test on 26H2 Home and Pro VMs, 2026-10-21 (build 26300.9457, Windows not activated, local account): write through the registry API (.NET `RegistryKey`) and observation in Settings (UI Automation, screenshots).
- Research: `docs/zrodla/2026-10-20-etap4a-zrodla.md` (in Polish), section "Wyniki VM 26H2 (partia 1)" (26H2 VM results, round 1).
- No Microsoft documentation for this value; evidence: VM test (observation below).

## Defaults on a fresh 26H2
Reading from the DSO-26H2-Home and DSO-26H2-Pro VMs, 2026-10-21: the `AccountNotifications` key exists, `EnableAccountNotifications` **none** (toggle on).

## Detection and undo
- Detection: all values from "What it changes" equal to the written ones -> `Applied`; missing value or a different value -> `NotApplied`.
- Undo restores the snapshot from before the change from the journal (a missing value = deleting it); `original` from the catalog is only a fallback.
- The `AccountNotifications` key existed before apply and stays.

## Risks and warnings
- `risk=safe`, `suggested=true`.
- No warnings in the catalog.

## VM test
Case `privacy-settings-account-notifications` (`Flow=Full`) in `tests/vm/cases.psd1` (`NotWrittenTo` for the `HKCU` of the Administrator process, because the item is per user; `KeyPresentAfterUndo`, because the key exists on a fresh installation). VM run on 2026-10-21 (`Invoke-VmTests.ps1`, Task 9 of the round): **DSO-26H2-Home and DSO-26H2-Pro – passed**, steps detect `NotApplied` → apply `Applied` → detect `Applied` → undo `Undone` → detect `NotApplied`, state after apply confirmed by an independent reading, final state = state before (`SnapshotDiff` empty). The Administrator's HKCU unchanged, the `AccountNotifications` key stayed (`KeyPresentAfterUndo`). The exploration of 2026-10-21 passed on Home and Pro.

## Observed effect
- **Home** (`DSO-26H2-Home`), 2026-10-21. Method: the "Pokaż powiadomienia w ustawieniach" (Show notifications in Settings) toggle in Settings (UI Automation) after a write through the API. Result: toggle "wyłączone" (Off); after undo "włączone" (On). Evidence: toggle state in Settings; the content itself wasn't observed.
- **Pro** (`DSO-26H2-Pro`), 2026-10-21. Same method and same result.
