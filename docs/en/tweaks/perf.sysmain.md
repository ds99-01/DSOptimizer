<!-- source: docs/tweaks/perf.sysmain.md sha256: e0fb82ae38bf42e2edd00758e716eb3010e70ba2797b0cf15f68bf01d491222a -->

# perf.sysmain – Turn off the SysMain service (Superfetch)

## Verdict for 26H2
Port as experimental: the mechanism (service startup type plus stopping) is ordinary and fully reversible, but Microsoft doesn't give any effect or benefit of turning it off ("No guidance"). The description and warnings don't promise a speed-up; whether the item makes sense is up to the user.

## What it changes
- Service `SysMain` (display name "Superfetch"): startup type `Automatic` -> `Disabled`, and the running service is stopped (`services[]`: `{ name: "SysMain", startType: "Disabled", stop: true, original: "Automatic" }`). `scope=machine`, services API in C# (`ChangeServiceConfig`), without a script and without the tweak writing to the registry.
- No `requiresReboot`: the startup type change and the stop take effect right away (on the VM the stop went through without a restart). The state after a restart wasn't checked.
- The engine stops the service without forcing it. When the stop is refused (dependent services are running or the time ran out), apply fails and the startup type goes back to the previous one (`FailedRolledBack`). On a fresh VM SysMain has no dependent services, so this path is covered only by a unit test on fakes. When the time runs out, the service is usually in the `StopPending` state, so restoring the startup type will succeed, but starting the service again will usually throw, and the result is more likely `FailedRollbackIncomplete` than `FailedRolledBack` (honest, not a success; not checked on a VM).

## Sources
- https://learn.microsoft.com/en-us/windows-server/security/windows-services/security-guidelines-for-disabling-system-services-in-windows-server – MS (Windows Server 2016), accessed 2026-10-18 (updated_at 2026-02-16): "Superfetch" table, service SysMain, description "Maintains and improves system performance over time.", startup type Manual, recommendation "No guidance". This is server documentation; for a 26H2 client MS gives neither the startup type nor the effects.
- https://learn.microsoft.com/en-us/answers/questions/3827250/how-to-disable-sysmain – MS Q&A (community answer from 2021-09-01, not MS documentation), accessed 2026-10-18: only confirms that changing the startup type through the service manager is the usual way. The same thread reports that the value edited in the registry "went back to 3"; not confirmed by MS.
- A search of MS domains (learn, support, techcommunity) didn't return an MS page about SysMain on the client.
- Reading from 26H2 Home and Pro VMs, 2026-10-18 (`docs/zrodla/2026-10-18-partia-c2-zrodla.md` (in Polish), "Wyniki VM" (VM results), section 1).

## Defaults on a fresh 26H2
Reading from the DSO-26H2-Home and DSO-26H2-Pro VMs (build 26300.9457), 2026-10-18 (Home = Pro):
- `Win32_Service`: `StartMode` = **Auto**, `State` = **Running**, `DelayedAutoStart` = **false**; registry `Services\SysMain\Start` = **DWORD 2**, no `DelayedAutoStart` value. The MS server page (Manual) doesn't match the 26H2 client.
- Dependent services: **none**. SysMain depends on `rpcss` and `fileinfo` (both running).
- Cycle done with cmdlets (not the engine): `Set-Service -StartupType Disabled` -> Disabled (`Start` = 4), `Stop-Service` without `-Force` -> Stopped, `Set-Service -StartupType Automatic` and start -> Auto + Running, comparison with the baseline matches.
- Not settled: the state after a restart, a reset of the startup type by Windows Update.

## Detection and undo
- Detection: only the startup type from the services API; `Disabled` -> `Applied`, any other -> `NotApplied`, missing service -> `NotApplicable`. The "running/stopped" state doesn't take part in detection, so `Applied` doesn't prove that the service is stopped (the VM case checks that).
- Undo: snapshot from the journal (existence, startup type, whether it was running). It restores the startup type and starts the service only if it was running before apply; a service that was stopped (Manual) stays stopped. The `original` fallback = `Automatic` (measured on the VM), without delayed start.

## Risks and warnings
- `risk=experimental`.
- Warning in the catalog: "Microsoft doesn't describe the effects of turning off this service, and the benefit isn't documented."
- Beyond the notes in the program (in the technical details or only here): in the MS services table: "No guidance"; the benefit isn't measured; dependent services can block the stop, and then the change is rolled back.
- The v3.2 description promised an improvement on NVMe and lower CPU usage. None of these claims is confirmed by an MS source, so they don't go into the catalog.

## VM test
VM run of 2026-10-18 (13:51, build 26300.9457, HEAD `da78bd7`), DSO-26H2-Home (Core) and DSO-26H2-Pro (Professional), both without failures (212 passed / 0 failed / 10 skipped on each VM; the skipped ones are only `disk-hibernation`). Local reports: `tests/vm/out/20261018-135146/<VM>/<RunLabel>/report.json` (folder in `.gitignore`).

Case `perf-sysmain` (`Flow=Full`, `main` run), Home and Pro identical:
- Checked: `detect-before` = `NotApplied`, `apply` = `Applied` (`requiresReboot` = false), `detect-after-apply` = `Applied`, `undo` = `Undone` (`finalState` = `NotApplied`), `detect-after-undo` = `NotApplied`. `CheckAfterApply` Passed (service `Stopped`, `StartMode` `Disabled`), `CheckAfterUndo` Passed (`State` and `StartMode` as before apply: `Running`/`Auto`). Final state = state before (snapshot `Services\SysMain\Start`).
- **Not checked:** the effect on performance, the state after a restart, the persistence of the setting after Windows Update (an update may restore the service), the path with running dependent services (covered only by a unit test on fakes).

## Reddit
To be added in part E.
