<!-- source: docs/tweaks/sec.bitlocker-decrypt-c.md sha256: fd187af41fbec18305bd05db0904b2b20d6774dba7c2e72472334022bb03693c -->

# sec.bitlocker-decrypt-c – Turn off BitLocker encryption on the C: drive

## Verdict for 26H2
Port as `kind=action`, `risk=caution` (new item; v3.2 had no decryption, `sec_bitlocker_kill` is the block on automatic encryption, a separate item `sec.bitlocker-prevent-auto`). Decisions:
- The action has no detection, journal or undo. The only evidence of the effect is the **verification** of `ConversionStatus` after `Decrypt`; a mismatched reading gives an exit code ≠ 0 (`Failed`), not a green `Executed`.
- `Decrypt` returns right away, and Windows decrypts in the background (even after a restart), so the action **doesn't wait** for the end. Success means "decryption started or in progress", not "drive decrypted".
- Auto-unlock keys of other drives (`FVE_E_AUTOUNLOCK_ENABLED`) are `Failed`; the action doesn't remove them itself (that would change other drives, a user decision).

## What it changes
- The script (`script.apply`, `-File`, PowerShell 5.1) calls WMI only through two helper functions: `Get-DsoVolumeConversionStatus` (returns an object with `ReturnValue`, `ConversionStatus`, `EncryptionPercentage`, and `$null` when there's no volume) and `Invoke-DsoVolumeDecrypt` (returns `ReturnValue` as `uint32`). The real bodies: `Get-CimInstance -Namespace root\CIMV2\Security\MicrosoftVolumeEncryption -ClassName Win32_EncryptableVolume -Filter "DriveLetter='C:'"` and `Invoke-CimMethod -MethodName 'GetConversionStatus'` / `'Decrypt'`. Unit tests mock both helpers; the harness has safeguards on them and on `Invoke-CimMethod`.
- Decisions only by WMI numeric codes and the `ConversionStatus` enum (0 FullyDecrypted, 1 FullyEncrypted, 2 EncryptionInProgress, 3 DecryptionInProgress, 4 EncryptionPaused, 5 DecryptionPaused), without language-dependent text. The percentage (`EncryptionPercentage`) goes only into the message: MS "Always use ConversionStatus".
- Result paths:
  - `ConversionStatus` = 0 before: `Executed`, message "C: isn't encrypted – nothing was changed", `Decrypt` **isn't called**.
  - 1, 2, 4: `Decrypt`, verification; status 3 afterwards: `Executed` "Decryption of C: started: N% encrypted. Windows continues in the background, even after a restart."; status 0 afterwards (very small volume): `Executed` "Decryption of C: finished (ConversionStatus = 0)."
  - 3, 5: `Decrypt` (MS: resume), verification; 3 afterwards: `Executed` "Decryption of C: in progress: N% encrypted. …".
  - `Decrypt` returns `0x80310029` (`FVE_E_AUTOUNLOCK_ENABLED`): `Failed` with the message "First remove the auto-unlock keys of other drives (Clear-BitLockerAutoUnlock) – the action doesn't do this"; `ClearAllAutoUnlockKeys` isn't called.
  - Another code ≠ 0 from `Decrypt` (for example `0x80310000` `FVE_E_LOCKED_VOLUME`) or a status after `Decrypt` outside {0, 3}: `Failed` with the hex code or the status value.
  - No C: volume in WMI (empty result): `Failed` "Couldn't find the C: volume …", never `Executed`. An error in any WMI call (before, `Decrypt`, after) is `Failed`, it isn't swallowed. Unknown `ConversionStatus` (> 5) or `ReturnValue` ≠ 0 when reading before `Decrypt`: `Failed`, `Decrypt` isn't called.
- Doesn't touch: the registry, services, other drives or auto-unlock keys, protectors, `manage-bde`, cmdlets of the BitLocker module.

## Sources
- MS Learn, `Win32_EncryptableVolume.Decrypt` (namespace `Root\CIMV2\Security\MicrosoftVolumeEncryption`, ms.date 2018-05-31): https://learn.microsoft.com/en-us/windows/win32/secprov/decrypt-win32-encryptablevolume
  - "begins decryption of a fully encrypted volume, or resumes decryption of a partially encrypted volume"; "When encryption is paused or in-progress, this method reverts the encryption and begins decryption. After decryption completes, all key protectors on this volume are removed".
  - "This method returns immediately. If the volume is already fully decrypted and no other errors exist, this method returns 0."
  - Codes: `FVE_E_LOCKED_VOLUME` 0x80310000, `FVE_E_AUTOUNLOCK_ENABLED` 0x80310029 ("keys used to automatically unlock data volumes are available. Use ClearAllAutoUnlockKeys to remove these keys").
  - "Calling the Decrypt method leaves data unprotected."
- MS Learn, `Win32_EncryptableVolume.GetConversionStatus` (ms.date 2018-05-31): https://learn.microsoft.com/en-us/windows/win32/secprov/getconversionstatus-win32-encryptablevolume
  - `ConversionStatus` 0–5 as above; `EncryptionPercentage` 0–100: "an encryption percentage of 0 or 100 does not necessarily indicate that the disk is fully decrypted or fully encrypted. Always use ConversionStatus".
- MS Learn, `Disable-BitLocker` (BitLocker module, a hint, we don't use it): "removes all key protectors and begins decrypting": https://learn.microsoft.com/en-us/powershell/module/bitlocker/disable-bitlocker
- Round C4 research: `docs/zrodla/2026-10-18-partia-c4-zrodla.md` (in Polish), section 2 and "Wyniki VM (2026-10-18, eksploracja C4)" (VM results, C4 exploration).

## Defaults on a fresh 26H2
Read from the VMs DSO-26H2-Home and DSO-26H2-Pro (build 26300.9457, 2026-10-18; details in the research):
- The `Win32_EncryptableVolume` class (`root\CIMV2\Security\MicrosoftVolumeEncryption`, filter `DriveLetter='C:'`) **exists and returns 1 volume on Home and Pro**. `GetConversionStatus` returns the fields `ReturnValue`, `ConversionStatus`, `EncryptionPercentage`, `EncryptionFlags`, `WipingPercentage`, `WipingStatus` (type `UInt32`; same shape on both editions).
- **Exploration 2026-10-18 (22:45 Home, 22:51 Pro):** Home `ConversionStatus` = 2 (EncryptionInProgress), `EncryptionPercentage` = 78, `ProtectionStatus` = 0 (Off), 0 protectors; Pro `ConversionStatus` = 0 (FullyDecrypted), `EncryptionPercentage` = 0, `ProtectionStatus` = 0.
- **Full VM run `vm-run-20261019-002807` (2026-10-19):** the action found automatic device encryption in progress on both editions: Home 6% encrypted, Pro 78% encrypted (local account). Observation: after the `czysta-26H2` checkpoint starts, automatic device encryption runs on Home and Pro, and the progress at the moment of the action varies between runs. MS: encryption starts in OOBE, and protection is armed only after signing in with a Microsoft/Entra account.

## Detection and undo
No detection (`kind=action`). **No undo** (no journal): decryption has no reverse in the tool; encrypting again is a separate user decision in Windows Settings (including new key protectors). The only evidence of the effect is the verification: exit code 0 only when `ConversionStatus` after `Decrypt` is 3 or 0.

## Risks and warnings
- `risk=caution`. Word for word from MS: "Calling the Decrypt method leaves data unprotected."; after decryption finishes, all key protectors on the volume are removed.
- No undo; the action doesn't wait for the end (progress: the next run or Settings).
- Auto-unlock keys of other drives (0x80310029): `Failed`, the action doesn't remove them. By hand: `Clear-BitLockerAutoUnlock`, if it's a deliberate decision.
- Without `sec.bitlocker-prevent-auto`, device encryption may start again (not measured; MS doesn't describe this). A company or school policy may require encryption or encrypt the drive again.
- Confirmation in the UI before running (spec, section 7); the CLI doesn't ask.

## VM test
Encrypted drive path: a real `Decrypt` exercised on the VM on BOTH editions (Home and Pro), because automatic device encryption runs there after the checkpoint starts; we don't do our own encryption in the VM (the author's decision). So we don't check on the VM: `Decrypt` on a fully encrypted drive (`ConversionStatus` = 1), the code `0x80310029`, or resuming (3, 5); these paths are covered only by unit tests on helper mocks.

VM run results (build 26300.9457, 2026-10-19):
- Full run `vm-run-20261019-002807` (HEAD `d589405`): **Home Passed** (`Executed`, "Odszyfrowywanie C: rozpoczęte: 6% zaszyfrowane…" (Decryption of C: started: 6% encrypted…), `Check` Passed). **Pro: action `Executed`** ("Odszyfrowywanie C: rozpoczęte: 78% zaszyfrowane…" (Decryption of C: started: 78% encrypted…)), but `Check` **Failed**: the old `Check` required status 0 on Pro, and the verification gave 3. This was a bug in the case (it assumed a stable BitLocker state at the checkpoint), not in the action; fix `962ae0e`.
- Rerun of this case alone after the fix, `vm-run-decrypt-20261019-005135` (HEAD `962ae0e`): **Home 4 passed / 0 failed** ("…78% zaszyfrowane…" (…78% encrypted…)), **Pro 4 / 0** ("…76% zaszyfrowane…" (…76% encrypted…)), `Check` (`ReturnValue` 0, `ConversionStatus` ∈ {3, 0}) Passed. After the runs, both VMs are `Off` at the `czysta-26H2` checkpoint.
- So the real `Decrypt` path + verification was checked on BOTH editions, thanks to automatic device encryption running after the checkpoint starts. The "already decrypted" path (status 0) wasn't measured on the VM; it's covered only by unit tests on mocks (likewise 1, 4, 3/5 and `0x80310029`).
- The `sec-bitlocker-decrypt-c` case (`Flow=Execute`, `Catalog=BuiltIn`) is in `tests/vm/cases.psd1`; it has one `Check` for both editions: `GetConversionStatus` returns `ReturnValue` 0, and `ConversionStatus` is 3 or 0 (the progress of automatic encryption at the moment of the action isn't deterministic).
