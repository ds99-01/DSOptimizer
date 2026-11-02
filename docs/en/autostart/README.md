<!-- source: docs/autostart/README.md sha256: e6d98b85da8c41bd13acfc535c6dbe7a5c591c2d192d086b81d891e6277b4b4d -->

# Startup apps – known items file `autostart-known.json`

The file `src/DSOptimizer.Catalog/catalog/autostart-known.json` tells the "Startup apps" page which items must not be turned off (padlock)
and which known programs can be turned off (suggestion). It is loaded by `AutostartKnownCatalog.Load` (Core) and embedded as
`BuiltInCatalog.AutostartKnownSource`. It is not part of `BuiltInCatalog.Sources` (those are only tweak arrays); the test
`Sources_MatchCatalogFilesOnDisk` skips it by name, and `AutostartKnownSource_IsEmbeddedAndMatchesFileOnDisk` makes sure the file on disk
and the resource are the same.

The file runs nothing and selects nothing. A padlock removes the checkbox, and a suggestion is selected only by the "Select suggested" button.

## Format

```json
{
  "entries": [
    { "id", "match": { "file", "folders", "publishers", "valueName?", "packageFamily?" }, "verdict", "summary", "source", "confirmed" }
  ]
}
```

- `id` – unique in the file, case-insensitive.
- `match.file` – the executable file name only, without a folder; a pattern with `*`, but not made of asterisks only.
- `match.folders` – a non-empty list of patterns for the file's folder. Each starts with one of the variables `%SystemRoot%`, `%ProgramFiles%`,
  `%ProgramFiles(x86)%`, `%ProgramData%`, `%LOCALAPPDATA%`, `%APPDATA%`, has `\` right after it and ends with `\` or `*`.
  A `protected` entry may use only `%SystemRoot%`, `%ProgramFiles%` and `%ProgramFiles(x86)%` – only an administrator can write there.
- `match.publishers` – a non-empty list of publisher patterns (`CompanyName` from the file version, for Store apps the package publisher);
  required in every entry, not made of asterisks only.
- `match.valueName` – optional: the value name in the Run key (or the file name in the Startup folder, the TaskId of a Store task) must be equal.
- `match.packageFamily` – optional: the Store package family must be equal.
- `verdict` – `protected` (padlock, read-only item) or `can-disable` (suggested for turning off).
- `summary` – ≤ 160 characters, in Polish, in plain words: what it is and what happens when it's turned off. No registry keys, no `=`,
  no identifiers, dates or speed promises. English in `src/DSOptimizer.Core/Localization/Translations/catalog-autostart-known.json`.
- `source` – a non-empty list: absolute `https://` addresses or documents from the repository's `docs/zrodla` folder (in Polish).
- `confirmed` – `true` when the file, folder, publisher and value names are confirmed by a reading from a system; `false` when they come
  only from descriptive sources.

Patterns are compared case-insensitively, `*` covers any text (including `\`). An error in the file gives
`AutostartCatalogException` with the entry id and the field name.

## Matching rules

- Only an item with a resolved file (full, expanded path) and a publisher gets a verdict. File, folder and publisher must all match,
  and a given `valueName` and `packageFamily` must be equal.
- The same file in another folder gets no verdict: `SecurityHealthSystray.exe` in `%TEMP%` or `steam.exe` directly in `%APPDATA%`
  gets neither a padlock nor a suggestion. The same goes for a file with a known name from a different publisher.
- Machine variables in folder patterns (`%SystemRoot%`, `%ProgramFiles%`, `%ProgramFiles(x86)%`, `%ProgramData%`) come from the process,
  never from the account settings; `%LOCALAPPDATA%` and `%APPDATA%` come from the signed-in user's account. The account can't move an
  administrator folder to a place of its own.
- A path with `/` or with a `.` or `..` segment never matches.
- `protected` entries are checked first, then `can-disable`: protection wins over any suggestion.
- An item without a match is not assessed – it can be switched, but the program doesn't suggest it.

## Evidence thresholds

The thresholds are different on purpose:

- `can-disable` requires `confirmed: true`, a document with a reading on a test machine (file name, folder, publisher and value name,
  `docs/zrodla/2026-11-02-autostart-programy.md` (in Polish)) and at least one `https://` address about the effect of turning it off. The loader enforces this.
- `protected` may have `confirmed: false`. An unneeded padlock costs less than a suggestion to turn off an antivirus or a touchpad
  driver, so descriptive sources are enough. Entries with `confirmed: false` haven't been confirmed on real hardware yet.
- When the sources don't name a folder (`asus-hotkeys`, `kaspersky`, `avast`, `bitdefender`, `logitech-g-hub`), the folder is
  `%ProgramFiles%\*` or `%ProgramFiles(x86)%\*` and the publisher is required: a file with that name in `%TEMP%` or `AppData` gets no padlock.
- `security-health` is the only confirmed `protected` entry: the `SecurityHealth` value in `HKLM\…\Run` is there on a clean system
  (`docs/zrodla/2026-11-02-autostart-pomiar.md` (in Polish)).

## Missing file = suggested

An item whose file certainly doesn't exist (the path is fully expanded and the system answers "does not exist") goes to the "File missing"
group and is suggested for turning off without any entry in this file – nothing can start it. When the file can't be resolved (an unknown
variable, an unquoted command with no matching file), the item is neither "file missing" nor suggested. A padlock wins here too.

