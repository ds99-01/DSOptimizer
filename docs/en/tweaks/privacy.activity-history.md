<!-- source: docs/tweaks/privacy.activity-history.md sha256: 58178abe752be4f2887e19e4069863e482a245e773019e96e3d9bd19efc8b374 -->

# privacy.activity-history – Activity history (EnableActivityFeed, PublishUserActivities, UploadUserActivities)

## Verdict for 26H2
Not ported: not observable on the VM (no Activity history page). Settings in 26H2 has no page on which the policies could change anything, so neither an effect nor the lack of one was proven for the three values.

## What it changes
The item isn't in the catalog and changes nothing. Entries considered:
- `HKLM\SOFTWARE\Policies\Microsoft\Windows\System`, `EnableActivityFeed`, DWord, `0`.
- `HKLM\SOFTWARE\Policies\Microsoft\Windows\System`, `PublishUserActivities`, DWord, `0`.
- `HKLM\SOFTWARE\Policies\Microsoft\Windows\System`, `UploadUserActivities`, DWord, `0`.

## Sources
- Test on 26H2 Home and Pro VMs, 2026-11-07 (build 26300.9457, Windows not activated, local account, no network): write through the registry API (.NET) and observation in Settings (UI Automation, screenshots), also with a VM restart.
- Microsoft Learn, Policy CSP Privacy: https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-privacy (accessed 2026-11-07).
- Win11Debloat: https://github.com/Raphire/Win11Debloat (branch master), file `Regfiles/Disable_Telemetry.reg`, lines 28–30 (UTF-16 file; line numbers after conversion to UTF-8, without blank lines); accessed 2026-11-06.
- WinUtil: https://github.com/ChrisTitusTech/winutil (branch main), file `config/tweaks.json`, lines 1–28; accessed 2026-11-06.

## Defaults on a fresh 26H2
Reading from the `DSO-EN-Home` and `DSO-EN-Pro` VMs, 2026-11-07: the `HKLM\SOFTWARE\Policies\Microsoft\Windows\System` key exists, all three values: **none**.

## Detection and undo
Not applicable (the item isn't in the catalog).

## Risks and warnings
Not applicable. The item didn't make it into the catalog.

## VM test
Not applicable (not ported): the item isn't in the catalog, so there is no VM case.

## Observed effect
- **Home** (`DSO-EN-Home`), 2026-11-07. Method: writing the three policies through the registry API, the `ms-settings:privacy-activityhistory` page and the "Privacy & security" list in a new Settings window before the write, after the write and after undo (20:28–20:29), then a repeat with a VM restart after the write and after undo (21:04–21:10). Result: **not observable**. `ms-settings:privacy-activityhistory` opens the Settings home page, and the "Privacy & security" list has no "Activity history" entry; the same in all phases, also after a VM restart.
- **Pro** (`DSO-EN-Pro`), 2026-11-07. Same method and same result.
