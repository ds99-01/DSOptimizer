<!-- source: docs/tweaks/privacy.edge-copilot.md sha256: b6ef4faae33f4262ccf6f9920eba4bac8af70e6b456d33338d1144cb3daab4f6 -->

# privacy.edge-copilot – Hide the Copilot button in Edge

## Verdict for 26H2
Port as a **non-suggested** item: six Edge policies written together; a test on a 26H2 VM (Home and Pro) showed that the "Chat" / "Czat" (Copilot) button disappears from the Edge toolbar. Which of the six policies does it wasn't separated; the others had no separate observation, and Microsoft's pages describe what they do. Variant without `HubsSidebarEnabled`: the test with these six policies gave the same effect as the test with seven, so the item doesn't hide the Edge sidebar. Not suggested, because Edge treats these settings as policies managed by your organization.

## What it changes
- `HKLM\SOFTWARE\Policies\Microsoft\Edge`, six **DWord** values set to `0`: `CopilotPageContext`, `EdgeEntraCopilotPageContext`, `Microsoft365CopilotChatIconEnabled`, `NewTabPageBingChatEnabled`, `EdgeHistoryAISearchEnabled`, `ComposeInlineEnabled`; `scope=machine`.
- `HubsSidebarEnabled` (in the sources together with these policies) isn't included: when it's disabled, Edge never shows the sidebar ("If you disable this policy, the Sidebar is never shown."), and without it the effect was the same (the "Chat" button disappears). The same page says: "As of Microsoft Edge version 141, the Microsoft365CopilotChatIconEnabled policy is the only means of controlling the display of Copilot in the toolbar.".
- On a fresh 26H2 there's no `Edge` key (the `HKLM\SOFTWARE\Policies\Microsoft` key exists). Apply creates the key, undo deletes the values. The `Edge` key disappears on undo only when the change created it and it's empty; when it holds values of another Edge item, it stays (the program reports this with a warning).
- No Windows restart. In the test Edge was started again in every phase; whether an already open Edge window shows the change right away wasn't checked.

## Sources
- Test on 26H2 Home and Pro VMs, 2026-11-07 (build 26300.9457, Windows not activated, local account, with network): write through the registry API (.NET) and observation of the Edge toolbar and the `edge://newtab` page (UI Automation, screenshots), plus reading the `edge://policy` table.
- Microsoft Edge policy pages (MS Learn, accessed 2026-11-07; all of them: path `SOFTWARE\Policies\Microsoft\Edge`, `REG_DWORD`, not marked as deprecated, not limited to domain-joined or MDM-managed computers):
  - policy index: https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies (ms.date 2026-11-04);
  - `CopilotPageContext`: https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/copilotpagecontext (ms.date 2026-05-20): "This policy applies only to Microsoft Entra ID profiles in Microsoft Edge. It doesn't apply to Microsoft account (MSA) profiles.";
  - `EdgeEntraCopilotPageContext`: https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/edgeentracopilotpagecontext (ms.date 2026-08-18): "applies only to users who are signed in to Microsoft Edge with a Microsoft Entra account";
  - `Microsoft365CopilotChatIconEnabled`: https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/microsoft365copilotchaticonenabled (ms.date 2026-05-20): "For users in an Entra ID Microsoft Edge profile …", "If the policy is disabled: Copilot doesn't appear in the toolbar.";
  - `NewTabPageBingChatEnabled`: https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/newtabpagebingchatenabled (ms.date 2026-05-20): the page title refers to the Edge Enterprise new tab page;
  - `EdgeHistoryAISearchEnabled`: https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/edgehistoryaisearchenabled (ms.date 2026-05-20): "When disabled, users can only perform exact match (verbatim) searches in their history.";
  - `ComposeInlineEnabled`: https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/composeinlineenabled (ms.date 2026-05-20): "This policy applies only to Microsoft Entra accounts and doesn't apply to Microsoft accounts.";
  - `HubsSidebarEnabled` (policy outside the item): https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/hubssidebarenabled (ms.date 2026-05-20, accessed 2026-11-08): "If you disable this policy, the Sidebar is never shown.", "As of Microsoft Edge version 141, the Microsoft365CopilotChatIconEnabled policy is the only means of controlling the display of Copilot in the toolbar.".
- Win11Debloat: https://github.com/Raphire/Win11Debloat (branch master), files `Config/Features.json` lines 770–774 and `Regfiles/Disable_Edge_AI_Features.reg` lines 2–11; accessed 2026-11-06.
- RemoveWindowsAI: https://github.com/zoicware/RemoveWindowsAI (branch main), file `RemoveWindowsAi.ps1`, lines 958–977; accessed 2026-11-06.
- privacy.sexy: https://github.com/undergroundwires/privacy.sexy (branch master), file `src/application/collections/windows.yaml`, lines 9945–10088; accessed 2026-11-06.

