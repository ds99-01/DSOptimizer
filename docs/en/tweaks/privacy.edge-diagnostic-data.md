<!-- source: docs/tweaks/privacy.edge-diagnostic-data.md sha256: 0e038bf88ada685b8355e60c6832ce32dd6080b17bcdb8bbd093abbbba3753da -->

# privacy.edge-diagnostic-data – Turn off diagnostic data and feedback in Edge

## Verdict for 26H2
Port as a **non-suggested** item: three Edge policies; a test on a 26H2 VM (Home and Pro) showed an effect in Edge settings: the required diagnostic data setting off and locked (`DiagnosticData`) and the feedback setting locked (`UserFeedbackAllowed`, separate observation), both with a notice that your organization manages them. `PersonalizationReportingEnabled` had no separate observation; what it does is described on Microsoft's page. Not suggested, because `DiagnosticData` = 0 also turns off Edge's required diagnostic data, which Microsoft doesn't recommend, and Edge shows the settings as managed by your organization.

## What it changes
- `HKLM\SOFTWARE\Policies\Microsoft\Edge`, three **DWord** values set to `0`: `DiagnosticData`, `PersonalizationReportingEnabled`, `UserFeedbackAllowed`; `scope=machine`.
- `UserFeedbackAllowed` is an optional entry from the sources; it's included because a separate observation showed an effect.
- On a fresh 26H2 there's no `Edge` key (the `HKLM\SOFTWARE\Policies\Microsoft` key exists). Apply creates the key, undo deletes the values. The `Edge` key disappears on undo only when the change created it and it's empty; when it holds values of another Edge item, it stays (the program reports this with a warning).
- No Windows restart. In the test Edge was started again in every phase; Microsoft's pages for the `DiagnosticData` and `UserFeedbackAllowed` policies say "Requires browser restart", so an open Edge window may not show the change until the browser restarts.

## Sources
- Test on 26H2 Home and Pro VMs, 2026-11-07 (build 26300.9457, Windows not activated, local account, with network): write through the registry API (.NET) and observation in Edge (UI Automation, screenshots), plus reading the `edge://policy` table.
- Microsoft Edge policy pages (MS Learn, accessed 2026-11-07; all of them: path `SOFTWARE\Policies\Microsoft\Edge`, `REG_DWORD`, not marked as deprecated, not limited to domain-joined or MDM-managed computers):
  - policy index: https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies (ms.date 2026-11-04);
  - `DiagnosticData`: https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/diagnosticdata (ms.date 2026-05-20): type Integer, "Off (0) = Off (Not recommended)", "'Off' turns off required and optional diagnostic data collection. This option isn't recommended.", "Requires browser restart";
  - `PersonalizationReportingEnabled`: https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/personalizationreportingenabled (ms.date 2026-05-20): title (accessed 2026-11-08) "Allow personalization of ads, Microsoft Edge, search, news and other Microsoft services by sending browsing history, favorites and collections, usage and other browsing data to Microsoft", "If you disable this policy, users can't change or override the setting.", "This setting isn't available for child accounts or enterprise accounts.";
  - `UserFeedbackAllowed`: https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/userfeedbackallowed (ms.date 2026-09-11): "If you disable this policy, users can't invoke Edge Feedback.", "Requires browser restart".
- Win11Debloat: https://github.com/Raphire/Win11Debloat (branch master), files `Regfiles/Disable_Telemetry.reg` lines 35–39 and `Regfiles/Disable_Edge_Ads_And_Suggestions.reg` lines 2–16 (`UserFeedbackAllowed`); accessed 2026-11-06.
- privacy.sexy: https://github.com/undergroundwires/privacy.sexy (branch master), file `src/application/collections/windows.yaml`, lines 9301–9330 and 10280–10317; accessed 2026-11-06.

## Defaults on a fresh 26H2
Reading from the `DSO-EN-Home` and `DSO-EN-Pro` VMs, 2026-11-07: the `HKLM\SOFTWARE\Policies\Microsoft` key exists, the `HKLM\SOFTWARE\Policies\Microsoft\Edge` key and all three values: **none** (in Edge settings "Required diagnostic data" as text without a toggle, "Send optional diagnostic data…" off and inactive, "Feedback" off and active). The "Feedback" state was read only on Home; on Pro the page didn't load in time.

