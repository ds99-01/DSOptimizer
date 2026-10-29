# DSOptimizer

**English** | [Polski](README.pl.md)

DSOptimizer is a free tool for cleaning up and configuring Windows 11, best used right after installing the system.
It brings together in one window the things you would usually do by hand in many different places: turning off
unneeded features and ads, removing preinstalled apps, installing popular programs, simple system repairs
and checking drivers. Every settings change is recorded in the history and can be undone.

## Language

The program's interface is currently in Polish only. An English interface is coming.
Page and button names below are given in Polish, with an English translation in brackets.

## What's inside

- **System settings** – the pages „Prywatność i AI” (Privacy and AI), „Wydajność” (Performance),
  „Dysk i system plików” (Disk and file system), „Sieć i LAN” (Network and LAN), „Bezpieczeństwo i szyfrowanie”
  (Security and encryption) and „Explorer i wygoda” (Explorer and convenience). For each item the program shows
  whether it is already set on this computer, what it changes and what you gain. Select what you want and click
  „Zastosuj” (Apply).
- **„Panele systemowe” (System panels)** – quick access to the Windows panels that usually take the longest to find.
- **„Naprawy” (Repairs)** – among others: Windows Update reset, checking and repairing system files, disk cleanup,
  removing the previous Windows installation, network settings reset, winget and Microsoft Store repair.
- **„Narzędzia” (Tools)** – e.g. restarting the taskbar and File Explorer, and launching WinUtil (Chris Titus).
- **„Specyfikacja” (Specification)** – a summary of the computer's hardware, which you can save as a report.
- **„Przygotowanie” (Preparation)** – steps after installing Windows: updates, chipset and graphics drivers,
  the laptop manufacturer's support page, devices without a driver. The program only checks the state and gives
  official links.
- **„Aplikacje” (Apps)** – the list of installed apps with suggestions of what can be removed. Apps important for
  the system are protected and cannot be selected.
- **„Instalacja” (Install)** – installing popular programs through winget (Microsoft) and RustDesk from its
  authors' GitHub.
- **„Historia zmian” (Change history)** – everything the program has changed, with an „Cofnij” (Undo) button
  wherever undo is possible.

## Requirements

- Windows 11, 64-bit. The program was tested on Windows 11 26H2 (Home and Pro); it may work on other versions,
  but it has not been tested on them.
- An account with administrator rights – Windows asks for consent at startup (UAC prompt).
- Nothing to install. The .NET Framework 4.8.1 the program needs is already part of Windows 11.
  The program also runs straight from a USB drive.

## Download

1. Go to the [releases](https://github.com/ds99-01/DSOptimizer/releases) page and download `DSOptimizer-<version>.zip`
   and `SHA256SUMS.txt`.
2. Check that the file has not been damaged or replaced. In PowerShell, in the folder with the downloaded file:

   ```powershell
   Get-FileHash .\DSOptimizer-<version>.zip -Algorithm SHA256
   ```

   The result must match the checksum next to the file name in `SHA256SUMS.txt` (letter case does not matter).
   If it is different, do not unpack the file – download it again.
3. Unpack the zip. You get a `DSOptimizer` folder – keep it whole, because `DSOptimizer.exe` needs the files
   next to it.
4. Run `DSOptimizer.exe` and confirm the UAC prompt.

### SmartScreen warning

The program does not have a digital signature yet, so the first time you run a file downloaded from the internet,
Windows shows a blue window "Windows protected your PC". Click **"More info"** and then **"Run anyway"**.
The window also appears after unpacking the zip. If the files got onto the USB drive some other way than
by downloading them from the internet, the window usually does not appear.

Microsoft Defender with up-to-date signatures does not block the program or any of its functions – details of the
check are in the [antivirus report](docs/zrodla/2026-10-26-etap5-raport-antywirus.md) (in Polish).

## Privacy

The program has no telemetry and does not update itself. It connects to the network on its own only when you ask
it to: it downloads WinUtil and RustDesk from their authors' GitHub, and installing and searching for apps goes
through winget from the Microsoft source. The „Przygotowanie” (Preparation) page opens manufacturers' pages and
Google in your browser, but sends nothing by itself.

Change history, logs and reports stay on the computer, in the `%ProgramData%\DSOptimizer` folder. When you run the
program from a USB drive, a copy of the log and reports is also saved next to `DSOptimizer.exe`.

## What can and cannot be undone

- **System settings** – you can undo every change with the „Cofnij” (Undo) button next to the item or on the
  „Historia zmian” (Change history) page. The program remembers the state before the change and returns to it.
  The few items that cannot be undone carry a clear warning about it. The program's settings also have an option
  to create a Windows restore point before changes.
- **Removing apps** – the program does not restore a removed app. Before removing a desktop program (not a Store
  app), it creates a Windows restore point, and if that fails, it asks whether to continue without one.
- **Repairs and actions** – they run once and have no Undo (e.g. cleaned-up files cannot be recovered).
  Read the item's description before running it.
- **Install** – you can uninstall an installed program on the „Aplikacje” (Apps) page.

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
```

- `--detect`, `--apply` and `--undo` apply to `kind=tweak` items; `--execute` applies to `kind=action` and `kind=panel` (`--apply` on an action or panel gives code 2).
- `--list-apps` returns the visible rows of the „Aplikacje” (Apps) page, and `--remove-app` removes one app by its id (`appx:<PackageFamilyName>` or `win32:<key>`); a protected or blocked row is refused. `--restore-point` defaults to `required`: without a restore point nothing is removed.
- `--list-install` returns the install list with the winget state, `--install-apps` installs the given items (`install.<name>` from the list or `winget:<id>`) without asking, and `--search-apps` returns `winget search` results. `--apps-catalog` replaces the built-in install list.
- Installing and searching through winget work only on the signed-in user's account; on another account they end with a refusal (code 1), and winget is not started.

### Exit codes

| Code | Meaning |
|---|---|
| 0 | Success (`--detect` always returns 0, regardless of the state) |
| 1 | Operation not performed, state unchanged (`Failed`, `FailedRolledBack`, `Refused`, `NotSupported`, `Skipped`; series aborted, e.g. no restore point with `--restore-point required`, field `aborted`) |
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

Item documentation is available in English in `docs/en` (`docs/en/tweaks`, `docs/en/apps`, `docs/en/prep`) and in Polish in `docs/tweaks`, `docs/apps` and `docs/prep` (the Polish version is the original). Source records (`docs/zrodla`) are in Polish only.

## License

DSOptimizer is released under the Mozilla Public License 2.0 – full text in the [LICENSE](LICENSE) file.
In short: you may use, modify and distribute the program, but if you distribute a modified version,
the source of the modified program files must be available under the same license.

The DSOptimizer name and the DŚO mark are not covered by the MPL 2.0 license; modified versions must use a different name.

The program uses libraries and fonts by other authors, under their own licenses – see
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## Author

Dawid Świeboda – [github.com/ds99-01](https://github.com/ds99-01)