## Entries

| id | verdict | file | folder | publisher | confirmed | sources |
|---|---|---|---|---|---|---|
| `steam` | `can-disable` | `steam.exe` | `%ProgramFiles(x86)%\Steam\` | `Valve Corporation` | yes | `docs/zrodla/2026-11-02-autostart-programy.md` (in Polish), [pcrisk](https://blog.pcrisk.com/windows/13714-stop-steam-from-opening-on-startup), [Help Desk Geek](https://helpdeskgeek.com/how-to-stop-steam-from-opening-on-startup/) |
| `epic-games-launcher` | `can-disable` | `EpicGamesLauncher.exe` | `%ProgramFiles%\Epic Games\Launcher\Portal\Binaries\Win64\` | `Epic Games, Inc.` | yes | `docs/zrodla/2026-11-02-autostart-programy.md` (in Polish), [Epic Games](https://www.epicgames.com/help/epic-games-store-c-202300000001639/launcher-support-c-202300000001735/how-to-stop-the-epic-games-launcher-from-running-on-pc-startup-a202300000017760) |
| `battle-net` | `can-disable` | `Battle.net.exe` | `%ProgramFiles(x86)%\Battle.net\` | `Blizzard Entertainment` | yes | `docs/zrodla/2026-11-02-autostart-programy.md` (in Polish), [Blizzard](https://eu.battle.net/support/en/article/358265) |
| `spotify` | `can-disable` | `Spotify.exe` | `%APPDATA%\Spotify\` | `Spotify Ltd` | yes | `docs/zrodla/2026-11-02-autostart-programy.md` (in Polish), [pcrisk](https://www.pcrisk.com/blog/windows/13713-stop-spotify-from-opening-on-startup), [Technipages](https://www.technipages.com/spotify-disable-automatic-startup) |
| `discord` | `can-disable` | `Discord.exe` | `%LOCALAPPDATA%\Discord\` | `Discord Inc.` | yes | `docs/zrodla/2026-11-02-autostart-programy.md` (in Polish), [pcrisk](https://blog.pcrisk.com/windows/13704-stop-discord-from-opening-on-startup) |
| `ea-app` | `can-disable` | `EALauncher.exe` | `%ProgramFiles%\Electronic Arts\EA Desktop\EA Desktop\` | `Electronic Arts` | yes | `docs/zrodla/2026-11-02-autostart-programy.md` (in Polish), [EA forum](https://forums.ea.com/discussions/ea-app-general-discussion-en/how-do-i-prevent-the-ea-app-from-starting-with-windows/7546567) |
| `security-health` | `protected` | `SecurityHealthSystray.exe` | `%SystemRoot%\System32\` | `Microsoft Corporation` | yes | `docs/zrodla/2026-11-02-autostart-pomiar.md` (in Polish), [Microsoft Q&A](https://learn.microsoft.com/en-us/answers/a/6762277), [Winhelponline](https://www.winhelponline.com/blog/windows-security-icon-tray-taskmgr-startup/) |
| `realtek-audio-service` | `protected` | `RtkAudUService64.exe` | `%SystemRoot%\System32\`, `%SystemRoot%\System32\DriverStore\FileRepository\*\` | `Realtek Semiconductor Corp.` | no | [Gridinsoft](https://blog.gridinsoft.com/rtkauduservice64-exe/), [ROG forum](https://rog-forum.asus.com/t5/promotions-general-discussions/unreadable-title/td-p/741740/page/69) |
| `realtek-hd-audio-manager` | `protected` | `RAVCpl64.exe` | `%ProgramFiles%\Realtek\Audio\HDA\` | `Realtek Semiconductor Corp.` | no | [Gridinsoft](https://blog.gridinsoft.com/ravcpl64-exe/), [Seven Forums](https://www.sevenforums.com/threads/can-i-safely-remove-syntpenh-exe-ravcpl64-exe-s-from-startup.146658/) |
| `elan-touchpad` | `protected` | `ETDCtrl*.exe` | `%ProgramFiles%\Elantech\*`, `%SystemRoot%\System32\` | `ELAN Microelectronics Corp.` | no | [Gridinsoft](https://blog.gridinsoft.com/etdctrl-exe/), [Appuals](https://appuals.com/fix-etd-control-center-not-responding/) |
| `synaptics-touchpad` | `protected` | `SynTPEnh.exe` | `%ProgramFiles%\Synaptics\SynTP\` | `Synaptics Incorporated` | no | [Gridinsoft](https://blog.gridinsoft.com/syntpenh-exe/), [Seven Forums](https://www.sevenforums.com/threads/can-i-safely-remove-syntpenh-exe-ravcpl64-exe-s-from-startup.146658/), [Windows Report](https://windowsreport.com/synaptics-touchpad-disabled-windows-startup/) |
| `asus-hotkeys` | `protected` | `HControl.exe` | `%ProgramFiles%\*`, `%ProgramFiles(x86)%\*` | `ASUSTeK Computer Inc.` | no | [file.net](https://www.file.net/process/hcontrol.exe.html), [ROG forum](https://rog-forum.asus.com/t5/rog-gaming-notebooks/atk-issue-solved/td-p/360155) |
| `kaspersky` | `protected` | `avp.exe` | `%ProgramFiles%\*`, `%ProgramFiles(x86)%\*` | `AO Kaspersky Lab` | no | [Wilders Security](https://www.wilderssecurity.com/threads/kis-v7-startup-issue.185155/) |
| `avast` | `protected` | `AvastUI.exe` | `%ProgramFiles%\*`, `%ProgramFiles(x86)%\*` | `AVAST Software*`, `Gen Digital*` | no | [Spybot forum](https://forums.spybot.info/showthread.php?p=418242) |
| `bitdefender` | `protected` | `bdagent.exe` | `%ProgramFiles%\*`, `%ProgramFiles(x86)%\*` | `Bitdefender SRL` | no | [Bitdefender](https://community.bitdefender.com/en/discussion/99506/bdagent-and-bdagentapp-startup-apps-both-using-the-same-exe), [Bitdefender](https://community.bitdefender.com/en/discussion/comment/337641) |
| `logitech-g-hub` | `protected` | `lghub.exe` | `%ProgramFiles%\*`, `%ProgramFiles(x86)%\*` | `Logitech Europe S.A.` | no | [Logitech](https://support.logi.com/hc/en-us/articles/9631120673943-G-HUB-or-Logi-Options-is-stuck-on-Loading-screen-with-macOS-Ventura) |

Notes on the sources:

- `can-disable` entries: paths, publishers and value names come from a reading after installing the programs with winget; the effect of
  turning them off (the program doesn't start by itself and works once opened) comes from the addresses in the table. The Run value of
  Discord's machine-wide installer (`C:\ProgramData\SquirrelMachineInstalls\Discord.exe --checkInstall`) doesn't match the `discord` entry
  and stays unassessed.
- The `ea-app` entry: turning off startup doesn't stop the `EABackgroundService` service the program installs next to it.
- The sources of `protected` entries are mostly tech sites and forums; the exact Run value names there are unverified, so these entries
  have no `valueName`. The `logitech-g-hub` source describes macOS; the effect on Windows isn't confirmed.

## Intentionally not assessed

These items have no entry – the program gives them neither a padlock nor a suggestion, and the decision stays with the user:

- **Microsoft Teams** – starts two ways (a Run value and a Store startup task), and a work account may have company policies.
- **OneDrive** – also syncs the Desktop, Documents and Pictures folders; when it's turned off, files may stop reaching the cloud, and the
  user may think they have a copy.
- **Dropbox** – sync stops until it's opened by hand, and the entry may come back after an update.
- **Google Drive** – sync and access to Drive in File Explorer don't work until it's opened by hand.
- **Java Update Scheduler** (`jusched.exe`) – turning it off hides the notifications about Java security fixes.
- **iTunesHelper** – Apple advises against turning it off ([Apple](https://support.apple.com/en-us/102018)); iTunes stops reacting by itself
  to a connected iPhone or iPod.
- **Graphics drivers** (NVIDIA, AMD, Intel) – the value names aren't confirmed, and the entry only drives extras (icon, recording,
  profiles, keyboard shortcuts), not the driver itself.
- **Bluetooth** (an old stack with a notification area icon) – the entry drives only the icon; connections are handled by the stack's service.
- **Microsoft Edge – startup boost** – Edge restores the entry by itself; it's turned off for good in Edge settings
  ([Microsoft](https://support.microsoft.com/en-us/edge/get-help-with-startup-boost)).

## How to add an entry

1. Install the program on a clean test machine and read its startup entry: location, value name, command, the file's `CompanyName`.
   Record the reading in a source document of the repository (like `docs/zrodla/2026-11-02-autostart-programy.md` (in Polish)).
2. Find an `https://` source about the effect of turning it off (the vendor's documentation or a tech site). Without a source, the entry stays out.
3. Add the entry to `autostart-known.json`: `can-disable` only with `confirmed: true`, a reading and an `https://` address; `protected` only
   with the folders `%SystemRoot%`, `%ProgramFiles%`, `%ProgramFiles(x86)%` and with a publisher.
4. Add the English `summary` to `catalog-autostart-known.json`, and a row to the Polish table and to `docs/en/autostart/README.md` (this file).
5. Run the tests (`AutostartKnownBuiltInTests`, `CatalogCopyRulesTests`) and the report `tools/i18n/Get-MissingTranslations.ps1`.
