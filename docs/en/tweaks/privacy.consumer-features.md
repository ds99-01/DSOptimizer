<!-- source: docs/tweaks/privacy.consumer-features.md sha256: 84ad96a6bcd5163de2184e0924e6cc944c446e1e46901f7807c28bbb0b658878 -->

# privacy.consumer-features – Consumer experiences (DisableWindowsConsumerFeatures)

## Verdict for 26H2
Not ported: no effect for the signed-in account. The `DisableWindowsConsumerFeatures` policy at most affects new profiles, which is out of scope.

## What it changes
The item isn't in the catalog and changes nothing. Entry considered: `HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent`, `DisableWindowsConsumerFeatures`, DWord, `1`.

## Sources
- Test on 26H2 Home and Pro VMs, 2026-10-21 (build 26300.9457, Windows not activated, local account): write through the registry API (.NET `RegistryKey`) and observation in Settings (UI Automation, screenshots).
- Research: `docs/zrodla/2026-10-20-etap4a-zrodla.md` (in Polish), section "Wyniki VM 26H2 (partia 1)" (26H2 VM results, round 1).
- No Microsoft documentation for this value; evidence: VM test (observation below).

## Defaults on a fresh 26H2
Reading from the VMs, 2026-10-21: value `DisableWindowsConsumerFeatures`: none (Home and Pro).

## Detection and undo
Not applicable (the item isn't in the catalog).

## Risks and warnings
Not applicable. The item didn't make it into the catalog.

## VM test
Not applicable (not ported): the item isn't in the catalog, so there is no VM case.

## Observed effect
- **Home** (`DSO-26H2-Home`), 2026-10-21. Method: writing the policy, checking Start and Settings, restarting the VM. Result: **no effect** for the signed-in account (pinned WhatsApp, LinkedIn, Solitaire stay, Settings unchanged), also after restarting the VM.
- **Pro** (`DSO-26H2-Pro`), 2026-10-21. Same method and same result.

## Reddit
To be added in part E.
