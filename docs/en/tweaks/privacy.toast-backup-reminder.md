<!-- source: docs/tweaks/privacy.toast-backup-reminder.md sha256: c521e5f49a8e9bf8a33c56ac9a54a2c6bb47d9cd7211ac4e62266338e8994338 -->

# privacy.toast-backup-reminder – Turn off system backup reminders

## Verdict for 26H2
Port as a **non-suggested** item: writing one value adds the system backup reminder sender ("Backup settings" in English Windows) to the notification sender list (System > Notifications) with its toggle off; a test on a 26H2 VM (Home and Pro) confirmed the toggle state. Evidence: toggle state in Settings; the content itself wasn't observed. Not suggested, because backup reminders stop working.

## What it changes
- `HKCU\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\Windows.SystemToast.BackupReminder` (in the engine `HKU\<SID of the signed-in user>`), value `Enabled`, **DWord**, set to `0`; `scope=user`.
- The effect is visible in a newly opened Settings window, without restarting File Explorer and without signing in again.

## Sources
- Test on 26H2 Home and Pro VMs, 2026-11-07 (build 26300.9457, Windows not activated, local account): write through the registry API (.NET) and observation of the notification sender list in Settings (UI Automation, screenshots).
- Win11Debloat: https://github.com/Raphire/Win11Debloat (branch master), file `Regfiles/Disable_Windows_Suggestions.reg`, lines 43–45; accessed 2026-11-06.
- No Microsoft documentation for this value; evidence: VM test (observation below).

## Defaults on a fresh 26H2
Reading from the `DSO-EN-Home` and `DSO-EN-Pro` VMs, 2026-11-07: the `Windows.SystemToast.BackupReminder` key and the `Enabled` value: **none** (the sender isn't in the list).

## Detection and undo
- Detection: `Enabled` = DWORD 0 -> `Applied`; missing value or a different value -> `NotApplied`.
- Undo restores the snapshot from before the change from the journal: the previous value, and if there was none, it deletes the value together with the `Windows.SystemToast.BackupReminder` key if apply created that key (`KeyAbsentAfterUndo`); `original` from the catalog (`<RemoveEntry>`) is only a fallback.
- On a fresh VM the `Notifications\Settings` key wasn't there right after sign-in; Windows created it a few minutes later (in the VM test together with the first system notification). If it existed before apply, it stays after undo (`KeyPresentAfterUndo`); if apply created it and it is empty, undo removes it together with the sender key.
- If apply created the `Notifications\Settings` key and it holds other entries at the time of undo (for example written by Windows), undo removes the value and the empty sender key, and the `Notifications\Settings` key stays; the undo succeeds, and the program reports a warning that the key remained after undo because it isn't empty.

## Risks and warnings
- `risk=safe`, `suggested=false`: turns off one notification category; no loss of other functions was observed.
- Warning in the catalog: "Backup reminders will stop appearing – keep them if you don't back up in another way."
- On Pro, in another phase of the test, a OneDrive notification "Turn On Windows Backup" appeared. It had a different sender (OneDrive), not this category.
- The Polish name of this sender wasn't read (the sender appears only after the write, and the value wasn't written on the Polish VM), so the program's Polish texts describe it without a name.

## VM test
Case `privacy-toast-backup-reminder` (`Flow=Full`) in `tests/vm/cases.psd1`; VM run of 2026-11-08 (build 26300.9457, `Invoke-VmTests.ps1`): **Passed** on `DSO-PL-Pro` (Professional) in the full run (917 passed, 0 failed, 25 skipped). On `DSO-EN-Home` (Core) and `DSO-EN-Pro` (Professional) the first full run (915 passed, 2 failed, 25 skipped) failed this case after the undo. The cause was an assumption in the test, not the item's behavior: the test assumed that the `Notifications\Settings` key exists before the change, while on a fresh VM it wasn't there right after sign-in (Windows created it a few minutes later). The case was fixed so that it provides this key itself before the change; rerun on `DSO-EN-Home` (both notification cases) it passed: **Passed**, 0 failed. The fixed case was not rerun separately on `DSO-EN-Pro`.

## Observed effect
- **Home** (`DSO-EN-Home`), 2026-11-07. Method: the `ms-settings:notifications` page in a newly opened Settings window (UI Automation, screenshots), the sender list, the `Windows.SystemToast.BackupReminder1_ToggleSwitch` toggle, before the write, after the write and after undo. Result: before the write the "Backup settings" sender wasn't in the list; after the write it was there with its toggle off; after undo it was gone again. Evidence: toggle state in Settings; the content itself wasn't observed.
- **Pro** (`DSO-EN-Pro`), 2026-11-07. Same method and same result.
- Not checked: the backup reminder itself from this sender; the effect is indirect (the sender's state in the list).
