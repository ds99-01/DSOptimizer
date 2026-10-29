<!-- source: docs/tweaks/privacy.copilot-policy.md sha256: 314abe4ddc703f554b2c916b1a12e9793596b43ae8928e211c11da92ff9de5d5 -->

# privacy.copilot-policy – TurnOffWindowsCopilot policy

## Verdict for 26H2
Not ported: the `TurnOffWindowsCopilot` policy has no effect on 26H2 (no evidence). Removing the `Microsoft.Copilot` app is round 2 of Stage 4a (Apps), not this item.

## What it changes
The item isn't in the catalog and changes nothing. Entry considered: `HKCU\Software\Policies\Microsoft\Windows\WindowsCopilot`, value `TurnOffWindowsCopilot`, DWord, `1` (`scope=user`; on the VM `HKU\<SID>\...`).

## Sources
- Test on 26H2 Home and Pro VMs, 2026-10-21 (build 26300.9457).
- Research: `docs/zrodla/2026-10-20-etap4a-zrodla.md` (in Polish), sections "1. Microsoft Copilot" and "Wyniki VM 26H2 (partia 1)" (26H2 VM results, round 1).

## Defaults on a fresh 26H2
Reading from the VMs, 2026-10-21: all `Software\Policies\...` values: none (Home and Pro). The `Microsoft.Copilot` app: on Home installed and pinned, on Pro not present.

## Detection and undo
Not applicable (the item isn't in the catalog).

## Risks and warnings
Not applicable. The item didn't make it into the catalog. It can come back if a VM test shows an effect.

## VM test
Not applicable (not ported): the item isn't in the catalog, so there is no VM case.

## Observed effect
- **Home** (`DSO-26H2-Home`), 2026-10-21. Method: writing the policy through the registry API, restarting File Explorer, the Win+C key and the taskbar button in the `dso` session. Result: **no effect**, the Copilot app (preinstalled and pinned) still opens with Win+C, and the button stays.
- **Pro** (`DSO-26H2-Pro`), 2026-10-21. Result: **no effect to show**, because a fresh Pro has no Copilot app (there is nothing to turn off).

## Reddit
To be added in part E.
