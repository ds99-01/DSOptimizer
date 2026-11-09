# DSOptimizer

**English** | [Polski](README.pl.md)

Website: [dsoptimizer.com](https://dsoptimizer.com/)

DSOptimizer is a free tool for cleaning up and configuring Windows 11, best used right after installing the system.
It brings together in one window the things you would usually do by hand in many different places: turning off
unneeded features and ads, removing preinstalled apps, installing popular programs, simple system repairs
and checking drivers. Every settings change is recorded in the history. Most settings can be undone in the program; the exceptions are described below.

## Language

The program is available in English and in Polish. The first time you start it, a window asks you to choose
the language; it is preselected to Polish when Windows is in Polish, otherwise to English. You can change the
language later in "Settings" → "Language"; the change takes effect after "Restart now" (this restarts only
the program, not the computer). In command-line mode, use `--lang pl` or `--lang en` (see below).
Page and button names in this document are given as they appear in the English interface.

The program was tested on Windows 11 26H2 in English and Polish. On Windows in other languages, some items may not detect
their state correctly. Text that comes from Windows itself (names of apps and devices, the output of winget,
DISM and SFC) is shown in the language of Windows.

## What's inside

- **System settings** – the pages "Privacy and AI", "Performance", "Disk and file system", "Network and LAN",
  "Security and encryption" and "Explorer and convenience". For each item the program shows whether it is
  already set on this computer, what it changes and what you gain. Select what you want and click "Apply".
  The "Show" list narrows the view to items that are not applied, applied, or selected on all pages.
- **"Specification"** – a summary of the computer's hardware, which you can save as a report.
- **"Preparation"** – steps after installing Windows: updates, chipset and graphics drivers, the laptop
  manufacturer's support page, devices without a driver. The program only checks the state and gives
  official links.
- **"Apps"** – the list of installed apps with suggestions of what can be removed. Apps important for
  the system are protected and cannot be selected.
- **"Startup apps"** – programs that start by themselves when the signed-in user signs in (from the registry,
  the Startup folders and Microsoft Store apps), with the processor time each used at startup according to the
  Windows measurement. You can disable and enable them: the program uses the same switch as Task Manager and
  Settings and never deletes anything. Items important for the system are protected; some known programs and
  items whose file no longer exists are suggested for disabling.
- **"Install"** – installing popular programs through winget (Microsoft) and RustDesk from its
  authors' GitHub.
- **"Repairs and tools"** – one page with three parts. Repairs, among others: Windows Update reset, checking
  and repairing system files, disk cleanup, removing the previous Windows installation, network settings reset,
  winget and Microsoft Store repair. Tools, e.g. restarting the taskbar and File Explorer, and launching
  WinUtil (Chris Titus). System panels: quick access to the Windows panels that usually take the longest to find.
- **"Change history"** – Settings changes, Startup apps changes, app removals and app installs, in one place. Settings and Startup apps changes can be undone here; app removals and installs have no "Undo".

## Requirements

- Windows 11 x64 (not tested on ARM64). The program was tested on Windows 11 26H2 (Home and Pro); it may work on other versions,
  but it has not been tested on them.
- An account with administrator rights – Windows asks for consent at startup (UAC prompt).
- Portable version: nothing to install. The .NET Framework 4.8.1 the program needs is already part of Windows 11.
  The portable version also runs straight from a USB drive.

## Download

Each release has two files, built from the same program files:

- `DSOptimizer-<version>.zip` – portable version: unpack and run, nothing is installed. It also works from a USB drive.
- `DSOptimizer-<version>-setup.exe` – installer: installs to `C:\Program Files\DSOptimizer` for all users, adds Start menu
  and desktop shortcuts. Uninstall it in Settings → Apps.

Uninstalling keeps the change history in `%ProgramData%\DSOptimizer`, so settings changed earlier can still be undone
after you install the program again or use the portable version. The installer refuses to install, update or uninstall
while DSOptimizer is running, also while a command-line command is in progress (read-only ones too); close the program
and wait for such commands to finish first.

The installer is built with Inno Setup by Jordan Russell and Martijn Laan ([jrsoftware.org](https://jrsoftware.org/)).

1. Go to the [releases](https://github.com/ds99-01/DSOptimizer/releases) page and download the file you want
   (`DSOptimizer-<version>.zip` or `DSOptimizer-<version>-setup.exe`) and `SHA256SUMS.txt`.
2. Check that the file has not been damaged or replaced. In PowerShell, in the folder with the downloaded file
   (for the installer use the `-setup.exe` file name):

   ```powershell
   Get-FileHash .\DSOptimizer-<version>.zip -Algorithm SHA256
   ```

   The result must match the checksum next to the file name in `SHA256SUMS.txt` (letter case does not matter).
   If it is different, do not unpack or run the file – download it again.
3. Portable version: unpack the zip. You get a `DSOptimizer` folder – keep it whole, because `DSOptimizer.exe` needs
   the files next to it. Installer: run `DSOptimizer-<version>-setup.exe` and follow the wizard.
4. Run `DSOptimizer.exe` (or the Start menu shortcut) and confirm the UAC prompt.

### SmartScreen warning

The program does not have a digital signature yet, so the first time you run a file downloaded from the internet,
Windows shows a blue window "Windows protected your PC". Click **"More info"** and then **"Run anyway"**.
The window also appears after unpacking the zip and when running the installer. If the files got onto the USB drive some other way than
by downloading them from the internet, the window usually does not appear.

Microsoft Defender with up-to-date signatures does not block the program or any of its functions – details of the
check are in the antivirus reports for [version 4.0](docs/zrodla/2026-10-26-etap5-raport-antywirus.md) and
[version 4.0.1](docs/zrodla/2026-10-30-v4-0-1-raport-antywirus.md) (both in Polish).

## Privacy

The program has no telemetry and does not update itself: it doesn't download or install anything on its own. When its
window opens, it asks GitHub (api.github.com) for the latest version number, and GitHub sees your IP address. You can
turn this off in Settings; command-line mode never checks. Apart from that, it connects to the network only when you
ask it to: it downloads WinUtil and RustDesk from their authors' GitHub, and installing and searching for apps goes
through winget from the Microsoft source. The "Preparation" page opens manufacturers' pages and
Google in your browser, but sends nothing by itself. The measurement of the program's connections is in the [network report for version 4.0.6](docs/zrodla/2026-11-08-v4-0-6-pomiar-sieci.md) (in Polish).

Change history and logs stay on the computer, in the `%ProgramData%\DSOptimizer` folder. Every account on this computer
can browse it and read the logs, but only administrators can write to it, and only they can open the change history and
settings files. If the folder's permissions are changed anyway, the program sets it aside at the next start and moves
the change history over if nobody touched it. A file from this folder shown in the log panel (for example a winget log)
can be saved elsewhere with "Save a copy…". The specification report is saved where you choose in the "Save As" window.
When you run the program from a USB drive, a copy of the log and the report is also saved next to `DSOptimizer.exe`.
The specification report contains disk serial numbers, so keep that in mind before sharing the drive or the report.

## What can and cannot be undone

- **System settings** – you can undo most changes with the "Undo" button next to the item or on the
  "Change history" page. The program remembers the state before the change and returns to it.
  The few items that cannot be undone carry a clear warning about it (the Undo button is missing there). The program's settings also have an option
  to create a Windows restore point before changes. When the window opens, the program checks whether the settings you applied are still applied; if some are not, a banner lets you select them and apply them again – nothing changes without your click.
- **Removing apps** – the program does not restore a removed app. Before removing a desktop program (not a Store
  app), it creates a Windows restore point, and if that fails, it asks whether to continue without one.
- **Repairs and actions** – they run once and have no Undo (e.g. cleaned-up files cannot be recovered).
  Read the item's description before running it.
- **Install** – you can uninstall an installed program on the "Apps" page.
- **Startup apps** – disabling and enabling only flips the switch Windows itself uses (the same as in Task Manager
  and Settings); the program never deletes a startup entry or file. You can undo a change on the "Change history"
  page or by switching the item back. Changes take effect at the next sign-in, and no restore point is created.

## Building from source

You need the .NET SDK version given in `global.json` (e.g. `winget install Microsoft.DotNet.SDK.10`).

```powershell
dotnet build DSOptimizer.sln -c Release
```

The built program: `src\DSOptimizer.App\bin\Release\net481\DSOptimizer.exe`. Tests (optional; some of them
require PowerShell running as administrator):

```powershell
dotnet test DSOptimizer.sln -c Release
```

## Command-line mode

When run with arguments, `DSOptimizer.exe` does not open a window: it runs one command, writes a JSON report to the `--out` file and exits with an exit code. Without arguments it opens the window. `--out` is required (the result goes to a file, not to stdout), and there must be exactly one command.

```text
DSOptimizer.exe --detect <id> --out <file.json>
DSOptimizer.exe --apply <id> [--restore-point required|ifavailable|skip] --out <file.json>
DSOptimizer.exe --undo <id> --out <file.json>
DSOptimizer.exe --execute <id> [--catalog <file.json>] --out <file.json>
DSOptimizer.exe --list-apps --out <file.json>
DSOptimizer.exe --remove-app <id> [--restore-point required|ifavailable|skip] --out <file.json>
DSOptimizer.exe --list-install [--apps-catalog <file.json>] --out <file.json>
DSOptimizer.exe --install-apps <id>[,<id>…] [--apps-catalog <file.json>] --out <file.json>
DSOptimizer.exe --search-apps <text> --out <file.json>
DSOptimizer.exe --list-autostart --out <file.json>
DSOptimizer.exe --set-autostart <id> disabled|enabled --out <file.json>
DSOptimizer.exe --undo-autostart <id> --out <file.json>
```

- `--detect`, `--apply` and `--undo` apply to `kind=tweak` items; `--execute` applies to `kind=action` and `kind=panel` (`--apply` on an action or panel gives code 2).
- `--list-apps` returns the visible rows of the "Apps" page, and `--remove-app` removes one app by its id (`appx:<PackageFamilyName>` or `win32:<key>`); a protected or blocked row is refused. `--restore-point` defaults to `required`: without a restore point nothing is removed.
- `--list-install` returns the install list with the winget state, `--install-apps` installs the given items (`install.<name>` from the list or `winget:<id>`) without asking, and `--search-apps` returns `winget search` results. `--apps-catalog` replaces the built-in install list.
- Installing and searching through winget work only on the signed-in user's account; on another account they end with a refusal (code 1), and winget is not started.
- `--list-autostart` returns the rows of the "Startup apps" page for the signed-in user's account in the `autostartRows` field: `id`, `source` (`RunUser`, `RunMachine`, `Run32`, `FolderUser`, `FolderCommon`, `StartupTask`), `name`, `publisher`, `scope` (`User`, `Machine`, `Store`), `state` (`Enabled`, `Disabled`, `Unknown`), `missingFile`, `verdict` (`Protected`, `CanDisable` or `null` when not assessed), `canToggle`, `cpuMs` (processor time from the Windows measurement, in milliseconds) and `diskBytes` (disk reads in bytes); both numbers are `null` when not measured.
- `--set-autostart` disables or enables one item by its id from the list (the state is not case-sensitive), and `--undo-autostart` restores the state from before the item's active change in the change history. An id that is not on the list gives code 1; a row with `canToggle=false` (protected or read-only) is refused (code 1) with the reason in `message`, and nothing is written; an item already in the target state gives `AlreadyApplied` (code 0). An undo that could not restore everything gives code 4 and lists what was not restored in `notRestored`. `--catalog`, `--apps-catalog` and `--restore-point` with these commands, or a missing or unknown state, give code 2.
- Commands that change the system (`--apply`, `--undo`, `--execute`, `--remove-app`, `--install-apps`, `--set-autostart`, `--undo-autostart`) end with code 1 and change nothing while another copy of DSOptimizer is running (a window or such a command). Read-only commands (`--detect`, `--list-apps`, `--list-install`, `--search-apps`, `--list-autostart`) also work while the window is open, but while any command is running the installer and the uninstaller refuse to work.

### Exit codes

| Code | Meaning |
|---|---|
| 0 | Success (`--detect` always returns 0, regardless of the state) |
| 1 | Operation not performed, state unchanged (`Failed`, `FailedRolledBack`, `Refused`, `NotSupported`, `Skipped`; series aborted, e.g. no restore point with `--restore-point required`, field `aborted`); another copy of DSOptimizer is running |
| 2 | Invalid arguments or a command that does not match the item's `kind` |
| 3 | Catalog error (file, JSON, validation, unknown id) |
| 4 | State needs manual checking (`FailedRollbackIncomplete`, `UndoIncomplete`, `NotConfirmed`, `InstalledUnverified`) |
| 5 | Internal error (exception, the `%ProgramData%\DSOptimizer` root cannot be secured) |
| 6 | The `--out` file cannot be written |

### JSON report

Main fields: `exitCode` (same as the process code), `result` (code name, e.g. `UsageError`), `state` (state detected before the operation), `outcome` (operation result), `finalState` (state after the operation), `warnings`, `restorePoint` (restore point status), `user` (target user's SID), `machine` (build, edition, machine name).

The `--out` file is UTF-8 without BOM. Windows PowerShell 5.1 reads such a file correctly (Polish characters) only with an explicit encoding: `Get-Content <file> -Raw -Encoding UTF8`.

### External catalog (`--catalog`)

`--catalog <file.json>` **replaces** the built-in catalog and runs the scripts from that file as administrator. Use trusted files only. Use of an external catalog is recorded in the report's `warnings`, together with the catalog hash (prefixed with `sha256:`). The hash is SHA-256 of the file name and the decoded JSON text, not of the file bytes, so it does not match the result of `Get-FileHash`.

## Documentation

Item documentation is available in English in `docs/en` (`docs/en/tweaks`, `docs/en/apps`, `docs/en/prep`, `docs/en/autostart`) and in Polish in `docs/tweaks`, `docs/apps`, `docs/prep` and `docs/autostart` (the Polish version is the original). The list of known startup items (protected and suggested, with sources) is described in [docs/en/autostart/README.md](docs/en/autostart/README.md). Source records (`docs/zrodla`) are written in Polish.

## License

DSOptimizer is released under the Mozilla Public License 2.0 – full text in the [LICENSE](LICENSE) file.
In short: you may use, modify and distribute the program, but if you distribute a modified version,
the source of the modified program files must be available under the same license.

The DSOptimizer name and the DŚO mark are not covered by the MPL 2.0 license; modified versions must use a different name.

The program uses libraries and fonts by other authors, under their own licenses – see
[THIRD_PARTY_NOTICES_EN.md](THIRD_PARTY_NOTICES_EN.md).

## Author

Dawid Świeboda – [github.com/ds99-01](https://github.com/ds99-01)

## Contact

- Questions and feedback: [contact@dsoptimizer.com](mailto:contact@dsoptimizer.com)
- Security issues: [security@dsoptimizer.com](mailto:security@dsoptimizer.com)

## Support the project

DSOptimizer is free. If it helped you, you can support its upkeep and development on [GitHub Sponsors](https://github.com/sponsors/ds99-01) or [buycoffee.to](https://buycoffee.to/dsoptimizer). Support is voluntary and does not unlock any features.
