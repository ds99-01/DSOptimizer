<!-- source: docs/tweaks/ux.game-dvr.md sha256: 3b070db62862b6a592269fcd9ab5527bdd9d4eceaaf33556d739330cb2aa6695 -->

# ux.game-dvr – Game DVR background recording (per user)

## Verdict for 26H2
Not ported: there is no MS source for the effect of `GameDVR_Enabled = 0` (the VM confirmed only that the key exists, the DWORD type and the default value 1), and the spec requires a mechanism documented by MS; instead, the `ux_gamedvr` alias from v3.2 points to `ux.game-dvr-policy`.

## What it changes
The item isn't in the catalog and changes nothing. Entry considered: `HKCU\System\GameConfigStore`, value `GameDVR_Enabled`, DWord, `1` -> `0` (`scope=user`).

Reasons for rejection:
- The meaning "0 = background recording off" comes only from an answer on Microsoft Q&A (hint; it's unknown whether the author is an MS employee). The effect wasn't measured on the VM.
- `AppCaptureEnabled` (`HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR`): the `GameDVR` key exists on a fresh 26H2, but the value **isn't there** (VM, Home and Pro), and there is no MS source. It doesn't go in either way.
- `GameDVR_FSEBehaviorMode` (v3.2): no MS source and no VM reading.

The machine policy documented by MS is a separate item: `ux.game-dvr-policy`.

## Sources
- https://learn.microsoft.com/en-us/answers/questions/443550/my-xbox-game-bar-automatically-disabled-and-doesnt – hint (Microsoft Q&A), accessed 2026-10-17: `GameDVR_Enabled` 1 = on.
- https://raw.githubusercontent.com/ChristitusTech/winutil/main/config/tweaks.json – hint, accessed 2026-10-17 (no GameDVR entry).
- Reading from 26H2 Home and Pro VMs, 2026-10-18 (existence and type).
- Research: `docs/zrodla/2026-10-17-partia-c1-zrodla.md` (in Polish), section 2.

## Defaults on a fresh 26H2
Reading from the DSO-26H2-Home and DSO-26H2-Pro VMs (build 26300.9457), 2026-10-18, account `dso`:
- `HKU\<SID>\System\GameConfigStore\GameDVR_Enabled` = **DWORD 1** (Home and Pro).
- `HKU\<SID>\Software\Microsoft\Windows\CurrentVersion\GameDVR\AppCaptureEnabled`: the key exists, **value: none**.

## Detection and undo
Not applicable (the item isn't in the catalog).

## Risks and warnings
Not applicable. The item was rejected in round C1. It can come back when an MS source appears or the effect is measured on a VM with an official tool (for example the toggle in Settings -> reading `GameDVR_Enabled`).

## VM test
Not applicable (not ported): the item isn't in the catalog, so there was no VM case.

## Reddit
To be added in part E.
