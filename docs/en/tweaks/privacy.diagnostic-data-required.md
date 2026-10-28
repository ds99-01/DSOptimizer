<!-- source: docs/tweaks/privacy.diagnostic-data-required.md sha256: 7570ee277e8666d06ef3f63a8cec623229155cfa1ecc048ab83b3d07a9ee326e -->

# privacy.diagnostic-data-required – Limit diagnostic data to required data

## Verdict for 26H2
Port: a test on a 26H2 VM (Home and Pro) showed a real effect (a banner saying the settings are managed by your organization, the optional data toggle grayed out). The description says "limit to required diagnostic data", not "turn off telemetry".

## What it changes
- `HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection`, value `AllowTelemetry`, **DWord**, set to `1`; `scope=machine`.
- The `DataCollection` key **exists on a fresh installation** (it has no `AllowTelemetry` value). Undo deletes only the value, the key stays (VM case: `KeyPresentAfterUndo`).
- Level 1 = required data. The item doesn't turn off sending diagnostic data.
- `AllowTelemetry=0` **is not ported**: according to Microsoft, the "Security" level is available only in Windows Enterprise/Education, and the VMs don't have these editions, so there is no evidence (Stage 4a evidence rule). On Home/Pro Microsoft treats 0 the same as 1.
- The user mechanism (the optional diagnostic data toggle: `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection`, `AllowTelemetry` and `MaxTelemetryAllowed`, 1/1 by default) isn't changed; the item uses the policy.
- The policy locks the optional data toggle in Settings (the `ConfigureTelemetryOptInSettingsUx` and `DisableDiagnosticDataViewer` policies aren't ported).

## Sources
- Microsoft documentation: the Diagnostic Data page (data levels), the `System/AllowTelemetry` policy (Windows 10 1507+; Pro, Enterprise, Education), "Manage connections from Windows operating system components to Microsoft services" (registry: `HKLM\Software\Policies\Microsoft\Windows\DataCollection\AllowTelemetry`).
- Test on 26H2 Home and Pro VMs, 2026-10-21 (build 26300.9457, Windows not activated, local account): write through the registry API (.NET `RegistryKey`) and observation in Settings (UI Automation, screenshots).
- Research: `docs/zrodla/2026-10-20-etap4a-zrodla.md` (in Polish), sections "2a. Allow Diagnostic Data (AllowTelemetry)" and "Wyniki VM 26H2 (partia 1)" (26H2 VM results, batch 1).

## Defaults on a fresh 26H2
Reading from the DSO-26H2-Home and DSO-26H2-Pro VMs, 2026-10-21: `HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection` **exists**, `AllowTelemetry` **no value**; the user toggle `...\CurrentVersion\Policies\DataCollection` `AllowTelemetry`/`MaxTelemetryAllowed` = 1/1 (optional data off).

## Detection and undo
- Detection: `AllowTelemetry` = DWORD 1 -> `Applied`; missing value or a different value -> `NotApplied`.
- Undo restores the snapshot from before the change from the journal (a missing value = deleting the value, the `DataCollection` key stays); `original` = `<RemoveEntry>` is only a fallback.

## Risks and warnings
- `risk=caution`, `suggested=true`.
- Catalog warnings: "This is a limit, not a shutoff – Microsoft still receives required data." and "Settings will show a message that some settings are managed by your organization."
- Beyond the notes in the program (in the technical details or only here): the optional data toggle in Settings is grayed out until the change is undone.
- No restart (no restart is required, `requiresReboot=false`); File Explorer doesn't need to be restarted.

## VM test
Case `privacy-diagnostic-data-required` (`Flow=Full`) in `tests/vm/cases.psd1`: `KeyPresentAfterUndo` for `HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection` (the key exists on a fresh installation, the value doesn't exist after undo). VM run on 2026-10-21 (`Invoke-VmTests.ps1`, Task 9 of the batch): **DSO-26H2-Home and DSO-26H2-Pro – passed**, steps detect `NotApplied` → apply `Applied` → detect `Applied` → undo `Undone` → detect `NotApplied`, state after apply confirmed by an independent reading, final state = state before (`SnapshotDiff` empty). The `HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection` key existed before apply and survived undo (`KeyPresentAfterUndo`), and `AllowTelemetry` again doesn't exist after undo. The exploration of 2026-10-21 passed on Home and Pro.

## Observed effect
- **Home** (`DSO-26H2-Home`, EditionID `Core`), 2026-10-21. Method: UI Automation of Settings (Privacy & security > Diagnostics & feedback) and screenshots after a write through the registry API. Result: the banner "Niektórymi z tych ustawień zarządza Twoja organizacja" (Some of these settings are managed by your organization) and "Twoja organizacja zezwala na wysyłanie tylko wymaganych danych diagnostycznych" (Your organization allows only required diagnostic data to be sent), the optional diagnostic data toggle grayed out. After undo (value deleted) the banner disappears and the toggle is active again.
- **Pro** (`DSO-26H2-Pro`, EditionID `Professional`), 2026-10-21. Same method and same result.

## Reddit
To be added in part E.
