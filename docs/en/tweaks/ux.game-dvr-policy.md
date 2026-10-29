<!-- source: docs/tweaks/ux.game-dvr-policy.md sha256: 116d9691cdf3040db5913712406515910b24eff257789d6c42c3a1dc23c6c821 -->

# ux.game-dvr-policy – Block game recording and broadcasting for all accounts

## Verdict for 26H2
Port with a warning: the mechanism is documented by MS (Policy CSP / ADMX), but the documentation talks about enforcement on Windows 10 and doesn't list the Home edition, so its effectiveness on 26H2 is unconfirmed.

## What it changes
- `HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR`, value `AllowGameDVR`, **DWord**, set to `0` (Not allowed); `scope=machine`.
- On a fresh 26H2 neither the value nor the `GameDVR` key exists. Apply creates the key; undo deletes the value **and** the empty `GameDVR` key created by apply (mechanism A1 for `registry[]`), and the parent key `...\Policies\Microsoft\Windows` stays.

**Change of meaning compared to v3.2:** `ux_gamedvr` in v3.2 was a per-user switch (HKCU keys). The alias now points to this item, that is, a machine policy for all users. The per-user equivalent (`GameDVR_Enabled`) wasn't ported (see `ux.game-dvr.md`: no MS source).

## Sources
- https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-applicationmanagement – MS, accessed 2026-10-17 (page with updated_at 2026-02-28), section AllowGameDVR: type `int`, 0 = Not allowed, 1 = Allowed (default); GP mapping: `Software\Policies\Microsoft\Windows\GameDVR`, value `AllowGameDVR`, file GameDVR.admx; editions: Pro, Enterprise, Education, IoT Enterprise (Home not listed); MS note "The policy is only enforced in Windows 10 for desktop".
- Reading from 26H2 Home and Pro VMs, 2026-10-18 (default state: key and value missing).
- Research: `docs/zrodla/2026-10-17-partia-c1-zrodla.md` (in Polish), section 2.

## Defaults on a fresh 26H2
Reading from the DSO-26H2-Home and DSO-26H2-Pro VMs (build 26300.9457), 2026-10-18:
- `HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR\AllowGameDVR`: **none** – not even the `GameDVR` key exists (Home and Pro). Missing value = "Not Configured" = allowed (CSP: default value 1).

## Detection and undo
- Detection: `AllowGameDVR` = 0 -> `Applied`; missing value or another value -> `NotApplied`. We don't infer "off" from a missing key.
- Undo: deletes the value (`original` = `<RemoveEntry>`), doesn't set 1; it also deletes the empty `GameDVR` key created by apply.

## Risks and warnings
- `risk=caution`: a machine policy, applies to all users, requires admin.
- Warnings in the catalog: "Microsoft documents this policy for Windows 10 Pro and higher; it may not work on Windows 11 or on the Home edition." and "Settings may show a message that some settings are managed by your organization."
- **Not tested** whether the policy is enforced on 26H2 Home/Pro (the VM only read the default state); the VM test checks the write, detection and a clean undo, not a change in system behavior.
- It turns off recording and broadcasting; it doesn't remove Game Bar.
- The gain in games wasn't measured (since 2026-10-26 the description in the program doesn't mention it; previously: "The gain in games wasn't measured").

## VM test
VM run of 2026-10-18 (build 26300.9457), DSO-26H2-Home (Core) and DSO-26H2-Pro (Professional), both **Passed**. Local reports: `tests/vm/out/20261018-080435/<VM>/<RunLabel>/report.json` (folder in `.gitignore`); in the `main` run Pester passed 140, 0 failed, 10 skipped (Home and Pro identical).

Two `Flow=Full` cases (`main` run), Home and Pro, in both: `detect-before` = `NotApplied`, `apply` code 0, `detect-after-apply` = `Applied`, `undo` code 0, `detect-after-undo` = `NotApplied`, `SnapshotDiff` empty.
- `ux-game-dvr-policy` (no `AllowGameDVR` before apply, `SnapshotBefore` `Exists=false`): the case's expectation (Pester green) = after undo the key `...\Policies\Microsoft\Windows\GameDVR` doesn't exist, and `...\Policies\Microsoft\Windows` stays; after apply `AllowGameDVR` = DWORD 0.
- `ux-game-dvr-policy-prev-value` (`AllowGameDVR` = DWORD 1 before apply, `SnapshotBefore` `Exists=true`): undo restores 1 from the snapshot (`SnapshotDiff` empty) instead of deleting the value; the `GameDVR` and `Windows` keys stay.
- **Not checked:** whether the policy is enforced on 26H2 Home/Pro (Game Bar, recording, the "managed by your organization" message); the VM checks only the write, detection and a clean undo.

## Reddit
To be added in part E.
