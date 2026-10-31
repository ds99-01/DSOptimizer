<!-- source: docs/tweaks/disk.reserved-storage.md sha256: b00c7702262bee05aa2459d4254f99cdf18b2989341656b7c1aa5362560f4ef5 -->

# disk.reserved-storage – Turn off reserved storage for updates

## Verdict for 26H2
Port with a warning: the mechanism (DISM cmdlets) is documented by MS and works in Windows PowerShell 5.1 on Home and Pro (VM), and the state can be read from an enum without parsing text.

## What it changes
- Apply: `Set-WindowsReservedStorageState -State Disabled` (module `Dism`), then verification with `(Get-WindowsReservedStorageState).ReservedStorageState`; if the state isn't `Disabled`, the script fails.
- Undo: `Set-WindowsReservedStorageState -State <Enabled|Disabled>` from the snapshot saved by `capture` (state before the change); a missing or unknown snapshot -> error without a change.
- Without `-Online`: the cmdlet doesn't have this parameter (the example in the MS documentation uses it, but the VM confirmed it's missing).
- The `ReserveManager` registry is neither written nor read (`ShippedWithReserves` is constant and doesn't reflect the state; hint, not MS).

## Sources
- https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/dism-storage-reserve?view=windows-11 – MS, accessed 2026-10-17 (updated_at 2021-09-06): `DISM /Online /Set-ReservedStorageState /State:Enabled|Disabled`, error "This operation is not supported when reserved storage is in use. Please wait for any servicing operations to complete and then try again later."
- https://learn.microsoft.com/en-us/powershell/module/dism/set-windowsreservedstoragestate?view=windowsserver2025-ps and .../get-windowsreservedstoragestate?view=windowsserver2025-ps – MS, accessed 2026-10-17.
- https://support.microsoft.com/help/4496396/windows-10-how-reserved-storage-works – MS, accessed 2026-10-17 (doesn't mention `ReserveManager` or the size).
- Reading from 26H2 Home and Pro VMs, 2026-10-18 (`docs/zrodla/2026-10-17-partia-c1-zrodla.md` (in Polish), "Wyniki VM" (VM results), section 6).
- Reading from the development host (Windows 11 Pro 26200, not a VM), 2026-10-18, read-only: the `detect` and `capture` scripts returned `Applied` / `Disabled` (reserved storage turned off on this host).

## Defaults on a fresh 26H2
Reading from the DSO-26H2-Home and DSO-26H2-Pro VMs (build 26300.9457), 2026-10-18:
- The `Get-/Set-WindowsReservedStorageState` cmdlets are available in PS 5.1 on Home and Pro (module `Dism` 3.0), `Set-` has the `State` parameter, and doesn't have `-Online`.
- `Get-` returns `Microsoft.Dism.Commands.ReservedStorageStateObject`: `ReservedStorageState` (enum Disabled/Enabled) = **Enabled**, and also `LogLevel` (a separate enum; not to be confused with the state).
- After a Disabled -> Enabled cycle, a new value `MiscPolicyInfo` stays in `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\ReserveManager` (none -> 1); `PassedPolicy` goes back to 1, the other values are unchanged.

## Detection and undo
- Detection: `(Get-WindowsReservedStorageState).ReservedStorageState`; `Disabled` -> `Applied`, `Enabled` -> `NotApplied`; missing cmdlet -> `NotSupported`; unknown value -> error (code 1, result `Unknown`, never a false `Applied`). We don't parse the output of `DISM.exe` (text).
- Error "reserved storage is in use" / pending update: the cmdlet throws an error, the script ends with code 1 and the MS message, detection still tells the truth (`NotApplied`), apply fails, and the engine starts the rollback (`Restore` of the undo script). Undo is idempotent: when `Get-` already returns the state from the snapshot (here: `Enabled`, because nothing changed), it ends with code 0 without calling `Set-` (which would also fail with "in use"). Thanks to this the rollback succeeds and the engine result is `FailedRolledBack` (journal entry `Aborted`), not `FailedRollbackIncomplete` with an active entry (mechanism in `TweakEngine.Apply.cs`: `RollBack`; the engine result has no separate test, it is a conclusion from the code; a unit test checks the undo script). Covered by a unit test on a mock of the cmdlet; **the behavior with a real pending update wasn't reproduced** on the VM.
- `capture` saves `ReservedStorageState` (not `LogLevel`). Undo restores this state, and when the state already matches, it does nothing.
- **What stays after undo:** the `MiscPolicyInfo` value in `ReserveManager`, which wasn't there before the change. That's why the `Snapshot` of the VM case has only `ReserveManager\PassedPolicy`, and `MiscPolicyInfo` is deliberately left out of it (it would give a false red); the state is also verified by a Check that reads `PassedPolicy` from the registry. The space freed after turning it off may become visible only after servicing finishes, and after turning it back on, the reserve may appear only after the next update (hint).

## Risks and warnings
- `risk=caution`.
- Warning in the catalog: "When free space is low, updates may fail more often."
- Beyond the notes in the program (in the technical details or only here): the operation isn't possible during servicing; `MiscPolicyInfo` stays after undo.
- In managed environments (WSUS/ConfigMgr), reserved storage works differently according to the MS description; not tested.
- Restart: not required (the cmdlets' stdout has no restart message; MS doesn't say it explicitly, unconfirmed in MS sources).
- Not tested on a VM: the behavior with a pending update, the actual size of the reserve (in the registry `BaseHardReserveSize` = 5 GiB, `BaseSoftReserveSize` = 1.5 GiB are base values, not a measured size).

## VM test
VM run of 2026-10-18 (build 26300.9457), DSO-26H2-Home (Core) and DSO-26H2-Pro (Professional), both **Passed**. Local reports: `tests/vm/out/20261018-080435/<VM>/<RunLabel>/report.json` (folder in `.gitignore`); in the `main` run Pester passed 140, 0 failed, 10 skipped (Home and Pro identical).

Case `disk-reserved-storage` (`Flow=Full`, `main` run), Home and Pro:
- Checked: `detect-before` = `NotApplied` (cmdlet: `Enabled`), `apply` code 0, `detect-after-apply` = `Applied`, `undo` code 0, `detect-after-undo` = `NotApplied`. A **Check** independent of the cmdlet: `CheckAfterApplyResult` = `Passed` (`ReserveManager\PassedPolicy` = 0), `CheckAfterUndoResult` = `Passed` (`PassedPolicy` = 1). `SnapshotDiff` empty for `PassedPolicy` (before 1, after undo 1). `MiscPolicyInfo` is neither in the Snapshot nor in the Check (it stays after undo, as described above).
- **Not checked:** the behavior with a pending update (only a unit test on a mock), the actual size of the reserve, managed environments (WSUS/ConfigMgr).
