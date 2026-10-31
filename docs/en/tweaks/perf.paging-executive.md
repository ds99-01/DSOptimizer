<!-- source: docs/tweaks/perf.paging-executive.md sha256: 527b0636a5a8cd851d9f103a459d22a34e05402760062636b791f4c5d303ffd4 -->

# perf.paging-executive – Keep kernel and driver code in memory

## Verdict for 26H2
Port as experimental: the value is documented by MS, but only in archived sources (Windows Server 2003, a blog post from 2007). No evidence of a benefit on 26H2: MS says nothing about Windows 10/11, and on the VM there is no indicator that would show the effect.

## What it changes
- `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management`, value `DisablePagingExecutive`, **DWord**: `0` -> `1`. `scope=machine`, written through the registry API in C#, without a script.
- According to MS (archive): 0 = drivers and system code can be paged out to disk (default), 1 = they must stay in physical memory. The change takes effect only after a restart, so `requiresReboot=true`.
- Side effect: kernel and driver code takes up physical memory permanently.

## Sources
- https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-server-2003/cc757875(v=ws.10) – MS, archive (Windows Server 2003), accessed 2026-10-18: type REG_DWORD, range 0/1, default 0, restart required; setting 1 is useful when debugging drivers and "improves performance on machines with a lot of memory".
- https://learn.microsoft.com/en-us/archive/blogs/irenak/sysk-318-registry-key-that-controls-device-driver-and-system-code-pagination-to-disk – MS, archived blog (SYSK 318, 2007-03-30), accessed 2026-10-18: the same description; on machines with a lot of memory it may improve performance, but it "would depend on the installed applications and their resource utilization".
- No MS source for Windows 10, 11 and 26H2 (it's unknown whether the modern kernel honors the value or how it works with VBS/HVCI).
- Reading from 26H2 Home and Pro VMs, 2026-10-18 (`docs/zrodla/2026-10-18-partia-c2-zrodla.md` (in Polish), "Wyniki VM" (VM results), section 2).

## Defaults on a fresh 26H2
Reading from the DSO-26H2-Home and DSO-26H2-Pro VMs (build 26300.9457), 2026-10-18 (Home = Pro):
- `DisablePagingExecutive` **exists and is DWORD 0**. So `original` = 0, and a snapshot on such an image will save 0, not a missing value.
- Not settled: what the measurable effect is, and the behavior with VBS/HVCI (VBS isn't turned on on the VM). That the value stays after writing 1 and a real restart was confirmed by the VM run (section "VM test").

## Detection and undo
- Detection: registry only; value DWORD 1 -> `Applied`, any other or missing -> `NotApplied`. The real kernel state isn't available through any documented API, so `Applied` means "value set according to MS", not "measured effect". Apply returns `Applied` with the required restart flag (`requiresReboot`); `PendingReboot` only when detection after the write doesn't confirm the value.
- Undo: restores the value from the snapshot in the journal. If the value wasn't there on some image, undo deletes it (`<RemoveEntry>` from the snapshot) instead of writing 0. The `original` fallback = 0 (confirmed by a VM reading).

## Risks and warnings
- `risk=experimental`.
- Warnings in the catalog: "Requires a computer restart." and "Part of the RAM stays permanently in use."
- Beyond the notes in the program (in the technical details or only here): kernel and driver code stays in physical memory; the MS sources are only archived (2003/2007) and mention a gain only on machines with a lot of memory and depending on the application; no evidence of a benefit on 26H2.
- The v3.2 description ("drastically speeds up syscalls") and the "32 GB RAM" threshold have no MS source, so they don't go into the catalog.

## VM test
VM run of 2026-10-18 (13:51, build 26300.9457, HEAD `da78bd7`), DSO-26H2-Home (Core) and DSO-26H2-Pro (Professional), both without errors (212 passed / 0 failed / 10 skipped on each VM; the skipped ones are only `disk-hibernation`). Local reports: `tests/vm/out/20261018-135146/<VM>/<RunLabel>/report.json` (folder in `.gitignore`).

Case `perf-paging-executive` (`Flow=Reboot`, run `reboot-perf-paging-executive`, a separate run from a clean checkpoint; Pester 14/14 on Home and Pro), Home and Pro identical:
- Checked: `detect-before` = `NotApplied`, `apply` = `Applied` (`requiresReboot` = true), `detect-after-apply` = `Applied`, **real restart** (confirmed by a later `LastBootUpTime` and the disappearance of a volatile marker), `detect-after-reboot` = `Applied`, `undo` = `Undone` (`requiresReboot` = true, `finalState` = `NotApplied`), `detect-after-undo` = `NotApplied`. Final state = state before (DWORD 0).
- There is no "value missing at the start" case, because the value exists on Home and Pro; the `<RemoveEntry>` path is covered by a unit test on fakes.
- **Not checked:** the effect of `DisablePagingExecutive` (the system gives no indicator, and the item has no evidence of a benefit on 26H2), the behavior under memory load or on real hardware. Only the registry value and its persistence after a restart are checked.
