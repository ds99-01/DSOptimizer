<!-- source: docs/tweaks/disk.ntfs-8dot3.md sha256: 9807c4af689f1e7f6111a8cc724ab994bdae75af38c1aa70aefd77650905d081 -->

# disk.ntfs-8dot3 – Turn off creating 8.3 short file names

## Verdict for 26H2
Port with a warning: the policy value is documented by MS and is 2 (per volume) on 26H2, but the tweak changes the global policy for new volumes, **not the C: volume**, which already has 8.3 names turned off on a fresh image.

## What it changes
- `HKLM\SYSTEM\CurrentControlSet\Control\FileSystem`, value `NtfsDisable8dot3NameCreation`, **DWord**: `2` -> `1` (off for all volumes). `scope=machine`, written through the registry API in C#, without a script.
- Values according to MS (`fsutil 8dot3name`): 0 = on for all, 1 = off for all, 2 = per-volume setting, 3 = off except on the system volume.
- Why `registry[]` instead of `fsutil 8dot3name set 1`: according to its own output, the tool only changes the registry state ("The registry state is now: 1 (Disable 8dot3 name creation on all volumes)"), and the MS documentation describes this value as what the parameter sets. The VM didn't check whether `fsutil` does anything else (for example on the running system's side).
- **Only affects new names**: existing 8.3 names stay (removing them is a separate `fsutil 8dot3name strip`, outside this tweak). The per-volume flag is stored on the disk, not in the registry (MS), and the tweak doesn't change it.

## Sources
- https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/fsutil-8dot3name – MS, accessed 2026-10-17 (updated_at 2026-02-16): values 0-3, key `NtfsDisable8dot3NameCreation`, the per-volume setting requires value 2; warning about permanently removing names and breaking applications.
- https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/fsutil-behavior – MS, accessed 2026-10-17: `disable8dot3`; unlike `disablelastaccess`, no sentence about a required restart.
- Reading from 26H2 Home and Pro VMs, 2026-10-18 (`docs/zrodla/2026-10-17-partia-c1-zrodla.md` (in Polish), "Wyniki VM" (VM results), section 5).

## Defaults on a fresh 26H2
Reading from the DSO-26H2-Home and DSO-26H2-Pro VMs (build 26300.9457), 2026-10-18 (Home = Pro):
- `NtfsDisable8dot3NameCreation` = **DWORD 2**; `fsutil behavior query disable8dot3`: "The registry state is: 2 (Per volume setting - the default)".
- `fsutil 8dot3name query C:`: "The volume state is: 1 (8dot3 name creation is DISABLED)", "Based on the above settings, 8dot3 name creation is DISABLED on "C:"". **Registry = 2 doesn't describe C:**; C: has 8.3 names turned off already on a fresh image.
- `fsutil 8dot3name set 1` -> registry 1, `set 2` -> registry 2 (starting state). The volume state wasn't read again after the restore.

## Detection and undo
- Detection: only the global registry value; `NtfsDisable8dot3NameCreation` = 1 -> `Applied`, any other (0, 2, 3) or missing -> `NotApplied`. No parsing of `fsutil` text (the output is text; the whole output on the VM was in English despite pl-PL, cause unresolved). We don't detect the state of an individual volume.
- Effect for the user: `Applied` means "global policy = off"; on C: (already off) and for existing names nothing changes; the change applies to newly created names and to volumes that the global policy currently governs.
- Undo: restores the value from the snapshot; the `original` fallback = 2 (confirmed by a VM reading). Turning it back on doesn't recreate the names of files created while it was off.

## Risks and warnings
- `risk=caution`.
- Warning in the catalog: "Old installers (16-bit, some MSI packages) and programs that use short names may stop working."
- Beyond the notes in the program (in the technical details or only here): backups that refer to 8.3 names may stop working too; the change only affects new names, and on a fresh 26H2 it changes nothing for C:.
- Value 1 also applies to external volumes connected later.
- `requiresReboot` = false: MS doesn't state a restart requirement for `disable8dot3` (a conclusion from the missing sentence in the documentation, unconfirmed on a VM). It wasn't tested on a VM whether the registry write takes effect on new files without a restart (no files were created, no `dir /x`).
- The performance gain wasn't measured (previously in the program: "the performance gain wasn't measured"; since 2026-10-26 the description only says what changes).

## VM test
VM run of 2026-10-18 (build 26300.9457), DSO-26H2-Home (Core) and DSO-26H2-Pro (Professional), both **Passed**. Local reports: `tests/vm/out/20261018-080435/<VM>/<RunLabel>/report.json` (folder in `.gitignore`); in the `main` run Pester passed 140, 0 failures, 10 skipped (Home and Pro identical).

Case `disk-ntfs-8dot3` (`Flow=Full`, `main` run), Home and Pro:
- Checked: `detect-before` = `NotApplied` (Setup set 0, unlike the `original` 2), `apply` code 0, `detect-after-apply` = `Applied` (registry = 1), `undo` code 0, `detect-after-undo` = `NotApplied`. `SnapshotBefore` `NtfsDisable8dot3NameCreation` DWORD 0, `SnapshotDiff` empty, so undo restored the value from the snapshot, not the fallback 2.
- Check: none. There was no restart (`requiresReboot` = false).
- **Not checked:** the effect on files (`dir /x`, new 8.3 names), the state of the C: volume (`fsutil 8dot3name query`), whether the write takes effect on new files without a restart; only the global registry value is checked.

## Reddit
To be added in part E.
