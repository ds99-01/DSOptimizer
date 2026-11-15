# DSOptimizer: details

[Back to README](../../README.md) · **English** | [Polski](../szczegoly.md)

Language, what is inside, requirements, download, privacy and what can be undone. Each item is also described on [dsoptimizer.com/docs](https://dsoptimizer.com/docs/).

## Language

The program is available in English and in Polish. The first time you start it, a window asks you to choose
the language; it is preselected to Polish when Windows is in Polish, otherwise to English. You can change the
language later in "Settings" → "Language"; the change takes effect after "Restart now" (this restarts only
the program, not the computer). In command-line mode, use `--lang pl` or `--lang en` (see [command-line mode](cli.md)).
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
check are in the antivirus reports for [version 4.0](../zrodla/2026-10-26-etap5-raport-antywirus.md) and
[version 4.0.1](../zrodla/2026-10-30-v4-0-1-raport-antywirus.md) (both in Polish).

## Privacy

The program has no telemetry and does not update itself: it doesn't download or install anything on its own. When its
window opens, it asks GitHub (api.github.com) for the latest version number, and GitHub sees your IP address. You can
turn this off in Settings; command-line mode never checks. Apart from that, it connects to the network only when you
ask it to: it downloads WinUtil and RustDesk from their authors' GitHub, and installing and searching for apps goes
through winget from the Microsoft source. The "Preparation" page opens manufacturers' pages and
Google in your browser, but sends nothing by itself. The measurement of the program's connections is in the [network report for version 4.0.6](../zrodla/2026-11-08-v4-0-6-pomiar-sieci.md) (in Polish).

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


## Documentation

Item documentation is available in English in `docs/en` (`docs/en/tweaks`, `docs/en/apps`, `docs/en/prep`, `docs/en/autostart`) and in Polish in `docs/tweaks`, `docs/apps`, `docs/prep` and `docs/autostart` (the Polish version is the original). The list of known startup items (protected and suggested, with sources) is described in [docs/en/autostart/README.md](../en/autostart/README.md). Source records (`docs/zrodla`) are written in Polish.

