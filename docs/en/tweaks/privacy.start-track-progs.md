<!-- source: docs/tweaks/privacy.start-track-progs.md sha256: d433f08e8f10681ddd1c1c3cc01210d179c6eff531dcfc431d93e993066ec818 -->

# privacy.start-track-progs – Turn off improving Start and search results

## Verdict for 26H2
Port: the "Improve Start and search results" toggle (the `ms-settings:privacy-general` page) is changed by writing one value; a test on a 26H2 VM (Home and Pro) confirmed the toggle state, and the "Show most used apps" setting (Personalization > Start) becomes inactive. Evidence: toggle state in Settings; the content itself wasn't observed.

## What it changes
- `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced` (in the engine `HKU\<SID of the signed-in user>`), value `Start_TrackProgs`, **DWord**, set to `0`; `scope=user`.
- Mechanism of the `SystemSettings_Privacy_StoreAppUsage_ToggleSwitch` toggle from a registry diff on the VM: turning it off writes `Start_TrackProgs` = 0, turning it on writes 1. On a clean VM the value doesn't exist and the toggle is on.
- The toggle in a newly opened Settings window is off right away, without a restart.
- Side effect: after the write the "Show most used apps" setting on Personalization > Start is inactive, so the list of most used apps can't be turned on.

## Sources
- Test on 26H2 Home and Pro VMs, 2026-11-07 (build 26300.9457, Windows not activated, local account, no network): write through the registry API (.NET), a registry diff after switching the toggle both ways and observation in Settings (UI Automation, screenshots).
- Win11Debloat: https://github.com/Raphire/Win11Debloat (branch master), file `Regfiles/Disable_Telemetry.reg`, lines 25–27; accessed 2026-11-06.
- WinUtil: https://github.com/ChrisTitusTech/winutil (branch main), file `config/tweaks.json`, line 543; accessed 2026-11-06.
- privacy.sexy: https://github.com/undergroundwires/privacy.sexy (branch master), file `src/application/collections/windows.yaml`, line 7792; accessed 2026-11-06.
- No Microsoft documentation for this value; evidence: VM test (observation below).

## Defaults on a fresh 26H2
Reading from the `DSO-EN-Home` and `DSO-EN-Pro` VMs, 2026-11-07: the `Explorer\Advanced` key exists, `Start_TrackProgs` **none** (toggle on; "Show most used apps" off but active).

## Detection and undo
- Detection: `Start_TrackProgs` = DWORD 0 -> `Applied`; missing value or a different value (including 1 written by the toggle) -> `NotApplied`.
- Undo restores the snapshot from before the change from the journal (a missing value = deleting it, 1 = toggle on); `original` from the catalog (`<RemoveEntry>`) is only a fallback.
- The `Explorer\Advanced` key existed before apply and stays (`KeyPresentAfterUndo`).

## Risks and warnings
- `risk=caution`, `suggested=false`: the item isn't suggested, because it turns off the "Show most used apps" feature; anyone who wants to turn off improving the results selects it themselves. After the write the "Show most used apps" setting is inactive (UI Automation reading `Enabled` = false on Home and Pro), so the list of most used apps in Start is no longer available; after undo the setting is active again.
- Warning in the catalog: "The "Show most used apps" setting in the Start menu settings will be inactive – you won't be able to turn that list on."

## VM test
Case `privacy-start-track-progs` (`Flow=Full`) in `tests/vm/cases.psd1`; VM run of 2026-11-08 (build 26300.9457, `Invoke-VmTests.ps1`): **Passed** on `DSO-EN-Home` (Core), `DSO-EN-Pro` and `DSO-PL-Pro` (Professional). Full runs: 915 passed, 2 failed, 25 skipped on both English VMs and 917 passed, 0 failed, 25 skipped on the Polish one. Both failures were in the cases of the two notification items (`privacy.toast-suggested`, `privacy.toast-backup-reminder`; described in their documentation), not this item.

## Observed effect
- **Home** (`DSO-EN-Home`), 2026-11-07, 20:24–20:26. Method: the `ms-settings:privacy-general` and `ms-settings:personalization-start` pages in a newly opened Settings window (UI Automation, screenshots), the "Improve Start and search results" toggle ("Popraw wyniki uruchamiania i wyszukiwania" in Polish Windows) and the "Show most used apps" setting ("Pokaż najczęściej używane aplikacje") before the write, after the write and after undo. Result: before the write the toggle on (no value), "Show most used apps" off and active; after the write the toggle off, "Show most used apps" off and inactive; after undo the toggle on, "Show most used apps" off and active. Evidence: toggle state in Settings; the content itself wasn't observed.
- **Pro** (`DSO-EN-Pro`), 2026-11-07, 20:24–20:26. Same method and same result.
- Undo in the test restored the value 1, because the toggle had been switched both ways once before the item's write (reading the mechanism); the state "no value = toggle on" is visible in the reading from before the switching.
- Not checked: the results in the Start menu and in search themselves, or the list of most used apps with the setting turned on.
