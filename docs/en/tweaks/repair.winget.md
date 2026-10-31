<!-- source: docs/tweaks/repair.winget.md sha256: d38c7c5bb57c03d9bde8471ae7382de5c4a31a90905af8b7ad00f5ccf81befb7 -->

# repair.winget – Repair winget (App Installer)

## Verdict for 26H2
Port as `kind=action`, `risk=safe` ("Repairs" category, Stage 4b, round 1): on a fresh installation `winget` is sometimes unavailable because the `winget.exe` alias is missing from the account profile. Registering the existing "App Installer" package for the account restores the alias without downloading anything and works on 26H2 Home and Pro. No undo.

## What it changes
Script (`script.apply`), only for the account that started DSOptimizer:
1. Refusal when the SID of the process ≠ the SID of the signed-in user: "The winget repair works only for the account DSOptimizer runs on: run DSOptimizer on account <name>." Nothing was changed.
2. Reading: the `Microsoft.DesktopAppInstaller` package in the `Installed` state for the account (`Get-AppxPackage -AllUsers`, the `PackageUserInformation` entry) and `winget --version` from the account's `%LOCALAPPDATA%\Microsoft\WindowsApps\winget.exe` alias.
3. Both conditions met (code 0 and a version number `v<digits>` in the output) → "winget works (version <v>), nothing was changed." without registration.
4. Otherwise `Add-AppxPackage -RegisterByFamilyName -MainPackage Microsoft.DesktopAppInstaller_8wekyb3d8bbwe` (registration of the package already present in the system; no download, no `-Path`, no modules from the internet) and a new reading by the same criterion.
5. Success (`Executed`) only after verification: "Registered "App Installer" again for account <SID>: winget works (version <v>, verified)."
6. When it still doesn't work after registration, or the package is missing: an error (`Failed`) "Registration didn't help: <state>. Update "App Installer" in Microsoft Store: ms-windows-store://pdp/?ProductId=9NBLGGH4NNS1." This address is the only one added to `ExternalLinkPolicy` (exact string; another `ProductId` or another scheme = refused).

We don't compare a minimum version: not measured (the 26H2 image has one version, and we don't download an older package).

## Sources
- Measurements on 26H2 Home and Pro VMs (build 26300.9457), 2026-10-22: `docs/zrodla/2026-10-22-etap4b-1-pomiary.md` (in Polish), section 6 and rows 6, 6i of the decision table.
- The Store ID of "App Installer", `9NBLGGH4NNS1`: the page `https://apps.microsoft.com/detail/9NBLGGH4NNS1` ("App Installer") and the Microsoft page "Use WinGet" give the same ID (checked 2026-10-22).
- No other MS documentation checked in this task; evidence: the VM test described below.

## Defaults on a fresh 26H2
Package `Microsoft.DesktopAppInstaller_1.26.509.0_x64__8wekyb3d8bbwe` (in the image `…_2025.926.104.0_neutral_~_…`) in the `Installed` state for the account, the `winget.exe` alias exists (ReparsePoint, length 0), `winget --version` = `v1.11.510`, code 0. On a fresh VM the action therefore ends with "winget działa …, nic nie zmieniono." (winget works …, nothing was changed.).

## Detection and undo
Not applicable: `kind=action` has no detection or undo. The evidence of the effect is the verification (`winget --version` code 0 with a version, and the package `Installed` for the account).

## Risks and warnings
- `risk=safe`: registering the existing package restores the alias; when winget already works, nothing changes.
- The action doesn't download or install anything; without the package in the system it ends with an error that points to the Store.
- It only confirms that `winget --version` runs; whether the sources work (`winget search`, `install`) needs the network and isn't verified (`winget source list` alone ends with code 0 also offline).
- Only the account DSOptimizer runs on; from another administrator account it refuses.

## VM test
Cases in `tests/vm/cases.psd1` (Task 8):
- `repair-winget-other-account` (Administrator via PS Direct, `MessageContains='uruchom DSOptimizer na koncie'`, `Check`: the package state for the accounts unchanged).
- `repair-winget-dso` (`RunAs='dso'`; `Setup` breaks the state by deleting the `winget.exe` alias in the `dso` profile, as in the measurement; `Check`: `winget --version` run as `dso` through a Task Scheduler task ends with code 0 and a version). The "Staged" state can't be prepared for App Installer (`Remove-AppxPackage` = 0x80073CFA, "part of Windows"), so the "breakage" is the alias alone.

VM run (Task 10, 2026-10-22, full `tests/vm/Invoke-VmTests.ps1` on the `etap4b-1` branch; reports outside git): Home and Pro 722 passed / 0 failed / 25 skipped each (6 cases with `Skip` outside the Repairs category). `repair-winget-other-account`: **Passed** on Home and Pro (`Failed`, "… uruchom DSOptimizer na koncie <PC>\dso." (… run DSOptimizer on account <PC>\dso.), the package state unchanged). `repair-winget-dso`: **Passed** on Home and Pro (`Executed`, "Zarejestrowano ponownie „Instalator aplikacji” … winget działa (wersja v1.11.510, odczyt zwrotny)." (Registered "App Installer" again … winget works (version v1.11.510, verified).), `Check` `winget --version` as `dso` = code 0).

## Observed effect
Method: in a process of the `dso` account (a Task Scheduler task, Interactive + Highest, like DSOptimizer after UAC), deleting the `winget.exe` alias, then `Add-AppxPackage -RegisterByFamilyName -MainPackage Microsoft.DesktopAppInstaller_8wekyb3d8bbwe` and `winget --version`; raw results outside git.
- **Home** (`DSO-26H2-Home`, 26300.9457), 2026-10-22: after the alias is deleted, `winget` = "Nie można odnaleźć określonego pliku" (The system cannot find the file specified), the package is still `Installed`; registration by family name (0.4–0.5 s) restores the alias, `winget --version` = `v1.11.510`, code 0. `Remove-AppxPackage` for the account: refused with 0x80073CFA.
- **Pro** (`DSO-26H2-Pro`, 26300.9457), 2026-10-22: identical (run A2: registration 0.53 s). With a network (N1): `winget source list` code 0, `winget search PowerToys` downloaded the source and returned a result, code 0 (the action doesn't check this).
