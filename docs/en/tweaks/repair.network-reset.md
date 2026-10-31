<!-- source: docs/tweaks/repair.network-reset.md sha256: bde2f21c8c42bb6c07bb43451e2921dc75866c1b30fc376b3d721673a348a340 -->

# repair.network-reset – Reset network settings

## Verdict for 26H2
Port as `kind=action`, `risk=caution`, `requiresReboot=true` (Stage 4b, round 1): `netsh winsock reset` and `netsh int ip reset` work on 26H2 Home and Pro, but the effect shows only after a restart, and on a fresh system `netsh int ip reset` always ends with code 1 and one access denial on a protected key. That's why the result isn't judged by "code ≠ 0 = error", but by the measured pattern (below).

## What it changes
Script (`script.apply`), commands by name (the tests mock `netsh`, `Get-NetIPConfiguration`, `Clear-DnsClientCache`):
1. `Get-NetIPConfiguration` – the IP configuration of each adapter goes to stdout (and so to the log): alias, IPv4 with mask, gateway, DNS, DHCP yes/no. A read error = `Failed` without any change.
2. `netsh winsock reset` – code ≠ 0 = `Failed` with the output; the next commands don't start.
3. `netsh int ip reset` – the result is judged by the rule below.
4. `Clear-DnsClientCache`.

An error after a successful `netsh winsock reset` (steps 3–4) ends the message with the sentence: "Winsock has already been reset – restart the computer; the IP configuration from before the reset is in the log file." On every error of a script action, the engine writes its stdout to the log file (the line "repair.network-reset: script output before the error:"), and the result in the window carries only stderr – so the copy of the IP configuration from step 1 is in the log even after a failure.

Last line of stdout: "The commands succeeded. Takes effect after the computer restarts. The IP configuration from before the reset is above and in the log file." The script resets `$LASTEXITCODE` to zero after accepting code 1 with a note, because the script wrapper ends with `exit $LASTEXITCODE`.

**Rule for judging `netsh int ip reset`** (VM measurement): code 0 = success. Code 1 **and** exactly one line ending with `failed.` **and** "Odmowa dost…" or "Access is denied" in the next non-empty line **and** all other `Resetting …` entries ending with `OK!` = success with the note "One entry protected by the system denied access – this is normal on Windows 11, the rest was reset." Any other case (another code, more than one `failed`, another message, no denial) = `Failed` with the output. The comparison is done without Polish characters ("Odmowa dost"), because the message arrives in the wrong encoding ("dost─Öpu": UTF-8 bytes read as OEM 852); the test covers the PL, EN and garbled variants. The test example is the verbatim output from the Home VM (34 entries, 33 `OK!`, one `failed.`).

## Sources
- Test on 26H2 Home and Pro VMs (26300.9457), 2026-10-22, contexts K1 (PowerShell Direct, Administrator) and K2 (process of the `dso` account, Interactive + Highest): `docs/zrodla/2026-10-22-etap4b-1-pomiary.md` (in Polish), section 2 and probe S1.
- ACL reading (S1, Home): 6 subkeys of `HKLM\SYSTEM\CurrentControlSet\Control\Nsi` can't be opened for writing by the Administrator (among others `{eb004a00-9b1a-11d4-9123-0050047759bc}\26` and `\4`; SDDL `O:BAG:SYD:P(A;OICI;CCDCLCSWRPRC;;;WD)`) – the factory state of a fresh system. Which of them the netsh denial concerns couldn't be confirmed (`netsh int ip reset <file>` doesn't create a log file on 26H2).
- No MS documentation checked in this task (the `netsh` command pages weren't fetched); evidence: the VM test. The restart requirement is confirmed by the tool's own message: "You must restart the computer in order to complete the reset." The behavior on 26H2 comes only from the measurement.

## Defaults on a fresh 26H2
- `netsh winsock reset`: code 0, "Sucessfully reset the Winsock Catalog. You must restart the computer in order to complete the reset."
- `netsh int ip reset`: code 1, 34 entries, 33 × "Resetting …, OK!" and one "Resetting , failed." with "Odmowa dostępu." (Access is denied.) in the next line, and at the end "Restart the computer to complete this action." From the 20th entry on, the names are empty.
- Reading 2026-10-22, Home and Pro, K1 and K2 identical.

## Detection and undo
Not applicable: `kind=action` has no detection or undo (no journal). **There's no verification of the effect before a restart:** until the restart, the adapter still has the old address, and the reset of the Winsock catalog and the IP stack takes effect only after a restart. The result says this plainly ("Takes effect after the computer restarts"), and the engine carries `requiresReboot` into the `Executed` result (the "Restart required" banner). The IP configuration from before the reset is written to stdout, so it can be restored manually.

## Risks and warnings
- `risk=caution`: removes the manual IP configuration and changes the network stack; no undo.
- Warnings in the catalog (also shown in the confirmation window; the text is based on the measurement): "Requires a restart; a manually set IP address, mask and gateway will be lost – the adapter will get an address from DHCP (DNS servers stay)." and "No undo – DSOptimizer saves the current IP settings in the log; VPN and network programs may need to be set up again."
- Measurement caveat: the adapter was disconnected (netsh wrote `DisableDhcpOnConnect=1` with `EnableDHCP=1`); the case "adapter connected, `EnableDHCP=0`" wasn't measured.
- Forbidden in this item (Global Constraints): `netcfg -d`, `ipconfig /release|/renew`, `netsh winhttp reset proxy`.

## VM test
Case `repair-network-reset` (`Flow=Execute`, `Check`: `netsh winsock show catalog` works; the report has `requiresReboot=true` thanks to `Expect.RequiresReboot` in the runner): added to `tests/vm/cases.psd1` in Task 5.

VM run (Task 10, 2026-10-22, full `tests/vm/Invoke-VmTests.ps1` on the `etap4b-1` branch; reports outside git): Home and Pro 722 passed / 0 failed / 25 skipped each (6 cases with `Skip` outside the Repairs category). `repair-network-reset`: **Passed** on Home and Pro – `Executed` with code 0, `requiresReboot=true` in the report, the message contains the IP configuration from before the reset (APIPA, no network), `netsh winsock reset: kod 0` and `netsh int ip reset: kod 1` ("kod" = code) with the note; `Check` (`netsh winsock show catalog` works) passed. The VM wasn't restarted after the reset and has no network or manual IP on a connected adapter – to be confirmed on hardware.

## Observed effect
Method: exit codes and the full `netsh` output, reading the registry `HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{GUID}` (`IPAddress`, `SubnetMask`, `DefaultGateway`, `NameServer`) before the reset, right after it and after a VM restart; a manual IP set with `netsh interface ipv4 set address … source=static` and `Set-DnsClientServerAddress`.
- **Home** (`DSO-26H2-Home`, 26300.9457, no network), 2026-10-22: `winsock reset` code 0; `int ip reset` code 1 (one access denial, the rest OK). The `IPAddress`, `SubnetMask`, `DefaultGateway` values disappear from the registry right away; the manual address works until the restart, and after the restart the adapter is on DHCP (APIPA, because there's no network). The manual DNS servers (`NameServer`) survived the reset and the restart.
- **Pro** (`DSO-26H2-Pro`, 26300.9457), 2026-10-22 (runs P1 and A2): the same result as Home. The network results of P1 are invalid (after the reset the adapter didn't get an address from DHCP); the result is based on A2.
