<!-- source: docs/tweaks/sec.bitlocker-prevent-auto.md sha256: aaf36dc7249e113e11a5ca0c1c9833f996b8f089476a6176bc0e4a8f14dcadfb -->

# sec.bitlocker-prevent-auto – Block automatic drive encryption (BitLocker)

## Verdict for 26H2
Port as `caution`: the mechanism and the value type are explicitly documented by MS (a document for OEMs), but **the effect on a running system isn't described**. Automatic encryption starts in OOBE, so the item doesn't decrypt the drive, and the documentation doesn't say whether it stops encryption that has already started. The item description promises only writing the value that MS gives as the block. v3.2 alias: `sec_bitlocker_kill`. Decrypting the drive is a separate action, `sec.bitlocker-decrypt-c`.

**RED FLAG – license:** MS writes that turning off device encryption outside the OEM scenario "is prohibited as it violates the Windows 11 licensing requirements" (full quote below). The sentence appears in a document "for OEMs", so it applies to manufacturers; for the end user, Windows has a "Device encryption" toggle in Settings. This is an interpretation, not legal advice.

## What it changes
- `HKLM\SYSTEM\CurrentControlSet\Control\BitLocker`, value `PreventDeviceEncryption`, **DWord**: none -> `1`. `scope=machine`, written through the registry API in C#, no script. No `requiresReboot` (MS says nothing about a restart; the effect concerns a future OOBE/initialization of device encryption).
- Doesn't touch BitLocker volumes or services; doesn't decrypt anything.

## Sources
- MS Learn, "BitLocker drive encryption in Windows 11 for OEMs" (ms.date 2023-04-26, page updated 2025-08-12): https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/oem-bitlocker
  - "BitLocker automatic device encryption starts during Out-of-box (OOBE) experience. However, protection is enabled (armed) only after users sign in with a Microsoft Account or an Azure Active Directory account. Until that, protection is suspended and data is not protected. BitLocker automatic device encryption is not enabled with local accounts".
  - Section "Disable BitLocker automatic device encryption": "OEMs can choose to disable device encryption and instead implement their own encryption technology on a device. Disabling encryption outside of this scenario is prohibited as it violates the Windows 11 licensing requirements and the Windows 11 *secure by default* principle."
  - Mechanism: Unattend `PreventDeviceEncryption` = True or "update the HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\BitLocker registry key: Value: PreventDeviceEncryption equal to True (1)." Further: "We do not recommend setting this registry key on devices with Recall feature."
- MS Learn, Unattend "PreventDeviceEncryption" (ms.date 2017-05-02): values `false` (default, encrypts automatically) / `true`; "These settings only apply to Windows 8" (page not updated; the OEM page from 2025 still points to this setting for Windows 11): https://learn.microsoft.com/en-us/windows-hardware/customize/desktop/unattend/microsoft-windows-securestartup-filterdriver-preventdeviceencryption
- Round C4 research: `docs/zrodla/2026-10-18-partia-c4-zrodla.md` (in Polish), section 1. The ElevenForum thread (same value) is a hint, not evidence.

