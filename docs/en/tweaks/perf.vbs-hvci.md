<!-- source: docs/tweaks/perf.vbs-hvci.md sha256: 0171adc7537a2a7d0f9aa36421fda760b9f98d7353d4ec113f41f989792200b3 -->

# perf.vbs-hvci – Turn off memory integrity (HVCI)

## Verdict for 26H2
Port with a restriction, as `risk=experimental`: only the narrow variant (memory integrity, `Scenarios\HypervisorEnforcedCodeIntegrity\Enabled` = 0), whose "off" value Microsoft gives. The v3.2 variant (additionally `EnableVirtualizationBasedSecurity` = 0) doesn't go in: MS doesn't confirm this value as "off", and it would also turn off the foundation of Credential Guard. Turning off memory integrity lowers kernel protection, so this is a conscious decision by the user, not an "optimization".

## What it changes
- Registry (`registry[]`, written through the C# API): `HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity`, value `Enabled`, **DWord**, `0`. `scope=machine`, `requiresReboot=true`. On a fresh 26H2 the key and the value don't exist, so `original` = `<RemoveEntry>`.
- The script (`script`) writes nothing to the registry. It does two things: `detect` reads `Win32_DeviceGuard` (whether memory integrity is running), and `capture` refuses the change (before any write) when `Scenarios\HypervisorEnforcedCodeIntegrity\Locked` means a UEFI lock. The script's `apply` and `undo` are no-ops (needed so that the operation counts as "mutating" and `capture` runs at all) (the engine reverts the value and the key).
- It doesn't touch: `EnableVirtualizationBasedSecurity`, `WasEnabledBy`, `LsaCfgFlags`, `Locked`, `HVCIEnabled` or BCD.

### Decision: `registry[]` + `script`, not `script` alone
The engine builds the tweak's state from all operations that take part in detection (`StateAggregator.Aggregate`, `src/DSOptimizer.Core/Engine/StateAggregator.cs`): `Applied` only when every operation gives `Match`, `NotApplied` when none does, `Partial` in all other cases. `ScriptOperation.Detect` (`src/DSOptimizer.Core/Operations/ScriptOperation.cs`) maps the last line `Applied`/`NotApplied`/`NotSupported` to `Match`/`Mismatch`/`NotSupported`, and `OperationFactory.Create` (`src/DSOptimizer.Core/Operations/OperationFactory.cs`) adds the registry entries, services and the script to one list. So a registry entry + a script with `detect` coexist in one tweak, and the case "configuration `Enabled` = 0, but `SecurityServicesRunning` still contains 2" gives `Partial` (registry `Match`, script `Mismatch`), that is, detection after the write ≠ `Applied`. With `requiresReboot=true`, `TweakEngine.Apply` then returns `PendingReboot` (`src/DSOptimizer.Core/Engine/TweakEngine.Apply.cs`), and after a restart, when memory integrity stops running, detection gives `Applied` and the journal entry moves from `PendingReboot` to `Active` (`PromotePendingReboot`, `TweakEngine.cs`).

Moving everything into `script` wasn't needed, and it would cost something: rule A1 (undo deletes empty keys created by apply, `RegistryValueOperation.Restore`, `src/DSOptimizer.Core/Operations/RegistryValueOperation.cs`) works only for `registry[]`, so the script would have to delete the key itself, and the registry would go through PowerShell instead of the C# API.

Refusing on a UEFI lock requires an operation whose `Capture` can throw. The `Capture` of a registry entry doesn't refuse, and "detect-only" operations aren't captured (`TweakEngine.Apply.cs`, `mutating`). That's why the script also has `apply` and `undo` (both are one-line no-ops; `apply` runs AFTER the registry write, so a refusal there would be a rollback, not a refusal before the change): then it is a mutating operation, the engine calls its `capture` before any change, and an error stops apply before the registry and journal are written.

## Sources
- https://learn.microsoft.com/en-us/windows/security/hardware-security/enable-virtualization-based-protection-of-code-integrity – MS Learn, accessed 2026-10-18 (ms.date 2026-08-14): names memory integrity = HVCI; keys `Scenarios\HypervisorEnforcedCodeIntegrity` (`Enabled`, `Locked`, `WasEnabledBy`); **`reg add ... /v Enabled /t REG_DWORD /d 0 /f`** as the way to turn it off (Troubleshooting section); UEFI lock (turning it off requires access to UEFI and turning off Secure Boot); `Win32_DeviceGuard`: `SecurityServicesRunning`, code 2 = memory integrity; the change requires a restart.
- https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/oem-hvci-enablement – MS Learn, accessed 2026-10-18 (updated_at 2026-06-12): memory integrity on by default on clean installations of Windows 11 on compatible hardware; Windows 11 22H2 and later shows a warning in Windows Security when it is off.
- https://learn.microsoft.com/en-us/windows/security/identity-protection/credential-guard/configure – MS Learn, accessed 2026-10-18 (updated_at 2026-10-08): a separate Credential Guard switch (`LsaCfgFlags`); the reason why we don't set `EnableVirtualizationBasedSecurity` to zero.
- https://techcommunity.microsoft.com/blog/windows-itpro-blog/expanding-memory-integrity-protection-across-windows-devices/4551984 – MS post (Windows IT Pro Blog), accessed 2026-10-18: only the title and address are confirmed (announcement of extending the protection to more devices); the page content wasn't downloaded. The date "from October 2026" comes from press reports (research, section 4), not from Microsoft; the catalog doesn't give the criteria.
- Reading from 26H2 Home and Pro VMs, 2026-10-18 (`docs/zrodla/2026-10-18-partia-c2-zrodla.md` (in Polish), "Wyniki VM" (VM results), point 4).

## Defaults on a fresh 26H2
Reading from the DSO-26H2-Home and DSO-26H2-Pro VMs (build 26300.9457), 2026-10-18 (Home = Pro), Gen2:
- `Win32_DeviceGuard`: `VirtualizationBasedSecurityStatus` = 0 (according to MS: VBS not enabled), `SecurityServicesConfigured` = [0], `SecurityServicesRunning` = [0]. Memory integrity **isn't running** on a fresh VM.
- The key `...\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity` **doesn't exist** (no `Enabled`, `Locked`, `WasEnabledBy`). The key `Control\CI\State` doesn't exist (no `HVCIEnabled`). In `Control\DeviceGuard` there are no VBS values; `LsaCfgFlags` none.
- Conclusion: `original` for `Enabled` = no value and no key (`<RemoveEntry>`). `CI\State\HVCIEnabled` isn't suitable as a detection signal (on a fresh system the key is missing), so it isn't used even as `detectOnly`.

## Detection and undo
The state consists of two signals (see "Decision" above), both numeric, without parsing language-dependent text:
1. Configuration (`registry[]`): `Enabled` = DWORD 0 → `Match`; missing value, 1 or another type → `Mismatch`.
2. Real state (`script.detect`), in this order (change E3-P2, 2026-10-21):
   - First, reading `Enabled` (helper `Get-DsoHvciEnabled`, .NET, 64-bit view, read-only). Missing value, a type other than DWord or a value ≠ 0 → the script returns `NotApplied` and **doesn't query CIM**: without the configuration `Enabled` = 0 the tool hasn't applied anything, so the service state doesn't matter.
   - Only with `Enabled` = DWord 0: `Get-CimInstance Win32_DeviceGuard` (namespace `root\Microsoft\Windows\DeviceGuard`); the `SecurityServicesRunning` field is an array, so the check is whether it contains the value 2 (not equality). Contains 2 → `NotApplied`, otherwise `Applied`. A CIM error, and also no answer (`$null` instead of an instance, an instance without `SecurityServicesRunning` or an empty array), ends the script with a code ≠ 0 (state unknown), never `Applied`.

Tweak result: `Applied` only when the configuration = 0 **and** memory integrity isn't running. Configuration 0, but the service is still running (before the restart) → `Partial`; after a write with `requiresReboot=true` this gives the result `PendingReboot`, after the restart `Applied`. No configuration (fresh VM, state after undo, memory integrity turned on by Windows) → `NotApplied`, regardless of `SecurityServicesRunning` (registry `Mismatch` + script `Mismatch`). Table: fresh system `NotApplied`; after apply before the restart (HVCI running) `Partial`/`PendingReboot`; after the restart `Applied`; after undo `NotApplied` (also before the restart happens).

History: until 2026-10-21 the script only queried CIM, so "no configuration and nothing running" gave `Partial` (registry `Mismatch` + script `Match`) without a journal entry; the GUI offered "Undo", and the `undo` script rightly refused without a snapshot (`UndoIncomplete`, CLI code 4). Measurement: 2026-10-21.

Apply on a system where memory integrity isn't running (VM): writing `Enabled` = 0 gives `Applied` right away with the `requiresReboot` flag. Refusal on a lock: `capture` (only it; the script's `apply` is a no-op) reads `Scenarios\HypervisorEnforcedCodeIntegrity\Locked` (.NET API, read-only): missing value or DWORD 0 → `Unlocked`; DWORD 1 → error with a message about the UEFI lock and turning off Secure Boot; any other type or value → error (we don't guess). A `capture` error stops apply before any change (to the registry and the journal), result `Failed`, not a success. MS doesn't describe whether the registry `Locked` = 1 always corresponds to a lock in the firmware, which is why the refusal is cautious.

Undo: the engine restores `Enabled` from the snapshot in the journal (the previous value or its absence); when the value wasn't there, it deletes it, and by rule A1 also the empty key created by apply. The `original` fallback = `<RemoveEntry>`. Restoring `Enabled` = 1 (if it was there) takes effect after a restart. The `undo` script changes nothing as long as there is an entry in the journal.

**Undo works only after an apply done by this tool.** Without a journal entry (the "defaults from the catalog" path) it's unknown whether memory integrity was on, and `original` = `<RemoveEntry>` from the VM is true only for hardware without HVCI (where HVCI is on by default, the "default" is `Enabled` = 1). That's why the `undo` script fails with an empty `$Snapshot` (message: no journal entry, nothing was changed, set it by hand in Windows Security). Undo runs in reverse order (script, then registry; `OperationFactory.Create` adds the registry before the script), and `UndoFromDefaults` stops at the first error, so `Enabled` stays untouched and the result is `UndoIncomplete` (CLI code 4, `CliExitCode.Incomplete`), not a success.

Lock and policies: when VBS is locked (`Control\DeviceGuard\Locked`) or memory integrity is enforced by GPO/MDM, writing `Enabled` = 0 may not work, and the item stays in `Partial` / `PendingReboot`. There is no false `Applied` (detection also reads `Win32_DeviceGuard`). Not verified (no such hardware or configuration in the tests).

## Risks and warnings
- `risk=experimental`.
- Warnings in the catalog: "Requires a computer restart; until then memory integrity keeps running." and "Undo works only after a change made in DSOptimizer – otherwise turn the protection on by hand in Windows Security."
- Beyond the notes in the program (in the technical details or only here): turning it off lowers kernel protection; MS turns memory integrity on by default on clean installations on compatible hardware and has announced extending this to more devices (Windows IT Pro Blog post; the date "from October 2026" comes from press reports, not from Microsoft); Windows Security will show a warning; with a UEFI lock the item refuses, and turning it off requires turning off Secure Boot.
- No claims about WSL2/Hyper-V/Docker (no downloaded MS source says this) and no performance numbers (the "10-25% CPU" description from v3.2 has no MS source).
- The impact on performance wasn't measured (previously in the program: "there was no measurement"; since 2026-10-26 the description says that Microsoft doesn't give the gain).

