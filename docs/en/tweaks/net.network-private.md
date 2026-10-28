<!-- source: docs/tweaks/net.network-private.md sha256: a24a5e480bf2e797978a906ed6e466d4646d7507e4184b9f2cc39c6ddaa13a7a -->

# net.network-private – Change the active network from public to private

## Verdict for 26H2
Port with a restriction, as `kind=action`, `risk=caution` (new item; v3.2 did this as the first step of `lan_godmode` for **all** profiles). Decisions: only **active `Public` profiles** are changed (never `DomainAuthenticated`, `Private` or profiles without connectivity), and the absence of such a profile is an exit code ≠ 0 with a message ("Failed"), not a green `Executed` with no effect.

The action has no detection, journal or undo. The only evidence of the effect is the **verification** of the category after the change (exit code 0 only when every changed profile is now `Private`).

## What it changes
- Script (`script.apply`, `-File`, PowerShell 5.1): through the helper function `Get-DsoNetConnectionProfile` it reads `Get-NetConnectionProfile`, selects profiles with `NetworkCategory` = `Public` and `IPv4Connectivity` or `IPv6Connectivity` ≠ `Disconnected`, and for each one calls, through `Set-DsoNetConnectionProfileCategory`, the cmdlet `Set-NetConnectionProfile -InputObject <profile> -NetworkCategory Private`. Then it reads the profiles again and checks the category of each changed one (matched by `InterfaceIndex` and `Name`).
- Comparisons use the **enum name** (`[string]$p.NetworkCategory -eq 'Public'`), not the number or formatted language-dependent text. Enum names aren't translated; a profile with a raw number instead of a name wouldn't be treated as `Public` (the script refuses rather than guess a numbering that MS hasn't published for the `MSFT_NetConnectionProfile` class).
- Output (the script's stdout; on `Executed` the engine passes it on in the result, and the CLI in the `message` field of the report): for each changed profile the name, interface alias, index and previous category, plus a ready hint for manual undo (`-InterfaceIndex <n> -NetworkCategory Public`).
- No active `Public` profile (including an empty `Get-NetConnectionProfile` result): `throw` with the message "no active connection with the public network profile – nothing was changed", exit code 1, nothing changed.
- Doesn't touch: `DomainAuthenticated` (MS: "You cannot set the DomainAuthenticated type by using this cmdlet"), `Private` profiles, profiles without connectivity, the firewall, services, the registry.

## Sources
- https://learn.microsoft.com/en-us/powershell/module/netconnection/set-netconnectionprofile – MS Learn, accessed 2026-10-18: `-NetworkCategory` (`Public`, `Private`, `DomainAuthenticated`), `-InputObject`, filters; "Private": "Your PC is discoverable and can be used for printer and file sharing if you set it up", "Public": "Your PC is hidden from other devices on the network and can't be used for printer and file sharing"; "You cannot set the DomainAuthenticated type by using this cmdlet".
- https://learn.microsoft.com/en-us/powershell/module/netconnection/get-netconnectionprofile?view=windowsserver2025-ps – MS Learn, accessed 2026-10-18: reading profiles, `NetworkCategory`, `IPv4Connectivity`, `IPv6Connectivity`, `InterfaceIndex`.
- https://learn.microsoft.com/en-us/windows/security/operating-system-security/network-security/windows-firewall/ – MS Learn (updated_at 2026-10-08), accessed 2026-10-18: firewall profiles (domain/private/public), rules per profile, Private can be set manually, Public is the default profile for unidentified networks; `Get-NetConnectionProfile`/`Set-NetConnectionProfile` to change the category. The wording "networks at home or work, where you know and trust the people and devices on the network" for Private.
- Hint (only from a search engine summary, not from the full page): MS Q&A threads about the "not found" error with `Set-NetConnectionProfile` filters and about "Provider load failure" when reading `MSFT_NetConnectionProfile` through .NET WMI; hence reading with a cmdlet in PowerShell, not with the CIM class from C#.
- MS Learn has no reference page for the `MSFT_NetConnectionProfile` class (namespace `root\StandardCimv2`, numeric `NetworkCategory` values); that's why the script doesn't depend on numbers.
- Reading from 26H2 Home and Pro VMs, 2026-10-18 (the "Wyniki VM" (VM results) section in `docs/zrodla/2026-10-18-partia-c3-zrodla.md` (in Polish)).

## Defaults on a fresh 26H2
Read from the VMs DSO-26H2-Home and DSO-26H2-Pro (build 26300.9457, 2026-10-18; both the same): `Get-NetConnectionProfile` returns **0 objects, without an error**; `Get-NetAdapter` shows one Ethernet adapter (Microsoft Hyper-V Network Adapter) in the `Disconnected` state; the CIM class `MSFT_NetConnectionProfile` in `root\StandardCimv2` is available and empty. The `NetworkCategory` value (enum name or number) and `IPv4Connectivity` **weren't observed** (no objects to read).

## Detection and undo
No detection (`kind=action`). **No undo** (no journal): the action prints the previous category and interface index of each changed profile, and you restore the category by hand with the official cmdlet: `Set-NetConnectionProfile -InterfaceIndex <index> -NetworkCategory Public`. If the change of one of several profiles fails or the verification doesn't confirm it, the error message also lists the profiles already changed (with the previous category), so you have what you need to undo them.

## Risks and warnings
- `risk=caution`. Word for word from MS: Private = "networks at home or work, where you know and trust the people and devices on the network"; in Private "Your PC is discoverable and can be used for printer and file sharing if you set it up". On someone else's network (café, hotel, someone else's office), this lowers protection. Windows Defender Firewall switches to the Private profile rules; MS doesn't list on the overview page which rules are then turned on, so the tool doesn't promise specific effects.
- The "Network List Manager Policies" policy (hint from MS Q&A) can block the category change; the behavior of `Set-NetConnectionProfile` under it isn't checked. The verification won't confirm the change, and the action will end with `Failed`.
- Confirmation in the UI before running (spec, section 7).

## VM test
VM run of 2026-10-18 (Home 19:58, Pro 20:10; build 26300.9457), DSO-26H2-Home (Core) and DSO-26H2-Pro (Professional), both without errors (278 passed / 0 failed / 17 skipped on each VM). Local reports: `tests/vm/out/<date>/<VM>/<RunLabel>/report.json` (folder in `.gitignore`).

Two cases in `tests/vm/cases.psd1` (`Flow=Execute`, `Catalog=BuiltIn`):
- `net-network-private-no-network`: **Passed** on Home and Pro. VM without a network (0 profiles): `execute` code 1, result `OperationFailed` / `Failed`, and the `message` in `report.json` has the Polish characters intact. Verbatim from the report: "Skrypt zakończył się kodem 1: brak aktywnego połączenia w profilu publicznym – nic nie zmieniono" (The script ended with code 1: no active connection with the public network profile – nothing was changed) (Unicode code points: `ł` U+0142, `ń` U+0144, `ą` U+0105, `ę` U+0119, dash `–` U+2013, so the message went from PowerShell through the CLI into JSON without encoding damage). The assertion `MessageContains = 'profilu publicznym'` (a fragment without Polish characters) passed. `Check` (`@(Get-NetConnectionProfile).Count -eq 0`): **Passed**, state = VM baseline, nothing appeared or changed. This is the only path that can be checked on the VM.
- `net-network-private-change`: **Skipped** ("brak sieci na VM" (no network on the VM)), the real change of a `Public` profile → `Private`. Reported as skipped, never as passed.

**Not tested on the VM: no network.** Not checked: the change of a real profile, reading `NetworkCategory`/`IPv4Connectivity` as enum names on a real profile (the VMs have 0 profiles, so the `[string]NetworkCategory` cast on a real profile remains unmeasured; assumption: `[string]` gives the English name; if it didn't, the script would end with the message "no active connection…" instead of changing anything), the firewall behavior after the change, or the behavior under a policy. The network effect (computer visibility, firewall rules) is outside the scope of the VM tests.

## Reddit
To be added in part E.