## Defaults on a fresh 26H2
Reading from the `DSO-EN-Home` and `DSO-EN-Pro` VMs, 2026-11-07: the `HKLM\SOFTWARE\Policies\Microsoft` key exists, the `HKLM\SOFTWARE\Policies\Microsoft\Edge` key and all six values: **none** (the "Chat" button on the Edge toolbar).

## Detection and undo
- Detection: all values from "What it changes" equal to the written ones -> `Applied`; missing value or a different value -> `NotApplied`.
- Undo restores the snapshot from before the change from the journal (a missing value = deleting it) and deletes the `Edge` key if apply created it and it's empty (`KeyAbsentAfterUndo`); when it holds values of another Edge item, the key stays and the program reports this with a warning. `original` from the catalog (`<RemoveEntry>`) is only a fallback.
- The `HKLM\SOFTWARE\Policies\Microsoft` key existed before apply and stays (`KeyPresentAfterUndo`).
- The `Edge` key is shared with other Edge items (for example `privacy.edge-shopping-recommendations`); when it existed before apply with another item's values, undo keeps the key and those values. The VM case `privacy-edge-shared-key` (for `privacy.edge-newtab-content`) checks the same mechanism.

## Risks and warnings
- `risk=caution`, `suggested=false`: the "Chat" (Copilot) button disappears from the Edge toolbar.
- Warnings in the catalog: "The "Chat" (Copilot) button won't be on the Edge toolbar until you undo this item." and "Edge will treat these Copilot settings as managed by your organization until you undo this item."
- The second warning is based on the `edge://policy` table: all six policies as Platform, Device, Mandatory, status OK. A notice about management by your organization next to the Copilot settings wasn't read; with other Edge policies such a notice appears next to the locked setting (`privacy.edge-shopping-recommendations`).
- According to Microsoft's pages, `CopilotPageContext`, `EdgeEntraCopilotPageContext` and `ComposeInlineEnabled` apply only to Edge profiles signed in with a Microsoft Entra account, and `Microsoft365CopilotChatIconEnabled` describes users in an Entra ID profile. The Edge profile on the VM was local, not signed in, and the "Chat" button disappeared anyway.
- Wording: the "Chat" / "Czat" button, the "Copilot menu" / "Menu Copilot" button (read in English and in Polish).

## VM test
Case `privacy-edge-copilot` (`Flow=Full`) in `tests/vm/cases.psd1`; VM run of 2026-11-08 (build 26300.9457, `Invoke-VmTests.ps1`): **Passed** on `DSO-EN-Home` (Core), `DSO-EN-Pro` and `DSO-PL-Pro` (Professional). Full runs: 915 passed, 2 failed, 25 skipped on both English VMs and 917 passed, 0 failed, 25 skipped on the Polish one. Both failures were in the cases of the two notification items (`privacy.toast-suggested`, `privacy.toast-backup-reminder`; described in their documentation), not this item.

## Observed effect
Edge started again in every phase with the `--no-first-run` flag (a Chromium switch, not a policy; without it the welcome screen covered the page), the toolbar and the `edge://newtab` page (UI Automation, screenshots) and the `edge://policy` table, before the write, after the write and after undo. Edge 157.0.4341.42 in all phases.
- **Home** (`DSO-EN-Home`), 2026-11-07, 20:44–20:48.
  - Seven policies with `HubsSidebarEnabled`. Result: before the write the "Chat" and "Copilot menu" buttons; after the write neither "Chat" nor "Copilot menu"; after undo "Chat".
  - Six policies without `HubsSidebarEnabled` (the item's variant). Result: before the write "Chat"; after the write none; after undo "Chat".
  - In `edge://policy` each of the six policies: `false`, Platform, Device, Mandatory, status OK.
- **Pro** (`DSO-EN-Pro`), 2026-11-07, 20:44–20:48.
  - Seven policies with `HubsSidebarEnabled`. Result: before the write "Chat"; after the write neither "Chat" nor "Copilot menu"; after undo "Chat".
  - Six policies without `HubsSidebarEnabled`. Result: before the write "Chat"; after the write none; after undo "Chat" and "Copilot menu". `edge://policy` as on Home.
- The "Copilot menu" button appeared in Edge 157 also in phases without the policies, so "Chat" is what shows the effect. The policies were written together; which of them does it wasn't separated. According to the `HubsSidebarEnabled` page, `Microsoft365CopilotChatIconEnabled` controls the display of Copilot in the toolbar; the other five policies had no separate observation; Bing Chat elements on the new tab page weren't recognized either before or after the write.
- Not checked: an Edge profile signed in with a Microsoft account or Microsoft Entra, a notice about management by your organization next to the Copilot settings, Edge versions other than 157.0.4341.42.
