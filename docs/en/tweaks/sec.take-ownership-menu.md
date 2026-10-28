<!-- source: docs/tweaks/sec.take-ownership-menu.md sha256: 8cf2cce762b8f8e0705662a22ba23fa5a59ce19b60d45006835430b2419ea3b1 -->

# sec.take-ownership-menu – Add a take ownership entry to the context menu

## Verdict for 26H2
Port as `caution`: the mechanism (static verbs in `HKLM\SOFTWARE\Classes`, a submenu with `ExtendedSubCommandsKey`, `Extended`, `AppliesTo`) is described by MS, and **the specific structure that works on 26H2 was settled by the VMs and Dawid's manual test with UAC** (below). The item only adds menu entries; it doesn't take ownership of anything itself. The v3.2 aliases `sec_take_ownership` (add) and `sec_take_ownership_remove` (remove) both map to this one item (apply = add, undo = remove). Everything in `registry[]` (C# API), no script, no `reg.exe`, no top-level `runas` key.

## What it changes
Under each of the two shell classes: `*` (files) and `Directory` (folders). All REG_SZ, paths in `HKLM\SOFTWARE\Classes\<Klasa>\shell\DSOptimizer.TakeOwnership` (not `HKCR\`, because `HKCR` is the merged view of `HKLM\Software\Classes` and `HKCU\Software\Classes`, and the tweak has `scope=machine`):

| Key | Value | Data |
|---|---|---|
| `...\DSOptimizer.TakeOwnership` | `MUIVerb` | `Przejmij na własność` |
| `...\DSOptimizer.TakeOwnership` | `Extended` | empty REG_SZ (entry only with Shift) |
| `...\DSOptimizer.TakeOwnership` (only `Directory`) | `AppliesTo` | `NOT (System.ItemPathDisplay:="C:\Windows" OR System.ItemPathDisplay:~<"C:\Windows\" OR System.ItemPathDisplay:="C:\")` |
| `...\DSOptimizer.TakeOwnership` | `ExtendedSubCommandsKey` | `<Klasa>\shell\DSOptimizer.TakeOwnership\ExtendedSubCommandsKey` (path relative to HKCR, for example `*\shell\DSOptimizer.TakeOwnership\ExtendedSubCommandsKey`) |
| `...\ExtendedSubCommandsKey\Shell\runas` | `MUIVerb` | `Przejmij na własność (administrator)` |
| `...\ExtendedSubCommandsKey\Shell\runas` | `HasLUAShield` | empty REG_SZ (UAC shield, visual only) |
| `...\ExtendedSubCommandsKey\Shell\runas\command` | (default) | file (`*`): `cmd.exe /c takeown /f "%1" /a && icacls "%1" /grant *S-1-5-32-544:F /c /l /q` |
| | | folder (`Directory`): `cmd.exe /c takeown /f "%1" /a /r /d Y && icacls "%1" /grant *S-1-5-32-544:F /t /c /l /q` |

- The Administrators group always by SID `S-1-5-32-544` (`*S-1-5-32-544`, the asterisk marks a SID in `icacls`), never by name (Polish Windows uses a translated group name).
- `runas` appears **only** in the submenu of our own key; the top-level `shell\runas` is neither created nor touched.
- `AppliesTo` is **only on `Directory`**. It isn't on `*`, because excluding files under `C:\Windows` wasn't tested on the VM (see the warnings).
- The labels (`MUIVerb`) are in Polish, written through the registry API (Unicode), without `reg.exe` and without argv.
- What the command does: `takeown /a` gives ownership to the Administrators group (not the current user), `icacls /grant *S-1-5-32-544:F` adds full control for that group; folders: `/r /d Y` and `/t` cover the whole tree, `/c` continues after errors, `/l` works on the link rather than its target, `/q` suppresses messages.

## Sources
- MS Learn, "Creating Shortcut Menu Handlers" (ms.date 2018-05-31, page updated 2025-07-03; accessed 2026-10-18): https://learn.microsoft.com/en-us/windows/win32/shell/context-menu-handlers
  - static verb `<ProgID>\shell\<verb>` with a `command` subkey; `Extended` (REG_SZ without data, verb only with Shift); `MUIVerb`; `HasLUAShield`; `AppliesTo` (AQS, only "fast properties"); cascading menus from the registry.
  - **Inconsistency in the MS source:** step 3 of the cascading menu description says "add an `ExtendedSubCommandsKey` subkey" (the subkey alone), while the introduction talks about reusing verb sets by "specifying the same `ExtendedSubCommandsKey`" (a value pointing to a path). **The VM settled it** (manual test, below): the `ExtendedSubCommandsKey` value **plus** the subkey works, and the subkey alone (literal step 3) doesn't create a submenu.
  - MS doesn't claim that the `runas` verb in a submenu elevates privileges; that's the result of the VM test.
- MS Learn, `ShellExecuteW`, verb `runas`: "Launches an application as Administrator. User Account Control (UAC) will prompt the user for consent": https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shellexecutew
- MS Learn, "Merged View of HKEY_CLASSES_ROOT": https://learn.microsoft.com/en-us/windows/win32/sysinfo/merged-view-of-hkey-classes-root
- MS Learn, `takeown` (ms.date 2025-11-04): `/a` – ownership for the Administrators group; `/r`; `/d {Y|N}` only with `/r`: "Y – … The directory permissions are replaced with permission granting the user full control only": https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/takeown
- MS Learn, `icacls` (ms.date 2025-06-06): numeric SID with an asterisk; `/grant`, `/t`, `/c`, `/l`, `/q`: https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/icacls
- MS Learn, Advanced Query Syntax and `System.ItemPathDisplay` (for `AppliesTo`): https://learn.microsoft.com/en-us/windows/win32/search/-search-3x-advancedquerysyntax, https://learn.microsoft.com/en-us/windows/win32/properties/props-system-itempathdisplay
- Batch C4 research: `docs/zrodla/2026-10-18-partia-c4-zrodla.md` (in Polish), section 3, "Wyniki VM" (VM results) and "Test ręczny UAC (Dawid, 2026-10-18/19)" (manual UAC test). Raw results: outside git.

### VM results (C4 exploration, VMs `DSO-26H2-Home` and `DSO-26H2-Pro`, build 26300.9457, pl-PL)
- **(3) collisions (2026-10-18):** in `HKLM\SOFTWARE\Classes\{*,Directory,Drive,Folder,AllFileSystemObjects}\shell` there's no `runas` or `DSOptimizer.TakeOwnership` (Home and Pro). On `*` there are only `pintohomefile`, `removeproperties`, `UpdateEncryptionSettingsWork`; on `Directory`: `cmd`, `find`, `Powershell`, `UpdateEncryptionSettings`.
- **(4a) visibility (run 2, 2026-10-18, `Shell.Application` `Verbs()`):** `Extended` hides the entry in `Verbs()` (it behaves like the menu without Shift), and `Verbs()` doesn't expand cascading menus; the child entry with `ExtendedSubCommandsKey` never appears in the list. **That's why the visibility and operation of the submenu can't be checked through `Verbs()`, and a manual test in Explorer settled it.** The root `C:\`: registration under `*` and `Directory` does **not** give an entry on `C:\` (it appears only with `Drive` or `Folder`).
- **(4b) `AppliesTo` (run 2, 2026-10-18, `Directory` only):** the positive control (`System.ItemName:="folder testowy"`) works. Variants A and C (`NOT ...:="C:\Windows"`) exclude only `C:\Windows` itself. **Variant B** (the one in the table above) excluded `C:\Windows`, `System32`, `C:\Windows\Temp` and `C:\`, and kept the test folder. Limitations: the path `C:\Windows` is fixed, text comparison; the result applies to folders (`Directory`), files under `C:\Windows` weren't filtered by AQS.
- **(5) recursion (C4 exploration, 2026-10-18):** `takeown /f <folder> /a /r /d Y` on Polish 26H2: exit 0, no prompt, covered the folder, a file, "podfolder bez list" (subfolder without list permission, `Deny ListDirectory`) and the file in it (Home and Pro). `icacls /setowner ... /t /c /l` left the file in the folder without List Folder with its previous owner (exit 0, but "Odmowa dostępu" (Access is denied)), so `takeown` was chosen for recursion.
- **(6) special names (run 2, 2026-10-18):** spaces, Polish letters, `&`, `^`, `!`: exit 0, owner `S-1-5-32-544` and explicit full control for Administrators on the target (file, or folder and its child), with nothing else changed. Names with `%` (also mixed): **exit 1, nothing changes** (`takeown`: "File or Directory not found"; mechanism: `cmd.exe` expands `%...%` even inside quotes – an inference, not a reading).
- **Manual UAC test (Dawid, VM Pro, 2026-10-18/19; results in `c4-manual-uac-{prepare,finish}.json`):**
  - Variant **subkey alone** `ExtendedSubCommandsKey\Shell\runas` (literally MS step 3): Explorer showed the entry **without a submenu arrow**; clicking it gave the message "Z tym plikiem nie jest skojarzona aplikacja…" (no app is associated with this file…); nothing changed (owner still `dso`). **Not usable.**
  - Variant **`ExtendedSubCommandsKey` value + subkey** with `Extended`, **without `AppliesTo`** and with "DSO: …" labels (test scope: only this combination, registered under `*` and `Directory`): submenu arrow visible, after choosing the entry a UAC prompt, after approval the owner is `S-1-5-32-544` on the file, the folder, the file in the folder, the "podfolder bez list" and the file in it. Explicit `(A;;FA;;;BA)` on the file, the folder and the file in the folder; the "podfolder bez list" has the ACL `PAI(A;OI;FA;;;BA)(A;CI;FA;;;BA)` (see R11), and the file in it `(A;;FA;;;BA)(A;ID;FA;;;BA)`. **This structure is the basis for the catalog entry.**
  - **Scope of confirmations:** `AppliesTo` (variant B) was checked with probe (4b) in `Verbs()`, on the Plain and Subkey structures, without `Extended`. The exact combination that ships (value + subkey + `Extended` + `AppliesTo` on `Directory` + Polish labels) was confirmed with one end-to-end click on the real catalog item (R14, 2026-10-19). **R14 confirmed by Dawid's click (VM Pro, session `dso`, after `--apply sec.take-ownership-menu` = `Applied`, 2026-10-19):** A1 file in `C:\r14` → submenu and UAC prompt: yes; A2 folder: yes; A3 `C:\Windows` and `C:\Windows\System32` (folders): no entry, as the `AppliesTo` exclusion intends; A4 file in `C:\Windows`: the entry is there (known limitation R12: the exclusion applies only to folders). Consistent with the description in this document.

## Defaults on a fresh 26H2
Read from the Home and Pro VMs (2026-10-18): none of the keys `...\shell\DSOptimizer.TakeOwnership` (or `...\shell\runas`) exists, so the `original` of each value = `<RemoveEntry>` (none). The keys `HKLM\SOFTWARE\Classes\*\shell` and `...\Directory\shell` exist on both VMs (see (3)).

## Detection and undo
- Detection: all values from `registry[]` equal to the target ones -> `Applied`; none -> `NotApplied`; some -> `Partial`. This is honest, because the item promises only menu entries; whether the menu actually shows and whether the command works isn't detected (it depends on Explorer and UAC).
- Undo from the journal restores the snapshot (no value = deletion). Rule A1 deletes **empty keys created by Apply** (from the deepest: `command`, `runas`, `Shell`, `ExtendedSubCommandsKey`, `DSOptimizer.TakeOwnership`), never below 2 segments under the hive. The keys `*\shell` and `Directory\shell` existed before and stay.
- **Undo doesn't touch other keys:** an existing `*\shell\runas` (or another verb) before apply is the same after undo (engine test on fakes). If someone added a value to our key, the key stays, and undo reports the warning "Key … remained after undo (it isn't empty)".
- Undo **doesn't undo** owner or permission changes made from the menu: it only removes the menu entry.
- The aliases `sec_take_ownership` and `sec_take_ownership_remove` point to the same item; "remove" in v3.2 = undo of this item.

## Risks and warnings
- `risk=caution`. Warnings in the catalog: "Don't use it on system files or in WindowsApps – changing the owner can break Windows updates and apps." and "Undo only removes the menu entry; it doesn't restore changed file owners and permissions."
- Risk details (some in the program's technical details, some only here):
  - Changing the owner of system files can break updates and Windows Resource Protection; the TrustedInstaller state can't be restored easily.
  - Undo removes only the menu and **doesn't undo** owner or permission changes.
  - **R11 – ACL replacement:** on a folder that an administrator can't list, `takeown /r /d Y` **replaces the whole ACL** (VM: after the command, the SDDL of "podfolder bez list" is `O:BAG:S-1-5-21-…-513D:PAI(A;OI;FA;;;BA)(A;CI;FA;;;BA)`: Administrators only, inheritance off; SYSTEM and Users lost access). Taking over such folders, for example `C:\Program Files\WindowsApps`, can break apps.
  - Recursion on a large tree takes a long time.
  - The entry is visible only with Shift in the classic menu ("Show more options") and requires UAC approval.
  - **The path `C:\Windows` is hard-coded** in the exclusion (`AppliesTo`): Windows on a different drive letter isn't excluded. The exclusion works only for folders; **files in `C:\Windows` aren't excluded** (not tested on the VM).
  - **R6 – the `%` character:** names containing `%` end with an error (exit 1) and change nothing, because `cmd.exe` expands `%...%`. Spaces, Polish letters and `& ^ !` work.
  - The drive root (`C:\`) doesn't get the entry, because the entries are under `*` and `Directory`, not under `Drive` (VM, (4a)).
- No promises of security or performance.

## VM test
Full VM run `vm-run-20261019-002807` (2026-10-19, about 00:28–00:46, build 26300.9457, HEAD `d589405`), Home and Pro. Local reports outside git (`tests/vm/out/...`).

- `sec-take-ownership-menu` (`Flow=Full`, `Catalog=BuiltIn`, `Snapshot` of all values, `KeyAbsentAfterUndo` for both `DSOptimizer.TakeOwnership` keys, sentinel `KeyPresentAfterUndo` under `*\shell`): **Passed** on Home and Pro. It checks the registry structure (apply, detection `Applied`, undo, end state = before, other `*\shell\runas` untouched), because `Extended` hides the entry in `Verbs()`, and `Verbs()` doesn't expand submenus.
- `sec-take-ownership-menu-effect`: **Skipped** on Home and Pro (reason from `cases.psd1`: requires a click in Explorer's classic menu (Shift) and UAC approval; PowerShell Direct runs as Administrator with a full token without UAC, and `Verbs()` doesn't show entries with `Extended` or submenus); reported as skipped, not passed. Operation from the menu (UAC) was confirmed by Dawid's manual test (above; variant value + subkey + `Extended`, 2026-10-18/19).
- Run summary: Home 309 passed / 0 failed / 21 skipped, Pro 308 / 1 / 21 (the only failure: the old `Check` of `sec-bitlocker-decrypt-c`, fixed in `962ae0e`). Skips from earlier batches: `disk-hibernation`, `net-smb-lan-effect`, `net-network-private-change`.
- **R14 confirmed by Dawid's click (VM Pro, session `dso`, after `--apply sec.take-ownership-menu` = `Applied`, 2026-10-19):** A1 file in `C:\r14` → submenu and UAC prompt: yes; A2 folder: yes; A3 `C:\Windows` and `C:\Windows\System32` (folders): no entry, as the `AppliesTo` exclusion intends; A4 file in `C:\Windows`: the entry is there (known limitation R12: the exclusion applies only to folders). Consistent with the description in this document. Still not confirmed: `takeown /d Y` was checked on Polish 26H2 (pl-PL), not on other languages; names with `%` in the menu and recursion on large trees weren't tested.

## Reddit
To be added in part E.
