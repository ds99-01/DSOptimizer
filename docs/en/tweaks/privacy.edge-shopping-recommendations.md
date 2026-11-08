<!-- source: docs/tweaks/privacy.edge-shopping-recommendations.md sha256: c046d8fb27e0532ac42d5a3cefff7d34437c68b6e1d4cf1da637d29015af23ba -->

# privacy.edge-shopping-recommendations – Turn off shopping and recommendations in Edge

## Verdict for 26H2
Port as a **non-suggested** item: three Edge policies written together; a test on a 26H2 VM (Home and Pro) showed an effect in Edge settings (the shopping setting off and locked, with a notice that your organization manages it). A separate effect is visible only for `EdgeShoppingAssistantEnabled`; `ShowRecommendationsEnabled` and `SpotlightExperiencesAndRecommendationsEnabled` had no separate observation, and what they do is described on Microsoft's pages. Not suggested, because Edge shows the setting as managed by your organization.

## What it changes
- `HKLM\SOFTWARE\Policies\Microsoft\Edge`, three **DWord** values set to `0`: `EdgeShoppingAssistantEnabled`, `ShowRecommendationsEnabled`, `SpotlightExperiencesAndRecommendationsEnabled`; `scope=machine`.
- On a fresh 26H2 there's no `Edge` key (the `HKLM\SOFTWARE\Policies\Microsoft` key exists). Apply creates the key, undo deletes the values. The `Edge` key disappears on undo only when the change created it and it's empty; when it holds values of another Edge item, it stays (the program reports this with a warning).
- No Windows restart. In the test Edge was started again in every phase; Microsoft's page for the `SpotlightExperiencesAndRecommendationsEnabled` policy says "Requires browser restart", so an open Edge window may not show the change until the browser restarts.

## Sources
- Test on 26H2 Home and Pro VMs, 2026-11-07 (build 26300.9457, Windows not activated, local account, with network): write through the registry API (.NET) and observation in Edge (UI Automation, screenshots), plus reading the `edge://policy` table.
- Microsoft Edge policy pages (MS Learn, accessed 2026-11-07; all of them: path `SOFTWARE\Policies\Microsoft\Edge`, `REG_DWORD`, not marked as deprecated, not limited to domain-joined or MDM-managed computers):
  - policy index: https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies (ms.date 2026-11-04);
  - `EdgeShoppingAssistantEnabled`: https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/edgeshoppingassistantenabled (ms.date 2026-05-20): "If you disable this policy, shopping features such as price comparison, coupons, rebates, and express checkout aren't automatically found for retail domains.";
  - `ShowRecommendationsEnabled`: https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/showrecommendationsenabled (ms.date 2026-05-20): "If you disable this setting, users won't receive any recommendations or notifications from Microsoft Edge.";
  - `SpotlightExperiencesAndRecommendationsEnabled`: https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/spotlightexperiencesandrecommendationsenabled (ms.date 2026-05-20): "If you disable this setting, spotlight experiences and recommendations are turned off.", "Requires browser restart", Windows only.
- Win11Debloat: https://github.com/Raphire/Win11Debloat (branch master), files `Config/Features.json` lines 540–544 and `Regfiles/Disable_Edge_Ads_And_Suggestions.reg` lines 2–16; accessed 2026-11-06.
- privacy.sexy: https://github.com/undergroundwires/privacy.sexy (branch master), file `src/application/collections/windows.yaml`, lines 10145–10277; accessed 2026-11-06.

## Defaults on a fresh 26H2
Reading from the `DSO-EN-Home` and `DSO-EN-Pro` VMs, 2026-11-07: the `HKLM\SOFTWARE\Policies\Microsoft` key exists, the `HKLM\SOFTWARE\Policies\Microsoft\Edge` key and all three values: **none** (the Edge shopping setting on and active).

## Detection and undo
- Detection: all values from "What it changes" equal to the written ones -> `Applied`; missing value or a different value -> `NotApplied`.
- Undo restores the snapshot from before the change from the journal (a missing value = deleting it) and deletes the `Edge` key if apply created it and it's empty (`KeyAbsentAfterUndo`); when it holds values of another Edge item, the key stays and the program reports this with a warning. `original` from the catalog (`<RemoveEntry>`) is only a fallback.
- The `HKLM\SOFTWARE\Policies\Microsoft` key existed before apply and stays (`KeyPresentAfterUndo`).
- The `Edge` key is shared with other Edge items; when it existed before apply with other values, undo keeps the key and those values.

## Risks and warnings
- `risk=safe`, `suggested=false`: turns off Edge shopping features; no loss of another function was observed.
- Warning in the catalog: "Edge will show the shopping setting as managed by your organization and won't let you change it until you undo this."
- The policies show in `edge://policy` as set for the device. The Polish wording of this notice and of the shopping setting wasn't read (the value wasn't written on the Polish VM), so the program's Polish texts describe them without a quote; in English: "This setting is managed by your organization." next to "Save time and money with Shopping in Microsoft Edge".
- According to Microsoft's page, `ShowRecommendationsEnabled` turns off all Edge recommendations and notifications; on the VM there were no prompts, banners or a separate setting, so this effect wasn't observed.

## VM test
Case `privacy-edge-shopping-recommendations` (`Flow=Full`) in `tests/vm/cases.psd1`; VM run of 2026-11-08 (build 26300.9457, `Invoke-VmTests.ps1`): **Passed** on `DSO-EN-Home` (Core), `DSO-EN-Pro` and `DSO-PL-Pro` (Professional). Full runs: 915 passed, 2 failed, 25 skipped on both English VMs and 917 passed, 0 failed, 25 skipped on the Polish one. Both failures were in the cases of the two notification items (`privacy.toast-suggested`, `privacy.toast-backup-reminder`; described in their documentation), not this item.

## Observed effect
- **Home** (`DSO-EN-Home`), 2026-11-07, 20:56–20:59. Method: Edge started again in every phase with the `--no-first-run` flag (a Chromium switch, not a policy; without it the welcome screen covered the page), the `edge://settings/?search=shopping` page (UI Automation, screenshots) and the `edge://policy` table, before the write, after the write and after undo. Edge 152.0.4191.66 before the write, 157.0.4341.42 after the write and after undo (updated during the test, with network). Result: before the write "Save time and money with Shopping in Microsoft Edge" was on and active; after the write it was off, inactive, with "This setting is managed by your organization." next to it; after undo it was on and active again. In `edge://policy` each of the three policies: `false`, Platform, Device, Mandatory, status OK.
- **Pro** (`DSO-EN-Pro`), 2026-11-07, 20:56–20:59. Same method and same result; Edge 152.0.4191.66 in all phases.
- Only the shopping setting showed an effect, that is `EdgeShoppingAssistantEnabled`. For `ShowRecommendationsEnabled` and `SpotlightExperiencesAndRecommendationsEnabled` there were no prompts, banners or a separate setting, so their effect wasn't observed (status OK in `edge://policy`, behavior according to Microsoft's pages).
- The first attempt (20:32–20:35, the `edge://settings` and `edge://newtab` pages) showed no differences, because the main settings page in this Edge version doesn't have the shopping setting; the policies already had status OK then.
- Not checked: the shopping suggestions themselves on a store page (price comparisons, coupons).
