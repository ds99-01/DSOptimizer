<!-- source: docs/tweaks/privacy.notepad-ai.md sha256: dcede79cb740121c8f36b555db98818cc61b3b5477c22c165292020436deea51 -->

# privacy.notepad-ai – Turn off AI features in Notepad

## Verdict for 26H2
Port as a **non-suggested** item: one value described by Microsoft; a test on a 26H2 VM (Home and Pro) showed that the "Writing tools" / "Narzędzia do pisania" button (AI features) disappears from Notepad, and that after the change Notepad starts and works. `risk=caution`, because a Notepad feature disappears.

## What it changes
- `HKLM\SOFTWARE\Policies\WindowsNotepad`, **DWord** value `DisableAIFeatures` set to `1`; `scope=machine`.
- On a fresh 26H2 there's no `WindowsNotepad` key (the `HKLM\SOFTWARE\Policies` key exists). Apply creates the key, undo deletes the value and the key, if the change created it and it's empty.
- No Windows restart. In the test Notepad was started again in every phase; whether an already open Notepad window shows the change right away wasn't checked.

## Sources
- Test on 26H2 Home and Pro VMs, 2026-11-07 (build 26300.9457, Windows not activated, local account, with network): write through the registry API (.NET) and observation of the Notepad window (UI Automation, screenshots).
- Microsoft Learn, "Manage AI features in Notepad": https://learn.microsoft.com/en-us/windows/client-management/manage-notepad (ms.date 2025-04-30, accessed 2026-11-07): "To disable AI features in Notepad, set the `DisableAIFeatures` registry value to `1` under `HKLM:\SOFTWARE\Policies\WindowsNotepad`."; requirements: "Windows 11, version 22H2 or later.", "Notepad version 11.2503.16.0 or later.". The page doesn't give the data type; **DWord** following Win11Debloat and WinUtil.
- Win11Debloat: https://github.com/Raphire/Win11Debloat (branch master), file `Regfiles/Disable_Notepad_AI_Features.reg`, lines 2–3; accessed 2026-11-06.
- WinUtil: https://github.com/ChrisTitusTech/winutil (branch main), file `config/tweaks.json`, lines 953–975; accessed 2026-11-06.

## Defaults on a fresh 26H2
Reading from the `DSO-EN-Home` and `DSO-EN-Pro` VMs, 2026-11-07: the `HKLM\SOFTWARE\Policies` key exists, the `HKLM\SOFTWARE\Policies\WindowsNotepad` key and the `DisableAIFeatures` value: **none** (the "Writing tools" button in Notepad).

## Detection and undo
- Detection: `DisableAIFeatures` = `1` -> `Applied`; missing value or a different value -> `NotApplied`.
- Undo restores the snapshot from before the change from the journal (a missing value = deleting it; a value that was already there comes back) and deletes the `WindowsNotepad` key if apply created it and it's empty (`KeyAbsentAfterUndo`). `original` from the catalog (`<RemoveEntry>`) is only a fallback.
- The `HKLM\SOFTWARE\Policies` key existed before apply and stays (`KeyPresentAfterUndo`).

## Risks and warnings
- `risk=caution`, `suggested=false`: the "Writing tools" / "Narzędzia do pisania" button (AI tools) disappears from Notepad.
- Warning in the catalog: "Notepad's AI features (the "Writing tools" button) won't be available until you undo this item."
- According to Microsoft the value works in Notepad from version 11.2503.16.0; the test used Notepad 11.2605.34.0.

## VM test
Case `privacy-notepad-ai` (`Flow=Full`) in `tests/vm/cases.psd1`; VM run of 2026-11-08 (build 26300.9457, `Invoke-VmTests.ps1`): **Passed** on `DSO-EN-Home` (Core), `DSO-EN-Pro` and `DSO-PL-Pro` (Professional). Full runs: 915 passed, 2 failed, 25 skipped on both English VMs and 917 passed, 0 failed, 25 skipped on the Polish one. Both failures were in the cases of the two notification items (`privacy.toast-suggested`, `privacy.toast-backup-reminder`; described in their documentation), not this item.

## Observed effect
Notepad (11.2605.34.0) started again in every phase and closed after reading (UI Automation, screenshots), before the write, after the write and after undo.
- **Home** (`DSO-EN-Home`), 2026-11-07, 20:50–20:51. Result: before the write the "Writing tools" button is there; after the write it isn't; after undo it is. Notepad started and was running after 10 s in every phase, so after the change Notepad starts.
- **Pro** (`DSO-EN-Pro`), 2026-11-07, 20:50–20:51. Same result as on Home; after the change Notepad starts and is running after 10 s.
- Wording: the "Writing tools" / "Narzędzia do pisania" button (read in English and in Polish).
- Not checked: the Notepad AI features themselves beyond the presence of the button, Notepad versions other than 11.2605.34.0.
