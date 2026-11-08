<!-- source: docs/tweaks/privacy.inking-typing-improve.md sha256: 9be54ef9cc13052fd0cd86b7dd57166bcf9ebe489ad93fddeba278de4a96117a -->

# privacy.inking-typing-improve – Turn off the custom inking and typing dictionary

## Verdict for 26H2
Port: the "Custom inking and typing dictionary" toggle (the `ms-settings:privacy-speechtyping` page) turns off after writing four values, the same ones the toggle itself writes; a test on a 26H2 VM (Home and Pro) confirmed the toggle state. Evidence: toggle state in Settings; the content itself wasn't observed.

## What it changes
- Four **DWord** values for the signed-in user (in the engine `HKU\<SID of the signed-in user>`), `scope=user`:
  - `HKCU\Software\Microsoft\InputPersonalization`, `RestrictImplicitInkCollection` set to `1`;
  - `HKCU\Software\Microsoft\InputPersonalization`, `RestrictImplicitTextCollection` set to `1`;
  - `HKCU\Software\Microsoft\InputPersonalization\TrainedDataStore`, `HarvestContacts` set to `0`;
  - `HKCU\Software\Microsoft\Personalization\Settings`, `AcceptedPrivacyPolicy` set to `0`.
- Mechanism of the `SystemSettings_Privacy_PersonalizeInkType_Toggle_ToggleSwitch` toggle from a registry diff on the VM: turning it off writes these four values as above, turning it on writes the values from the clean VM (`0`, `0`, `1`, `1`).
- When turning off, the toggle also writes other values that the item doesn't change: `HKCU\Software\Microsoft\Input\Personalization` `UserDictKey` (binary) and `UserDictName` (text, different on each VM), `HKCU\Software\Microsoft\Windows\CurrentVersion\CPSS\Store\InkingAndTypingPersonalization` `Value` (`0`, `1` when on) and the `Value` values in `…\CPSS\Store\IME\Bopomofo\…` and `…\CPSS\Store\IME\Japanese\LearningLevel` (IME settings).
- The source entry `HKCU\Software\Microsoft\Input\TIPC` `Enabled` = 0 is **not included**. The after-write phase in the test covered five values (these four and `TIPC\Enabled` = 0). That four are enough follows from the toggle mechanism: the registry diff when switching to off doesn't touch the `TIPC` key. The setting that `TIPC` may correspond to, on the Diagnostics & feedback page ("Improve the language recognition and suggestion capabilities…"), was inactive and off in all phases, because optional diagnostic data is off, so the effect of `TIPC\Enabled` isn't visible. On a clean VM the `TIPC` key exists, the `Enabled` value doesn't.
- No restart: the toggle state shows in a newly opened Settings window.

## Sources
- Test on 26H2 Home and Pro VMs, 2026-11-07 (build 26300.9457, Windows not activated, local account, no network): write through the registry API (.NET), a registry diff after switching the toggle both ways and observation in Settings (UI Automation, screenshots).
- Win11Debloat: https://github.com/Raphire/Win11Debloat (branch master), file `Regfiles/Disable_Telemetry.reg`, lines 11–21; accessed 2026-11-06.
- RemoveWindowsAI: https://github.com/zoicware/RemoveWindowsAI (branch main), file `RemoveWindowsAi.ps1`, lines 1120–1122; accessed 2026-11-06.
- WinUtil: https://github.com/ChrisTitusTech/winutil (branch main), file `config/tweaks.json`, telemetry section (no line number); accessed 2026-11-06.
- No Microsoft documentation for these values; evidence: VM test (observation below).

## Defaults on a fresh 26H2
Reading from the `DSO-EN-Home` and `DSO-EN-Pro` VMs, 2026-11-07 (before any click), Home and Pro the same: the `InputPersonalization`, `InputPersonalization\TrainedDataStore` and `Personalization\Settings` keys exist; `RestrictImplicitInkCollection` = 0, `RestrictImplicitTextCollection` = 0, `HarvestContacts` = 1, `AcceptedPrivacyPolicy` = 1 (toggle on).

## Detection and undo
- Detection: all four values equal to the written ones -> `Applied`; none -> `NotApplied`; some -> `Partial`.
- Undo restores the snapshot from before the change from the journal (on a clean Windows `0`, `0`, `1`, `1`, that is, toggle on); `original` from the catalog (the same values from the clean VM) is only a fallback.
- All three keys existed before apply and stay (`KeyPresentAfterUndo`).

## Risks and warnings
- `risk=caution`, `suggested=false`: the item isn't suggested, because turning it off may clear the custom dictionary (and whether undo brings it back wasn't checked). According to the Windows description under the toggle, turning it off clears the custom dictionary, and suggestions are no longer personalized. The description in English: "Get better suggestions by creating a custom dictionary using your typing and handwriting info. If you turn off this setting, your dictionary will be cleared and your suggestions won’t be personalized."; in Polish: "Uzyskaj lepsze sugestie, tworząc słownik niestandardowy przy użyciu informacji o pisaniu i piśmie ręcznym. Jeśli wyłączysz to ustawienie, słownik zostanie wyczyszczony, a Twoje sugestie nie będą spersonalizowane.".
- Warning in the catalog: "According to Windows, turning this off clears the custom typing and handwriting dictionary; undo may not bring it back."
- Whether the dictionary actually disappears and whether undo restores it: not checked (the dictionary on the VM was empty).
- The item doesn't write `UserDictKey` or `UserDictName` (`HKCU\Software\Microsoft\Input\Personalization`), which the Windows toggle changes when turning off, so whether writing the item's four values alone clears the dictionary: not checked.

## VM test
Cases `privacy-inking-typing-improve` and `privacy-inking-typing-improve-prev-value` (`Flow=Full`) in `tests/vm/cases.psd1`; VM run of 2026-11-08 (build 26300.9457, `Invoke-VmTests.ps1`): both cases **Passed** on `DSO-EN-Home` (Core), `DSO-EN-Pro` and `DSO-PL-Pro` (Professional). Full runs: 915 passed, 2 failed, 25 skipped on both English VMs and 917 passed, 0 failed, 25 skipped on the Polish one. Both failures were in the cases of the two notification items (`privacy.toast-suggested`, `privacy.toast-backup-reminder`; described in their documentation), not this item.

## Observed effect
- **Home** (`DSO-EN-Home`), 2026-11-07, 20:29–20:30. Method: the `ms-settings:privacy-speechtyping` and `ms-settings:privacy-feedback` pages in a newly opened Settings window (UI Automation, screenshots), the "Custom inking and typing dictionary" toggle ("Niestandardowe pisanie odręczne i słownik pisania" in Polish Windows) before the write, after the write and after undo. Result: before the write on; after the write off; after undo on. Evidence: toggle state in Settings; the content itself wasn't observed.
- **Pro** (`DSO-EN-Pro`), 2026-11-07, 20:29–20:31. Same method and same result.
- Not checked: whether the user's dictionary actually disappears and whether undo restores it (the dictionary on the VM was empty), or the typing suggestions themselves.
