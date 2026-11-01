<!-- source: docs/tweaks/privacy.soft-landing.md sha256: 45969e2edfcb1f9cbfdc8a4b0c373683c46e920f43c633939b595568781d648c -->

# privacy.soft-landing – Windows tips, policy (DisableSoftLanding)

## Verdict for 26H2
Not ported: no effect: the tips checkbox is still active and selected. Tips are turned off by the `privacy.windows-tips` item (the checkbox in the notification settings).

## What it changes
The item isn't in the catalog and changes nothing. Entry considered: `HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent`, `DisableSoftLanding`, DWord, `1`.

## Sources
- Test on 26H2 Home and Pro VMs, 2026-10-21 (build 26300.9457, Windows not activated, local account): write through the registry API (.NET `RegistryKey`) and observation in Settings (UI Automation, screenshots).
- No Microsoft documentation for this value; evidence: VM test (observation below).

## Defaults on a fresh 26H2
Reading from the VMs, 2026-10-21: value `DisableSoftLanding`: none (Home and Pro).

## Detection and undo
Not applicable (the item isn't in the catalog).

## Risks and warnings
Not applicable. The item didn't make it into the catalog.

## VM test
Not applicable (not ported): the item isn't in the catalog, so there is no VM case.

## Observed effect
- **Home** (`DSO-26H2-Home`), 2026-10-21. Method: writing the policy, checking the tips checkbox in Settings. Result: **no effect**, the "Uzyskaj porady i sugestie…" (Get tips and suggestions…) checkbox is still active and selected.
- **Pro** (`DSO-26H2-Pro`), 2026-10-21. Same method and same result.
