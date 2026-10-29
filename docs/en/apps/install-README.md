<!-- source: docs/apps/install-README.md sha256: 8652e6ffa8bff4162dc6f11ec7ca9231a12e032183acbd0071830bc88fa60499 -->

# App install – list `apps.json`

The file `src/DSOptimizer.Catalog/catalog/apps.json` is the app list of the "Install" page (Stage 4c). It's loaded by
`AppsInstallCatalog.Load` (Core). It's embedded as a separate resource `BuiltInCatalog.AppsInstallSource`
(`DSOptimizer.AppsInstall.apps.json`) and, like `apps-bloat.json`, isn't part of `BuiltInCatalog.Sources` (the tweak catalog).
The program itself doesn't download anything. winget installs, only from the `winget` source (`--source winget`), with verification of the installer hash
from the manifest. Exception: one item downloaded by the program: RustDesk from GitHub, rules in `RustDeskSource`
(one repository, one signing publisher, rules in the code,
not in this file; evidence in the section "RustDesk from GitHub (spike 2026-10-24)").

## Format

```json
{
  "entries": [
    {
      "id": "install.7zip",
      "wingetId": "7zip.7zip",
      "title": "7-Zip",
      "summary": "Program do pakowania i rozpakowywania archiwów (7z, zip, rar i inne).",
      "category": "tools",
      "match": { "displayName": "7-Zip*", "publisher": "Igor Pavlov" },
      "warning": "optional",
      "source": "https://github.com/microsoft/winget-pkgs/tree/master/manifests/7/7zip/7zip"
    }
  ]
}
```

- `id`: stable identifier in the program, pattern `^install\.[a-z0-9-]+$`, unique case-insensitively.
- `wingetId`: exact package identifier in the `winget` source, always installed with `--exact`. Pattern
  `WingetArguments.IsValidWingetId` (`^[A-Za-z0-9][A-Za-z0-9.+_-]{0,127}$`), unique case-insensitively.
- `github`: the GitHub repository from whose releases the program downloads the installer itself – instead of `wingetId` (an entry has exactly one
  of them). Allowed only for items from the list in the code (`GitHubSources`): today only `install.rustdesk` → `rustdesk/rustdesk`
  (exact comparison); any other pair = file error. The download address, file, hash and signature are checked by `RustDeskSource`, not by the JSON.
- `title`: name in the program, at most 60 characters.
- `summary`: one sentence in Polish (what it is), at most 160 characters.
- `category`: one of the values `browsers` (Browsers), `messengers` (Messaging), `multimedia` (Multimedia),
  `documents` (Documents), `tools` (Tools), `remote-help` (Remote support), `games` (Games), `runtimes`
  (Runtimes). Order = order in the UI (`InstallCategory`), display names from `InstallText.CategoryName`.
  Entries in the file follow the same category order.
- `match`: the "already installed?" rule. The format and the matching code are the same as in `apps-bloat.json`: patterns with `*`, no regex,
  case-insensitive. A rule has `packageName` (AppX only) **or** `displayName` and/or `publisher` (Win32
  only; when both are given, both must match). `packageName` together with `displayName` = error. One rule per entry. Matching
  works on the **raw** reading of the installed list (before the "Apps" page classification), so entries with `SystemComponent`
  and `ParentKeyName` count too. The rule comes from a real Uninstall entry after installation on a VM (table below). When there are
  several entries (package + MSI components), we take the one visible to the user.
- `warning`: optional warning next to the item and in the batch confirmation.
- `source`: absolute `https` address (manufacturer's page or the manifest in `microsoft/winget-pkgs`; a `github` item – the releases page
  of the repository, `https://github.com/rustdesk/rustdesk/releases`).

There's no selection field and no presets (Dawid's decision 2026-10-15: nothing selects itself). The test
`AppsInstallCatalogTests.BuiltIn_NoEntryPreselected` makes sure the built-in file has no other fields than the ones above.

An error in the file = `InstallCatalogException(entryId, field, message)` with the message `apps: <id>, field <field>: <text>`. The same
exception is thrown for a duplicate `id` or `wingetId` and an unknown category.

## `match` rules (from the measurement)

- No version in the rule. Opera changes the version in `DisplayName` by itself a few minutes after installation (`Opera Stable 137.0.6036.39` →
  `.63`), and 7-Zip, LibreOffice, K-Lite, foobar2000, Everything, CPU-Z, HWMonitor, Signal, VC++ and .NET also have version numbers.
- No assumptions about the installer type. After the App Installer update (v1.11.510 → v1.29.380 during the batch on Pro), winget chose
  MSI instead of EXE for VLC and Notepad++. VLC has the same entry in both variants (`VLC media player` / `VideoLAN`). Notepad++ has
  a different name and publisher: EXE (Home, v1.11) `Notepad++ (64-bit x64)` / `Notepad++ Team`, MSI (Pro, v1.29) `Notepad++ (x64)` /
  `Notepad++ team (MSI installer)`. The format allows one rule per entry, so the rule covers both variants with patterns:
  `displayName: "Notepad++*"`, `publisher: "Notepad++*"` (no format change).
- No publisher when it's translated. Brave writes `Publisher` = `Autorzy Brave` (Brave Authors) on a Polish system, so the rule has only
  `displayName: "Brave"` (exact).
- Language in the name. Firefox writes `Mozilla Firefox (x64 pl)` (measurement with `--locale pl-PL`) or `Mozilla Firefox (x64 en-US)`
  (spike, default installer). The rule `Mozilla Firefox (*` covers both and doesn't catch `Mozilla Maintenance Service`.
- Architecture in the name. The runtimes have `(x64)`/`(x86)` in the rule, so an x86 runtime doesn't mark the x64 item
  as installed (`Microsoft Windows Desktop Runtime - 8.* (x64)`, `Microsoft Visual C++ *Redistributable (x64) - 14.*`).
  .NET 8 and 9 leave the same visible entry in HKLM32 and a `SystemComponent=1` entry in HKLM 64; both match the rule.
