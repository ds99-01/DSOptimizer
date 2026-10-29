<!-- source: docs/tweaks/privacy.click-to-do.md sha256: 37506514cc1ceb742f2090f82e65c7fea96bcdb0506d195ed4adfe607b27e58f -->

# privacy.click-to-do – DisableClickToDo policy

## Verdict for 26H2
Not ported: not proven (VM without an NPU, feature unavailable). This isn't evidence that there is no effect on hardware with an NPU, only that it couldn't be checked on our VMs.

## What it changes
The item isn't in the catalog and changes nothing. Entry considered: `DisableClickToDo`, DWord, `1`, in `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI` and `HKCU\Software\Policies\Microsoft\Windows\WindowsAI` (on the VM `HKU\<SID>\...`).

## Sources
- Test on 26H2 Home and Pro VMs, 2026-10-21 (build 26300.9457).
- Research: `docs/zrodla/2026-10-20-etap4a-zrodla.md` (in Polish), sections "2. Recall i Click to Do" (Recall and Click to Do) and "Wyniki VM 26H2 (partia 1)" (26H2 VM results, round 1).

## Defaults on a fresh 26H2
Reading from the VMs, 2026-10-21: `WindowsAI` policy values: none (Home and Pro). Click to Do has no entry point, process or page in Settings.

## Detection and undo
Not applicable (the item isn't in the catalog).

## Risks and warnings
Not applicable. The item didn't make it into the catalog.

## VM test
Not applicable (not ported): the item isn't in the catalog, so there is no VM case.

## Observed effect
- **Home** (`DSO-26H2-Home`), 2026-10-21. Method: looking for an entry point, a process and a page in Settings before and after writing the policy and restarting File Explorer. Result: **no effect to show**, the feature is unavailable even before the change (VM without an NPU).
- **Pro** (`DSO-26H2-Pro`), 2026-10-21. Same method and same result.

## Reddit
To be added in part E.
