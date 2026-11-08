<!-- source: docs/tweaks/privacy.settings-365-ads.md sha256: 6bbdf140fd29100537f437652384d0318a7c8e73dd08325ff06f47acb06b8bb9 -->

# privacy.settings-365-ads – Microsoft account cards on the Settings home page (DisableConsumerAccountStateContent)

## Verdict for 26H2
Not ported: no observed effect on Home and Pro. The Microsoft account cards on the Settings home page stayed after the policy was written and after a VM restart; the Enterprise and Education editions, for which Microsoft lists this policy, weren't checked, and neither was the "Back up your PC to Microsoft account" card after a VM restart (in that run the card didn't appear in any phase).

## What it changes
The item isn't in the catalog and changes nothing. Entry considered:
- `HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent`, `DisableConsumerAccountStateContent`, DWord, `1`.

## Sources
- Test on 26H2 Home and Pro VMs, 2026-11-07 (build 26300.9457, Windows not activated, local account): write through the registry API (.NET) and observation of the Settings home page (UI Automation, screenshots), also with a VM restart.
- Microsoft Learn, Policy CSP Experience: https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-experience (ms.date 2026-10-14, accessed 2026-11-07): the policy is listed only for the Enterprise and Education editions.
- Win11Debloat: https://github.com/Raphire/Win11Debloat (branch master), files `Config/Features.json`, lines 744–748, and `Regfiles/Disable_Settings_365_Ads.reg`, lines 2–3 (UTF-16 file; line numbers after conversion to UTF-8, without blank lines); accessed 2026-11-06.
- RemoveWindowsAI: https://github.com/zoicware/RemoveWindowsAI (branch main), file `RemoveWindowsAi.ps1`, line 1125; accessed 2026-11-06.

## Defaults on a fresh 26H2
Reading from the `DSO-EN-Home` and `DSO-EN-Pro` VMs, 2026-11-07: the `HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent` key and the `DisableConsumerAccountStateContent` value: **none**.

## Detection and undo
Not applicable (the item isn't in the catalog).

## Risks and warnings
Not applicable. The item didn't make it into the catalog.

## VM test
Not applicable (not ported): the item isn't in the catalog, so there is no VM case.

## Observed effect
- **Home** (`DSO-EN-Home`), 2026-11-07. Method: writing the policy through the registry API, the Settings home page (`ms-settings:` in a new window) before the write, after the write and after undo (20:21–20:22, with network), then a repeat with a VM restart after the write and after undo (21:32–21:35, no network). Result: **no effect**. The "Back up your PC to Microsoft account" and "It’s all here with Microsoft account" cards are there before the write, after the write and after undo. In the repeat with a restart the "It’s all here with Microsoft account" card is there in all phases, and the "Back up your PC to Microsoft account" card wasn't there in any phase, so for that card the result after a restart is unread.
- **Pro** (`DSO-EN-Pro`), 2026-11-07. Same method and same result.
