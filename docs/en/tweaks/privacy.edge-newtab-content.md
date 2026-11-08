<!-- source: docs/tweaks/privacy.edge-newtab-content.md sha256: 58560eaca897b56ada3d75d052ced9c39d2d90756ef12763bbc61a59fabce4d4 -->

# privacy.edge-newtab-content – Hide MSN content and default tiles on the Edge new tab page

## Verdict for 26H2
Port as a **non-suggested** item: two Edge policies, each with a separately observed effect; a test on a 26H2 VM (Home and Pro) showed that the MSN feed disappears from the new tab page (`NewTabPageContentEnabled`) and that of the default tiles only Microsoft 365 remains (`NewTabPageHideDefaultTopSites`). Not suggested, because Edge treats these settings as policies managed by your organization.

## What it changes
- `HKLM\SOFTWARE\Policies\Microsoft\Edge`, two **DWord** values: `NewTabPageContentEnabled` set to `0` and `NewTabPageHideDefaultTopSites` set to `1`; `scope=machine`.
- On a fresh 26H2 there's no `Edge` key (the `HKLM\SOFTWARE\Policies\Microsoft` key exists). Apply creates the key, undo deletes the values. The `Edge` key disappears on undo only when the change created it and it's empty; when it holds values of another Edge item, it stays (the program reports this with a warning).
- No Windows restart. In the test Edge was started again in every phase; whether an already open Edge window shows the change right away wasn't checked.

## Sources
- Test on 26H2 Home and Pro VMs, 2026-11-07 (build 26300.9457, Windows not activated, local account, with network): write through the registry API (.NET) and observation of the `edge://newtab` page (UI Automation, screenshots), plus reading the `edge://policy` table.
- Microsoft Edge policy pages (MS Learn, accessed 2026-11-07; both: path `SOFTWARE\Policies\Microsoft\Edge`, `REG_DWORD`, not marked as deprecated, not limited to domain-joined or MDM-managed computers):
  - policy index: https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies (ms.date 2026-11-04);
  - `NewTabPageContentEnabled`: https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/newtabpagecontentenabled (ms.date 2026-05-20): "If you disable this policy, Microsoft Edge doesn't display Microsoft content on the new tab page."; the policy covers "all profile types, namely unsigned local user profiles, profiles signed in using a Microsoft Account";
  - `NewTabPageHideDefaultTopSites`: https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/newtabpagehidedefaulttopsites (ms.date 2026-05-20): "If you set this policy to true, the default top site tiles are hidden.".
- Win11Debloat: https://github.com/Raphire/Win11Debloat (branch master), files `Config/Features.json` lines 540–544 and `Regfiles/Disable_Edge_Ads_And_Suggestions.reg` lines 2–16; accessed 2026-11-06.

## Defaults on a fresh 26H2
Reading from the `DSO-EN-Home` and `DSO-EN-Pro` VMs, 2026-11-07: the `HKLM\SOFTWARE\Policies\Microsoft` key exists, the `HKLM\SOFTWARE\Policies\Microsoft\Edge` key and both values: **none** (the new tab page shows the MSN feed and six default tiles).

## Detection and undo
- Detection: all values from "What it changes" equal to the written ones -> `Applied`; missing value or a different value -> `NotApplied`.
- Undo restores the snapshot from before the change from the journal (a missing value = deleting it) and deletes the `Edge` key if apply created it and it's empty (`KeyAbsentAfterUndo`); when it holds values of another Edge item, the key stays and the program reports this with a warning. `original` from the catalog (`<RemoveEntry>`) is only a fallback.
- The `HKLM\SOFTWARE\Policies\Microsoft` key existed before apply and stays (`KeyPresentAfterUndo`).
- The `Edge` key is shared with other Edge items (for example `privacy.edge-shopping-recommendations`); when it existed before apply with another item's values, undo keeps the key and those values. A separate VM case, `privacy-edge-shared-key`, guards this.

## Risks and warnings
- `risk=safe`, `suggested=false`: MSN content and the default tiles disappear; no loss of a function was observed.
- Warning in the catalog: "Edge will treat these new tab settings as managed by your organization until you undo this item."
- The warning is based on the `edge://policy` table: both policies as Platform, Device, Mandatory, status OK. A notice about management by your organization next to the new tab settings wasn't read (the wording of the new tab settings wasn't read in English or in Polish); with other Edge policies such a notice appears next to the locked setting (`privacy.edge-shopping-recommendations`).
- Microsoft 365 stays among the tiles. Tiles added by the user weren't checked.

## VM test
Case `privacy-edge-newtab-content` (`Flow=Full`) in `tests/vm/cases.psd1`; VM run of 2026-11-08 (build 26300.9457, `Invoke-VmTests.ps1`): both cases **Passed** on `DSO-EN-Home` (Core), `DSO-EN-Pro` and `DSO-PL-Pro` (Professional). Full runs: 915 passed, 2 failed, 25 skipped on both English VMs and 917 passed, 0 failed, 25 skipped on the Polish one. Both failures were in the cases of the two notification items (`privacy.toast-suggested`, `privacy.toast-backup-reminder`; described in their documentation), not this item. Case `privacy-edge-shared-key` (`Flow=Full`) checks that undoing this item keeps the `Edge` key and the `EdgeShoppingAssistantEnabled` value written earlier (`KeyPresentAfterUndo`, `SnapshotDiff` empty).

## Observed effect
Edge started again in every phase with the `--no-first-run` flag (a Chromium switch, not a policy; without it the welcome screen covered the page), the `edge://newtab` page (UI Automation, screenshots) and the `edge://policy` table, before the write, after the write and after undo. The MSN content and the tile names were in Polish on both VMs (location by IP address).
- **Home** (`DSO-EN-Home`), 2026-11-07.
  - `NewTabPageContentEnabled` (20:35–20:37; Edge 152.0.4191.66 before and after the write, 157.0.4341.42 after undo). Result: before the write the MSN feed (among others "Odkrywaj", "Ustawienia kanału informacyjnego", articles, product ads); after the write there's no feed (UI Automation elements 219 → 72); after undo the feed is back. In `edge://policy`: `false`, Platform, Device, Mandatory, status OK.
  - `NewTabPageHideDefaultTopSites` (20:37–20:39; Edge 157.0.4341.42). Result: before the write the tiles Microsoft 365, Booking.com, Allegro, Rtv Euro Agd, Mediamarkt, LinkedIn; after the write only Microsoft 365. In `edge://policy`: `true`, Platform, Device, Mandatory, status OK. The after-undo phase on Home wasn't read: a OneDrive sign-in window and a notification covered Edge, and UI Automation returned neither the tiles nor the policy table.
- **Pro** (`DSO-EN-Pro`), 2026-11-07.
  - `NewTabPageContentEnabled` (20:35–20:37; Edge 152.0.4191.66 before the write, 157.0.4341.42 after the write and after undo). Same result as on Home (UI Automation elements 218 → 71); `edge://policy` as on Home.
  - `NewTabPageHideDefaultTopSites` (repeat 21:12–21:14; Edge 152.0.4191.66). Result: after the write only Microsoft 365; after undo Microsoft 365, Booking.com, Allegro, Rtv Euro Agd, Mediamarkt, LinkedIn; `edge://policy` as on Home. The first attempt on Pro (20:37–20:39) showed six tiles before the write and one after it, but a Settings window covered `edge://policy` then, hence the repeat.
- Not checked: the wording of the new tab settings in Edge and whether a notice about management by your organization appears next to them.
