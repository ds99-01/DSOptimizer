<!-- source: docs/tweaks/disk.ntfs-last-access.md sha256: 1d805c0f09744d6b5aadb774ed40fa7b7b96d80a994f1817f39737e48ac6ca88 -->

# disk.ntfs-last-access – Turn off recording the last access time of files

## Verdict for 26H2
Port: the registry value exists on a fresh 26H2 and changes exactly as `fsutil` describes (observed on the VM); the effect on files wasn't measured. Detection also recognizes mode 3 (through a mask of the lowest 2 bits).

## What it changes
- `HKLM\SYSTEM\CurrentControlSet\Control\FileSystem`, value `NtfsDisableLastAccessUpdate`, **DWord**: `0x80000002` (2147483650) -> `0x80000001` (2147483649). `scope=machine`. The item is a script (`script`, not `registry[]`), because detection needs a bit mask, and the engine compares only whole values in `registry[]`, so the C# API alone isn't enough (see "Detection and undo"). The script reads and writes the value only through .NET (`Microsoft.Win32.RegistryKey.OpenBaseKey(LocalMachine, Registry64)`) in the helper functions `Get-/Set-/Remove-DsoLastAccessValue`, without `reg.exe`, `fsutil` and `HKLM:`.
- `0x80000001` is exactly what `fsutil behavior set disablelastaccess 1` writes (VM reading). This choice matches the assumption that the tweak writes the same as the official tool.
- We don't use `fsutil`, because the VM didn't settle whether `fsutil` does anything besides writing this value. The tool says "takes effect immediately (no reboot required)", so it probably also changes the setting of the running system, which the registry write doesn't do (this is inference, not a test result). That's why `requiresReboot = true`: the MS documentation says "You must restart your computer for this parameter to take effect", and writing the registry alone takes effect after a restart.

## Sources
- https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/fsutil-behavior – MS, accessed 2026-10-17 (updated_at 2026-02-16): `disablelastaccess {1|0}`, the parameter updates `NtfsDisableLastAccessUpdate`, "You must restart your computer for this parameter to take effect"; tools (Backup, Remote Storage) may be affected. The documentation describes only 0 and 1.
- https://learn.microsoft.com/en-us/archive/blogs/embedded/preventing-last-access-chatter – MS archive (2006), accessed 2026-10-17.
- https://winaero.com/disable-ntfs-last-access-time-updates-in-windows-10 – hint, accessed 2026-10-17: modes 0-3 and `0x8000000N` (outside the MS documentation).
- Reading from 26H2 Home and Pro VMs, 2026-10-18 (`docs/zrodla/2026-10-17-partia-c1-zrodla.md` (in Polish), "Wyniki VM" (VM results), section 4).

## Defaults on a fresh 26H2
Reading from the DSO-26H2-Home and DSO-26H2-Pro VMs (build 26300.9457), 2026-10-18 (Home = Pro):
- `NtfsDisableLastAccessUpdate` = **DWORD `0x80000002`** (read as Int32: -2147483646); `fsutil behavior query disablelastaccess`: `DisableLastAccess = 2  (System Managed, Last Access Time Updates ENABLED)`.
- `fsutil behavior set disablelastaccess 1` -> `0x80000001`, `set 0` -> `0x80000000`, `set 2` -> `0x80000002` (restoring the starting state). The `0x80000000` flag was set in every observed state; the mode is the lowest 2 bits. **Mode 3 wasn't tested.**

