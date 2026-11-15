# DSOptimizer: command-line mode

[Back to README](../../README.md) · **English** | [Polski](../cli.md)

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