## Detection and undo
- Detection: all values from "What it changes" equal to the written ones -> `Applied`; missing value or a different value -> `NotApplied`.
- Undo restores the snapshot from before the change from the journal (a missing value = deleting it) and deletes the `Edge` key if apply created it and it's empty (`KeyAbsentAfterUndo`); when it holds values of another Edge item, the key stays and the program reports this with a warning. `original` from the catalog (`<RemoveEntry>`) is only a fallback.
- The `HKLM\SOFTWARE\Policies\Microsoft` key existed before apply and stays (`KeyPresentAfterUndo`).
- The `Edge` key is shared with other Edge items (for example `privacy.edge-shopping-recommendations`); when it existed before apply with another item's values, undo keeps the key and those values. The VM case `privacy-edge-shared-key` (for `privacy.edge-newtab-content`) checks the same mechanism.

## Risks and warnings
- `risk=caution`, `suggested=false`: `DiagnosticData` = 0 also turns off Edge's required diagnostic data ("'Off' turns off required and optional diagnostic data collection"), and Microsoft doesn't recommend it ("Off (Not recommended)"). The value `0` stays, because the sources write it and the test showed an effect with it; the value `1` ("RequiredData") wasn't tested.
- Warnings in the catalog: "It also turns off Edge's required diagnostic data, which Microsoft doesn't recommend." and "Edge will show the diagnostic data and feedback settings as managed by your organization and won't let you change them."
- The Polish wording of the Edge settings and of the organization notice wasn't read (the value wasn't written on the Polish VM), so the program's Polish texts describe them without a quote; in English: "Required diagnostic data", "Send optional diagnostic data to improve Microsoft products", "Feedback", "This setting is managed by your organization".

## VM test
Case `privacy-edge-diagnostic-data` (`Flow=Full`) in `tests/vm/cases.psd1`; VM run of 2026-11-08 (build 26300.9457, `Invoke-VmTests.ps1`): **Passed** on `DSO-EN-Home` (Core), `DSO-EN-Pro` and `DSO-PL-Pro` (Professional). Full runs: 915 passed, 2 failed, 25 skipped on both English VMs and 917 passed, 0 failed, 25 skipped on the Polish one. Both failures were in the cases of the two notification items (`privacy.toast-suggested`, `privacy.toast-backup-reminder`; described in their documentation), not this item.

## Observed effect
Edge started again in every phase with the `--no-first-run` flag (a Chromium switch, not a policy; without it the welcome screen covered the page), the `edge://settings/?search=…` pages (UI Automation, screenshots) and the `edge://policy` table, before the write, after the write and after undo.
- **Home** (`DSO-EN-Home`), 2026-11-07.
  - `DiagnosticData` and `PersonalizationReportingEnabled` written together (20:59–21:02; the `edge://settings/?search=diagnostic` page; Edge 157.0.4341.42). Result: before the write "Required diagnostic data" as text without a toggle, "Send optional diagnostic data…" off and inactive; after the write a "Required diagnostic data" toggle appears, off, inactive, with "This setting is managed by your organization", and "Send optional diagnostic data…" also gets this notice; after undo as before the write. In `edge://policy`: `DiagnosticData` `0`, `PersonalizationReportingEnabled` `false`; both Platform, Device, Mandatory, status OK.
  - `UserFeedbackAllowed` separately (21:02–21:04; the `edge://settings/?search=feedback` page; Edge 157.0.4341.42). Result: before the write "Feedback" off and active; after the write off, inactive, with "This setting is managed by your organization."; after undo off and active. The setting is already off on a clean VM, so the effect is that it gets locked. In `edge://policy`: `false`, Platform, Device, Mandatory, status OK.
- **Pro** (`DSO-EN-Pro`), 2026-11-07.
  - `DiagnosticData` and `PersonalizationReportingEnabled` (20:59–21:02; Edge 152.0.4191.66 before the write, 157.0.4341.42 after the write and after undo). Same result as on Home; `edge://policy` as on Home.
  - `UserFeedbackAllowed` (21:02–21:04; Edge 157.0.4341.42). After the write and after undo the same result as on Home; the before-write phase on Pro wasn't read (the page didn't load in time). `edge://policy` as on Home.
- The effect showed in the diagnostic data setting (`DiagnosticData`) and the feedback setting (`UserFeedbackAllowed`). `PersonalizationReportingEnabled` had no separate observation: the "personalization" search returned only "Manage your history, privacy and personalization settings." in all phases, and the "Allow Microsoft to save your browsing activity… to personalize…" setting was already off on the clean VM and didn't change (status OK in `edge://policy`, behavior according to Microsoft's page).
- The first attempt (20:40–20:44, the `edge://settings/privacy` page) didn't show these settings, because in Edge 157 this page is only a list of subpages.
- Not checked: the sending of diagnostic data from Edge itself (network traffic), or the value `1` of the `DiagnosticData` policy.
