<!-- source: docs/tweaks/net.ipv6-disable.md sha256: 21c53232ff14bc93b2af9b5f1c7fccb3bd44775a9615743c8afee89017a791f9 -->

# net.ipv6-disable – Turn off IPv6

## Verdict for 26H2
Port as `experimental`, with warnings taken word for word from MS: the value 0xFF is documented ("IPv6 disabled"), but MS explicitly doesn't recommend disabling IPv6 and recommends Prefer IPv4 (`net.ipv6-prefer-ipv4`) instead. The v3.2 description ("relieves DNS", "speeds up name resolution") and the warnings about Intune/DirectAccess have no MS source, so they don't go into the catalog. v3.2 alias: `net_ipv6`.

## What it changes
- `HKLM\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters`, value `DisabledComponents`, **DWord**: none -> `0xFF` (255). `scope=machine`, written through the registry API in C#, no script.
- The value `0xFFFFFFFF` isn't used: MS writes that it delays system startup by five seconds, and that the correct value is 0xFF.
- The change takes effect after a restart (MS), so `requiresReboot=true`.
- The same registry value is written by `net.ipv6-prefer-ipv4` (0x20); the items are marked as conflicting with each other (the `conflicts` field, enforced by the engine on Apply).

## Sources
- https://learn.microsoft.com/en-us/troubleshoot/windows-server/networking/configure-ipv6-in-windows – MS, accessed 2026-10-18 (fetched twice): 255 / 0xFF / `1111 1111` = IPv6 disabled (max); "We don't recommend that you disable IPv6 or IPv6 components or unbind IPv6 from interfaces. If you do, some Windows components might not function"; "You cannot completely disable IPv6… you will still be able to run ping `::1`"; the adapter's IPv6 checkbox doesn't change state (expected behavior); 0xffffffff -> startup delayed by five seconds; "Values other than 0 or 32 causes the Routing and Remote Access service to fail"; restart required.
- Reading from 26H2 Home and Pro VMs, 2026-10-18 (`docs/zrodla/2026-10-18-partia-c3-zrodla.md` (in Polish), "Wyniki VM" (VM results), section 2).

## Defaults on a fresh 26H2
Read from the VMs DSO-26H2-Home and DSO-26H2-Pro (build 26300.9457), 2026-10-18 (Home = Pro):
- The `Tcpip6\Parameters` key exists, and **`DisabledComponents` doesn't exist** (none, not DWORD 0). Hence `original` = `<RemoveEntry>`.
- `Get-NetAdapterBinding -ComponentID ms_tcpip6`: 1 binding (Ethernet), `Enabled` = true. The binding description is in Polish, so it isn't used for detection.

## Detection and undo
- Detection: registry only, by equality: exactly DWORD 0xFF -> `Applied`; 0xFFFFFFFF, 0x20 and any other value or none -> `NotApplied`. There's no check of the real state of the stack (MS gives no documented reading for that; the adapter checkbox doesn't change), so `Applied` means "value set as MS describes".
- Undo: restores the value from the journal snapshot; if there was no value, it deletes it (it doesn't write 0). Without a journal entry, the fallback is `original` = `<RemoveEntry>`; if `net.ipv6-prefer-ipv4` was in effect before the change without a journal entry, that setting will be lost.
- Conflict with `net.ipv6-prefer-ipv4` (same registry value): the engine enforces the `conflicts` field. Apply of this item is refused (`Refused`, with no write at all and no change to the journal) while `net.ipv6-prefer-ipv4` has an active journal entry; that item has to be undone first, after which Apply succeeds (engine tests on fakes). If the journal can't be read, Apply ends with `Failed` without writing (it doesn't let the conflict through). Undo of this item without its own journal entry (undo to defaults) is also refused (`Refused`) while `net.ipv6-prefer-ipv4` is active, so as not to overwrite its value. Limitation: the conflict covers only journal entries; a `DisabledComponents` value set by hand outside the tool isn't detected as a conflict (apply overwrites it, and undo restores it from the snapshot).
- No `CheckAfterApply` with the prefix policy: MS doesn't define what `Get-NetPrefixPolicy` looks like at 0xFF.

## Risks and warnings
- `risk=experimental`.
- Warnings in the catalog: "Microsoft doesn't recommend turning off IPv6: some Windows features may stop working, such as the Routing and Remote Access service." and "Requires a computer restart."
- Beyond the notes in the program (in the technical details or only here): Prefer IPv4 is recommended instead of disabling; the Routing and Remote Access service stops working at values other than 0 and 32; `::1` still responds, and the adapter's IPv6 checkbox doesn't change; same value as `net.ipv6-prefer-ipv4`, so Apply is refused while that item is active (a value set outside the tool isn't detected as a conflict).
- No performance promises.

## VM test
VM run of 2026-10-18 (Home 19:58, Pro 20:10; build 26300.9457), DSO-26H2-Home (Core) and DSO-26H2-Pro (Professional), both without errors (278 passed / 0 failed / 17 skipped on each VM). Local reports: `tests/vm/out/<date>/<VM>/<RunLabel>/report.json` (folder in `.gitignore`).

Case `net-ipv6-disable` (`Flow=Reboot`, run `reboot-net-ipv6-disable`, a separate run from a clean checkpoint; Pester 14/14 on Home and Pro), **Passed**, Home and Pro identical:
- Checked: `detect-before` = `NotApplied` (`DisabledComponents` doesn't exist), `apply` = `Applied` (`requiresReboot` = true; snapshot after apply: DWORD 255 = 0xFF), `detect-after-apply` = `Applied`, **real restart** (later `LastBootUpTime`, `MarkerGone` = true), `detect-after-reboot` = `Applied`, `undo` = `Undone` (`requiresReboot` = true), after the second restart `detect-after-undo` = `NotApplied`. End state = state before: value absent, the `Tcpip6\Parameters` key stays (`KeyPresentAfterUndo`), `SnapshotDiff` empty.
- No `CheckAfterApply`: MS doesn't define a reading of the stack state at 0xFF (the adapter checkbox doesn't change), so only the registry value and its persistence after a restart are checked.
- **Not checked:** the effect on Windows components and traffic (VM without a network), startup time, the behavior of services that depend on IPv6.

## Reddit
To be added in part E.
