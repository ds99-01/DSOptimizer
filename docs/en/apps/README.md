<!-- source: docs/apps/README.md sha256: 219206ba1dbc4cc3b0fc4700cf81df53a21b18fa2c174de4dce8c377549b3637 -->

# Apps – classification file `apps-bloat.json`

The file `src/DSOptimizer.Catalog/catalog/apps-bloat.json` tells the "Apps" page what is junk and what must not be removed.
It's loaded by `AppsBloatCatalog.Load` (Core) and embedded as `BuiltInCatalog.AppsBloatSource`. It isn't part of `BuiltInCatalog.Sources`
(those are only tweak tables); the test `Sources_MatchCatalogFilesOnDisk` skips it by name, and `AppsBloatSource_IsEmbeddedAndMatchesFileOnDisk`
makes sure the file on disk and the resource are the same.

## Format

```json
{
  "entries":   [ { "id", "match", "group", "suggested", "summary", "warning?", "source", "cleanupUrl?" } ],
  "protected": [ { "id", "match", "reason", "source?" } ]
}
```

- `id` – unique in the whole file (entries and protected together), case-insensitive.
- `match` – matching rule; patterns with `*`, no regex, case-insensitive:
  - `packageName` – only AppX packages (Store), from `Get-AppxPackage`, the `Name` field; doesn't combine with the others;
  - `displayName` and/or `publisher` – only Win32 programs (Uninstall entry); when both are given, both must match.
- `group` – `microsoft`, `producent` or `trial`.
- `suggested` – `true`: the row goes to the "Suggested for removal" group and selects itself; `false`: the "Other" group
  (can be selected by hand), with `warning`, if there is one.
