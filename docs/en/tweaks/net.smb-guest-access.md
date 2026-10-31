<!-- source: docs/tweaks/net.smb-guest-access.md sha256: 3721aa3f04572c7b35dd028b5f5663f363f22f1fe60f1de48164ebdb85560965 -->

# net.smb-guest-access – Allow guest logons to network shares

## Verdict for 26H2
Port with a restriction, as `risk=caution`: the mechanism is documented by Microsoft (`Set-SmbClientConfiguration -EnableInsecureGuestLogons`, the "Enable insecure guest logons" policy), but MS explicitly advises against enabling guest access. The item lowers the security of someone else's computer, so the warnings are taken word for word from MS. The v3.2 description ("computers visible on the network") doesn't go in: MS doesn't link guest access with the visibility of computers on the LAN. v3.2 alias: `lan_godmode`. Split from v3.2: the guest and signing settings are separate items (`net.smb-signing-client-off`); discovery services, NetBIOS, the network profile and the SMB server side don't go in (no MS evidence that they're needed).

## What it changes
- Registry (`registry[]`, written through the C# API): `HKLM\SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters`, value `AllowInsecureGuestAuth`, **DWord**, `1`. `scope=machine`, no `requiresReboot` (a write through the registry API is visible in `Get-SmbClientConfiguration` without a restart: confirmed by the VM run of 2026-10-18, see "VM test"). The path and type were established by a reading on the VM after a change with the official cmdlet `Set-SmbClientConfiguration -EnableInsecureGuestLogons` (MS Learn doesn't give the key, see Sources).
- Script (`script.detect`): read only, `Get-SmbClientConfiguration` through the helper function `Get-DsoSmbClientSetting`, the Boolean value `EnableInsecureGuestLogons`. The script doesn't write anything.
- Doesn't touch: policies (`Software\Policies\...`), `RequireSecuritySignature`, discovery services, NetBIOS, the network profile, the SMB server.

### Decision: `registry[]` + `script.detect`, not a script alone
The tweak's state is made up of all the operations that take part in detection (`StateAggregator.Aggregate`): `Applied` only when each gives `Match`, `NotApplied` when none does, `Partial` in the other cases. The registry entry expresses what the tool wrote (and what the journal can undo), and the cmdlet reading is the effective state. Intended design: a mismatch between the registry and the cmdlet gives `Partial`, and an apply after which the state isn't `Applied` isn't reported as a success (the registry is rolled back). The behavior under an actual policy (for example Group Policy) hasn't been checked: it wasn't checked whether `Get-SmbClientConfiguration` takes the policy into account; if it doesn't, the item may show a match despite the policy (CARRY: verification on a VM with a policy).

## Sources
- https://learn.microsoft.com/en-us/windows-server/storage/file-server/enable-insecure-guest-logons-smb2-and-smb3 – MS Learn, accessed 2026-10-18 (ms.date 2025-08-13): by default the SMB2/3 client doesn't allow guest access; enabling it: the "Enable insecure guest logons" policy or `Set-SmbClientConfiguration -EnableInsecureGuestLogons $true -Force`; warnings: spoofed server without a credentials prompt, ransomware, AitM attacks, "We recommend that you don't enable insecure guest logons", "Guest logons don't support standard security features such as SMB signing and SMB encryption". The page **doesn't give** the name or path of the registry key and doesn't say what the Windows 11 Home default is.
- https://learn.microsoft.com/en-us/windows-server/storage/file-server/smb-signing – MS Learn, accessed 2026-10-18: "Requiring SMB signing also disables guest access to shares. In these cases, you must disable SMB signing manually to restore access for guest accounts."
- https://learn.microsoft.com/en-us/windows-server/storage/file-server/smb-security-hardening – MS Learn, accessed 2026-10-18: Windows 11 Pro doesn't allow guest access by default; signing required by default since 24H2.
- https://learn.microsoft.com/en-us/powershell/module/smbshare/set-smbclientconfiguration?view=windowsserver2025-ps – MS Learn, accessed 2026-10-18: the `-EnableInsecureGuestLogons` parameter (Boolean).
- https://learn.microsoft.com/en-us/troubleshoot/windows-client/networking/access-denied-access-smb-file-share – MS Learn, accessed 2026-10-18 (ms.date 2026-07-09): "Do not re-enable insecure guest logons as a fix except as a temporary, risk-accepted workaround".
- Reading from 26H2 Home and Pro VMs, 2026-10-18 (`docs/zrodla/2026-10-18-partia-c3-zrodla.md` (in Polish), "Wyniki VM" (VM results), sections 3+4): name and type of the registry value, default readings, no restart.

## Defaults on a fresh 26H2
Read from the VMs DSO-26H2-Home (Core) and DSO-26H2-Pro (Professional), build 26300.9457, 2026-10-18:

| | Home | Pro |
|---|---|---|
| `Get-SmbClientConfiguration`: `EnableInsecureGuestLogons` | **True** | **False** |
| `Get-SmbClientConfiguration`: `RequireSecuritySignature` | True | True |
| `Parameters\AllowInsecureGuestAuth` in the registry | none | none |
| `Policies\Microsoft\Windows\LanmanWorkstation` | key missing | key missing |

The `LanmanWorkstation\Parameters` key exists on both editions. Conclusion: the default depends on the edition, not on a registry value (its absence means "the edition's default"). On Home, `EnableInsecureGuestLogons` is True by default, on Pro False; with signing required (True on both), a guest connection is blocked anyway according to MS. The MS statement "Home doesn't require signing" wasn't confirmed: signing is required on both (see `net.smb-signing-client-off`).

`Set-SmbClientConfiguration -EnableInsecureGuestLogons $x -Force` writes **DWORD `AllowInsecureGuestAuth`** (1/0) in `LanmanWorkstation\Parameters`, doesn't touch Policies, and a change written with this cmdlet was immediately visible in `Get-SmbClientConfiguration` (without a restart; only the write by the cmdlet was measured). Restoring with the cmdlet leaves an explicit DWORD where there was no value before (Home: 1, Pro: 0), which is why the tool's undo uses the snapshot and deletes the value instead of setting it explicitly.

## Detection and undo
The state is made up of two signals (see "Decision"), both Boolean, without parsing language-dependent text:
1. Configuration (`registry[]`): `AllowInsecureGuestAuth` = DWORD 1 → `Match`; no value, 0 or another type → `Mismatch`.
2. Effective state (`script.detect`): `EnableInsecureGuestLogons -eq $true` → `Applied`, `$false` → `NotApplied`. A cmdlet error or a response that isn't a Boolean value (`$null`, text, number) ends the script with an error → state `Unknown`, never `Applied`.

Result on a fresh system (not an error, just a consequence of the edition defaults):
- **Pro**: registry `Mismatch` + cmdlet `False` → `NotApplied`; after apply `Applied`.
- **Home**: registry `Mismatch` (no value) + cmdlet `True` → **`Partial`**. This is an honest result: the `EnableInsecureGuestLogons` setting is already True (Home default), but the configuration isn't written explicitly by this tool, and with signing required a guest connection is blocked anyway. Apply writes 1 and gives `Applied`; undo goes back to `Partial`.
- A policy overriding the registry (registry 1, cmdlet `False`) → `Partial` in the unit test model (design assumption that the cmdlet shows the effective value). This scenario wasn't measured on a VM; the behavior is covered by unit tests on the model.
- **Observation from the VM (C4 exploration, probe 7, 2026-10-18; Pro):** the policy value `HKLM\SOFTWARE\Policies\Microsoft\Windows\LanmanWorkstation\AllowInsecureGuestAuth` (DWORD) changes the cmdlet reading: `EnableInsecureGuestLogons` False before → **True** after writing the policy = 1 → False after deleting it; on Home True the whole time (True by default). So the policy affects the cmdlet's view independently of `Parameters\AllowInsecureGuestAuth`. Consequence for the aggregation above (description, no new measurements): with the policy at 1 and no value in `Parameters`, the registry gives `Mismatch` and the cmdlet True, so the state is `Partial` (as on a fresh Home), not `Applied`.

Undo: the engine restores `AllowInsecureGuestAuth` from the journal snapshot (the previous value or its absence); if there was no value, it deletes it (`<RemoveEntry>`), and the `Parameters` key existed before apply and stays. `original` in the catalog = `<RemoveEntry>`.

**Without a journal entry** (undo to defaults) the item deletes the `AllowInsecureGuestAuth` value, so the edition's default comes back (Home: `EnableInsecureGuestLogons` True, Pro: False; these are factory-state readings from the VMs, with no value in the registry). This doesn't lower protection below the Windows default and doesn't guess one value for both editions. Deleting the value restores the edition's default: confirmed by the VM run of 2026-10-18 (`CheckAfterUndo`, see "VM test"; discriminating evidence only on Pro, because on Home both no value and the value 1 give the reading True). The conclusion "no value = the edition's default" is based on the factory-state reading from the VMs (there was no value, the cmdlet gave Home True, Pro False) and on this run. **Not checked:** apply/undo behavior under Group Policy or after a restart (only the policy's effect on the cmdlet reading was measured, probe 7 above).

## Risks and warnings
- `risk=caution`. Warnings in the catalog: "Microsoft advises against this: a fake server on the network can take over the connection without a password, and traffic isn't encrypted." and "You usually also need to apply "Don't require SMB signing on the client" – without it, Windows still blocks the guest."
- Basis – MS quotes: "enabling guest logons can pose a security threat" – a spoofed server without a credentials prompt, ransomware, AitM; "We recommend that you don't enable insecure guest logons."; "Guest logons don't support standard security features such as SMB signing and SMB encryption".
- On Windows 11 24H2 Pro/Enterprise/Education, allowing guest access alone isn't enough: "Requiring SMB signing also disables guest access to shares". On a fresh 26H2, client signing is required on Home and Pro (VM reading), so a guest connection also needs `net.smb-signing-client-off`.
- No promise of computer visibility on the LAN. Solution preferred by MS: credentials.
- On Home, the item initially shows `Partial` (see Detection).

## VM test
VM run of 2026-10-18 (Home 19:58, Pro 20:10; build 26300.9457), DSO-26H2-Home (Core) and DSO-26H2-Pro (Professional), both without errors (278 passed / 0 failed / 17 skipped on each VM). Local reports: `tests/vm/out/<date>/<VM>/<RunLabel>/report.json` (folder in `.gitignore`).

Case `net-smb-guest-access` (`Flow=Full`, `Catalog=BuiltIn`, no restart), **Passed** on Home and Pro (Pester main 191/0/17):
- Checked (Pro): `detect-before` = `NotApplied`, `apply` = `Applied` (`requiresReboot` = false; snapshot after apply: DWORD 1), `detect-after-apply` = `Applied`, `undo` = `Undone`, `detect-after-undo` = `NotApplied`. **Home:** `detect-before` = `Partial` (registry without a value, cmdlet already True), after apply `Applied`, after undo `Partial` (as described in the "Detection and undo" section). Final state = state before: value absent, the `Parameters` key stays (`KeyPresentAfterUndo`), `SnapshotDiff` empty.
- `CheckAfterApply` (`(Get-SmbClientConfiguration).EnableInsecureGuestLogons -eq $true`, right after apply, no restart): **Passed** on Home and Pro. So a write through the registry API in C# is visible in the cmdlet without a restart. The decisive result is on **Pro** (False before apply, True after apply); on Home the reading was True before apply too, so that result alone proves nothing on Home.
- `CheckAfterUndo` (cmdlet reading equal to the edition's default, edition from `EditionID`: Home True, Pro False): **Passed** on both. After the value is deleted (undo from a snapshot without a value), the cmdlet goes back to the edition's default. The decisive one is **Pro** (True after apply, False after undo); on Home there's no distinction (True in both states).
- `net-smb-lan-effect`: **Skipped** ("brak sieci i serwera SMB na VM" (no network and no SMB server on the VM)), reported as skipped, not passed.
- **Not tested on the VM: no network and no SMB server.** Not checked: the effect on the LAN (guest connection, computer visibility), persistence after a restart, and the behavior of the item itself (apply/undo/detection) under Group Policy; only the effect of the policy value on the cmdlet reading was measured (see "Detection and undo", probe 7).
