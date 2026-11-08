<!-- source: docs/tweaks/privacy.explorer-sync-provider-notifications.md sha256: f40380b547b2828383bdc34ee4d47d1d6ea0c4548e327c04ca20ac2b241bba90 -->

# privacy.explorer-sync-provider-notifications – Turn off sync provider notifications

## Verdict for 26H2
Port: writing one value clears the "Show sync provider notifications" checkbox in File Explorer Options; a test on a 26H2 VM (Home and Pro) confirmed the state of the checkbox. The notifications themselves in File Explorer weren't observed.

## What it changes
- `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced` (in the engine `HKU\<SID of the signed-in user>`), value `ShowSyncProviderNotifications`, **DWord**, set to `0`; `scope=user`.
- The checkbox in File Explorer Options is cleared right away, even before File Explorer restarts.

## Sources
- Test on 26H2 Home and Pro VMs, 2026-11-07 (build 26300.9457, Windows not activated, local account): write through the registry API (.NET) and observation in File Explorer Options (window messages and MSAA, screenshots).
- Win11Debloat: https://github.com/Raphire/Win11Debloat (branch master), file `Regfiles/Disable_Windows_Suggestions.reg`, lines 28–30 (section "Sync provider ads"); accessed 2026-11-06.
- privacy.sexy: https://github.com/undergroundwires/privacy.sexy (branch master), file `src/application/collections/windows.yaml`, line 30489; accessed 2026-11-06.
- No Microsoft documentation for this value; evidence: VM test (observation below).

## Defaults on a fresh 26H2
Reading from the `DSO-EN-Home` and `DSO-EN-Pro` VMs, 2026-11-07: the `Explorer\Advanced` key exists, `ShowSyncProviderNotifications` **none** (checkbox selected).

## Detection and undo
- Detection: `ShowSyncProviderNotifications` = DWORD 0 -> `Applied`; missing value or a different value -> `NotApplied`.
- Undo restores the snapshot from before the change from the journal (a missing value = deleting it); `original` from the catalog (`<RemoveEntry>`) is only a fallback.
- The `Explorer\Advanced` key existed before apply and stays (`KeyPresentAfterUndo`).

## Risks and warnings
- `risk=safe`, `suggested=true`: changes one checkbox in File Explorer Options; no loss of function was observed.
- No warnings in the catalog.

## VM test
Case `privacy-explorer-sync-provider-notifications` (`Flow=Full`) in `tests/vm/cases.psd1`; VM run of 2026-11-08 (build 26300.9457, `Invoke-VmTests.ps1`): **Passed** on `DSO-EN-Home` (Core), `DSO-EN-Pro` and `DSO-PL-Pro` (Professional). Full runs: 915 passed, 2 failed, 25 skipped on both English VMs and 917 passed, 0 failed, 25 skipped on the Polish one. Both failures were in the cases of the two notification items (`privacy.toast-suggested`, `privacy.toast-backup-reminder`; described in their documentation), not this item.

## Observed effect
- **Home** (`DSO-EN-Home`), 2026-11-07. Method: File Explorer Options, View tab, state of the "Show sync provider notifications" checkbox ("Pokaż powiadomienia dostawcy synchronizacji" in Polish Windows) before the write, after the write, after File Explorer restarts and after undo. Result: before the write the checkbox was selected (no value); after the write it was cleared, and stayed cleared after File Explorer restarted; after undo it was selected again.
- **Pro** (`DSO-EN-Pro`), 2026-11-07. Same method and same result.
- Not checked: the sync provider notifications themselves in the File Explorer window. OneDrive wasn't used on the VM and no such messages appeared, so the evidence is the state of the checkbox in File Explorer Options.