## Detection and undo
- Detection (script, mask of the lowest 2 bits of the DWord value): `(v & 3)` equal to 1 or 3 -> `Applied`; `(v & 3)` equal to 0 or 2, a missing value or a type other than DWord -> `NotApplied`. The `0x80000000` flag is constant in every observed state; the rest decides the mode. States: `0x80000002` (system-managed, updates on; default), `0x80000000` (user-managed, on), `0x80000001` (user-managed, off), `0x80000003` (system-managed, off according to the Winaero hint). Bare `1` and `3` without the flag are also `Applied` (MS documents 0 and 1). Mode 3 wasn't observed on 26H2; recognizing it relies on the hint and the mask, not on a VM reading.
- Why a script: the engine compares whole values in `registry[]` and doesn't support a mask, so with a comparison against the exact `0x80000001`, a user who already has updates turned off in mode 3 (or a bare `1`) would see the item as "to be applied". The C# API alone isn't enough; switching to a script (in line with the rule "scripts only when the API isn't enough") is the only way to get the mask. The registry matches `query` for the observed 0/1/2.
- Apply: when the state is already `Applied` (also mode 3), it writes nothing; otherwise it writes `0x80000001` (DWord, like `fsutil behavior set disablelastaccess 1`) and checks with detection that the result is `Applied`.
- Snapshot (capture): `DWord:<unsigned decimal value>` (for example `DWord:2147483650`) or `Absent` when there is no value; a type other than DWord makes capture fail before anything is changed.
- Undo: restores exactly the snapshot: `DWord:<n>` -> writes that value, `Absent` -> deletes the value; when the state already equals the snapshot, it ends with code 0 without writing (idempotent), and after writing or deleting it checks the effect. An invalid snapshot is an error without changes. There is no `original` fallback from the catalog (no `registry[]`); the snapshot from the journal is the only source, and the 26H2 default state is `0x80000002` (confirmed by a VM reading). Undo without a journal entry (apply done outside the tool) therefore ends with `UndoIncomplete` (CLI code 4) with a message about the missing valid snapshot and changes nothing; the default value isn't restored from the catalog.

## Risks and warnings
- `risk=safe` (reversible value, doesn't delete data).
- Warnings in the catalog: "Requires a computer restart." and "Programs that rely on the last access time (backups, cleanup scripts) will get outdated data."
- Beyond the notes in the program (in the technical details or only here): MS: the parameter may affect Backup and Remote Storage; the restart discrepancy between MS and `fsutil` (decided according to MS: restart required).
- According to the Winaero hint, in system-managed mode the timestamp is modified at system startup, and individual comments say that `0x80000003` went back to `0x80000002` after a restart; unconfirmed, mode 3 out of scope.
- Not tested on a VM: the effect on the `LastAccessTime` of files.
- The performance gain wasn't measured (since 2026-10-26 the description in the program only mentions fewer small writes).

## VM test
VM run of 2026-10-18 (13:51, build 26300.9457, HEAD `da78bd7`), DSO-26H2-Home (Core) and DSO-26H2-Pro (Professional), both without failures (212 passed / 0 failed / 10 skipped on each VM; the skipped ones are only `disk-hibernation`). Local reports: `tests/vm/out/20261018-135146/<VM>/<RunLabel>/report.json` (folder in `.gitignore`). The run was refreshed after the detection change in batch C2 (Task 2: bit mask instead of comparing the exact value `0x80000001`); the earlier C1 run (2026-10-18 08:04, 13/13 in the Reboot run) covered the previous detection.

Case `disk-ntfs-last-access` (`Flow=Reboot`, run `reboot-disk-ntfs-last-access`, a separate run from a clean checkpoint; Pester 14/14 on Home and Pro), Home and Pro identical:
- Checked: `detect-before` = `NotApplied`, `apply` = `Applied` (`requiresReboot` = true), `detect-after-apply` = `Applied`, **real restart** (confirmed), `detect-after-reboot` = `Applied`, `undo` = `Undone`, `detect-after-undo` = `NotApplied`. The restart didn't revert the setting on the VM.
- Case `disk-ntfs-last-access-mode3` (`DetectOnly`, in the `main` run): Setup wrote `0x80000003` through the .NET API, detection = `Applied` (consistent with the mask from Task 2).
- Check: none.
- **Not checked:** the effect on the `LastAccessTime` of files and the behavior on real hardware. The detailed snapshot values from the refreshed run weren't analyzed here (the last `SnapshotDiff` analysis comes from the C1 run: undo restored the value from the snapshot, not the fallback).

## Reddit
To be added in part E.
