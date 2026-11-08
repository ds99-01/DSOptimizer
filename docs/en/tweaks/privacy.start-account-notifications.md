<!-- source: docs/tweaks/privacy.start-account-notifications.md sha256: 97b97c829ad0039eed2000efcbefd2d1af6bbe2220eb9c105ea6def432bf2a44 -->

# privacy.start-account-notifications – Turn off account notifications in the Start menu

## Verdict for 26H2
Port: the "Show account-related notifications" toggle (Personalization > Start) is changed by writing one value; a test on a 26H2 VM (Home and Pro) confirmed the toggle state. Evidence: toggle state in Settings; the content itself wasn't observed.

## What it changes
- `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced` (in the engine `HKU\<SID of the signed-in user>`), value `Start_AccountNotifications`, **DWord**, set to `0`; `scope=user`.
- Mechanism of the `SystemSettings_Start_AccountNotifications_ToggleSwitch` toggle from a registry diff on the VM: turning it off writes `Start_AccountNotifications` = 0, turning it on writes 1. On a clean VM the value doesn't exist and the toggle is on.
- The toggle in Settings is off right away, even before File Explorer restarts.

## Sources
- Test on 26H2 Home and Pro VMs, 2026-11-07 (build 26300.9457, Windows not activated, local account): write through the registry API (.NET), a registry diff after switching the toggle both ways and observation in Settings (UI Automation, screenshots).
- Win11Debloat: https://github.com/Raphire/Win11Debloat (branch master), file `Regfiles/Disable_Windows_Suggestions.reg`, lines 40–42; accessed 2026-11-06.
- No Microsoft documentation for this value; evidence: VM test (observation below).

## Defaults on a fresh 26H2
Reading from the `DSO-EN-Home` and `DSO-EN-Pro` VMs, 2026-11-07: the `Explorer\Advanced` key exists, `Start_AccountNotifications` **none** (toggle on).

## Detection and undo
- Detection: `Start_AccountNotifications` = DWORD 0 -> `Applied`; missing value or a different value (including 1 written by the toggle) -> `NotApplied`.
- Undo restores the snapshot from before the change from the journal (a missing value = deleting it, 1 = toggle on); `original` from the catalog (`<RemoveEntry>`) is only a fallback.
- The `Explorer\Advanced` key existed before apply and stays (`KeyPresentAfterUndo`).

## Risks and warnings
- `risk=safe`, `suggested=true`: turns off account notifications in Start; no loss of function was observed.
- No warnings in the catalog.

## VM test
Case `privacy-start-account-notifications` (`Flow=Full`) in `tests/vm/cases.psd1`; VM run of 2026-11-08 (build 26300.9457, `Invoke-VmTests.ps1`): **Passed** on `DSO-EN-Home` (Core), `DSO-EN-Pro` and `DSO-PL-Pro` (Professional). Full runs: 915 passed, 2 failed, 25 skipped on both English VMs and 917 passed, 0 failed, 25 skipped on the Polish one. Both failures were in the cases of the two notification items (`privacy.toast-suggested`, `privacy.toast-backup-reminder`; described in their documentation), not this item.

## Observed effect
- **Home** (`DSO-EN-Home`), 2026-11-07. Method: the `ms-settings:personalization-start` page in a newly opened Settings window (UI Automation, screenshots), the "Show account-related notifications" toggle ("Pokaż powiadomienia związane z kontem" in Polish Windows) before the write, after the write, after File Explorer restarts and after undo. Result: before the write on (no value); after the write off, and still off after File Explorer restarted; after undo on. Evidence: toggle state in Settings; the content itself wasn't observed.
- **Pro** (`DSO-EN-Pro`), 2026-11-07. Same method and same result.
- Undo in the test restored the value 1, because the toggle had been switched both ways once before the item's write (reading the mechanism); the state "no value = toggle on" is visible in the reading from before the switching.
- Not checked: account notifications in the Start menu itself (local account; Start menu screenshots without assessing a difference).
