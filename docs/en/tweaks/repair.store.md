<!-- source: docs/tweaks/repair.store.md sha256: 38700e646acff1945555778b42e12ae7c8c5c07faceb98c909573e307fbf3eb5 -->

# repair.store – Repair Microsoft Store on this account

Formerly `tools.store-repair` ("Tools" category); since Stage 4b batch 1 the item is in the "Repairs" category as `repair.store`, and the old ID and the v3.2 alias `tool_msstore` work as aliases (`--execute tools.store-repair` still resolves to this action).

## Verdict for 26H2
Port as `kind=action`, `risk=caution` (v3.2 alias `tool_msstore`), but in a changed, honest form: the action registers again, from its manifest, the `Microsoft.WindowsStore` package that is already installed for the account running DSOptimizer (a mechanism from the MS documentation), and counts it as a success only after verification. It doesn't install Microsoft Store and doesn't promise "Restores Microsoft Store on LTSC" from v3.2 (`wsreset.exe -i`): that was unreliable (measured: exit code 0 on an error).

## What it changes
Changes the registration of the `Microsoft.WindowsStore` app package for one account (the one that started DSOptimizer). The package files in `C:\Program Files\WindowsApps\…` stay unchanged; nothing is downloaded. The script (`script.apply`) works only through three helpers defined in the script (the tests mock them):
1. `Get-DsoProcessSid` – the SID of the account of the current process (`WindowsIdentity.GetCurrent().User`).
2. `Get-DsoStorePackage` – `Get-AppxPackage -AllUsers -Name 'Microsoft.WindowsStore'` (the EXACT name, not the `*Store*` pattern, because that one also catches `Microsoft.StorePurchaseApp`), with the fields `Name`, `PackageFullName`, `Version`, `InstallLocation`, `Status` and `Users` (SID and `InstallState` from `PackageUserInformation`). The `-AllUsers` switch is needed, because without it `PackageUserInformation` is empty (measured).
3. `Register-DsoStorePackage` – `Add-AppxPackage -Path "<InstallLocation>\AppxManifest.xml" -Register -DisableDevelopmentMode` (example 3 from the MS documentation).

A fourth helper, `Get-DsoAccountName` (`SecurityIdentifier.Translate` → account name, an error = the SID alone; as in `repair.winget`), is used only for the refusal message (a).