## VM test
Case `perf-vbs-hvci` (`Flow=Reboot`, `Catalog=BuiltIn`) in `tests/vm/cases.psd1`: `Snapshot` of the `Enabled` value, the initial and final state is no value and no key (`KeyAbsentAfterUndo`), `CheckAfterApply`/`CheckAfterUndo` from `Win32_DeviceGuard` (`Get-CimInstance` with `-ErrorAction Stop` and an explicit check that the instance isn't `$null`).

VM run of 2026-10-18 (13:51, state from before the E3-P2 fix: `detect-before` and `detect-after-undo` = `Partial`, today `NotApplied`, see below, build 26300.9457, HEAD `da78bd7`), DSO-26H2-Home (Core) and DSO-26H2-Pro (Professional), both without failures (212 passed / 0 failures / 10 skipped on each VM; the skipped ones are only `disk-hibernation`). Local reports: `tests/vm/out/20261018-135146/<VM>/<RunLabel>/report.json` (folder in `.gitignore`).

Case `perf-vbs-hvci` (`Flow=Reboot`, run `reboot-perf-vbs-hvci`, a separate run from a clean checkpoint; Pester 16/16 on Home and Pro), Home and Pro identical:
- Checked: `detect-before` = `Partial` (no `Enabled` value, memory integrity not running), `apply` = `Applied` (`requiresReboot` = true), `detect-after-apply` = `Applied`, **real restart** (confirmed), `detect-after-reboot` = `Applied`, `undo` = `Undone` (`requiresReboot` = true, `finalState` = `Partial`), `detect-after-undo` = `Partial`. `CheckAfterApply` and `CheckAfterUndo` Passed (configuration and `Win32_DeviceGuard.SecurityServicesRunning` without 2); the `HypervisorEnforcedCodeIntegrity` key created by apply doesn't exist after undo.

Run after the E3-P2 fix (2026-10-21, `Flow=Reboot`): `detect-before` = `NotApplied`, `apply` = `Applied`, after the restart `Applied`, `undo` = `Undone`, `detect-after-undo` = `NotApplied`, code 0.

**Real state not tested on a VM:** on a fresh Gen2 VM memory integrity isn't running (`VirtualizationBasedSecurityStatus` = 0). So the VM case checks the configuration (write, restart, undo, disappearance of the key) and that `Win32_DeviceGuard` still doesn't report 2 in `SecurityServicesRunning`. The scenario "configuration off, memory integrity still running" (`PendingReboot`, then `Applied` after the restart), the refusal on `Locked` = 1 and detection from different `SecurityServicesRunning` arrays are covered by unit tests on mocks and fakes (`PerfVbsHvciTests`). Not checked: whether VBS starts after being turned on on a Gen2 VM, the behavior with a real UEFI lock, the effect on hardware with memory integrity running.

## Reddit
To be added in part E.
