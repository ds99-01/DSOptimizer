<!-- source: docs/tweaks/net.smb-harden.md sha256: 5489baa7b26bb586dc102bef5cffea282bb06bd94d4caa05a845185f433b884d -->

# net.smb-harden – Turn off guest logons and require SMB signing

## Verdict for 26H2
Port with a restriction, as `kind=action`, `risk=caution` (Dawid's decision of 2026-10-18: SMB client only, `caution` instead of `safe` from the spec). The action sets two SMB client settings to the values Microsoft gives as secure: insecure guest logons off, signing required. The old `lan_panic` item from v3.2 did more (all network profiles to Public, stopping discovery services, NetBIOS), which Microsoft doesn't describe as hardening, so we don't carry it over; the description "locking network services down for good" was untrue for what this version does.

The action has no detection, journal or undo (`TweakEngine.Execute` runs only `script.apply`). The only evidence of the effect is the **verification** in the script itself: when it doesn't match the expected values, the script ends with code ≠ 0 and the engine returns `Failed` with a message, not a green `Executed`.

## What it changes
- Script (`script.apply`, `-File`, PowerShell 5.1): through the helper function `Set-DsoSmbClientSetting` it calls `Set-SmbClientConfiguration -EnableInsecureGuestLogons $false -RequireSecuritySignature $true -Force`, and then through `Get-DsoSmbClientSetting` reads the Boolean values `EnableInsecureGuestLogons` and `RequireSecuritySignature` from `Get-SmbClientConfiguration`.
- Effect in the registry (measured on the VM after the same cmdlet, `docs/zrodla/2026-10-18-partia-c3-zrodla.md` (in Polish), "Wyniki VM" (VM results)): DWORD `AllowInsecureGuestAuth` = 0 and `RequireSecuritySignature` = 1 in `HKLM\SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters`. The cmdlet writes them; the script doesn't touch the registry directly.
- Verification: both values must be of type bool and equal to the expected ones (guest `False`, signing `True`). No Boolean value (cmdlet error, `$null`, text) or a mismatch = exception, exit code 1, message on stderr ("Verification doesn't match the expected values: …").
- Doesn't touch: the SMB server (`Set-SmbServerConfiguration`), discovery services (`fdPHost`, `FDResPub`, `SSDPSRV`), NetBIOS, the network profile, policies.

## Sources
- https://learn.microsoft.com/en-us/windows-server/storage/file-server/smb-signing – MS Learn, accessed 2026-10-18: `Set-SmbClientConfiguration -RequireSecuritySignature $true` ("Control SMB signing behavior"), verification with `Get-SmbClientConfiguration`. The page says that Windows 11 24H2 Home doesn't require signing.
- https://learn.microsoft.com/en-us/windows-server/storage/file-server/smb-security-hardening – MS Learn, accessed 2026-10-18: since Windows 11 24H2 all SMB connections must be signed by default (no distinction between editions). **Microsoft's texts contradict each other about Home; the measured data wins** (see below).
- https://learn.microsoft.com/en-us/windows-server/storage/file-server/enable-insecure-guest-logons-smb2-and-smb3 – MS Learn, accessed 2026-10-18: "Windows disables insecure guest logons by default" (Windows 11 Pro/Enterprise/Education), "We recommend that you don't enable insecure guest logons."
- https://learn.microsoft.com/en-us/powershell/module/smbshare/set-smbclientconfiguration?view=windowsserver2025-ps – MS Learn, accessed 2026-10-18: `-EnableInsecureGuestLogons`, `-RequireSecuritySignature`, `-Force`.
- https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-10/security/threat-protection/security-policy-settings/microsoft-network-client-digitally-sign-communications-always – MS (archive), accessed 2026-10-18: "Best practice: Enable Microsoft network client: Digitally sign communications (always)".
- Reading from 26H2 Home and Pro VMs, 2026-10-18 (the "Wyniki VM" (VM results) section in the research file).

## Defaults on a fresh 26H2
Read from the VMs DSO-26H2-Home (Core) and DSO-26H2-Pro (Professional), build 26300.9457, 2026-10-18:

| | Home | Pro |
|---|---|---|
| `EnableInsecureGuestLogons` (`Get-SmbClientConfiguration`) | **True** | **False** |
| `RequireSecuritySignature` (`Get-SmbClientConfiguration`) | True | True |
| `Parameters\AllowInsecureGuestAuth` in the registry | none | none |
| `Parameters\RequireSecuritySignature` in the registry | none | none |

Conclusion for this action: on **Pro**, the target state (guest False, signing True) is already the default, so the action only writes explicit values to the registry. On **Home**, signing is also already True, so the only real change is turning off guest access (from True to False): this tightens the Home default rather than restoring it. The MS Learn sentence saying that Home doesn't require signing **wasn't confirmed** on the VM (it also contradicts "SMB security hardening").

## Detection and undo
No detection (`kind=action`): there's no status row in the UI, and running it again always does the write and the verification. The only evidence of the effect is the exit code and the message. The action's stdout goes to the `message` field of the CLI report (`Executed`), for example "SMB client: EnableInsecureGuestLogons = False, RequireSecuritySignature = True (verification matches).".

**No undo** (by design, no journal). By hand, with Microsoft's official cmdlet, for example `Set-SmbClientConfiguration -EnableInsecureGuestLogons $true -RequireSecuritySignature $false -Force` (only for the values you want to restore; the edition defaults are in the table above). The cmdlet leaves explicit DWORDs in the registry (measured on the VM: after `Set-SmbClientConfiguration` the value doesn't go back to "none").

