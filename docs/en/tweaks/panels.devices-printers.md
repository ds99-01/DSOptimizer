<!-- source: docs/tweaks/panels.devices-printers.md sha256: 8f45bfd88d7012446d2605324c92e716fe6e25cbecc726ccb52d9fb038a7e6bd -->

# panels.devices-printers – Devices and Printers

## Verdict for 26H2
Port as `kind=panel`, `risk=safe`: on the 26H2 Home and Pro VMs the command opens the classic Devices and Printers window in File Explorer, not Settings. It goes in with a caveat: it needs File Explorer of this account in the current session (guard R23); outside such a session it refuses instead of leaving an elevated File Explorer behind.

## What it changes
Nothing: `kind=panel` opens a window and doesn't write to the registry, services or files. `launch` field: `file` = `shell:::{A8A91A66-3A7D-4424-8D24-04E180695C7A}`, `arguments` = empty.
The launcher starts `%SystemRoot%\explorer.exe` through CreateProcess (a fixed path from `Environment.GetFolderPath(Windows)`, not System32) with the argument `shell:::{A8A91A66-3A7D-4424-8D24-04E180695C7A}`, without ShellExecute and without waiting for the window. Before starting, it checks that `explorer.exe` of the same account is running in the current session (shell guard, spec A6); otherwise `Failed` "File Explorer for this account isn't running in the current session – not starting an elevated File Explorer." and nothing starts.

Rejected variants (not separate items; VM data in the "Sources" section):
- `Start-Process 'shell:::{A8A91A66-3A7D-4424-8D24-04E180695C7A}'` (WinUtil, ShellExecute): in K1 it blocks for about 108 s and returns the error "Wykonanie serwera nie powiodło się" (Server execution failed); in K2 it returns an error after 49 ms even though the window is open – a false negative result. ShellExecute doesn't exist in the launcher.
- `control.exe /name Microsoft.DevicesAndPrinters` (MS canonical name): redirects to the Settings app (`SystemSettings.exe` + `ApplicationFrameHost.exe`), not to the classic window; the `ms-settings:` fallback was rejected (decision D2).

## Sources
- WinUtil `config/feature.json` @ `144d516` (MIT, 2026-07-31): WPFPanelPrinter = "Printer Panel", `InvokeScript`: `Start-Process 'shell:::{A8A91A66-3A7D-4424-8D24-04E180695C7A}'`. A hint for the list and the command, not evidence of the effect – https://raw.githubusercontent.com/ChrisTitusTech/winutil/144d51675190ffcb2553b7ba5313f8d602c8ecf9/config/feature.json
- https://learn.microsoft.com/en-us/windows/win32/shell/controlpanel-canonical-names – MS "Canonical Names of Control Panel Items": `Microsoft.DevicesAndPrinters` = `{A8A91A66-3A7D-4424-8D24-04E180695C7A}` ("Devices and Printers").
- Reading from 26H2 VMs (build 26300.9457), Home (`DSO-26H2-Home`) and Pro (`DSO-26H2-Pro`), 2026-10-19, K1 = PS Direct as Administrator (session 0), K2 = elevated interactive task in the session of user `dso`: follow-up probe (CreateProcess `C:\Windows\explorer.exe shell:::{A8A91A66-…}`, K2): the started `explorer.exe` exits with code 1 within 3 s, and the Devices and Printers window in File Explorer (`CabinetWClass`) belongs to a new `explorer.exe /factory,{75dff2b7-…} -Embedding` process of the user's account (session 1, `Limited` token, `IsUserShell` = false).
  - K1 (session 0): CreateProcess without the guard left a running, elevated `explorer.exe` of Administrator (`Default` token, `Elevated` = true) with no window for the user. That's why guard R23 refuses in K1 (no File Explorer of Administrator in session 0), and the VM case through PS Direct doesn't check the window.
  - K2: the Devices and Printers window in File Explorer (`CabinetWClass`). Home = Pro in all fields except the PIDs.
- Research and results: `docs/zrodla/2026-10-19-partia-c5-zrodla.md` (in Polish), sections "Wyniki VM" (VM results) and "Sonda uzupełniająca" (follow-up probe).

## Defaults on a fresh 26H2
Not applicable: the item doesn't read or write any settings. The only system state after running it is the window's process (VM reading, 2026-10-19, above).

## Detection and undo
Not applicable: `kind=panel` has no detection and no undo. `Executed` only means that the process started; whether the window appeared is judged by the VM test (K2; smoke test with the real DSOptimizer.exe in the dso session – to be confirmed by Dawid (R30)).