Flow and results (no path gives a success without verification):
- (a) Process SID ≠ `$UserSid` (the SID of the signed-in user): `Failed` "The Microsoft Store repair works only for the account DSOptimizer runs on: run DSOptimizer on account <name>. Nothing was changed." (the same sentence as in `repair.winget`; the name from `Get-DsoAccountName`, and when the SID can't be translated – the SID alone) WITHOUT reading the packages and calling the registration. Measured reason: registration from the `Administrator` account adds the package to the Administrator, while the entry of the `dso` account stays unchanged, so the "repair" wouldn't touch the account that matters.
- (b) No `Microsoft.WindowsStore` package on the disk (it isn't in the `-AllUsers` list; e.g. LTSC): `Failed` "The Microsoft Store package isn't installed for this account; this action doesn't install it." (decision D4), without registration. Several packages with this name for the account (or several on the disk without an `Installed` entry for the account): `Failed` (it's unclear which one to register). A package on the disk without an `Installed` entry for the account (R33, below): registration and verification.
- (c) No install location or an unreadable `AppxManifest.xml`: `Failed` with the reason, without registration.
- (d) An `Add-AppxPackage` error: `Failed` with the error message.
- (e) After registration, verification: no package for the account or `Status` ≠ `Ok`: `Failed`.
- (f) Verification: the package for the account in the `Installed` state and `Status` = `Ok`: `Executed` with the version and the full package name.

### R33 (Stage 4b, batch 1): package on the disk, but not registered for the account
An extension of the mechanism to a state that used to end with a refusal. The script distinguishes two states (function `Select-DsoUserStore`):
- `Installed`: the package has an entry for the account in the `Installed` state – the previous behavior (registering again from the manifest). This entry wins even when other versions of the package are also on the disk.
- `OnDisk`: the `Microsoft.WindowsStore` package is on the disk (e.g. `Staged` for `S-1-5-18`, an entry of another account only, no entries), but the account has no `Installed` entry – registration with `Add-AppxPackage -Path <InstallLocation>\AppxManifest.xml -Register -DisableDevelopmentMode` and verification: `Installed` for the account's SID and `Status` = `Ok`. Several such packages (ambiguous), no install location or an unreadable manifest: `Failed` without registration.
- No package on the disk: the previous refusal ("isn't installed for this account; this action doesn't install it"), without downloading.
- The refusal when the process SID ≠ the SID of the signed-in user works as before, before any change.

**R33 verdict: it goes in** (VM measurement, 26H2 Home and Pro, 2026-10-22, 3 runs; details in "Observed effect").

The script NEVER calls `wsreset.exe` (including `wsreset -i`) or `Remove-AppxPackage` (the harness safeguards throw on an unmocked call).

## Sources
- MS: `Add-AppxPackage` (ms.date 2023-05-15), accessed 2026-10-19: https://learn.microsoft.com/en-us/powershell/module/appx/add-appxpackage – example 3 "Add a disabled app package in development mode" (`Get-AppxPackage … InstallLocation`, `Add-AppxPackage -Path $ManifestPath -Register -DisableDevelopmentMode`); `-DisableDevelopmentMode` registers "an existing app package installation that has been disabled, didn't register, or has become corrupted".
- MS: `Get-AppxPackage` (ms.date 2023-05-15), accessed 2026-10-19: https://learn.microsoft.com/en-us/powershell/module/appx/get-appxpackage – the `-AllUsers` parameter.
- MS: "What's new in Windows 11 Enterprise LTSC 2024" (ms.date 2026-07-15), accessed 2026-10-19: https://learn.microsoft.com/en-us/windows/whats-new/ltsc/whats-new-windows-11-2024 – "Support for LTSC by apps and tools, such as in-box apps and Microsoft Store, that are designed for the general availability channel release of Windows might be limited." (the only verbatim MS quote about the Store on LTSC; it says "support might be limited", not "there's no Store").
- VM measurement, 26H2 Home and Pro (build 26300.9457), 2026-10-22 (Stage 4b batch 1, research: `docs/zrodla/2026-10-22-etap4b-1-pomiary.md` (in Polish), section 5): the `Staged` state, registration from the manifest, verification (R33).
- Reading on 26H2 Home and Pro VMs (build 26300.9457), 2026-10-19 (C5 exploration, research: `docs/zrodla/2026-10-19-partia-c5-zrodla.md` (in Polish), questions 8–11), Ruling R19:
  - Package: `Name` = `Microsoft.WindowsStore`, version 22506.1400.2.0 (`Microsoft.WindowsStore_22506.1400.2.0_x64__8wekyb3d8bbwe`), `Status` = `Ok`, `InstallLocation` = `C:\Program Files\WindowsApps\Microsoft.WindowsStore_22506.1400.2.0_x64__8wekyb3d8bbwe`, `AppxManifest.xml` exists. The `*Store*` filter also catches `Microsoft.StorePurchaseApp` (22509.1400.2.0).
  - Account `Administrator` (PS Direct, K1): before registration 0 packages for the current account (an empty result without an exception); the registration finishes without an error in about 2 s and adds the Store to the Administrator account, while the entry of the `dso` account stays unchanged.
  - Account `dso` (elevated session, K2): the registration ends with `Ok` in about 2 s, `Status` after = `Ok`; no change is visible, because the package was already registered.
  - `PackageUserInformation` is empty in a reading without `-AllUsers`.
  - `wsreset.exe -i`: exit code 0 on error 0x80070520 (K1) and 0x80072ee7 (K2), so code 0 doesn't prove success; the action doesn't use it.
  - Not tested on a VM: no package on the disk (LTSC; covered by mocks in the tests) and the Store working after registration (VM without a network). `Remove-AppxPackage` as `dso` was measured only on 2026-10-22 (preparing the `Staged` state, "Observed effect" section).

## Defaults on a fresh 26H2
Package `Microsoft.WindowsStore` 22506.1400.2.0, `Status` = `Ok`, installed for the `dso` account (`Installed`) on 26H2 Home and Pro VMs (`Get-AppxPackage -AllUsers` reading from 2026-10-19). The `Administrator` account didn't have the Store until it was registered from its session.

## Detection and undo
Not applicable: `kind=action` has no detection or undo (no journal). Registering a package has no reverse operation, hence `risk=caution` and the "No undo" warning. The evidence of the effect is the verification in the script (the package for the account in the `Installed` state, `Status` = `Ok`); exit code 0 isn't possible without it.

## Risks and warnings
- `risk=caution`: changes the registration of a system package; a failed attempt may leave Microsoft Store unregistered for the account; no undo.
- The action doesn't install Microsoft Store. For LTSC, Microsoft writes that support for in-box apps and Microsoft Store "might be limited" (quote above); when the package is missing, the action ends with an error, without unofficial scripts that install the Store.
- Checking whether the Store starts and signs in after registration needs the network; the action confirms only the package state (`Status` = `Ok`), not that the Store works.
- Works only for the account that started DSOptimizer. With "run as a different administrator" (administrator account ≠ signed-in user) the action refuses and changes nothing (honestly: the registration would go to the wrong account).
- Store in use (Home and Pro VMs, Task 10, 2026-10-22): right after sign-in, `Add-AppxPackage -Register` returns `0x80073D02` ("aplikacje trzeba zamknąć: Microsoft.WindowsStore" (the apps must be closed: Microsoft.WindowsStore)), although no process from the Store package is visible; the same registration about 76–80 s after `explorer.exe` starts goes through. The action then ends with an error with the message "Microsoft Store is in use right now … close the Store, wait a minute and run the repair again" (the details of the original error are in the same message). The program doesn't close the Store itself (no `-ForceApplicationShutdown`).

## VM test
VM run `20261019-110405` (2026-10-19, HEAD `fd320ed`, build 26300.9457): Home (`DSO-26H2-Home`) and Pro (`DSO-26H2-Pro`), `tests/vm/Invoke-VmTests.ps1`. Reports are local, outside git. The whole run: Home 401 passed / 0 failed / 29 skipped, Pro 401 / 0 / 29 (`main` 313/0/29 + the Reboot runs); in the C5 cases `CleanupError` empty.
- `repair-store-other-account` (formerly `tools-store-repair-other-account`) (`Flow=Execute`, `Catalog=BuiltIn`): **Passed** on Home and Pro (`ExecuteExit=1`, `Failed`, the message then contained "Rejestracja działa tylko dla konta" (Registration works only for the account); since the fixes after the final review of batch 1, the case checks for "uruchom DSOptimizer na koncie" (run DSOptimizer on account)). The runner is an Administrator, while `$UserSid` is the `dso` account, so the action refuses without registration. `Setup` saves the `Get-AppxPackage -AllUsers -Name Microsoft.WindowsStore` reading (it throws when the package is missing or there's no line with `|S-1-5-21-`, which confirms the shape of `UserSecurityId.Sid`; it also saves a reading of the manifest, read-only); `Check` requires the `-AllUsers` reading after the action to be identical to the one before (no new Administrator entry); `Cleanup` deletes the helper files.
- `tools-store-repair-register-dso` (run 2026-10-19): **Skipped** (the runner couldn't do `RunAs`). Replaced by the active `repair-store-register-dso` (`RunAs='dso'`, Task 9): the `Installed` state unchanged, the action registers again and the verification stays `Installed` + `Status` = `Ok`.
- `repair-store-staged-dso` (`RunAs='dso'`, R33): `Setup` prepares the `Staged` state as in the measurement (`Remove-AppxPackage -Package <PackageFullName>` in a `dso` process through a Task Scheduler task, Interactive + Highest), `Check`: `InstallState` for the `dso` SID = `Installed`. The Store working after the repair needs a network, which the VM doesn't have.
- K2 exploration (R19, `dso` session): registration from the manifest ends with `Ok` in about 2 s, `Status` after = `Ok`; K1 registers the package for the Administrator, and the `dso` entry stays unchanged. Research and results of the K2 exploration: `docs/zrodla/2026-10-19-partia-c5-zrodla.md` (in Polish) (sections "Wyniki VM" (VM results), "Sonda uzupełniająca" (Supplementary probe)).
- **Not tested on a VM:** the positive path with `DSOptimizer.exe` in the GUI (a successful registration in the session of the account that started the app; the CLI as `dso` is covered by `repair-store-register-dso` and `repair-store-staged-dso`) and the Store working after the repair (no network). There's also no installation of the Store (including on LTSC) and no `wsreset -i`.
- **Smoke test with the real `DSOptimizer.exe` in the `dso` session: to be confirmed by Dawid** (Ruling R30). Not done, because Dawid was away; the item goes into `main` with this caveat (like D2 in batch C4).

VM run (Task 10, 2026-10-22, full `tests/vm/Invoke-VmTests.ps1` on the `etap4b-1` branch; reports outside git): Home and Pro 722 passed / 0 failed / 25 skipped each (6 cases with `Skip` outside the Repairs category). `repair-store-other-account`, `repair-store-register-dso` and `repair-store-staged-dso`: **Passed** on Home and Pro (refusal for another account; `Executed` "zarejestrowany ponownie" (registered again); R33 `Executed` "był na dysku bez wpisu Installed dla tego konta" (it was on the disk without an Installed entry for this account), `Check` = `Installed` + `Status Ok` for the `dso` SID). `repair-store-register-dso` failed in the first run on both VMs with `0x80073D02` (the Store "in use" right after sign-in, see "Risks and warnings"), so `Setup` waits until 120 s have passed since `explorer.exe` of the `dso` account started.

## Observed effect
Method (2026-10-22, 3 runs on `DSO-26H2-Home` and `DSO-26H2-Pro`, build 26300.9457; research: `docs/zrodla/2026-10-22-etap4b-1-pomiary.md` (in Polish), section 5): in a process of the `dso` account (a Task Scheduler task, Interactive + Highest, like DSOptimizer after UAC) `Remove-AppxPackage -Package <Store PackageFullName>` (0.8 s), a `Get-AppxPackage -AllUsers` reading, `Add-AppxPackage -Register <InstallLocation>\AppxManifest.xml -DisableDevelopmentMode`, a new reading.
- Before: `Get-AppxPackage -AllUsers` shows for `Microsoft.WindowsStore_22506.1400.2.0_x64__8wekyb3d8bbwe` only `S-1-5-18` = `Staged`, without an entry for the `dso` SID; `Get-AppxProvisionedPackage -Online` still has the Store (`Microsoft.WindowsStore_22506.1400.2.0_neutral_~_8wekyb3d8bbwe`).
- After registration: `InstallState` for the `dso` SID = `Installed`, `Status` = `Ok`; the "Microsoft Store" window opens (offline page; started with `explorer.exe shell:AppsFolder\Microsoft.WindowsStore_8wekyb3d8bbwe!App`).
- Without a network and without registration: starting the Store in the `Staged` state doesn't open a window (H1: no window within 71 s; P1: File Explorer opens "Dokumenty" (Documents)).
- Conclusion: R33 goes in. The repair has to run as the signed-in user (process SID = SID of the signed-in user), as before.

## Reddit
To be added in part E.
