<!-- source: docs/tweaks/privacy.diagtrack.md sha256: c5acb62ab25f892c7af04a7fc7357470b1e7ed6c60f4beb49246619b678cd602 -->

# privacy.diagtrack – Turn off the DiagTrack telemetry service

## Verdict for 26H2
Port as `risk=caution`, `suggested=true` (ruling R14): a test on a 26H2 VM (Home and Pro) showed that the service is stopped and doesn't start after a restart, and that a Windows Update search after a restart with the service disabled works. No Microsoft documentation for disabling this service; evidence: VM test. The item doesn't promise that Windows stops sending diagnostic data.

## What it changes
- The `DiagTrack` service (`services[]`): `startType=Disabled`, `stop=true`; `scope=machine`. No registry entries and no script (service API in C#).
- Detection is based on the **startup type**, not on whether the service is running. Undo restores the startup type from the snapshot even when the service was in the `StartPending` state at capture time (Review Focus 5; unit test on fakes).
- The item doesn't change the `AllowTelemetry` policy (separate item `privacy.diagnostic-data-required`).

## Sources
- Microsoft's page on diagnostic data calls the component "Connected User Experiences and Telemetry", and as management methods lists only policies (`System/AllowTelemetry`, `LimitDumpCollection`, `LimitDiagnosticLogCollection`) and Group Policy; stopping or disabling the `DiagTrack` service isn't described there (**no Microsoft documentation**).
- Test on 26H2 Home and Pro VMs, 2026-10-21 (build 26300.9457, Windows not activated, local account): change through the service API, VM restart, search through the Windows Update Agent.
- Research: `docs/zrodla/2026-10-20-etap4a-zrodla.md` (in Polish), sections "2e. Usługa DiagTrack" (DiagTrack service) and "Wyniki VM 26H2 (partia 1)" (26H2 VM results, batch 1).

## Defaults on a fresh 26H2
Reading from the DSO-26H2-Home and DSO-26H2-Pro VMs, 2026-10-21: `DiagTrack` startup type **Automatic**, state **Running**; `original` in the catalog = Automatic.

## Detection and undo
- Detection: startup type `Disabled` -> `Applied`; any other startup type -> `NotApplied`.
- Undo restores the snapshot from before the change from the journal (the startup type and, if the service was running, starts it); `original` = Automatic is only a fallback.

## Risks and warnings
- `risk=caution` (R14), `suggested=true`.
- Catalog warnings: "Microsoft doesn't document turning off this service, and Windows Update also uses it – if you have trouble with updates, undo the change." and "It doesn't guarantee that Windows stops sending diagnostic data."
- Beyond the notes in the program (in the technical details or only here): on the VM (Home and Pro) a Windows Update search after a restart with the service disabled worked (WUA `Search`: ResultCode=2, 4 updates, no error). This is one observation, not a guarantee.
- Known engine limitation (deferred): it stops the service only when it is `Running`; a service that is still starting at the moment of apply may run until the restart. The startup type is `Disabled` in both cases.
- A computer restart isn't required (effect right away, persistent after a restart).
- The performance gain wasn't measured (previously in the program: "The performance gain wasn't measured"; since 2026-10-26 the description says only what changes).

## VM test
Case `privacy-diagtrack` (`Flow=Full`) in `tests/vm/cases.psd1`: a non-empty `Snapshot` of the `DiagTrack` service (state before = state after undo), after apply `Disabled` + `Stopped`, after undo `Automatic` + `Running`. VM run on 2026-10-21 (`Invoke-VmTests.ps1`, Task 9 of the batch): **DSO-26H2-Home and DSO-26H2-Pro – passed**, steps detect `NotApplied` → apply `Applied` → detect `Applied` → undo `Undone` → detect `NotApplied`, state after apply confirmed by an independent reading, final state = state before (`SnapshotDiff` empty). After apply the service is `Disabled` + `Stopped` (`Start`=4), after undo the startup type from the snapshot: `Auto` + `Running` (`Start`=2), as before apply. The exploration of 2026-10-21 passed on Home and Pro.

## Observed effect
- **Home** (`DSO-26H2-Home`, EditionID `Core`), 2026-10-21. Method: service state (startup type, running) after the change and after a VM restart; Windows Update Agent `Search("IsInstalled=0 and Type='Software'")` after the restart. Result: the service is stopped and doesn't start after the restart; search: ResultCode=2 (success), 4 updates, no error. After undo: Automatic and Running.
- **Pro** (`DSO-26H2-Pro`, EditionID `Professional`), 2026-10-21. Same method and same result.

## Reddit
To be added in part E.
