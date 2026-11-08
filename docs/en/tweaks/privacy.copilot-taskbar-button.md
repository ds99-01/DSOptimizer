<!-- source: docs/tweaks/privacy.copilot-taskbar-button.md sha256: ede6232ae5e96fb5f0093e2f8eb3a72ab2824e28e145b796fa7356ff197519e4 -->

# privacy.copilot-taskbar-button – Copilot button on the taskbar (ShowCopilotButton)

## Verdict for 26H2
Not ported: no observed effect on Home and Pro. The 26H2 taskbar has no separate Copilot button, so it wasn't proven that the value changes anything; the pinned Copilot app is a different thing (removing it is in the Apps category).

## What it changes
The item isn't in the catalog and changes nothing. Entry considered:
- `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced` (on the VM `HKU\<SID>\...`), `ShowCopilotButton`, DWord, `0`.

## Sources
- Test on 26H2 Home and Pro VMs, 2026-11-07 (build 26300.9457, Windows not activated, local account, no network): write through the registry API (.NET) and observation of the taskbar (UI Automation, screenshots), also after restarting Explorer.
- No MS documentation for this value, evidence: VM test (observation below).
- Win11Debloat: https://github.com/Raphire/Win11Debloat (branch master), file `Regfiles/Disable_Copilot.reg`, lines 2–4 (UTF-16 file; line numbers after conversion to UTF-8, without blank lines); accessed 2026-11-06.
- RemoveWindowsAI: https://github.com/zoicware/RemoveWindowsAI (branch main), file `RemoveWindowsAi.ps1`, line 945; accessed 2026-11-06.

## Defaults on a fresh 26H2
Reading from the `DSO-EN-Home` and `DSO-EN-Pro` VMs, 2026-11-07: the account's `Explorer\Advanced` key exists, the `ShowCopilotButton` value: **none**. On Home the "Copilot" app (`Microsoft.Copilot`) is pinned; on Pro this app isn't there, "Microsoft 365 Copilot" is pinned.

## Detection and undo
Not applicable (the item isn't in the catalog).

## Risks and warnings
Not applicable. The item didn't make it into the catalog.

## VM test
Not applicable (not ported): the item isn't in the catalog, so there is no VM case.

## Observed effect
- **Home** (`DSO-EN-Home`), 2026-11-07. Method: writing the value through the registry API, the taskbar before the write, after the write, after restarting Explorer and after undo (20:52–20:53). Result: **no effect**. There is no separate Copilot button on the taskbar in any phase; the pinned "Copilot" app (in Polish "Copilot — przypięto") stays after the write and after restarting Explorer.
- **Pro** (`DSO-EN-Pro`), 2026-11-07. Same method. Result: **no effect**. There is no separate Copilot button in any phase; the Copilot app isn't there, and the pinned "Microsoft 365 Copilot" is unchanged.