- `summary` – ≤ 160 characters, in Polish, without registry key names and cmdlets.
- `warning` – optional warning shown next to the row.
- `source` – how we know it's junk (MS documentation, VM reading, spec, manufacturer). A suggested entry must have a source
  with an `https://` address, the marker "spec 6.2 (decyzja autora)" (the author's decision) or a VM reading; the test `SuggestedEntries_HaveVerifiableSource` checks this.
- `cleanupUrl` – optional `https` address of the manufacturer's official leftover cleanup tool. The program doesn't download anything from it,
  it only shows it as text. Only an address checked by downloading the page may go in (table below); the test `VerifiedCleanupUrls` makes sure
  there's no other one in the file.
- `reason` (only `protected`) – why the app is protected, shown next to the padlock.
- `source` in `protected` – optional, only for the person reading the file (the loader doesn't read it); the runtime dependency entries have it.

The order of entries in `entries` matters: **the first match** sets the group, so narrow rules go before broad ones.
An error in the file gives `AppsCatalogException` with the entry id and the field name.

## Classification rules (`AppClassifier`)

1. Hidden completely: AppX with `IsFramework`, resource packages, `NonRemovable`, `SignatureKind=System`; Win32 with `SystemComponent=1`,
   with `ParentKeyName`, with `ReleaseType` ∈ {`Update`, `Hotfix`, `Security Update`} and without `UninstallString` and `QuietUninstallString`.
2. **Protected wins over every junk rule.** The row has the `Protected` group, a padlock and doesn't go into a batch.
3. The first matching entry from `entries` sets the group; no match = "Other".
4. Reason for blocking (when several match): protected, another account, uncertain user, no safe command.

## Identification sources

- Protected list and starting point of the suggestions: Stage 4a design (debloat), point 6.2.
- **Microsoft packages:** reading from a fresh 26H2 VM (Home and Pro, build 26300.9457, 2026-10-21), section "Wyniki VM 26H2 (partia 2)" (26H2 VM results, round 2)
  in `docs/zrodla/2026-10-20-etap4a-zrodla.md` (in Polish). All names from the VM are confirmed by reading `Get-AppxPackage`.
  Outside the VM only `Microsoft.MicrosoftOfficeHub` and `Microsoft.Getstarted` remained (names from MS Learn, forum archive
  "Which AppXPackage can be removed without causing system problems?"; not on a fresh 26H2).
- **Manufacturers (Lenovo, ASUS, Acer, HP, Dell):** there was no OEM hardware, so the names come from manufacturer pages and forums (addresses in the
  `source` field of each entry). **No manufacturer entry is suggested**, so a mistake in a name won't remove anything; at worst
  the app won't get a warning. The Lenovo Vantage package name (`E046963F.LenovoCompanion`) wasn't confirmed online
  (it stays as a non-suggested entry with a warning, because a missing entry would be worse than a mistake in it).
- **Trial:** McAfee and Norton patterns narrowed down to consumer products. McAfee Agent / Endpoint / Drive Encryption / Data Loss Prevention
  and Trellix are on the **protected** list (they may be company software). Norton Family and Utilities don't match any pattern.
- The protected driver patterns by publisher (`Intel*`, `NVIDIA*` etc.) are broad on purpose: better not to allow removal than to allow it.
- **Win32 runtime dependencies:** Visual C++ Redistributable, .NET Runtime / Windows Desktop Runtime, ASP.NET Core Runtime
  and Shared Framework, Microsoft Update Health Tools, Microsoft GameInput. They aren't on a fresh 26H2 VM (there are only hidden AppX packages
  VCLibs/.NET Native), so the Uninstall entry names come from a reading of a Windows 11 host (2026-10-21, read only), and the justification
  from Microsoft Learn / Microsoft Support (addresses in the `source` field, checked by downloading the page on 2026-10-21). GameInput is an MSI program
  ("Microsoft GameInput"), not an AppX package – hence the `displayName` rule. Entries with `SystemComponent=1` (e.g. "Microsoft .NET Host",
  "Visual C++ … Minimum Runtime") are hidden anyway; the rules catch the visible entries of the bundle packages. Developer tools (.NET SDK,
  Targeting Pack) don't match the rules.

### `cleanupUrl` addresses (checked by downloading the page)

| Address | Checked | Result |
|---|---|---|
| `https://support.norton.com/sp/en/us/home/current/solutions/v60392881` | 2026-10-21 | works; title "Download and run the Norton 360 Remover tool for Windows", description: removes, or removes and reinstalls, the Norton device security app |
| `https://www.mcafee.com/support/s/article/000002766?language=pl` | 2026-10-21 | McAfee article about the removal tool (MCPR); page download: timeout, curl: 403 (bot blocking) – the address was given and checked by hand by the author. Assigned to LiveSafe, Total Protection, AntiVirus Plus, Internet Security and Personal Security |

### Not added, because they couldn't be confirmed

- Adobe trial, Office trial, Spotify, Dropbox, ExpressVPN, LastPass, Amazon: no established Uninstall entry names; people buy and use some of them,
  so without a name from real hardware there's no entry. Booking.com is there (pattern by name, source: Which? and borncity.com); if the laptop
  only has a Start menu shortcut and not an Uninstall entry, the program won't see it.
- Acer Quick Access, Acer Jumpstart, Dell SupportAssist in the Store version, `AD2F1837.myHP`: package names without a reliable source.
- LinkedIn (`7EE7776C.LinkedInforWindows`) and the old personal Teams (`MicrosoftTeams`): names not confirmed, entries removed from the draft.

## Borderline decisions

| Item | Decision | Why |
|---|---|---|
| Get Help (`Microsoft.GetHelp`) | Other (not suggested) | An app for contacting support; it isn't advertising, and the research proposed "suggested" as the equivalent of Tips |
| Microsoft Bing (`Microsoft.BingSearch`) | Other | Not in the spec; ambiguous |
| Dev Home (`Microsoft.Windows.DevHome`) | Other | A tool for developers, may be in use |
| Family (`MicrosoftCorporationII.MicrosoftFamily`) | Other | Parental controls, may be in use |
| Sticky Notes (`Microsoft.MicrosoftStickyNotes`) | **Protected** | A utility app (design decision) |
| Xbox TCUI, Xbox Identity Provider, Game Speech Window | Other | Xbox dependencies, may be needed by games and Game Bar (design decision) |
| Game Bar (`Microsoft.XboxGamingOverlay`) | Other, explicit entry | The author's decision 2026-10-21 |
| Phone Link, Mobile devices, Windows Web Experience Pack | Other | No entry; Widgets are handled by round 1 |
| Windows Application Compatibility Enhancements, AVC/MPEG-2/VP9/RAW/WebMedia codecs, Store Experience Host (Microsoft Store purchases) | Protected | System components and codecs, like the other codecs from the spec |
| Edge as a package (`Microsoft.MicrosoftEdge.Stable`) | Protected | On the VM the package exists as a separate item next to the Win32 Edge entry |
| Remote Desktop Connection | Protected (in Polish and in English) | The `mstsc` Uninstall entry has its name only in `DisplayName_Localized`; the pattern depends on the system language |
| HP Support Assistant, Dell SupportAssist | **Other** (the draft had "suggested") | They may update drivers; no OEM hardware to check |
| MyASUS, Armoury Crate, MyDell, Dell Optimizer | Other + warning about the battery and Fn | They control the battery and performance |
| Dell Digital Delivery | Other + warning | May keep installers of purchased software |
| McAfee WebAdvisor, Security Scan Plus | Suggested | No independent source; the author's decision 2026-10-21 after reading the list |
| Consumer McAfee and Norton | Suggested + warning "If this is a paid antivirus you use, don't remove it" | Spec 6.2 says: trial suggested; the program can't tell a trial from a paid license |
| McAfee Agent, Endpoint, Drive Encryption, DLP, Trellix | **Protected** | May be company software |
| McAfee LiveSafe, Total Protection, AntiVirus Plus, Internet Security | Suggested; official mcafee.com pages in `source` | Spec 6.2 (McAfee trial); the patterns are product names, not all of "McAfee*" |
| McAfee Personal Security | Suggested + warning | No official McAfee page found for this name; suggested by the author's decision 2026-10-21 |
| WildTangent, HP Games by WildTangent | Suggested; source "spec 6.2 (decyzja autora)" (the author's decision) | No official manufacturer page in the checked sources; patterns `WildTangent*` and `HP Games by WildTangent*` (no `*WildTangent*`) |
| Booking.com | Suggested; source "spec 6.2 (decyzja autora)" (the author's decision) | No official page for the entry; name from Which? |
| Microsoft 365 (Office Hub), Tips | Suggested; source MS Learn (address in `source`) | The names aren't on a fresh 26H2, only in an archived list from Windows 10 |
| Microsoft Visual C++ Redistributable (`Microsoft Visual C++ * Redistributable*`) | **Protected** | Many programs and games need them; after removal they stop starting |
| .NET Runtime, Windows Desktop Runtime (`Microsoft .NET*Runtime*`, `Microsoft Windows Desktop Runtime*`) | **Protected** | Without them, .NET programs won't start |
| ASP.NET Core Runtime / Shared Framework (`Microsoft ASP.NET Core*Runtime*`, `…*Shared Framework*`) | **Protected** | Without them, ASP.NET Core programs and services won't start |
| Microsoft Update Health Tools | **Protected** | A component of the Windows Update service (KB4023057) |
| Microsoft GameInput (`Microsoft GameInput*`, MSI program) | **Protected** | Controller support in games; games install it back |
