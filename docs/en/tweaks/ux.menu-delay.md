<!-- source: docs/tweaks/ux.menu-delay.md sha256: 7679f06aeb8234cb212cd7d4c8780b1f063da0ce36181ceabdec123464e21724 -->

# ux.menu-delay – Open submenus faster

## Verdict for 26H2
Port: the `MenuShowDelay` value exists on a fresh 26H2 as `REG_SZ "400"` (VM), so the tweak has a real, readable state; its effectiveness in File Explorer on 26H2 wasn't measured.

## What it changes
- `HKCU\Control Panel\Desktop`, value `MenuShowDelay`, type **String** (REG_SZ, text with a number of milliseconds), `400` -> `10`.
- Scope `scope=user`: the catalog writes to the hive of the signed-in interactive user, not to the HKCU of the Administrator account.

The target value `"10"` comes from v3.2 (`ux_menu`). The MS documentation (TechNet archive) describes the value only as "milliseconds in decimal notation", default 400, **without giving the lower and upper limits of the range**. So the MS source doesn't confirm that `10` is the "right" value; it's a deliberate choice (practically instant submenus, but non-zero, because the value 0 is sometimes described as a cause of submenus opening by accident – hint). WinUtil (hint) uses 200. Changing the value means changing one field in `ux.json`.

## Sources
- https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-2000-server/cc978617(v=technet.10) – MS archive, accessed 2026-10-17: type REG_SZ, range "milliseconds in decimal", default 400, description of the value.
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfow – MS, accessed 2026-10-17 (only a summary from a search): `SPI_GETMENUSHOWDELAY`/`SPI_SETMENUSHOWDELAY`, value in ms. The catalog doesn't use this API.
- https://raw.githubusercontent.com/ChristitusTech/winutil/main/config/tweaks.json – hint (WinUtil: 200, String, original 400), accessed 2026-10-17.
- Reading from 26H2 Home and Pro VMs, 2026-10-18 (`docs/zrodla/2026-10-17-partia-c1-zrodla.md` (in Polish), "Wyniki VM" (VM results)).
- Research: `docs/zrodla/2026-10-17-partia-c1-zrodla.md` (in Polish), section 1.

## Defaults on a fresh 26H2
Reading from the DSO-26H2-Home and DSO-26H2-Pro VMs (build 26300.9457), 2026-10-18, account `dso`:
- `HKU\<SID>\Control Panel\Desktop\MenuShowDelay` = **REG_SZ `"400"`** (Home and Pro identical).

Not settled (not tested): the system's behavior after the value is deleted.

## Detection and undo
- Detection: the `MenuShowDelay` value (String) equal to `"10"` = `Applied`; `"400"` or a missing value = `NotApplied`.
- Undo: restores the value from the snapshot saved at apply (type String). When there is no snapshot, the `original` fallback = `"400"` (confirmed on the VM).

## Risks and warnings
- `risk=safe`.
- Warning in the catalog: "You may need to sign in again before the change works in programs that are already running." Neither MS nor the VM settles this (the VM only read the value), so the warning stays.
- Some Win32 applications may ignore the value (hint).

## VM test
VM run of 2026-10-18 (build 26300.9457), DSO-26H2-Home (Core) and DSO-26H2-Pro (Professional), both **Passed**. Local reports: `tests/vm/out/20261018-080435/<VM>/<RunLabel>/report.json` (folder in `.gitignore`); in the `main` run Pester passed 140, 0 failures, 10 skipped (Home and Pro identical).

Case `ux-menu-delay` (`Flow=Full`, `main` run), Home and Pro:
- Checked: `detect-before` = `NotApplied` (Setup set `MenuShowDelay` = `"250"`, unlike the `original` `"400"`), `apply` code 0, `detect-after-apply` = `Applied`, `undo` code 0, `detect-after-undo` = `NotApplied`. `SnapshotBefore` contains `HKU\<SID>\Control Panel\Desktop\MenuShowDelay` String `"250"`, and `SnapshotDiff` (before vs. after undo) is empty, so undo restored the value from the snapshot, not from the `"400"` fallback. Snapshot after apply = `"10"`. `NotWrittenToDiff` empty: apply didn't touch the `HKCU` of the Administrator account.
- Check: none (registry item). There was no restart (not required in the catalog).
- **Not checked:** whether menus really open faster, whether the change requires signing in again, and how Win32 applications behave (the VM checks only the write, detection and undo of the value).

## Reddit
To be added in part E.
