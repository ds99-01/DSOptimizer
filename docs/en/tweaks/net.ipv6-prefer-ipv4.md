<!-- source: docs/tweaks/net.ipv6-prefer-ipv4.md sha256: 83f1e7ab609dbd84ddfc9ed36a4f728cbc56753737bde4bc5fa533e2f1af4ae8 -->

# net.ipv6-prefer-ipv4 – Prefer IPv4 over IPv6

## Verdict for 26H2
Port as `caution`: the value 0x20 is explicitly documented by MS as "Prefer IPv4 over IPv6" and recommended instead of disabling IPv6. The network effect (address order in traffic) isn't measured, because the VMs have no network. New item; v3.2 only had full disabling (`net.ipv6-disable`).

## What it changes
- `HKLM\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters`, value `DisabledComponents`, **DWord**: none -> `0x20` (32). `scope=machine`, written through the registry API in C#, no script.
- According to MS, the value is a bit mask; 0x20 = only the PreferIpv4 bit. The change takes effect after a restart (MS: "You must restart your computer for these changes to take effect"), so `requiresReboot=true`.
- The same registry value is written by `net.ipv6-disable` (0xFF). The items are marked in the catalog as conflicting with each other (the `conflicts` field, enforced by the engine on Apply).

## Sources
- https://learn.microsoft.com/en-us/troubleshoot/windows-server/networking/configure-ipv6-in-windows – MS, accessed 2026-10-18 (fetched twice): `DisabledComponents` REG_DWORD, min 0x00 (default), max 0xFF; "Prefer IPv4 over IPv6" = 32 / 0x20 / `xx1x xxxx`, "Recommended instead of disabling IPv6"; restart required; "Values other than 0 or 32 causes the Routing and Remote Access service to fail" (0x20 is an allowed value).
- Reading from 26H2 Home and Pro VMs, 2026-10-18 (`docs/zrodla/2026-10-18-partia-c3-zrodla.md` (in Polish), "Wyniki VM" (VM results), section 2).

## Defaults on a fresh 26H2
Read from the VMs DSO-26H2-Home and DSO-26H2-Pro (build 26300.9457), 2026-10-18 (Home = Pro):
- The `Tcpip6\Parameters` key exists (subkeys `Interfaces`, `Winsock`), and **`DisabledComponents` doesn't exist** (none, not DWORD 0). Hence `original` = `<RemoveEntry>`.
- `Get-NetPrefixPolicy` works without a network (9 entries): `::ffff:0:0/96` has precedence **35**, `::/0` has **40**, so by default IPv6 wins over IPv4.

## Detection and undo
- Detection: registry only, by equality: exactly DWORD 0x20 -> `Applied`, any other value or none -> `NotApplied`. The engine has no bit masks, so for example 0x21 (the Tunnel bit also set) is `NotApplied`; apply overwrites the whole value, and the journal snapshot keeps the previous one (undo restores it). The real effect (address order) isn't detected by the catalog.
- Undo: restores the value from the journal snapshot; if there was no value, it deletes it (it doesn't write 0). Without a journal entry, the fallback is `original` = `<RemoveEntry>` (the fresh 26H2 state measured on the VMs).
- Informational `CheckAfterApply` in the VM case: `Get-NetPrefixPolicy` (CIM, no language-dependent text) after a restart checks that the precedence of `::ffff:0:0/96` is higher than that of `::/0` (numeric comparison). The criterion comes from the MS page "Configure IPv6 for advanced users" (the sentence: "The '::ffff:0:0/96' prefix should have a higher precedence than the '::/0' prefix"), not from an independent traffic measurement. The prefix policy can be read without a network (VM reading above); result on the VM after a restart: see the "VM test" section.
- Conflict with `net.ipv6-disable` (same registry value): the engine enforces the `conflicts` field. Apply of this item is refused (`Refused`, with no write at all and no change to the journal) while `net.ipv6-disable` has an active journal entry; that item has to be undone first, after which Apply succeeds (engine tests on fakes). If the journal can't be read, Apply ends with `Failed` without writing (it doesn't let the conflict through). Undo of this item without its own journal entry (undo to defaults) is also refused (`Refused`) while `net.ipv6-disable` is active, so as not to overwrite its value. Limitation: the conflict covers only journal entries; a `DisabledComponents` value set by hand outside the tool isn't detected as a conflict (apply overwrites it, and undo restores it from the snapshot).

## Risks and warnings
- `risk=caution`.
- Warning in the catalog: "Requires a computer restart."
- Beyond the notes in the program (in the technical details or only here): the whole value is written (bits other than 0x20 are overwritten, only exactly 0x20 is recognized); same value as `net.ipv6-disable`, so Apply is refused while that item is active (a value set outside the tool isn't detected as a conflict); the impact on traffic and apps isn't measured (VM without a network).
- No promises of performance or faster DNS.

## VM test
VM run of 2026-10-18 (Home 19:58, Pro 20:10; build 26300.9457), DSO-26H2-Home (Core) and DSO-26H2-Pro (Professional), both without errors (278 passed / 0 failed / 17 skipped on each VM). Local reports: `tests/vm/out/<date>/<VM>/<RunLabel>/report.json` (folder in `.gitignore`).

Case `net-ipv6-prefer-ipv4` (`Flow=Reboot`, run `reboot-net-ipv6-prefer-ipv4`, a separate run from a clean checkpoint; Pester 15/15 on Home and Pro), **Passed**, Home and Pro identical:
- Checked: `detect-before` = `NotApplied` (no `DisabledComponents` value), `apply` = `Applied` (`requiresReboot` = true; snapshot after apply: DWORD 32 = 0x20), `detect-after-apply` = `Applied`, **real restart** (later `LastBootUpTime`, `MarkerGone` = true), `detect-after-reboot` = `Applied`, `undo` = `Undone` (`requiresReboot` = true), after the second restart `detect-after-undo` = `NotApplied`. Final state = state before: value absent, the `Tcpip6\Parameters` key stays (`KeyPresentAfterUndo`), `SnapshotDiff` empty.
- `CheckAfterApply` (after the restart, `Get-NetPrefixPolicy`): **Passed**. The script requires exactly one `::ffff:0:0/96` entry, exactly one `::/0` entry, and the `Precedence` of the first higher than that of the second. Before the change, the VM reading gave 35 and 40 (that is, the other way around), so the criterion tells the state before from the state after. The specific values after apply aren't stored in the report (only the Passed result). The criterion comes from the MS sentence quoted in the "Detection and undo" section; the VM confirmed it as the behavior of `Get-NetPrefixPolicy` after a restart, not as an effect on traffic.
- In run 20261018-195830, `CheckAfterUndo` wasn't defined: the return of the prefix policy table to 35 / 40 after undo and a restart wasn't checked then (only the missing registry value and `NotApplied` were checked). After the final review, `CheckAfterUndo` was added (precedence of `::ffff:0:0/96` lower than that of `::/0`). In run 20261018-204534 (`reboot-net-ipv6-prefer-ipv4`, Home and Pro), `CheckAfterUndo` = **Passed**, just like `CheckAfterApply`: after undo and the second restart, the precedence of `::ffff:0:0/96` is again lower than that of `::/0`.
- **Not checked:** the actual preference for IPv4 in traffic (VM without a network), the behavior on hardware with working IPv6.

## Reddit
To be added in part E.