**Interaction with `net.smb-guest-access` and `net.smb-signing-client-off`** (tweaks with a journal, same registry key): the action overwrites their state. If one of them was applied, undoing it later restores the journal snapshot from before it was applied (the previous value or no value), which removes the hardening. The engine doesn't detect this; the warning in the catalog says so explicitly.

## Risks and warnings
- `risk=caution`. Warnings (consistent with the `warnings` field in the catalog): "Old NAS devices and network drives without signing, or that only let guests in, may stop connecting." and "Undoing the guest logon or SMB signing item later removes this protection."
- Beyond the notes in the program (in the technical details or only here): no undo, with the command for manual undo; Microsoft "Windows disables insecure guest logons by default" + "We recommend that you don't enable insecure guest logons." and the change relative to the Home default; signing (already True on both editions, but enforcing it can block connections to devices without signing: `STATUS_INVALID_SIGNATURE`, "security policies block unauthenticated guest access"); overwriting the state of the SMB tweaks from the journal; client-only scope; behavior under a policy (GPO) not checked: the intent is `Failed` when the verification doesn't match, but it wasn't checked whether `Get-SmbClientConfiguration` takes the policy into account; if it doesn't, the action may report `Executed` (verification matches) despite a policy that still overrides the setting. Limitation; verification on a VM with a policy set by the official tool is a CARRY to the next batch.
- The effect on the local network isn't measured.

## VM test
VM run of 2026-10-18 (Home 19:58, Pro 20:10; build 26300.9457), DSO-26H2-Home (Core) and DSO-26H2-Pro (Professional), both without errors (278 passed / 0 failed / 17 skipped on each VM). Local reports: `tests/vm/out/<data>/<VM>/<RunLabel>/report.json` (folder in `.gitignore`).

Case `net-smb-harden` (`Flow=Execute`, `Catalog=BuiltIn`), **Passed** on Home and Pro (Pester main 191/0/17):
- Checked: `execute` code 0, result `Executed`; the `message` field of the CLI report contains the action's stdout: "Klient SMB: EnableInsecureGuestLogons = False, RequireSecuritySignature = True (odczyt zwrotny zgodny)." (in English: "SMB client: EnableInsecureGuestLogons = False, RequireSecuritySignature = True (verification matches)."). `Check` (independent of the action script): **Passed**: the cmdlet gives guest `False` and signing `True`, and the registry DWORDs are 0 and 1.
- `Cleanup` without an error (`CleanupError` none): the cleanup script sets the edition values with the cmdlet, deletes the explicit DWORDs and throws an exception itself when the reading doesn't match the edition default, so no error means that **after the DWORDs are deleted, the cmdlet reading matches the edition default** (Home: guest True, Pro: False; signing True). Limitation: before deleting, `Cleanup` sets values already equal to the defaults, so this reading alone doesn't tell "went back to the default after deletion" from "the reading didn't change". Stronger evidence (a change in the reading after the value is deleted) comes from `net.smb-guest-access` (Pro) and `net.smb-signing-client-off` (Home and Pro).
- `Snapshot` (`AllowInsecureGuestAuth`, `RequireSecuritySignature`): no value before and after `Cleanup`, `SnapshotDiff` empty, so the case left no traces in the registry.
- **Not tested on the VM: no network and no SMB server.** Not checked: connections to a server that requires or doesn't support signing, guest connections, behavior under a policy (no network and no SMB server on the VM).

## Reddit
To be added in part E.
