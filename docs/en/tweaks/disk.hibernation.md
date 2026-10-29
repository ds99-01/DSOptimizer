<!-- source: docs/tweaks/disk.hibernation.md sha256: 11a239c55830fa116224df629ce890d9d8a15dc4c40de42d50732eeb04799c48 -->

# disk.hibernation – Turn off hibernation

## Verdict for 26H2
Port as `risk=experimental`: the mechanism (`powercfg /hibernate off|on`) is documented by MS, but apply and undo haven't been checked on any machine that supports hibernation (Hyper-V VMs don't support it), so the item stays experimental until it is verified on real hardware.

## What it changes
- Apply: `powercfg.exe /hibernate off`, only when detection says `NotApplied`. On a machine without hibernation support (`NotSupported`) the engine refuses apply, and the script also fails on its own without calling `powercfg` (nothing is written).
- Undo: `powercfg.exe /hibernate on`, only when the snapshot from `capture` says `Enabled`. `/h on` is skipped (code 0) only with the same evidence as `capture` `Enabled` (hibernation file present and `HibernateEnabled` not equal to 0); the "half-applied" state (`HibernateEnabled` = 0, the file is still there) is not "already on", so rollback calls `/h on`. After `/h on` the script checks the same evidence (it assumes that `HiberFilePresent` refreshes right after `/h on`; MS doesn't describe this, and it can't be checked on a VM). Snapshot `Disabled` or `Unsupported`: nothing to restore (code 0). No snapshot or an unknown value: error (it's unclear whether to turn hibernation on).
- Apart from `powercfg`, the scripts write nothing. Side effect of `powercfg /h off` itself, observed on the VM: it creates `HKLM\SYSTEM\CurrentControlSet\Control\Power\HibernateEnabled` = DWORD 0.
- It doesn't change the file type (`/hibernate /type`) or size (`/size`).

## Sources
- https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/powercfg-command-line-options – MS, accessed 2026-10-17: `/hibernate on|off`, `/type reduced|full`, `/size`.
- https://learn.microsoft.com/en-us/troubleshoot/windows-client/setup-upgrade-and-drivers/disable-and-re-enable-hibernation – MS, accessed 2026-10-17: `powercfg.exe /hibernate off|on`; after hibernation is turned off, hybrid sleep doesn't work (warning about data loss on power failure).
- https://learn.microsoft.com/en-us/windows/win32/power/system-power-states – MS, accessed 2026-10-17: hibernation file sizes, fast startup as hibernation after signing out.
- https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-system_power_capabilities – MS, accessed 2026-10-18 (page with updated_at 2024-02-22): `SystemS4` = "the operating system supports sleep state S4 (hibernation)", `HiberFilePresent` = "the system hibernation file is present".
- https://learn.microsoft.com/en-us/windows/win32/api/powerbase/nf-powerbase-callntpowerinformation – MS, accessed 2026-10-18: `CallNtPowerInformation` (PowrProf.dll), level `SystemPowerCapabilities` (4) returns `SYSTEM_POWER_CAPABILITIES`; "represents the currently supported power capabilities. It may change as drivers are installed".
- Reading from 26H2 Home and Pro VMs, 2026-10-18 (`docs/zrodla/2026-10-17-partia-c1-zrodla.md` (in Polish), "Wyniki VM" (VM results), section 3).
- Reading from the development host (Windows 11 Pro 26200, not a VM), 2026-10-18, read-only: `SystemS4` = TRUE, `HiberFilePresent` = FALSE, `HibernateEnabled` = 0 (hibernation turned off by the owner), the `detect` and `capture` scripts returned `Applied` / `Disabled`.

## Defaults on a fresh 26H2
Reading from the DSO-26H2-Home and DSO-26H2-Pro VMs (build 26300.9457), 2026-10-18 (Home = Pro):
- `powercfg /a`: "The system firmware does not support hibernation" (also S1, S2, S3, S0 Low Power Idle); `C:\hiberfil.sys` doesn't exist at any step.
- `Control\Power\HibernateEnabled`: **none**; `HibernateEnabledDefault` = DWORD 1; `HiberFileSizePercent` = DWORD 0; `Control\Session Manager\Power\HiberbootEnabled` = DWORD 1; `Session Manager\Power\HibernateEnabled` – none.
- `powercfg /h off` ends with code 0 and, **despite the missing support**, creates `HibernateEnabled` = 0; `powercfg /h on` ends with code 1.

## Detection and undo
No parsing of `powercfg` text (it is localized, and `powercfg /a` returns code 0 even when hibernation is missing). The `detect` script reads:
- `CallNtPowerInformation(SystemPowerCapabilities)` through `Add-Type` (byte 6 = `SystemS4`, byte 8 = `HiberFilePresent`, layout from the MS documentation: `BOOLEAN` fields one after another),
- `HKLM\SYSTEM\CurrentControlSet\Control\Power\HibernateEnabled` (64-bit view).

Rules (in this order):
1. `HibernateEnabled` = 0 and no hibernation file -> `Applied`.
2. `SystemS4` = TRUE, or the hibernation file exists, or `HibernateEnabled` = 0 (but the file is still there) -> `NotApplied`.
3. In all other cases (`SystemS4` = FALSE, no file, `HibernateEnabled` not equal to 0, including a missing value) -> `NotSupported`. This is how a clean VM should behave (expected from `powercfg /a` and the missing `HibernateEnabled` value; `SystemS4` wasn't read on the VM directly; the VM run of 2026-10-18 (case `DetectOnly`, Home and Pro) returned `NotSupported`, which confirms rule 3 indirectly).

`capture` saves the state from before the change only with evidence: `Enabled` requires a hibernation file (`HiberFilePresent` = TRUE, a field documented by MS) and `HibernateEnabled` not equal to 0; `Disabled` = an explicit `HibernateEnabled` = 0 and no file (`Applied`); `Unsupported` = `NotSupported`. `SystemS4` = TRUE alone, without a hibernation file and without an explicit `HibernateEnabled` = 0 (a computer with S4 support on which hibernation is off by default), is ambiguous: `capture` fails, so the engine stops apply before any change, and undo will never turn on hibernation that wasn't there. Detection (`NotApplied`) shares the same inputs, but it is apply that is gated by `capture`. `original` doesn't apply (script tweak).

**Uncertainty (MS doesn't describe this):** the MS documentation doesn't say how `SystemS4` behaves after `powercfg /h off`, so rule 1 doesn't rely on `SystemS4`: the "off" state is an explicit `HibernateEnabled` = 0 and no file. On one machine (the development host, hibernation off) `SystemS4` stays TRUE, but that is one observation, not a guarantee. The value `HibernateEnabled` = 0 without hardware support (as on the VM after `/h off`) is reported as `Applied` (hibernation really is off); that's why apply first requires the `NotApplied` state. Unit tests check the whole matrix on mocks (`DiskCatalogTests`), not on the real `powercfg`.

## Risks and warnings
- `risk=experimental` (design decision): not checked on hardware with hibernation support.
- Warnings in the catalog: "On laptops, hibernation and fast startup go away, and if the battery runs out during sleep, unsaved data can be lost."
- Basis (MS documentation, **not a VM test result**): fast startup uses the hibernation file (the sentence "powercfg /h off turns off Fast Startup" doesn't appear literally in MS sources); without hibernation, hybrid sleep doesn't work (MS warning about data loss on power failure).
- `HiberbootEnabled` = 1 on the VM despite "Fast Startup: Hibernation is not available": this value doesn't prove that fast startup works.
- Not tested on a VM: apply and undo on a machine with hibernation support, deletion of `hiberfil.sys`, the effect on boot time, the default file type (full/reduced), and whether `on` after `off` restores the previous type and size. Undo turns hibernation on with the default settings of `powercfg /h on`; it doesn't restore the `/type` or `/size` from before the change.
- Restart: MS doesn't state a requirement; `requiresReboot` = false, unconfirmed.

## VM test
VM run of 2026-10-18 (build 26300.9457), DSO-26H2-Home (Core) and DSO-26H2-Pro (Professional), both **Passed**. Local reports: `tests/vm/out/20261018-080435/<VM>/<RunLabel>/report.json` (folder in `.gitignore`); in the `main` run Pester passed 140, 0 failed, 10 skipped (Home and Pro identical).

- `disk-hibernation-unsupported` (`Flow=DetectOnly`, `main` run): **Passed** on Home and Pro. `detect-before` code 0, result `Success`, state **`NotSupported`**. This confirms detection rule 3 for a clean VM with a run; `SystemS4` = FALSE is an indirect conclusion here (the script returned `NotSupported`), not a separate reading. The `DetectOnly` case has no `Snapshot`, so the absence of registry changes wasn't checked separately here.
- `disk-hibernation` (`Flow=Full`): **Skipped** on Home and Pro. Reason from `cases.psd1`: "VM Hyper-V bez obsługi S4 – apply/undo do weryfikacji na sprzęcie" (Hyper-V VM without S4 support – apply/undo to be verified on hardware). All tests of this case were skipped (10 of 10 skipped in `main`); none of them passed.
- **Not checked (to be verified on hardware with hibernation support):** apply `powercfg /h off` and the removal of `hiberfil.sys`, undo `/h on`, the behavior of `SystemS4` after turning it off, the effect on fast startup, restoring the file type and size. The item stays `risk=experimental`.

## Reddit
To be added in part E.