## Defaults on a fresh 26H2
Read from the VMs DSO-26H2-Home and DSO-26H2-Pro (build 26300.9457, round C4 exploration, 2026-10-18; both the same):
- The key `HKLM\SYSTEM\CurrentControlSet\Control\BitLocker` **exists** on Home and Pro (subkeys including `AutoDE`, `PcrMon`, `RecoveryPasswordRotation`, `Telemetry`; Home also has `KeyBackupMonitor`), with no values directly in the key.
- `PreventDeviceEncryption`: **`Exists=false`** on Home and Pro (the value doesn't exist; that's different from the value `0`). Hence `original` = `<RemoveEntry>` is confirmed by the reading, and the system key `Control\BitLocker` survives undo (undo deletes only our value), so `Expect.KeyPresentAfterUndo` in the VM case is correct.
- The **DWord** type follows from the MS text ("PreventDeviceEncryption equal to True (1)", Sources section). The VM reading after writing through the registry API shows only the type of our own write, so it isn't independent evidence of the type.
- **Discrepancy between MS and the observation:** MS writes that automatic encryption "is not enabled with local accounts", yet on the VMs with a local account it was in progress on Home and Pro. You mustn't conclude from this that the item is unnecessary with a local account.
- State observed after the checkpoint starts (local account): automatic device encryption was **in progress** on Home and Pro, and the progress varies between runs. Exploration 2026-10-18: Home `ConversionStatus` = 2 (EncryptionInProgress), 78%, `ProtectionStatus` = 0 (Off), 0 protectors (22:45); Pro `ConversionStatus` = 0, 0% (22:51). Full VM run `vm-run-20261019-002807` (2026-10-19): Home 6% encrypted, Pro 78% encrypted. We give this as an observation; according to MS, encryption starts in OOBE, and protection is armed only after signing in with a Microsoft/Entra account. We don't draw conclusions from this about how this value works.

## Detection and undo
- Detection: registry only, by equality: exactly DWORD `1` -> `Applied`, another value or none -> `NotApplied`. This is honest, because the item promises only writing the value; the real effect (whether encryption turns on) isn't detected.
- Undo from the journal restores the snapshot from before the change: a value that existed (for example `0`) comes back; if there was no value, it's deleted (it doesn't write `0`).
- If `PreventDeviceEncryption` = 1 already existed before apply (for example set by the OEM), detection shows `Applied`, and apply ends with `AlreadyApplied` without writing and without a journal entry: the tool doesn't take over this value. Undo **without a journal entry** (undo to defaults) deletes the value (fallback `original` = `<RemoveEntry>`), including one set outside the tool.

## Risks and warnings
- `risk=caution`.
- Warnings in the catalog: "An unencrypted drive doesn't protect data if the computer is stolen or lost; a company or school policy may require encryption." and "Microsoft says only the hardware manufacturer may turn off encryption under the Windows 11 license and advises against it if Recall is on."
- Beyond the notes in the program (in the technical details or only here): full MS quotes in the "Sources" section (license outside the OEM scenario; not recommended with the Recall feature); the item doesn't decrypt the drive (that's what `sec.bitlocker-decrypt-c` is for); MS describes the value as a block on automatic encryption at the first start (OOBE), and the effect on a running system isn't described.
- No promises of protection or performance.

## VM test
Full VM run `vm-run-20261019-002807` (2026-10-19, about 00:28–00:46, build 26300.9457, HEAD `d589405`), Home and Pro, code 0 for this item's cases. Reports are local, outside git (`tests/vm/out/<date>/<VM>/<RunLabel>/report.json`).

- `sec-bitlocker-prevent-auto` (`Flow=Full`, `Catalog=BuiltIn`, value snapshot, `KeyPresentAfterUndo` for the `Control\BitLocker` key): **Passed** on Home and Pro. Checked: detection before `NotApplied`, apply `Applied` (DWORD `PreventDeviceEncryption` = 1 in the snapshot after apply), detection after apply `Applied`, undo `Undone`, detection after undo `NotApplied`; final state = state before (value deleted, `SnapshotDiff` empty), the `Control\BitLocker` key stays.
- `sec-bitlocker-prevent-auto-effect` (`Flow=DetectOnly`): **Skipped** on Home and Pro. Reason: the effect "encryption doesn't start" requires a new OOBE with a Microsoft account, and the VM has a local account and a checkpoint after OOBE. Reported as skipped, not passed.
- Run summary (all cases): Home 309 passed / 0 failed / 21 skipped (`main` 221/0/21 + 6 green Reboot runs), Pro 308 / 1 / 21 (the only failure is the old `Check` of the `sec-bitlocker-decrypt-c` case, fixed in `962ae0e`; see `sec.bitlocker-decrypt-c`).
- **Not tested on the VM:** the effect of the value (whether automatic encryption doesn't start in OOBE).

## Reddit
To be added in part E.
