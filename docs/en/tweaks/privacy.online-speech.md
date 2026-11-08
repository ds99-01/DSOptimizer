<!-- source: docs/tweaks/privacy.online-speech.md sha256: 729247d0d516ea4fb5e29cab4b8008e3acacffa6ba233f32732bc0cc11c19caa -->

# privacy.online-speech – Turn off online speech recognition

## Verdict for 26H2
Port as a **non-suggested** item, based on reverse evidence: on a clean 26H2 VM (Home and Pro) the online speech recognition toggle is already off, so writing `0` changes nothing on screen; a test with the opposite value showed that `HasAccepted` controls the toggle both ways (`1` = on, `0` = off). The item makes sense when the user turned online speech recognition on earlier. Evidence: toggle state in Settings; the content itself wasn't observed.

## What it changes
- `HKCU\Software\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy` (in the engine `HKU\<SID of the signed-in user>`), value `HasAccepted`, **DWord**, set to `0`; `scope=user`.
- Mechanism of the toggle from a registry diff on the VM: switching it from off to on creates the `Settings` and `OnlineSpeechPrivacy` keys and writes `HasAccepted` = 1; switching it from on to off writes `HasAccepted` = 0.
- On a clean VM there are no `Speech_OneCore\Settings` and `Settings\OnlineSpeechPrivacy` keys (the `Speech_OneCore` key exists). Apply creates both keys; undo deletes the value and the `Settings` key together with `OnlineSpeechPrivacy` if apply created them.
- No restart: the toggle state shows in a newly opened Settings window.

## Sources
- Test on 26H2 Home and Pro VMs, 2026-11-07 (build 26300.9457, Windows not activated, local account, no network): write through the registry API (.NET), a registry diff after switching the toggle both ways, a write of the opposite value (`HasAccepted` = 1) and observation in Settings (UI Automation, screenshots).
- Win11Debloat: https://github.com/Raphire/Win11Debloat (branch master), file `Regfiles/Disable_Telemetry.reg`, lines 8–10; accessed 2026-11-06.
- WinUtil: https://github.com/ChrisTitusTech/winutil (branch main), file `config/tweaks.json`, entry `OnlineSpeechPrivacy` (around lines 480–490, approximate line number); accessed 2026-11-06.
- No Microsoft documentation for this value; evidence: VM test (observation below).

## Defaults on a fresh 26H2
Reading from the `DSO-EN-Home` and `DSO-EN-Pro` VMs, 2026-11-07: the `HKCU\Software\Microsoft\Speech_OneCore` key exists, the `Speech_OneCore\Settings` and `Settings\OnlineSpeechPrivacy` keys and the `HasAccepted` value: **none** (the online speech recognition toggle off).

## Detection and undo
- Detection: `HasAccepted` = DWORD 0 -> `Applied`; missing value or a different value (including 1 written by the toggle) -> `NotApplied`.
- On a fresh Windows the toggle is off, but the value and the keys don't exist, so detection returns `NotApplied` (the program shows the item as not applied), and applying it writes `0`, creates both keys and changes nothing on screen. `Applied` also shows when the user turned the toggle off after turning it on earlier (Windows then writes `HasAccepted` = 0).
- Undo restores the snapshot from before the change from the journal (a missing value = deleting it, 1 = toggle on) and deletes the `Settings` key with `OnlineSpeechPrivacy` if apply created them (`KeyAbsentAfterUndo`); `original` from the catalog (`<RemoveEntry>`) is only a fallback.
- The `Speech_OneCore` key existed before apply and stays (`KeyPresentAfterUndo`).

## Risks and warnings
- `risk=caution`, `suggested=false`: turns off online speech recognition; Windows says next to the toggle that apps with this technology can't be used then.
- Warning in the catalog: "You won't be able to use apps that use Microsoft's online speech recognition technology (as Windows describes it next to the toggle)."
- The warning is based on the Windows description next to the toggle: in English "If you turn off online speech recognition, you won’t be able to use apps that use Microsoft’s online speech recognition technology. …", in Polish "Jeśli wyłączysz rozpoznawanie mowy online, nie będzie można korzystać z aplikacji używających technologii rozpoznawania mowy online firmy Microsoft. …".
- Dictation (Win+H): not checked whether it stops working. The warning in the catalog doesn't promise it.

## VM test
Case `privacy-online-speech` (`Flow=Full`) in `tests/vm/cases.psd1`; VM run of 2026-11-08 (build 26300.9457, `Invoke-VmTests.ps1`): **Passed** on `DSO-EN-Home` (Core), `DSO-EN-Pro` and `DSO-PL-Pro` (Professional). Full runs: 915 passed, 2 failed, 25 skipped on both English VMs and 917 passed, 0 failed, 25 skipped on the Polish one. Both failures were in the cases of the two notification items (`privacy.toast-suggested`, `privacy.toast-backup-reminder`; described in their documentation), not this item.

## Observed effect
The `ms-settings:privacy-speech` page in a newly opened Settings window (UI Automation, screenshots). In UI Automation the toggle's name equals the description under it; the English heading: "Online speech recognition" (the Polish heading wasn't read as a separate element), the page description: "Use your voice for apps using Microsoft’s online speech recognition technology." ("Używaj głosu w aplikacjach, korzystając z technologii rozpoznawania mowy online firmy Microsoft." in Polish Windows).
- **Home** (`DSO-EN-Home`), 2026-11-07. Method: the main run (20:26–20:27) writing `0`: before the write, after the write and after undo; then the reverse evidence (20:54–20:55): a write of `HasAccepted` = 1 (with side values from the same registry diff, undone afterwards) and a return to `HasAccepted` = 0. Result: in the main run the toggle was off in all phases (before = after = undone), because it was already off on the clean VM; in the reverse evidence writing `1` turns the toggle on and shows the "Start contributing my voice clips" section, and returning to `0` turns it off again. Evidence: toggle state in Settings; the content itself wasn't observed.
- **Pro** (`DSO-EN-Pro`), 2026-11-07, main run 20:26–20:28, reverse evidence 20:54–20:55. Same method and same result.
- Conclusion: `HasAccepted` controls the toggle both ways, and `0` = off.
- Not checked: whether dictation (Win+H) stops working. The "Windows Input Experience" window opened only in the before-write phase and didn't depend on the value, which didn't change. The apps that use online speech recognition themselves weren't checked either.
