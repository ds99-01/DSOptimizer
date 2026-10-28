<!-- source: docs/tweaks/privacy.recall.md sha256: e4fdb900b973ab357bfedb0822edc9edf265395ccb1cf9854b5b324bcf15843c -->

# privacy.recall – Recall: AllowRecallEnablement and DisableAIDataAnalysis (snapshots)

## Verdict for 26H2
Not ported: not proven (VM without an NPU, feature unavailable). This applies to both values: `AllowRecallEnablement` and `DisableAIDataAnalysis` (snapshots). This isn't evidence that there is no effect on hardware with an NPU, only that it couldn't be checked on our VMs.

## What it changes
The item isn't in the catalog and changes nothing. Entries considered:
- `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI`, `AllowRecallEnablement`, DWord, `0`.
- `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI` and `HKCU\Software\Policies\Microsoft\Windows\WindowsAI` (on the VM `HKU\<SID>\...`), `DisableAIDataAnalysis`, DWord, `1` (turns off saving snapshots).

If the item ever goes in (after a test on hardware with an NPU): `suggested=false` and the warning "deletes saved snapshots".

## Sources
- Test on 26H2 Home and Pro VMs, 2026-10-21 (build 26300.9457).
- Research: `docs/zrodla/2026-10-20-etap4a-zrodla.md` (in Polish), sections "2. Recall i Click to Do" (Recall and Click to Do) and "Wyniki VM 26H2 (partia 1)" (26H2 VM results, batch 1).

## Defaults on a fresh 26H2
Reading from the VMs, 2026-10-21: Windows feature `Recall` = `DisabledWithPayloadRemoved` (Home and Pro); `WindowsAI` policy values none.

## Detection and undo
Not applicable (the item isn't in the catalog).

## Risks and warnings
Not applicable. The item didn't make it into the catalog.

## VM test
Not applicable (not ported): the item isn't in the catalog, so there is no VM case.

## Observed effect
- **Home** (`DSO-26H2-Home`), 2026-10-21. Method: reading the state of the `Recall` feature, the Settings pages, writing the policies through the registry API and restarting the VM. Result: **no effect to show**, the `Recall` feature = `DisabledWithPayloadRemoved` before the change and after restarting the VM, and Settings has no Recall page (VM without an NPU).
- **Pro** (`DSO-26H2-Pro`), 2026-10-21. Same method and same result. Snapshots (`DisableAIDataAnalysis`): also no effect to show, because the feature is unavailable.

## Reddit
To be added in part E.
