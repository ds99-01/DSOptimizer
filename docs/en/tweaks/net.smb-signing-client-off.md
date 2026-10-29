<!-- source: docs/tweaks/net.smb-signing-client-off.md sha256: 55d177fee7373fb3278641f3c5d97d736688cef4fd3ccee1c26f3290efd0c77e -->

# net.smb-signing-client-off – Don't require SMB signing on the client

## Verdict for 26H2
Port with a restriction, as `risk=caution`. New item (Dawid's decision of 2026-10-18, split of `lan_godmode`): MS describes signing and guest access as separate settings, and on Windows 11 24H2 Pro/Enterprise/Education guest access also requires turning off the signing requirement ("Requiring SMB signing also disables guest access to shares"). At the same time, MS advises against turning off signing. Affects only the SMB client (outgoing connections); the SMB server isn't touched (v3.2 didn't touch it either).

## What it changes
- Registry (`registry[]`, written through the C# API): `HKLM\SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters`, value `RequireSecuritySignature`, **DWord**, `0`. `scope=machine`, no `requiresReboot` (MS: a change of the signing policy takes effect without a restart; a write through the registry API is visible in `Get-SmbClientConfiguration` without a restart: confirmed by the VM run of 2026-10-18, see "VM test"). The path and type were established by a reading on the VM after a change with the official cmdlet `Set-SmbClientConfiguration -RequireSecuritySignature $false`.
- Script (`script.detect`): read only, `Get-SmbClientConfiguration` through the helper function `Get-DsoSmbClientSetting`, the Boolean value `RequireSecuritySignature`. It doesn't write anything.
- Doesn't touch: `EnableSecuritySignature` (applies only to SMB1, MS warns against using it), the SMB server (`Set-SmbServerConfiguration`), policies, `AllowInsecureGuestAuth`.
- The decision for `registry[]` + `script.detect` is as in `net.smb-guest-access` (aggregation in `StateAggregator`; a mismatch between the registry and the cmdlet gives `Partial`). The behavior under an actual policy (for example Group Policy) hasn't been checked: it wasn't checked whether `Get-SmbClientConfiguration` takes the policy into account; if it doesn't, the item may show a match despite the policy (CARRY: verification on a VM with a policy).

## Sources
- https://learn.microsoft.com/en-us/windows-server/storage/file-server/smb-signing – MS Learn, accessed 2026-10-18 (ms.date 2025-08-13): turning it off through the policy "Microsoft network client: Digitally sign communications (always)" = Disabled or `Set-SmbClientConfiguration -RequireSecuritySignature $false`; verification with `Get-SmbClientConfiguration | FL RequireSecuritySignature` (True/False); "Caution: We don't recommend disabling SMB signing as a workaround for third-party servers. We also don't recommend trying to sign with guest accounts."; "Requiring SMB signing also disables guest access to shares." The page also says that Windows 11 24H2 Home doesn't require signing, which **wasn't confirmed on the VM** (see below).
- https://learn.microsoft.com/en-us/windows-server/storage/file-server/smb-security-hardening – MS Learn, accessed 2026-10-18: since Windows 11 24H2 all outgoing and incoming SMB connections must be signed by default (no distinction between editions).
- https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-10/security/threat-protection/security-policy-settings/microsoft-network-client-digitally-sign-communications-always – MS (archive), accessed 2026-10-18: the `RequireSecuritySignature` value as the policy's equivalent, change without a restart; the page doesn't give the key path.
- https://learn.microsoft.com/en-us/powershell/module/smbshare/set-smbclientconfiguration?view=windowsserver2025-ps – MS Learn, accessed 2026-10-18: the `-RequireSecuritySignature` parameter.
- Reading from 26H2 Home and Pro VMs, 2026-10-18 (`docs/zrodla/2026-10-18-partia-c3-zrodla.md` (in Polish), "Wyniki VM" (VM results), sections 3+4).

## Defaults on a fresh 26H2
Read from the VMs DSO-26H2-Home (Core) and DSO-26H2-Pro (Professional), build 26300.9457, 2026-10-18 (Home = Pro):
- `Get-SmbClientConfiguration`: `RequireSecuritySignature` = **True** on both editions (the data **doesn't confirm** the MS statement that Home doesn't require signing; the contradiction in MS texts stays in the documents, the measurement is True). `EnableSecuritySignature` = True.
- `Parameters\RequireSecuritySignature` in the registry: **no value** (Home and Pro). So no value means the default: required.
- The `Policies\Microsoft\Windows\LanmanWorkstation` key doesn't exist; the cmdlet doesn't write it.

`Set-SmbClientConfiguration -RequireSecuritySignature $false` writes **DWORD `RequireSecuritySignature` = 0** in `LanmanWorkstation\Parameters`, immediately visible in the cmdlet (only the write by the cmdlet was measured); restoring with the cmdlet (`$true`) leaves an explicit DWORD 1 where there was no value before. That's why the tool's undo uses the snapshot and deletes the value.

## Detection and undo
The state is made up of two signals, both Boolean, without parsing language-dependent text:
1. Configuration (`registry[]`): `RequireSecuritySignature` = DWORD 0 → `Match`; no value, 1 or another type → `Mismatch`.
2. Effective state (`script.detect`): `RequireSecuritySignature -eq $false` → `Applied`, `$true` → `NotApplied`. A cmdlet error or a response that isn't a Boolean value ends the script with an error → `Unknown`, never `Applied`.

On a fresh 26H2 (Home and Pro): registry `Mismatch` + cmdlet `True` → `NotApplied`; after apply `Applied`. Registry 0 but cmdlet `True` → `Partial` (intended design; whether a policy causes such a mismatch wasn't checked); apply in that situation isn't reported as a success.

Undo: journal snapshot (the previous value or none; none comes back as none, the `Parameters` key stays). `original` = `<RemoveEntry>`. **Without a journal entry** the item deletes the `RequireSecuritySignature` value, so the default comes back (on a fresh 26H2 Home and Pro, the reading with no value is True, meaning signing required); we assume this restores the default state, so protection doesn't drop below the factory level. Confirmed on the VMs on 2026-10-18 (Home and Pro, "VM test"): a write through the registry API in C# is immediately visible in `Get-SmbClientConfiguration` without a restart, and after the value is deleted the reading goes back to the default (True). **Not checked:** behavior under a policy or after a restart.

## Risks and warnings
- `risk=caution`. Word for word from MS: "We don't recommend disabling SMB signing as a workaround for third-party servers. We also don't recommend trying to sign with guest accounts." and "Requiring SMB signing also disables guest access to shares."
- The item changes only the requirement on the client side; the SMB server and whether the server signs connections stay unchanged. The item alone doesn't turn on guest access (that's what `net.smb-guest-access` is for).
- The effect on the local network isn't measured.

## VM test
VM run of 2026-10-18 (Home 19:58, Pro 20:10; build 26300.9457), DSO-26H2-Home (Core) and DSO-26H2-Pro (Professional), both without errors (278 passed / 0 failed / 17 skipped on each VM). Local reports: `tests/vm/out/<date>/<VM>/<RunLabel>/report.json` (folder in `.gitignore`).

Case `net-smb-signing-client-off` (`Flow=Full`, `Catalog=BuiltIn`, no restart), **Passed** on Home and Pro (Pester main 191/0/17), both editions identical:
- Checked: `detect-before` = `NotApplied` (no value, cmdlet True), `apply` = `Applied` (`requiresReboot` = false; snapshot after apply: DWORD 0), `detect-after-apply` = `Applied`, `undo` = `Undone`, `detect-after-undo` = `NotApplied`. Final state = state before: value absent, the `Parameters` key stays, `SnapshotDiff` empty.
- `CheckAfterApply` (`RequireSecuritySignature -eq $false`, right after apply, no restart): **Passed**: a write through the registry API is visible in the cmdlet without a restart (change from True to False). `CheckAfterUndo` (`-eq $true`): **Passed**: after the value is deleted, the reading goes back to the default True.
- `net-smb-lan-effect`: **Skipped** ("brak sieci i serwera SMB na VM" (no network and no SMB server on the VM)).
- **Not tested on the VM: no network and no SMB server.** Not checked: connections to a server that requires or doesn't support signing, behavior under a policy, persistence after a restart.

## Reddit
To be added in part E.