- The branch (HKLM 64/32, HKU) isn't part of the rule. The list reader from 4a reads all three.
- The rules are Win32 only. AppX packages installed alongside (NotepadPlusPlus, WinRAR.ShellExtension, AdobeAcrobatReaderCoreApp,
  PowerToys packages) don't give a status.
- No rule matches the entries of a fresh 26H2 (Microsoft Edge, Edge Update, Edge WebView2 Runtime) or the components
  installed alongside (Mozilla Maintenance Service, Microsoft GameInput, `Microsoft Visual C++ 2022 X64 Minimum Runtime`,
  `Microsoft .NET Runtime`). The Foxit PDF Reader rule (`Foxit PDF Reader` / `Foxit Software Inc.`, exact) doesn't catch the paid
  editor of the same publisher (`Foxit PDF Editor`, winget `Foxit.PhantomPDF`; negative test on a synthetic name). The test `AppsInstallCatalogTests.BuiltIn_MatchFromMeasuredUninstallEntry` checks this on entries
  from the measurement.

## List – entries with evidence (35)

Evidence: VM measurement 2026-10-24 (`docs/zrodla/2026-10-24-etap4c-pomiary.md` (in Polish), raw results outside git). `show` = 0 on Home and Pro for every item (installer type in brackets
from `show`). "Pro"/"Home" = code and time of the attempt without `--locale` (the program's command, Ruling R14); `0x8A150061` = already installed
in the `network` phase or as a dependency. "Scope" = branch of the measured Uninstall entry (`winget show` v1.11 doesn't print the installer
scope): HKU = current account only, HKLM/HKLM32 = whole computer. "Entry" = visible Uninstall entry (`DisplayName` /
`Publisher`) after installation. `source` = manifest folder in `microsoft/winget-pkgs` (existence checked through the GitHub API
on 2026-10-24 for every item). Foxit PDF Reader was added later the same day (Dawid's request): winget measurement (Pro only) and CLI run, raw results outside git.
RustDesk (`install.rustdesk`) has no winget package: the `wingetId` column holds the `github` repository, `show` doesn't apply, and the evidence
is the Home VM spike (section "RustDesk from GitHub (spike 2026-10-24)"); the Pro, Home and "CLI run" columns come from the CLI run
with network (RustDesk plan, Task 7 – paragraph "RustDesk: CLI run with network" below; `msiexec` time from the MSI log `/l*v`).

| `id` | `wingetId` | category | `show` | scope | Pro | Home | CLI run | entry | `match` | verdict | `source` |
|---|---|---|---|---|---|---|---|---|---|---|---|
| `install.chrome` | `Google.Chrome` | browsers | 0 (wix) | computer (HKLM) | 0 (96 s) | 0 (87 s) | Pro: `Installed` (86 s) | `Google Chrome` / `Google LLC` | `displayName: "Google Chrome"`, `publisher: "Google LLC"` | in | https://github.com/microsoft/winget-pkgs/tree/master/manifests/g/Google/Chrome |
| `install.firefox` | `Mozilla.Firefox` | browsers | 0 (nullsoft) | computer (HKLM) | 0 (26 s, `network` phase, with `--locale` → 129.0.2) | 0 (23 s, as above) | Pro: `Installed` (24 s); Home: `Installed` (24 s) | `Mozilla Firefox (x64 pl)` / `Mozilla`; spike without `--locale`: `Mozilla Firefox (x64 en-US)` 157.0.1 | `displayName: "Mozilla Firefox (*"`, `publisher: "Mozilla"` | in (also installs `Mozilla Maintenance Service`) | https://github.com/microsoft/winget-pkgs/tree/master/manifests/m/Mozilla/Firefox |
| `install.brave` | `Brave.Brave` | browsers | 0 (exe) | account (HKU) | 0 (68 s) | – | Pro: `Installed` (44 s) | `Brave` / `Autorzy Brave` | `displayName: "Brave"` | in | https://github.com/microsoft/winget-pkgs/tree/master/manifests/b/Brave/Brave |
| `install.opera` | `Opera.Opera` | browsers | 0 (exe) | account (HKU) | 0 (35 s) | – | Pro: `Installed` (46 s) | `Opera Stable 137.0.6036.39` / `Opera Software` (after a few minutes `.63`) | `displayName: "Opera Stable*"`, `publisher: "Opera Software"` | in | https://github.com/microsoft/winget-pkgs/tree/master/manifests/o/Opera/Opera |
| `install.discord` | `Discord.Discord` | messengers | 0 (exe) | account (HKU) | 0 (11 s) | 0 (14 s) | Pro: `Installed` (33 s); Home: `Installed` (25 s) | `Discord` / `Discord Inc.` (spike); in the reading right after code 0 the entry isn't there yet, `list --id --exact` a few seconds later = 0 with the name `Discord` (Home and Pro) | `displayName: "Discord"`, `publisher: "Discord Inc."` | in (entry with a delay, verification up to 60 s – R16) | https://github.com/microsoft/winget-pkgs/tree/master/manifests/d/Discord/Discord |
| `install.telegram` | `Telegram.TelegramDesktop` | messengers | 0 (inno) | account (HKU) | 0 (19 s) | – | Pro: `Installed` (17 s) | `Telegram Desktop` / `Telegram FZ-LLC` | `displayName: "Telegram Desktop*"`, `publisher: "Telegram FZ-LLC"` | in | https://github.com/microsoft/winget-pkgs/tree/master/manifests/t/Telegram/TelegramDesktop |
| `install.signal` | `OpenWhisperSystems.Signal` | messengers | 0 (nullsoft) | account (HKU) | 0 (42 s) | – | Pro: `Installed` (36 s) | `Signal 8.32.0` / `Signal Messenger, LLC` | `displayName: "Signal*"`, `publisher: "Signal Messenger, LLC"` | in | https://github.com/microsoft/winget-pkgs/tree/master/manifests/o/OpenWhisperSystems/Signal |
| `install.zoom` | `Zoom.Zoom` | messengers | 0 (msi) | computer (HKLM) | 0 (21 s) | – | Pro: `Installed` (30 s) | `Zoom Workplace (64-bit)` / `Zoom` | `displayName: "Zoom Workplace*"`, `publisher: "Zoom"` | in | https://github.com/microsoft/winget-pkgs/tree/master/manifests/z/Zoom/Zoom |
| `install.vlc` | `VideoLAN.VLC` | multimedia | 0 (nullsoft) | computer (HKLM) | 0 (92 s, v1.29: MSI) | 0 (230 s, EXE) | Pro: `Installed` (25 s); Home: `Installed` (34 s) | `VLC media player` / `VideoLAN` (EXE and MSI) | `displayName: "VLC media player"`, `publisher: "VideoLAN"` | in | https://github.com/microsoft/winget-pkgs/tree/master/manifests/v/VideoLAN/VLC |
| `install.k-lite` | `CodecGuide.K-LiteCodecPack.Standard` | multimedia | 0 (inno) | computer (HKLM32) | 0 (103 s) | – | Pro: `Installed` (27 s) | `K-Lite Codec Pack 20.1.0 Standard` / `KLCP` | `displayName: "K-Lite Codec Pack * Standard"`, `publisher: "KLCP"` | in | https://github.com/microsoft/winget-pkgs/tree/master/manifests/c/CodecGuide/K-LiteCodecPack/Standard |
| `install.foobar2000` | `PeterPawlowski.foobar2000` | multimedia | 0 (nullsoft) | computer (HKLM) | 0 (19 s) | – | Pro: `Installed` (7 s) | `foobar2000 v2.26.1 (x64)` / `Peter Pawlowski` | `displayName: "foobar2000*"`, `publisher: "Peter Pawlowski"` | in | https://github.com/microsoft/winget-pkgs/tree/master/manifests/p/PeterPawlowski/foobar2000 |
| `install.foxit-reader` | `Foxit.FoxitReader` | documents | 0 (exe – multilingual installer `…_L10N_Setup_x64.exe`, MSI inside) | computer (HKLM) | 0 (87 s) | – | Pro: `Installed` (80 s) | `Foxit PDF Reader` / `Foxit Software Inc.` 2026.2.1.39815 (`MsiExec.exe /I{0E43C5B8-B377-11F1-8E85-BB2B83F9BAD7}`) | `displayName: "Foxit PDF Reader"`, `publisher: "Foxit Software Inc."` (doesn't catch `Foxit PDF Editor`) | in (installs the `FoxitReaderUpdateService` service, automatic start) | https://github.com/microsoft/winget-pkgs/tree/master/manifests/f/Foxit/FoxitReader |
| `install.libreoffice` | `TheDocumentFoundation.LibreOffice` | documents | 0 (msi) | computer (HKLM) | 0 (118 s, `network` phase) | 0 (104 s, `network` phase) | Pro: `Installed` (73 s) | `LibreOffice 26.8.1.1` / `The Document Foundation` | `displayName: "LibreOffice*"`, `publisher: "The Document Foundation"` | in (also installs VC++ x64 as a winget dependency) | https://github.com/microsoft/winget-pkgs/tree/master/manifests/t/TheDocumentFoundation/LibreOffice |
| `install.acrobat-reader` | `Adobe.Acrobat.Reader.64-bit` | documents | 0 (exe) | computer (HKLM) | 0 (113 s) | – | Pro: `Installed` (168 s) | `Adobe Acrobat (64-bit)` / `Adobe` | `displayName: "Adobe Acrobat*"`, `publisher: "Adobe"` | in (adds the AppX `AdobeAcrobatReaderCoreApp` for the account and to the image) | https://github.com/microsoft/winget-pkgs/tree/master/manifests/a/Adobe/Acrobat/Reader/64-bit |
| `install.sumatrapdf` | `SumatraPDF.SumatraPDF` | documents | 0 (exe) | account (HKU) | 0 (7 s) | 0 (34 s) | Pro: `Installed` (9 s); Home: `Installed` (8 s) | `SumatraPDF` / `Krzysztof Kowalczyk` | `displayName: "SumatraPDF"`, `publisher: "Krzysztof Kowalczyk"` | in | https://github.com/microsoft/winget-pkgs/tree/master/manifests/s/SumatraPDF/SumatraPDF |
| `install.7zip` | `7zip.7zip` | tools | 0 (exe) | computer (HKLM) | 0 (7 s, `network` phase) | 0 (6 s, `network` phase) | Pro: `Installed` (7 s); Home: `Installed` (7 s) | `7-Zip 26.05 (x64)` / `Igor Pavlov` | `displayName: "7-Zip*"`, `publisher: "Igor Pavlov"` | in | https://github.com/microsoft/winget-pkgs/tree/master/manifests/7/7zip/7zip |
| `install.notepadplusplus` | `Notepad++.Notepad++` | tools | 0 (nullsoft) | computer (HKLM) | 0 (8 s, v1.29: MSI) | 0 (26 s, EXE) | Pro: `Installed` (24 s) | Pro MSI: `Notepad++ (x64)` / `Notepad++ team (MSI installer)`; Home EXE: `Notepad++ (64-bit x64)` / `Notepad++ Team` (+ AppX `NotepadPlusPlus`) | `displayName: "Notepad++*"`, `publisher: "Notepad++*"` | in (one rule for both variants) | https://github.com/microsoft/winget-pkgs/tree/master/manifests/n/Notepad++/Notepad++ |
| `install.everything` | `voidtools.Everything` | tools | 0 (wix) | computer (HKLM) | 0 (7 s) | – | Pro: `Installed` (14 s) | `Everything 1.4.1.1032 (x64)` / `voidtools` | `displayName: "Everything*"`, `publisher: "voidtools"` | in | https://github.com/microsoft/winget-pkgs/tree/master/manifests/v/voidtools/Everything |
| `install.powertoys` | `Microsoft.PowerToys` | tools | 0 (burn) | account (HKU) | 0 (57 s) | – | Pro: `Installed` (43 s) | `PowerToys (Preview) x64` / `Microsoft Corporation` (+ HKLM `PowerToys (Preview)` with `SystemComponent=1`, 5 AppX packages) | `displayName: "PowerToys*"`, `publisher: "Microsoft Corporation"` | in | https://github.com/microsoft/winget-pkgs/tree/master/manifests/m/Microsoft/PowerToys |
| `install.winrar` | `RARLab.WinRAR` | tools | 0 (exe) | computer (HKLM) | 0 (5 s, `pl` installer with `--locale`) | – | Pro: `Installed` (12 s) | `WinRAR 7.23 (64-bitowy)` / `win.rar GmbH` (+ AppX `WinRAR.ShellExtension`) | `displayName: "WinRAR*"`, `publisher: "win.rar GmbH"` | in (paid after the trial period) | https://github.com/microsoft/winget-pkgs/tree/master/manifests/r/RARLab/WinRAR |
| `install.sharex` | `ShareX.ShareX` | tools | 0 (inno) | computer (HKLM) | 0 (26 s) | – | Pro: `Installed` (41 s) | `ShareX` / `ShareX Team` | `displayName: "ShareX"`, `publisher: "ShareX Team"` | in | https://github.com/microsoft/winget-pkgs/tree/master/manifests/s/ShareX/ShareX |
| `install.cpu-z` | `CPUID.CPU-Z` | tools | 0 (inno) | computer (HKLM) | 0 (5 s) | – | Pro: `Installed` (7 s) | `CPUID CPU-Z 3.01` / `CPUID, Inc.` | `displayName: "CPUID CPU-Z*"`, `publisher: "CPUID, Inc."` | in | https://github.com/microsoft/winget-pkgs/tree/master/manifests/c/CPUID/CPU-Z |
| `install.hwmonitor` | `CPUID.HWMonitor` | tools | 0 (inno) | computer (HKLM) | 0 (5 s) | – | Pro: `Failed` `0x80004004` (winget "Cancelling operation", program installed – `--list-install` = `true`; after R21 = `NotConfirmed`); Home (retry): `Installed` (8 s) | `CPUID HWMonitor 1.68` / `CPUID, Inc.` | `displayName: "CPUID HWMonitor*"`, `publisher: "CPUID, Inc."` | in | https://github.com/microsoft/winget-pkgs/tree/master/manifests/c/CPUID/HWMonitor |
| `install.gpu-z` | `TechPowerUp.GPU-Z` | tools | 0 (exe) | computer (HKLM32) | 0 (23 s) | – | Pro: `Installed` (19 s) | `TechPowerUp GPU-Z` / `TechPowerUp` | `displayName: "TechPowerUp GPU-Z"`, `publisher: "TechPowerUp"` | in | https://github.com/microsoft/winget-pkgs/tree/master/manifests/t/TechPowerUp/GPU-Z |
| `install.hwinfo` | `REALiX.HWiNFO` | tools | 0 (inno – entry `…_is1`; license "Proprietary", free version) | computer (HKLM) | 0 (10 s; the installer starts HWiNFO64 by itself after installation) | – | Pro: CLI run – to be added | `HWiNFO® 64` (character U+00AE) / `Martin Malik, REALiX s.r.o.` | `displayName: "HWiNFO* 64"`, `publisher: "Martin Malik, REALiX s.r.o."` | in (added 2026-10-26 at Dawid's request) | https://github.com/microsoft/winget-pkgs/tree/master/manifests/r/REALiX/HWiNFO |
| `install.rustdesk` | `github`: `rustdesk/rustdesk` | remote-help | – (not in winget; GitHub API `releases/latest`: `1.5.0`, MSI) | computer (HKLM) | 0 (`msiexec` 3.7 s, CLI run) | 0 (`msiexec` 3.5 s, CLI run; spike 7.5 s) | Pro (with 7-Zip): `Installed` (7.6 s), second time `AlreadyInstalled`; Home: `Installed` (7.2 s), second time `AlreadyInstalled` | `RustDesk` / `Purslane Tech Pte. Ltd.` 1.5.0.29846474 (HKLM 64, MSI `{9D5F8A39-0D7B-4BC4-851D-DA8501D03DE7}`, `MsiExec.exe /X{…}`) | `displayName: "RustDesk"`, `publisher: "Purslane Tech*"` (doesn't catch `RustDesk Client`) | in (Home spike: `msiexec` code 0 in 7.5 s; installs the `RustDesk` service, 2 firewall rules, desktop/Start/Startup shortcuts for all accounts) | https://github.com/rustdesk/rustdesk/releases |
| `install.anydesk` | `AnyDesk.AnyDesk` | remote-help | 0 (exe) | computer (HKLM32) | 0 (12 s) | 0 (65 s) | Pro: `Installed` (13 s) | `AnyDesk` / `AnyDesk Software GmbH` | `displayName: "AnyDesk"`, `publisher: "AnyDesk Software GmbH"` | in | https://github.com/microsoft/winget-pkgs/tree/master/manifests/a/AnyDesk/AnyDesk |
| `install.teamviewer` | `TeamViewer.TeamViewer` | remote-help | 0 (wix in zip) | computer (HKLM) | 0 (21 s) | – | Pro: `Installed` (23 s); Home: `Installed` (18 s) | `TeamViewer` / `TeamViewer` | `displayName: "TeamViewer"`, `publisher: "TeamViewer"` | in | https://github.com/microsoft/winget-pkgs/tree/master/manifests/t/TeamViewer/TeamViewer |
| `install.steam` | `Valve.Steam` | games | 0 (nullsoft) | computer (HKLM32) | 0 (5 s) | 0 (8 s) | Pro: `Installed` (7 s) | `Steam` / `Valve Corporation` | `displayName: "Steam"`, `publisher: "Valve Corporation"` | in | https://github.com/microsoft/winget-pkgs/tree/master/manifests/v/Valve/Steam |
| `install.epic-games` | `EpicGames.EpicGamesLauncher` | games | 0 (wix) | computer (HKLM) | 0 (54 s) | – | Pro: `Installed` (39 s) | `Epic Games Launcher` / `Epic Games, Inc.` (+ `Microsoft GameInput`) | `displayName: "Epic Games Launcher"`, `publisher: "Epic Games, Inc."` | in | https://github.com/microsoft/winget-pkgs/tree/master/manifests/e/EpicGames/EpicGamesLauncher |
| `install.gog-galaxy` | `GOG.Galaxy` | games | 0 (inno) | computer (HKLM) | 0 (42 s) | – | Pro: `Installed` (42 s); Home: `Installed` (51 s) | `GOG GALAXY` / `GOG.com` | `displayName: "GOG GALAXY"`, `publisher: "GOG.com"` | in | https://github.com/microsoft/winget-pkgs/tree/master/manifests/g/GOG/Galaxy |
| `install.vcredist-x64` | `Microsoft.VCRedist.2015+.x64` | runtimes | 0 (burn) | computer (HKLM32) | `0x8A150061` (LibreOffice dependency from the `network` phase); spike: 0 (7 s) | `0x8A150061` (as above) | Pro: `Installed` (11 s); Home (retry): `Installed` (11 s) | `Microsoft Visual C++ v14 Redistributable (x64) - 14.51.36247` / `Microsoft Corporation` (+ 2 × HKLM `SystemComponent=1`) | `displayName: "Microsoft Visual C++ *Redistributable (x64) - 14.*"`, `publisher: "Microsoft Corporation"` (also catches the name `Microsoft Visual C++ 2015-2022 Redistributable (x64) - 14.51.36231` from T11 Home) | in (installation: spike and winget dependency) | https://github.com/microsoft/winget-pkgs/tree/master/manifests/m/Microsoft/VCRedist/2015+/x64 |
| `install.vcredist-x86` | `Microsoft.VCRedist.2015+.x86` | runtimes | 0 (burn) | computer (HKLM32) | 0 (9 s) | – | Pro: `Installed` (8 s) | `Microsoft Visual C++ v14 Redistributable (x86) - 14.51.36247` / `Microsoft Corporation` (+ 2 × `SystemComponent=1`) | `displayName: "Microsoft Visual C++ *Redistributable (x86) - 14.*"`, `publisher: "Microsoft Corporation"` (also catches `… 2015-2022 Redistributable (x86) - 14.51.36231` from T11 Home) | in | https://github.com/microsoft/winget-pkgs/tree/master/manifests/m/Microsoft/VCRedist/2015+/x86 |
| `install.dotnet-desktop-8` | `Microsoft.DotNet.DesktopRuntime.8` | runtimes | 0 (burn) | computer (HKLM32) | 0 (13 s) | – | Pro: `Installed` (12 s); Home: `Installed` (17 s) | `Microsoft Windows Desktop Runtime - 8.0.32 (x64)` / `Microsoft Corporation` (+ 4 × HKLM `SystemComponent=1`) | `displayName: "Microsoft Windows Desktop Runtime - 8.* (x64)"`, `publisher: "Microsoft Corporation"` | in | https://github.com/microsoft/winget-pkgs/tree/master/manifests/m/Microsoft/DotNet/DesktopRuntime/8 |
| `install.dotnet-desktop-9` | `Microsoft.DotNet.DesktopRuntime.9` | runtimes | 0 (burn) | computer (HKLM32) | 0 (11 s) | – | Pro: `Installed` (13 s) | `Microsoft Windows Desktop Runtime - 9.0.21 (x64)` / `Microsoft Corporation` (+ 4 × HKLM `SystemComponent=1`) | `displayName: "Microsoft Windows Desktop Runtime - 9.* (x64)"`, `publisher: "Microsoft Corporation"` | in | https://github.com/microsoft/winget-pkgs/tree/master/manifests/m/Microsoft/DotNet/DesktopRuntime/9 |

Evidence rule (spec 2): every winget item has `show` = 0, and each one went through a full installation with a reading of the Uninstall entry on Pro
(Discord – entry with a delay, below). VC++ x64 gave only `0x8A150061` in the main measurement, because winget had installed it earlier
as a LibreOffice dependency; its own `install` with verification passed in the Pro spike (0, 7 s) and in the Task 11 CLI batch
(Pro `Installed`, 11 s; Home – retry – `Installed`, 11 s). On Home, one item from each category passed: Chrome
and Firefox, Discord, VLC, SumatraPDF and LibreOffice, Notepad++ and 7-Zip, AnyDesk, Steam (VC++ x64 only as a LibreOffice
dependency), plus PuTTY from outside the list. No item stays on the list only because of a "same installer family" rule.

Discord: right after code 0 the Uninstall entry wasn't there yet (Home and Pro), but `winget list --id Discord.Discord --exact`
run a few seconds later returned 0 with the `Discord` row, and the spike read the HKU entry `Discord` / `Discord Inc.`. The program's
verification waits up to 60 s (Ruling R16), so the item stays.

Longest installation: VLC on Home, 230 s (EXE). The limit `InstallTimeouts.PerApp` = 30 min stays (a change would only be needed
for an installation longer than 7 min).

### CLI run with network (Task 11, 2026-10-24)

The "CLI run" column = the item's result in the batch `DSOptimizer.exe --install-apps … --out …` run as `dso` from a
Task Scheduler task (`tests/vm/explore/Invoke-E4CExploration.ps1 -Phase cli-install`, network `Default Switch` only for the duration of the phase), time
= from the `Started` event to the result event in the journal (download, installation and verification). Raw results: outside git.

- **Pro, the whole list (32) in one batch:** 31 × `Installed` ("Zainstalowano i sprawdzono." (Installed and verified.)), 1 × `Failed` (HWMonitor), CLI code 1,
  17 min 13 s. `--list-install` after the batch: 32/32 `installed = true`. Journal: 32 `app-install` entries, each `Started` → result.
  App Installer updated itself during the batch (winget v1.11.510 before, v1.29.380 after); Notepad++ still came as EXE.
- **HWMonitor on Pro:** after "Starting package install…" winget printed "Cancelling operation", "Installation abandoned" and ended with
  `0x80004004` (E_ABORT), while the Inno installer reached "Installation process succeeded" in the `--log` log and wrote the Uninstall entry.
  Most likely cause: winget was interrupted by the App Installer update at the same time (not measured directly).
  Retry on Home (VC++ x64 + HWMonitor, winget v1.11.510): both `Installed`, code 0. The item stays.
  After the R21 fix (the wave after the final review), the same run would give "Not confirmed – winget ended with code 0x80004004, but the
  program is on the list of installed apps; check the winget log" (`NotConfirmed`, CLI code 4 instead of 1): after an unknown code
  the list item gets one fresh reading, and a visible program is never "Installed", because the code can also mean
  an installation interrupted halfway. The result wasn't measured again on a VM (it needs the App Installer self-update to be repeated).
- **Home (one item from each category, different from the Task 2 measurement) + `winget:PuTTY.PuTTY`:** 9/9 `Installed`, CLI code 0,
  3 min 34 s; PuTTY confirmed by `winget list --id --exact` (`Installed`, not `InstalledUnverified`). `--list-install`:
  8 list items from this batch = `installed = true`. winget without an update (v1.11.510).
- **Discord (R16):** the HKU entry appeared after winget exited – "Zainstalowano i sprawdzono." (Installed and verified.) about 14 s (Home) and 20 s (Pro) after the line
  "Successfully installed", so within the 60 s window. Only Adobe Acrobat Reader waited longer (about 30 s, Pro).
- **VC++ as a component of another installer:** on Home one of the batch installers (with no winget dependency in the output; the logs can't tell which one) installed
  `Microsoft Visual C++ 2015-2022 Redistributable (x64)` and `(x86)` – 14.51.36231 (older entry name). The rule from T11 didn't
  recognize it (`installed = false`); a retry of `install.vcredist-x64` on Home gave `Installed` (code 0), so winget installed
  the v14 version next to the older one. Since the wave of fixes after the final review (M4), the rule ` - 14.*` catches both name variants, so such an item
  gets the "Installed" status and doesn't start.

- **Foxit PDF Reader (2026-10-24, after Task 11 was closed, Pro only):** `--install-apps install.foxit-reader` as `dso` from a Task
  Scheduler task (a one-off script on the `Invoke-E4CExploration.ps1` helpers: the `cli-install` phase with the list limited to this item):
  `Installed` ("Zainstalowano i sprawdzono." (Installed and verified.), version 2026.2.1.39815), CLI code 0, 80 s from `Started` to the result (whole CLI process 90 s),
  winget v1.11.510 without an update. `--list-install`: `install.foxit-reader` = `installed = true`, the other 32 = `false`. Journal: one
  `app-install` entry (`Started` → `Installed`). New after installation: one Uninstall entry (HKLM 64, MSI, without `QuietUninstallString`),
  the `FoxitReaderUpdateService` service ("Foxit PDF Reader Update Service", `Auto`, `LocalSystem`, running), no new AppX packages
  (apart from the `Microsoft.Winget.Source` source downloaded by winget) and no `Run` entries (HKLM 64/32, the account's HKU). Language: multilingual package (`packagelang` = `ml`, folders `Lang\<culture>` including `pl-PL`),
  the app description in the registry (`Capabilities\ApplicationDescription`) in Polish; the window language wasn't checked (the program wasn't started).

### RustDesk: CLI run with network (RustDesk plan, Task 7, 2026-10-24)

`tests/vm/explore/Invoke-E4CExploration.ps1 -Phase cli-install -CliIds … -CliRepeat` on clean VMs (`czysta-26H2`, winget v1.11.510
without an update), `DSOptimizer.exe --install-apps … --out …` as `dso` from a Task Scheduler task (Interactive + Highest), network
`Default Switch` only for the duration of the phase. Item time = from the `Started` event to the result in the journal (UTC; API query, download of
26 345 472 B, SHA256, signature, `msiexec`, verification). The size of MSI 1.5.0 doesn't appear in the artifacts of this run (the CLI doesn't
save it, and a download with a size different from `size` from the API fails): the source is the spike (file `download.json` from the raw results, outside git), field `Size` of the entry `rustdesk-1.5.0-x86_64.msi` (= `size` from the API for
the same release 1.5.0). "Whole process" = `Seconds` from `raw/cli-install.result.json` (Task Scheduler task time, including
process start), not `startedAt` → `finishedAt` of the CLI report. `msiexec` times from the MSI log lines – local VM time (UTC+2), hence
12:16 next to 10:16 in the journal. Raw results outside git
(`e4c.json`, `raw/cli-install.json`, `raw/cli-install.result.json`, `raw/cli-install-again.json`, `raw/cli-install-again.result.json`,
`raw/cli-list.json`, `raw/journal.json`, MSI log).

- **Pro – mixed batch `install.7zip,install.rustdesk`:** 7-Zip `Installed` (winget `0x00000000`, 26.05, 10.1 s), RustDesk
  `Installed` "Zainstalowano i sprawdzono." (Installed and verified.) (`source = github`, `msiCode` 0, `wingetCode` null, `version` 1.5.0, 7.6 s; `msiexec`
  12:16:16.8 → 12:16:20.5 local VM time (UTC+2) = 3.7 s), CLI code 0, whole process 27.7 s (`Seconds` from `raw/cli-install.result.json`;
  CLI report `startedAt` → `finishedAt` = 25.3 s). `--list-install`: `install.rustdesk` `installed = true`,
  `source = github`, `gitHub = rustdesk/rustdesk`. Second batch (`-CliRepeat`): both items `AlreadyInstalled` "Była już
  zainstalowana – nic nie uruchomiono." (It was already installed – nothing was run.), code 0, 4.6 s (`Seconds` from `raw/cli-install-again.result.json`; no new journal entry and no new MSI log; that there's no GitHub query on this path is checked by the test
  `AppInstallEngineGitHubTests.MatchedBeforeRun_AlreadyInstalled_NoHttp` – the VM's network traffic wasn't measured).
- **Home – `install.rustdesk` alone:** `Installed` (`msiCode` 0, 1.5.0, 7.2 s; `msiexec` 12:13:53.8 → 12:13:57.3 local VM time
  = 3.5 s), CLI code 0, process 17 s (`Seconds` from `raw/cli-install.result.json`; CLI report `startedAt` → `finishedAt` = 12.9 s);
  `--list-install` `installed = true`, `source = github`; second batch `AlreadyInstalled`, code 0, 3.6 s (`Seconds` from
  `raw/cli-install-again.result.json`).
- **Journal (both VMs):** one `app-install` entry for RustDesk – `Started` "github: rustdesk/rustdesk" → `Installed` "github:
  rustdesk/rustdesk, wersja 1.5.0, log: C:\ProgramData\DSOptimizer\logs\winget\rustdesk-msi-<czas>.log" (github: rustdesk/rustdesk, version 1.5.0, log: …\rustdesk-msi-<time>.log), status `Active`
  (on Pro preceded by the 7-Zip entry). The second run didn't add an entry.
- **After the batch (read only in the VM):** `C:\ProgramData\DSOptimizer\downloads` exists and has no files (MSI deleted), MSI log
  `logs\winget\rustdesk-msi-*.log` (about 390 KB, "Product: RustDesk -- Installation completed successfully."), `RustDesk` service
  ("RustDesk Service", `Automatic`, `Running`), 2 firewall rules "RustDesk Service" (inbound and outbound, allow) – as
  in the spike. New Uninstall entry: HKLM 64 `{9D5F8A39-0D7B-4BC4-851D-DA8501D03DE7}` `RustDesk` / `Purslane Tech Pte. Ltd.`
  1.5.0.29846474 (on Pro also `7-Zip 26.05 (x64)`); nothing disappeared or changed.
- At the end, both VMs: revert `czysta-26H2`, adapter without a switch, `Off` (`cleanup.Ok = true`, checked with `Get-VM`/`Get-VMNetworkAdapter`).

### RustDesk from GitHub (spike 2026-10-24)

RustDesk has no package in the `winget` source (`show` = `0x8A150014`, section "Rejected"), so the `install.rustdesk` item downloads
the official MSI from the GitHub release. Spike: VM
`DSO-26H2-Home` (26300), account `dso` from a Task Scheduler task (as after UAC), network `Default Switch` only for the duration of the spike, then revert.
Raw results outside git (`releases.json`, `download.json`, `install.json`, `diff.json`,
`timeline.txt`, `msi-install.log`); API and code research also outside git.

- **Version:** `GET https://api.github.com/repos/rustdesk/rustdesk/releases/latest` = tag `1.5.0` (2026-09-30), `prerelease=false`,
  previous `1.4.9`; `nightly` is a separate prerelease release that `latest` skips.
- **File:** `rustdesk-1.5.0-x86_64.msi`, 26 345 472 B. The name contains the version, so we take the address from the API.
- **Hash:** the `digest` field from the API (`sha256:624fe792…77580495`) = `Get-FileHash` of the downloaded file; the same for 1.4.9 and for the EXE variants.
- **Download:** `browser_download_url` on `github.com` → redirect to `release-assets.githubusercontent.com`, 0.8 s.
- **Signature (MSI and EXE, 1.5.0 and 1.4.9):** `Valid`; Subject `CN=PURSLANE, O=PURSLANE, S=North West, C=SG, …, SERIALNUMBER=53481265A`;
  Issuer `CN=Sectigo Public Code Signing CA EV R36` (Sectigo Limited); certificate valid until 2028-02-22; Sectigo timestamp
  (`Sectigo Public Time Stamping Signer R37`). The program pins `O=PURSLANE`, `C=SG` and `SERIALNUMBER=53481265A`, not the thumbprint.
- **Installation:** `%SystemRoot%\System32\msiexec.exe /i "<msi>" /qn /norestart /l*v "<log>"` as `dso` = code 0 in 7.5 s.
- **Uninstall entry:** HKLM 64 `{9D5F8A39-0D7B-4BC4-851D-DA8501D03DE7}`, `DisplayName` = `RustDesk`, `Publisher` =
  `Purslane Tech Pte. Ltd.`, `DisplayVersion` = `1.5.0.29846474`, `UninstallString` = `MsiExec.exe /X{9D5F8A39-0D7B-4BC4-851D-DA8501D03DE7}`,
  without `QuietUninstallString` and `SystemComponent`. The second new entry in the same diff is `Microsoft OneDrive` in the account's HKU (publisher
  Microsoft Corporation) – it doesn't belong to RustDesk.
- **Extras:** `RustDesk` service ("RustDesk Service", automatic, running); 2 firewall rules "RustDesk Service" (inbound
  and outbound, allow); shortcuts: public desktop (`C:\Users\Public\Desktop\RustDesk.lnk`), Start menu (`RustDesk\RustDesk.lnk`,
  `Uninstall RustDesk.lnk`) and the Startup folder of all accounts (`…\Start Menu\Programs\Startup\RustDesk Tray.lnk`); after 20 s, `RustDesk` processes
  as SYSTEM (session 0 and 1) and as `dso`. Hence the item's warning (spec 4, verbatim in `apps.json`).
- **`match` rule:** `displayName: "RustDesk"` (exact), `publisher: "Purslane Tech*"`; doesn't catch a similar name of the same
  publisher (`RustDesk Client`, negative test on a synthetic name).

### Warnings (`warning`) from the measurement

- **Only for the current account** (entry in HKU): Brave, Opera, Discord, Telegram, Signal, SumatraPDF, PowerToys. Text:
  "Installs only for the signed-in Windows account."
- **Additional components:** Firefox → `Mozilla Maintenance Service` service, LibreOffice → Microsoft Visual C++ v14
  Redistributable (x64) (winget dependency), Epic Games Launcher → Microsoft GameInput, Adobe Acrobat Reader → AppX package
  `AdobeAcrobatReaderCoreApp` (account and Windows image), PowerToys → 5 AppX packages, WinRAR → `WinRAR.ShellExtension`,
  Notepad++ (EXE installer) → `NotepadPlusPlus`, Foxit PDF Reader → `FoxitReaderUpdateService` service (automatic start).
- **WinRAR:** "Paid after the trial period." (spec point 4).

### Rule limitations (deliberate)

- Visual C++ x64/x86: the rule `Microsoft Visual C++ *Redistributable (x64) - 14.*` (and likewise `(x86)`) catches both measured
  names of the 14.x family: `… v14 Redistributable (x64) - 14.51.36247` (T2) and `… 2015-2022 Redistributable (x64) - 14.51.36231`
  (T11 Home), both give "Installed". It doesn't catch 2013 (` - 12.`), 2012 (` - 11.`), 2010 (`… 2010  x64 Redistributable - 10.`)
  or the components `… X64 Minimum/Additional Runtime - 14.…` (negative tests on synthetic names). Another 14.x release
  with yet another entry name won't get the status; then winget either returns "Already installed" or – as on Home in T11 –
  installs the v14 version next to the older one (code 0, `Installed`), so nothing breaks.
- Adobe: `Adobe Acrobat*` / `Adobe` also matches the paid Acrobat. Since the 64-bit version, Reader and Acrobat are one installer
  with the same entry (`Adobe Acrobat (64-bit)`), so the "Installed" status is true then.
- Zoom (`Zoom`) and TeamViewer (`TeamViewer`): publisher and name exactly from the measurement. Older releases with a different entry won't get
  the status, and winget will return "Already installed" anyway.

## Rejected (with reason)

| `wingetId` | Reason | Evidence |
|---|---|---|
| `Spotify.Spotify` | the installer doesn't work from an administrator account: `0x8A150056` "The installer cannot be run from an administrator context." | spike 2026-10-24 (Pro); `show` = 0 (the package is in the source) |
| `RustDesk.RustDesk` | not in the `winget` source; use `install.rustdesk` from GitHub instead | `show` = `0x8A150014` on Home and Pro; GitHub API: no `manifests/r/RustDesk/RustDesk` (404, 2026-10-24) |
| `Microsoft.DirectX` | spec: no Uninstall entry. Measurement: the installer is an **MSIX** package today (`UAPSignedBinary_Microsoft.DirectX.x64.appx`) and wasn't installed, so the status and verification are unconfirmed (Ruling R18) | `show` = 0, `Installer Type: msix` |
| `Microsoft.Teams` | it's already on a fresh 26H2 (AppX `MSTeams`, 4a research). winget returns `0x8A150061`, and an MSIX installation through winget wasn't confirmed. The "Apps" page (4a) suggests Teams for removal, so an item on the install list would be misleading (Ruling R18) | `show` = 0, `install` = `0x8A150061` (Pro) |

Deliberately outside the list (spec point 4): graphics card drivers, Microsoft Edge, Store packages (`msstore`).

## Search: a candidate from outside the list

`winget:PuTTY.PuTTY` (`search --query putty`, first row). Installation measured on Home and Pro: code 0, HKLM entry
`PuTTY release 0.85 (64-bit)` / `Simon Tatham`, `list --id --exact` = 0. In the CLI run on Home (Task 11)
`--install-apps winget:PuTTY.PuTTY` = `Installed` (verification `winget list --id --exact`), HKLM entry as above.

## The "Install" page (UI after the fixes of 2026-10-25)

- Category list: "All" (selected on every visit), "Selected (N)" (also 0), then the categories from this file in `InstallCategory` order. The "Search results" group is gone.
- One "Search apps, such as VLC" field filters the list live (`InstallSearch`: title, `summary`, `wingetId`, GitHub repo; the query and the text are joined without spaces, "-", "." and "_", so "7zip" finds "7-Zip"). Result: a "From the list (N)" section with category headers. winget starts only after Enter or "Also search winget" and adds an "In winget (N)" section (packages not on the list, with a note that they weren't checked on a VM). A winget result with the Id of a list item (also `RustDesk.RustDesk` → `install.rustdesk`) isn't a separate package – the note "is on the list – above".
- Clicking a category clears the field. The selection survives a change of category, text and a new search; "Selected" shows list items and winget results together.
- The list is flat: app rows interleaved with non-focusable lines (headers, notes, messages).
- Change history shows the install entry with the kind icon, the second line "App install" and a short failure reason from `JournalEvent.Reason` ("Not installed – no network"). The codes `0x800704C7` (installer interrupted, possibly an antivirus) and `0x800700E1`/`0x800700E2` (an antivirus blocked the installer) have their own texts; for a list item with a visible program, they're covered by the R21 rule (`NotConfirmed`).
