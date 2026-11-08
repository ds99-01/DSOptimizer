<!-- source: docs/tweaks/privacy.lockscreen-spotlight.md sha256: ba30cc18202f2dd422dc984281fe85a689129c487de3ee9d446b18adb0fd446d -->

# privacy.lockscreen-spotlight – Turn off Windows spotlight on the lock screen

## Verdict for 26H2
Port as a **non-suggested** item (`risk=caution`): writing one value switches the "Personalize your lock screen" list from "Windows spotlight" to "Picture"; a test on a 26H2 VM (Home and Pro) confirmed the state of the list, and undo brought back "Windows spotlight". The lock screen picture itself wasn't observed.

## What it changes
- `HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager` (in the engine `HKU\<SID of the signed-in user>`), value `SubscribedContent-338387Enabled`, **DWord**, set to `0`; `scope=user`.
- The effect is visible in a newly opened Settings window, without restarting File Explorer and without signing in again.
- The second value from the same sources, `RotatingLockScreenOverlayEnabled` = 0, isn't included: written alone, it changed nothing on Home or Pro (the list before the write, after the write and after undo = Windows spotlight). On a clean VM its value is 1.

## Sources
- Test on 26H2 Home and Pro VMs, 2026-11-07 (build 26300.9457, Windows not activated, local account): write through the registry API (.NET) and observation of the lock screen page in Settings (UI Automation, screenshots).
- Win11Debloat: https://github.com/Raphire/Win11Debloat (branch master), file `Config/Features.json`, lines 487–492 (`DisableLockscreenTips`), and `Regfiles/Disable_Lockscreen_Tips.reg`, lines 2–5; accessed 2026-11-06.
- No Microsoft documentation for this value; evidence: VM test (observation below).

## Defaults on a fresh 26H2
Reading from the `DSO-EN-Home` and `DSO-EN-Pro` VMs, 2026-11-07: the `ContentDeliveryManager` key exists, `SubscribedContent-338387Enabled` **none** (list = Windows spotlight); `RotatingLockScreenOverlayEnabled` = 1 (the item doesn't change this value).

## Detection and undo
- Detection: `SubscribedContent-338387Enabled` = DWORD 0 -> `Applied`; missing value or a different value -> `NotApplied`.
- Undo restores the snapshot from before the change from the journal (a missing value = deleting it); `original` from the catalog (`<RemoveEntry>`) is only a fallback. After undo the list went back to "Windows spotlight".
- The `ContentDeliveryManager` key existed before apply and stays (`KeyPresentAfterUndo`).

## Risks and warnings
- `risk=caution`, `suggested=false`: the lock screen loses Windows spotlight (the list switches to "Picture").
- Warning in the catalog: "Turns off Windows spotlight on the lock screen – instead of pictures of the day, you get a regular picture."
- Relation to `privacy.spotlight` (desktop background): that item's policy (`DisableWindowsSpotlightFeatures` = 1) didn't change the "Personalize your lock screen" list on Home or Pro, even after a VM restart. The two items write different values and act on different places: this one on the lock screen, the other one on the desktop background.
- The "Get fun facts, tips, tricks, and more on your lock screen" checkbox appears after the write, turned off; its Polish label wasn't read.

## VM test
Case `privacy-lockscreen-spotlight` (`Flow=Full`) in `tests/vm/cases.psd1`; VM run of 2026-11-08 (build 26300.9457, `Invoke-VmTests.ps1`): **Passed** on `DSO-EN-Home` (Core), `DSO-EN-Pro` and `DSO-PL-Pro` (Professional). Full runs: 915 passed, 2 failed, 25 skipped on both English VMs and 917 passed, 0 failed, 25 skipped on the Polish one. Both failures were in the cases of the two notification items (`privacy.toast-suggested`, `privacy.toast-backup-reminder`; described in their documentation), not this item.

## Observed effect
- **Home** (`DSO-EN-Home`), 2026-11-07. Method: the `ms-settings:lockscreen` page in a newly opened Settings window (UI Automation, screenshots) before writing `SubscribedContent-338387Enabled` alone, after the write and after undo. Result: before the write the "Personalize your lock screen" list ("Personalizuj ekran blokady" in Polish Windows) = "Windows spotlight" ("W centrum uwagi Windows"), no "fun facts" checkbox; after the write the list = "Picture", and "Choose a photo", "Browse photos" and the "Get fun facts…" checkbox (off) appear; after undo "Windows spotlight" again, no "fun facts" checkbox. An earlier run with both values at once gave the same result.
- **Pro** (`DSO-EN-Pro`), 2026-11-07. Same method and same result.
- Windows on the VM isn't activated, so all controls on the page were greyed out: this is a reading of the list state, not a click.
- Not checked: a change of the lock screen picture itself. The preview on the page was the same before and after the write (the default Windows picture), and the lock screen wasn't invoked, because it would lock the session used for the observation. On activated Windows the page may look different. The Polish label of the "Picture" option wasn't read.