## Risks and warnings
- `risk=safe`: the item changes nothing in the system; you close the window yourself (DSOptimizer doesn't wait for it and doesn't close it).
- The panel starts through `%SystemRoot%\explorer.exe` and needs a running File Explorer of the same account in the current session. Without it (for example, the app elevated with a different administrator account than the signed-in user), the item ends with an error and starts nothing: an elevated File Explorer would remain as a separate process (measured in the VM).
- Measured on a VM with Polish regional settings; the window title may differ in another culture.
- No account shell in the session (explorer.exe without `/factory`; `/factory` processes alone don't count, because they survive the death of the shell): `Failed` without starting anything. The guard doesn't check the token of the shell it finds (a deliberate limitation). The VM case `panels-devices-printers-no-shell` (PS Direct as Administrator, session 0) checks exactly this: it expects `Failed` with the message "Brak Eksploratora tego konta" (File Explorer for this account isn't running) and no new explorer.exe with `shell:::{A8A91A66` in the command line; the window will be checked by the smoke test with the real DSOptimizer.exe in the dso session – to be confirmed by Dawid (R30).
- The scenario "DSOptimizer elevated with a different administrator account than the signed-in user" wasn't measured on a VM for this panel (the `control.exe`/`explorer.exe` host itself uses associations and COM in the elevated process; DSOptimizer controls only the path of the file it starts).

## VM test
VM run `20261019-110405` (2026-10-19, HEAD `fd320ed`, build 26300.9457): Home (`DSO-26H2-Home`) and Pro (`DSO-26H2-Pro`), `tests/vm/Invoke-VmTests.ps1`. Reports are local, outside git. Whole run: Home 401 passed / 0 failed / 29 skipped, Pro 401 / 0 / 29 (`main` 313/0/29 + Reboot runs); `CleanupError` empty in the C5 cases.
- `panels-devices-printers-no-shell` (`Flow=Execute`, `ExecuteExit=1`, expected `Failed`, `MessageContains` "Brak Eksploratora tego konta"): **Passed** on Home and Pro. Execution result: `OperationFailed` / `Failed`, message "Brak Eksploratora tego konta w bieżącej sesji – nie uruchamiam podniesionego Eksploratora." (in the English program: "File Explorer for this account isn't running in the current session – not starting an elevated File Explorer.") Guard R23 refuses because PS Direct as Administrator (session 0) has no shell of this account. `Check` waits 3 s and requires that there is no `explorer.exe` with `shell:::{A8A91A66` in the command line (nothing started); `Cleanup` closes only such a process. The case **isn't `Skip`**, but it also **doesn't check the window**: it checks only the refusal without a shell.
- K2 exploration (`dso` session, `Limited` token): the "Urządzenia i drukarki — Eksplorator plików" (Devices and Printers — File Explorer) window (`CabinetWClass`), which belongs to the account's `explorer.exe /factory,{…} -Embedding` process. Home = Pro. Research and K2 exploration results: `docs/zrodla/2026-10-19-partia-c5-zrodla.md` (in Polish), sections "Wyniki VM" (VM results) and "Sonda uzupełniająca" (follow-up probe).
- Dawid's manual click (Pro VM, `dso` session, Win+R, 2026-10-19): `shell:::{A8A91A66-…}` opens the classic "Urządzenia i drukarki" (Devices and Printers) window.
- The `control.exe /name Microsoft.DevicesAndPrinters` variant was rejected based on the VM exploration (K1/K2, not Dawid's click): it redirects to Settings (`SystemSettings.exe`). The `control.exe /name Microsoft.System` variant concerns `panels.system-properties` (exploration: no window), not this item.
- `shell:::{A8A91A66-…}` clicked from Win+R goes through the shell, while the item's `launch` goes through CreateProcess `explorer.exe shell:::{GUID}` (behind the guard), so it isn't the same path: the click confirms the panel, while the item's `launch` is confirmed only by the K2 exploration.
- **Smoke test with the real `DSOptimizer.exe` in the `dso` session: to be confirmed by Dawid** (Ruling R30). Not done because Dawid was away; the item goes into `main` with this caveat (like D2 in round C4). A mismatch between "the process is running" (Check) and a window visible to the user will show up only on first use from the app.
- The positive path (a window with the account's shell running) has no case in the runner (the runner is Administrator in session 0); only the smoke test in the `dso` session checks it.

## Reddit
To be added in part E.
