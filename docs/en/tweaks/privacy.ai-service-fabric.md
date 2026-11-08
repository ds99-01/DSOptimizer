<!-- source: docs/tweaks/privacy.ai-service-fabric.md sha256: 351269a53b64516ca4a5d244b09c25c4056ab52da0866d494e738f927fe74f84 -->

# privacy.ai-service-fabric – Windows AI Components Host service set to manual (WSAIFabricSvc)

## Verdict for 26H2
Not ported: negligible gain. After 4 minutes idle the service uses 1.8 MB of private memory with 0.0 s of CPU time, so changing its startup type gives a negligible gain; the change to manual itself wasn't tested (it isn't known whether the service starts on demand anyway or whether anything stops working).

## What it changes
The item isn't in the catalog and changes nothing. Entry considered:
- `HKLM\SYSTEM\CurrentControlSet\Services\WSAIFabricSvc`, `Start`, DWord, `3` (manual start).

## Sources
- Test on 26H2 Home and Pro VMs, 2026-11-07 (build 26300.9457, Windows not activated, local account, VMs without an NPU): reading the service state and its process's resource use only, without changing the value.
- No MS documentation for this change.
- Win11Debloat: https://github.com/Raphire/Win11Debloat (branch master), file `Regfiles/Disable_AI_Service_Auto_Start.reg`, lines 2–3 (UTF-16 file; line numbers after conversion to UTF-8, without blank lines); accessed 2026-11-06.
- WinUtil: https://github.com/ChrisTitusTech/winutil (branch main), file `config/tweaks.json`, lines 953–1008; accessed 2026-11-06.

## Defaults on a fresh 26H2
Reading from the `DSO-EN-Home` and `DSO-EN-Pro` VMs, 2026-11-07: the `WSAIFabricSvc` service ("Windows AI Components Host") has an automatic startup type (Auto) and is running (Running) in its own `svchost.exe -k WSAIFabricSvcGroup -p` process.

## Detection and undo
Not applicable (the item isn't in the catalog).

## Risks and warnings
Not applicable. The item didn't make it into the catalog.

## VM test
Not applicable (not ported): the item isn't in the catalog, so there is no VM case.

## Observed effect
- **Home** (`DSO-EN-Home`), 2026-11-07. Method: reading only the service state and its process's memory and CPU time after 4 minutes idle, without any write; then the VM went back to its checkpoint. Result: the service is Auto, Running; **1.8 MB** of private memory (working set 9.4 MB), **0.0 s** of CPU time (the memory and CPU figures were recorded once for both VMs, not split into Home and Pro).
- **Pro** (`DSO-EN-Pro`), 2026-11-07. Same method and same result.
