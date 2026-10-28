<!-- source: docs/tweaks/net.throttling-index.md sha256: 934561574ac22ee34db9125511e3ac4ffd6af49659a5ef32ec72e189657dda7c -->

# net.throttling-index – NetworkThrottlingIndex / SystemResponsiveness (not ported)

## Verdict for 26H2
Not ported (Dawid's decision of 2026-10-18): `NetworkThrottlingIndex` has no MS documentation, and `SystemResponsiveness` = 0 from v3.2 is treated as 20 according to MS, so it changes nothing. The v3.2 alias `net_throttle` has no item in the catalog.

## What it changes
The item isn't in the catalog and changes nothing. Entry considered (v3.2): `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile`, `NetworkThrottlingIndex` = 0xFFFFFFFF and `SystemResponsiveness` = 0 (both DWord).

Reasons for rejection:
- MS (MMCSS): `SystemResponsiveness` is the percentage of CPU guaranteed to low-priority tasks; values below 10 and above 100 are treated as 20. So the zero from v3.2 doesn't give "0%", and the documented effect is the default value.
- `NetworkThrottlingIndex` doesn't appear in MS documentation (the MMCSS page doesn't list it). The semantics (10 by default, 0xFFFFFFFF = off) come only from an anonymous community answer, which isn't MS evidence.
- The v3.2 description ("80% bandwidth limit") has no MS source: MS talks about CPU for low-priority tasks, not about connection bandwidth.
- The network effect can't be measured on a VM without a network; writing a value that MS doesn't honor or doesn't document would be a fake tweak.

## Sources
- https://learn.microsoft.com/en-us/windows/win32/procthread/multimedia-class-scheduler-service – MS, accessed 2026-10-18 (fetched twice): `SystemResponsiveness` (REG_DWORD, rounded to multiples of 10, values below 10 and above 100 treated as 20, 100 turns off MMCSS); `NetworkThrottlingIndex` isn't listed.
- https://learn.microsoft.com/en-my/answers/questions/4324930/network-throttling-mechanism – community answer (this is NOT MS documentation), accessed 2026-10-18: 10 by default, `ffffffff` as off.
- Research: `docs/zrodla/2026-10-18-partia-c3-zrodla.md` (in Polish), section 1.

## Defaults on a fresh 26H2
Read from the VMs DSO-26H2-Home and DSO-26H2-Pro (build 26300.9457), 2026-10-18 (Home = Pro, read only), key `SystemProfile`:
- `NetworkThrottlingIndex`: **DWORD 10** (0x0000000A).
- `SystemResponsiveness`: **DWORD 20** (0x00000014).
- The key contains only these two values and the `Tasks` subkey.
- Not settled: the effect of writing 0xFFFFFFFF or its persistence after a restart (not tested), the network effect (VM without a network).

## Detection and undo
Not applicable, the item isn't ported. If it were ever reconsidered: only `NetworkThrottlingIndex` = 0xFFFFFFFF in `registry[]` (`original` = 10 according to the VM reading), `risk=experimental`, without a `SystemResponsiveness` entry.

## Risks and warnings
Reasons in the "What it changes" section. If the item were ever added, it would have to say that the value comes from unofficial guides, that MS doesn't document it, and that the effect is unmeasured.

## VM test
Not applicable (the item isn't ported, there's no VM case in the catalog). Only the reading of the defaults on the Home and Pro VMs was done on 2026-10-18 (10 and 20, see "Defaults on a fresh 26H2"). Writing 0xFFFFFFFF, persistence after a restart and the network effect weren't checked.

## Reddit
To be added in part E.
