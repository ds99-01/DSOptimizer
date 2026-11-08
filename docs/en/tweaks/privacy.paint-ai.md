<!-- source: docs/tweaks/privacy.paint-ai.md sha256: 49b18e04964c412a42c97df2da314d7f2289204edb6087ae44d2f7763c9e4689 -->

# privacy.paint-ai – AI features in Paint (DisableCocreator, DisableGenerativeFill, DisableImageCreator)

## Verdict for 26H2
Not ported: not observable on the VM (Paint's AI features need an app update). The Paint version in the image doesn't offer AI features without an update, so it wasn't proven that the policies turn them off; a newer Paint version wasn't checked.

## What it changes
The item isn't in the catalog and changes nothing. Entries considered:
- `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Paint`, `DisableCocreator`, DWord, `1`.
- `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Paint`, `DisableGenerativeFill`, DWord, `1`.
- `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Paint`, `DisableImageCreator`, DWord, `1`.

## Sources
- Test on 26H2 Home and Pro VMs, 2026-11-07 (build 26300.9457, Windows not activated, local account, with network): write through the registry API (.NET) and observation in Paint (UI Automation, screenshots).
- Microsoft Learn, Policy CSP WindowsAI: https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-windowsai (ms.date 2026-10-14, accessed 2026-11-07).
- Win11Debloat: https://github.com/Raphire/Win11Debloat (branch master), file `Regfiles/Disable_Paint_AI_Features.reg`, lines 2–4 (UTF-16 file; line numbers after conversion to UTF-8, without blank lines); accessed 2026-11-06.
- RemoveWindowsAI: https://github.com/zoicware/RemoveWindowsAI (branch main), file `RemoveWindowsAi.ps1`, lines 1134–1138; accessed 2026-11-06.

## Defaults on a fresh 26H2
Reading from the `DSO-EN-Home` and `DSO-EN-Pro` VMs, 2026-11-07: the `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Paint` key and all three values: **none**. Paint 11.2601.440.0.

## Detection and undo
Not applicable (the item isn't in the catalog).

## Risks and warnings
Not applicable. The item didn't make it into the catalog.

## VM test
Not applicable (not ported): the item isn't in the catalog, so there is no VM case.

## Observed effect
- **Home** (`DSO-EN-Home`), 2026-11-07. Method: writing the three values through the registry API; Paint 11.2601.440.0 started again before the write, after the write and after undo (20:49–20:50) and closed after reading. Result: **not observable**. In every phase Paint shows "Download updates to use AI features – You need the latest updates to use new AI features.", and the "Copilot" button is there before the write, after the write and after undo. The only difference: the "Sign in" button disappears after the write and comes back after undo, which doesn't prove the AI features are off. Paint started and was running after 10 s in every phase.
- **Pro** (`DSO-EN-Pro`), 2026-11-07. Same method and same result.
