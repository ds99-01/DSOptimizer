<!-- source: docs/tweaks/privacy.feedback-notifications.md sha256: 09beac62c77656e5cc3540d311861b57cca3e0b2a82ab9e49749f4a9dea2ddd2 -->

# privacy.feedback-notifications – Turn off feedback notifications

## Verdict for 26H2
Port: a test on a 26H2 VM (Home and Pro) showed a change in the state of the "Diagnostics & feedback" page in Settings (a banner, the "Feedback frequency" list disappears). The feedback notifications themselves weren't observed.

## What it changes
- `HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection`, value `DoNotShowFeedbackNotifications`, **DWord**, set to `1`; `scope=machine`.
- The `DataCollection` key **exists on a fresh installation**; undo deletes only the value, the key stays (VM case: `KeyPresentAfterUndo`).
- Shared key with `privacy.diagnostic-data-required`; the items are independent.

## Sources
- Microsoft documentation: the `Experience/DoNotShowFeedbackNotifications` policy (Windows 10 1607+; "users will no longer see feedback notifications through the Windows Feedback app"), Group Policy "Do not show feedback notifications", registry from "Manage connections…": `HKLM\Software\Policies\Microsoft\Windows\DataCollection\DoNotShowFeedbackNotifications` = 1.
- Test on 26H2 Home and Pro VMs, 2026-10-21 (build 26300.9457, Windows not activated, local account): write through the registry API (.NET `RegistryKey`) and observation in Settings (UI Automation, screenshots).
- Research: `docs/zrodla/2026-10-20-etap4a-zrodla.md` (in Polish), sections "2d. Feedback frequency" and "Wyniki VM 26H2 (partia 1)" (26H2 VM results, round 1).

## Defaults on a fresh 26H2
Reading from the DSO-26H2-Home and DSO-26H2-Pro VMs, 2026-10-21: the `DataCollection` key exists, `DoNotShowFeedbackNotifications` **no value** (the "Częstotliwość opinii" (Feedback frequency) list visible).

## Detection and undo
- Detection: `DoNotShowFeedbackNotifications` = DWORD 1 -> `Applied`; missing value or a different value -> `NotApplied`.
- Undo restores the snapshot from before the change from the journal (a missing value = deleting the value, the key stays); `original` = `<RemoveEntry>` is only a fallback.

## Risks and warnings
- `risk=safe`, `suggested=true`.
- Warning in the catalog: "Settings will show a message that some settings are managed by your organization." Evidence: page state in Settings; the notifications themselves weren't observed.

## VM test
Case `privacy-feedback-notifications` (`Flow=Full`) in `tests/vm/cases.psd1`: `KeyPresentAfterUndo` for `HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection`. VM run on 2026-10-21 (`Invoke-VmTests.ps1`, Task 9 of the round): **DSO-26H2-Home and DSO-26H2-Pro – passed**, steps detect `NotApplied` → apply `Applied` → detect `Applied` → undo `Undone` → detect `NotApplied`, state after apply confirmed by an independent reading, final state = state before (`SnapshotDiff` empty). The `DataCollection` key survived undo (`KeyPresentAfterUndo`), `DoNotShowFeedbackNotifications` doesn't exist after undo. The exploration of 2026-10-21 passed on Home and Pro.

## Observed effect
- **Home** (`DSO-26H2-Home`, EditionID `Core`), 2026-10-21. Method: UI Automation of Settings (Diagnostics & feedback) and screenshots after a write through the registry API. Result: the "zarządza organizacja" (managed by your organization) banner, the "Częstotliwość opinii" (Feedback frequency) list disappears from the page; the notifications themselves weren't observed. After undo the list comes back and the banner disappears.
- **Pro** (`DSO-26H2-Pro`, EditionID `Professional`), 2026-10-21. Same method and same result.
