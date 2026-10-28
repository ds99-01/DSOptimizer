<!-- source: docs/tweaks/perf.spectre-mitigations.md sha256: 7d95ed29fe68d8554611c6c41f51ff3a42bbb6f7d78f3ba7e7dfdfe7d27ab6ab -->

# perf.spectre-mitigations – turning off Spectre/Meltdown mitigations (not ported)

## Verdict for 26H2
Not ported: the MS client page (KB4073119) ends at Windows 11 23H2, turning off the mitigations lowers protection on someone else's computer with no measured benefit, and detection sees only the registry, not the real state of the mitigations. Dawid's decision of 2026-10-18. The `cpu_spectre` alias from v3.2 has no item in the catalog.

## What it changes
The item isn't in the catalog and changes nothing. The entry under consideration (v3.2 and MS): `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management`, values `FeatureSettingsOverride` = 3 and `FeatureSettingsOverrideMask` = 3 (both DWord), restart required.

Reasons for rejection:
- The MS client page "Windows client guidance…" (KB4073119) covers Windows 11 up to 23H2 under "Applies to"; the last change-log entry is 2024-04-16; 24H2, 25H2 and 26H2 aren't listed.
- Turning them off lowers protection (Spectre variant 2, Meltdown and related). MS warns that Hyper-V hosts, Remote Desktop Services hosts and systems that run untrusted code are at increased risk. The tool runs on other people's computers, so lowering security with no measured benefit goes against its purpose.
- No measured or documented benefit on a modern processor; MS doesn't say how much turning them off gains. A result from a VM wouldn't be evidence for real hardware (the processor is virtualized).
- Registry detection doesn't see the real state of the mitigations (that's what the `SpeculationControl` module from the PowerShell Gallery does, and it needs a network connection).
- The Intel/AMD variants from the spec don't exist for turning them off: MS gives one pair 3/3 (or 1/3 for variant 2 only); vendor-specific variants are about turning on additional mitigations.
- The v3.2 description ("restores full branch prediction speed") and the warnings about corporate hypervisors have no MS source.

## Sources
- https://support.microsoft.com/help/4073119 – MS (KB4073119, Windows client guidance for IT Pros), accessed 2026-10-18: disabling values 3/3 and 1/3, restart, warnings, Applies-to up to Windows 11 23H2, change-log up to 2024-04-16.
- https://support.microsoft.com/en-us/topic/windows-server-guidance-to-protect-against-speculative-execution-side-channel-vulnerabilities-2f965763-00e2-8f98-b632-0d96f30c8c8e – MS (server version), accessed 2026-10-18: "To disable…" sections, warnings for Hyper-V, RDS and untrusted code.
- KB4074629 (output of `Get-SpeculationControlSettings`) – referenced by the MS page, not downloaded separately.
- Research: `docs/zrodla/2026-10-18-partia-c2-zrodla.md` (in Polish), section 3. The values 3/3 and 1/3 come from downloads made with a summarizing tool; before any port, they must be compared by hand with the original KB4073119.

## Defaults on a fresh 26H2
Reading from the DSO-26H2-Home and DSO-26H2-Pro VMs (build 26300.9457), 2026-10-18 (Home = Pro, read-only):
- `FeatureSettingsOverride`: **none** (the `Memory Management` key exists).
- `FeatureSettingsOverrideMask`: **none**.
- `FeatureSettings` (a related value, not from the KB): DWORD 0; its meaning isn't in the MS sources, so it isn't interpreted.

## Detection and undo
Not applicable, the item isn't ported. If it were reconsidered: two `registry[]` entries (both == 3 -> `Applied`), `original` = `<RemoveEntry>` (on a fresh 26H2 both values are missing). Open: whether deleting the values gives the same state as an explicit 0/3 (MS describes "restore defaults" as 0/3) and whether the system overwrites the values after a restart.

## Risks and warnings
Reasons in the "What it changes" section. If the item ever went in, it would need `risk=experimental`, a warning about the loss of protection and about multi-user hosts and untrusted code (sentences from MS), with no performance promises.

## VM test
Not applicable (the item isn't ported). Only the defaults were read, see above.

## Reddit
To be added in part E.
