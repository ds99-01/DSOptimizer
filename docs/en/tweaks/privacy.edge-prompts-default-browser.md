<!-- source: docs/tweaks/privacy.edge-prompts-default-browser.md sha256: b826959b4008140b24d8d8b02655e2af3498b1b83a857f85821f6526e5bc7bbb -->

# privacy.edge-prompts-default-browser – Edge default browser prompts (DefaultBrowserSettingEnabled)

## Verdict for 26H2
Not ported: not observable on the VM (no default browser prompt). There was no prompt in any phase, including without the policies, so neither an effect nor the lack of one was proven for either policy. Edge was the default browser on the VM and was started with the `--no-first-run` flag, which may suppress prompts by itself.

## What it changes
The item isn't in the catalog and changes nothing. Entries considered:
- `HKLM\SOFTWARE\Policies\Microsoft\Edge`, `DefaultBrowserSettingEnabled`, DWord, `0`.
- `HKLM\SOFTWARE\Policies\Microsoft\Edge`, `DefaultBrowserSettingsCampaignEnabled`, DWord, `0`.

## Sources
- Test on 26H2 Home and Pro VMs, 2026-11-07 (build 26300.9457, Windows not activated, local account, with network): write through the registry API (.NET), observation in Edge (UI Automation, screenshots) and reading the `edge://policy` table.
- Microsoft Edge policy pages (MS Learn, ms.date 2026-05-20, accessed 2026-11-07):
  - `DefaultBrowserSettingEnabled`: https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/defaultbrowsersettingenabled: "This policy only works for PCs running Windows 7.";
  - `DefaultBrowserSettingsCampaignEnabled`: https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/defaultbrowsersettingscampaignenabled.
- Win11Debloat: https://github.com/Raphire/Win11Debloat (branch master), file `Regfiles/Disable_Edge_Ads_And_Suggestions.reg`, line 13 (UTF-16 file; line numbers after conversion to UTF-8, without blank lines); accessed 2026-11-06.

## Defaults on a fresh 26H2
Reading from the `DSO-EN-Home` and `DSO-EN-Pro` VMs, 2026-11-07: the `HKLM\SOFTWARE\Policies\Microsoft\Edge` key and both values: **none**. Edge is the default browser ("Microsoft Edge is your default browser", the "Make default" button inactive).

## Detection and undo
Not applicable (the item isn't in the catalog).

## Risks and warnings
Not applicable. The item didn't make it into the catalog.

## VM test
Not applicable (not ported): the item isn't in the catalog, so there is no VM case.

## Observed effect
- **Home** (`DSO-EN-Home`), 2026-11-07. Method: writing both policies through the registry API; Edge 157.0.4341.42 started again in every phase with the `--no-first-run` flag (a Chromium switch, not a policy; without it the welcome screen covered the page); the `edge://settings/defaultBrowser` and `edge://policy` pages before the write, after the write and after undo (20:39–20:41). Result: **not observable**. There was no default browser prompt in any phase; Edge was the default browser on the VM, and the script doesn't settle whether the policies work. Both policies had status OK in `edge://policy`.
- **Pro** (`DSO-EN-Pro`), 2026-11-07. Same method (20:39–20:41, Edge 157.0.4341.42) and same result.
