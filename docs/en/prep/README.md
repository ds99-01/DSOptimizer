<!-- source: docs/prep/README.md sha256: c2b0e546071b101bfe2b6434e7a55a27a12a51e1adb3e0a35730b73ba6ead315 -->

# Preparation – verified manufacturer addresses

The "Preparation" page (Stage 4a, round 3) shows text links to official manufacturer pages. This file is the only source of
the address list in `PrepLinks` (Core). The test `LinksMatchDocsPrepReadme` reads both tables below and checks that the code has exactly the same
entries with the same check date.

## Rules

- An address goes into the table only after it has been checked: manufacturer's domain, `https`, the page exists and does what the `Purpose` column says.
  The `Source` column describes how it was checked (curl, page download, search engine on the manufacturer's domain). Many manufacturer pages
  block bots (403, timeout); then the evidence is the same page in the search results on the manufacturer's domain.
- An address that couldn't be confirmed doesn't go into the table. It goes to the "Unconfirmed" section at the bottom.
- `Purpose`: `chipset` (chipset drivers by processor manufacturer), `graphics` (card drivers by card manufacturer),
  `support` (laptop manufacturer's support page), `support-number` (the same with the serial number; the address has a literal `{serial}`).
  A template with the number goes in only when the manufacturer's page documents it or an address with a real number in the correct
  format opens the product page.
- `Checked` in the `yyyy-MM-dd` format. After a new check, the date changes here and in the code (the test keeps them in sync).
- The program doesn't download anything from these pages; it opens them in the user account's browser (`ShellUrlOpener`).
- Manufacturer names in both tables are the same: AMD, Intel, NVIDIA, Lenovo, HP, Dell, ASUS, Acer, MSI, Microsoft Surface.

## Addresses

| Manufacturer | Purpose | Address | Source | Checked |
|---|---|---|---|---|
| AMD | chipset | https://www.amd.com/en/support/download/drivers.html | curl: 200, title "Drivers and Support for Processors and Graphics", "Chipsets" section and the "Auto-Detect and Install Tool" (the `/pl/` version gives 404) | 2026-10-21 |
| AMD | graphics | https://www.amd.com/en/support/download/drivers.html | as above: the same page covers Radeon drivers ("Auto-Detect and Install Driver Updates for AMD Radeon™ Series Graphics and Ryzen™ Chips") | 2026-10-21 |
| Intel | chipset | https://www.intel.com/content/www/us/en/download/19347/chipset-inf-utility.html | downloaded page: "Chipset INF Utility" download page, version 10.1.20658.8883 from 2026-01-08 (curl: 403, bot blocking; the `/pl/pl/` variant redirects to intel.pl and ends on a 404 page) | 2026-10-21 |
| Intel | graphics | https://www.intel.com/content/www/us/en/support/products/80939/graphics.html | downloaded page: title "Support for Intel® Graphics", section "List of Drivers for Intel® Graphics", links to drivers for Arc, Iris Xe, UHD/HD and older cards (curl: 403, bot blocking; the `/pl/pl/` variant redirects to intel.pl) | 2026-10-21 |
| NVIDIA | graphics | https://www.nvidia.com/pl-pl/drivers/ | curl: 200, title "Pobierz oficjalne sterowniki NVIDIA" (Download official NVIDIA drivers) | 2026-10-21 |
| Lenovo | support | https://pcsupport.lenovo.com/pl/pl/ | curl: 200, title "Oficjalny laptop Lenovo, akcesoria & wsparcie PC - Lenovo Support PL" (Official Lenovo laptop, accessories & PC support) (page download: 403) | 2026-10-21 |
| HP | support | https://support.hp.com/pl-pl/drivers | curl: 200, HP Support app (the page has no content without JS, downloaded page empty); model pages under this path ("… Pobieranie oprogramowania i sterowników" (Software and driver downloads)) in the search results on support.hp.com | 2026-10-21 |
| Dell | support | https://www.dell.com/support/home/pl-pl | downloaded page: title "Strona wsparcia – Dell Polska" (Support page – Dell Poland), buttons "Znajdź sterownik" (Find a driver) and "Sterowniki i pliki do pobrania" (Drivers and downloads) (curl: 403) | 2026-10-21 |
| ASUS | support | https://www.asus.com/pl/support/ | curl: 200, title "Oficjalne Wsparcie – ASUS Polska" (Official Support – ASUS Poland) | 2026-10-21 |
| Acer | support | https://www.acer.com/pl-pl/support/drivers-and-manuals | search engine on acer.com: title "Pobierz sterowniki i instrukcje pomocy technicznej firmy Acer" (Download Acer drivers and support manuals), search by serial number, SNID or model (curl and page download: no response) | 2026-10-21 |
| MSI | support | https://www.msi.com/support | search engine on msi.com: "Support" page with model search and drivers (curl and page download: 403) | 2026-10-21 |
| Microsoft Surface | support | https://support.microsoft.com/pl-pl/surface/drivers-firmware/download-drivers-and-firmware-for-surface | curl without following redirects: 200, title "Pobieranie sterowników i oprogramowania układowego dla urządzenia Surface" (Download drivers and firmware for Surface); the old address with a GUID (`…/download-drivers-and-firmware-for-surface-09bb2e09-…`) gives 301 to this one | 2026-10-21 |

No manufacturer has a `support-number` entry (reason in "Unconfirmed").

## Manufacturer names in WMI

`Win32_ComputerSystem.Manufacturer` is the manufacturer from the SMBIOS table (type 1), the same string as `sys_vendor` in Linux
(Microsoft devblogs, "How do I obtain the computer manufacturer's name?", 2008-12-18: WMI reads it "by querying the SMBIOS
directly"). That's why the source of the names is the Linux kernel DMI tables with exact matching (`DMI_EXACT_MATCH(DMI_SYS_VENDOR, …)`)
and readings from forums. `PrepLinks.Oem` compares case-insensitively and after `Trim`, so `ASUSTeK Computer Inc.`
(older models) matches the ASUS entry.

| Manufacturer | Manufacturer names in WMI |
|---|---|
| Lenovo | LENOVO |
| HP | HP; Hewlett-Packard |
| Dell | Dell Inc.; Dell Computer Corporation |
| ASUS | ASUSTeK COMPUTER INC. |
| Acer | Acer |
| MSI | Micro-Star International Co., Ltd. |
| Microsoft Surface | Microsoft Corporation |

Sources (checked 2026-10-21):
- Lenovo `LENOVO`: `DMI_EXACT_MATCH(DMI_SYS_VENDOR, "LENOVO")` in `drivers/gpu/drm/drm_panel_orientation_quirks.c` and `drivers/firmware/efi/sysfb_efi.c`
  (github.com/torvalds/linux); administrator scripts with `(Get-CimInstance -ClassName Win32_ComputerSystem).Manufacturer -eq "LENOVO"`
  (aaronparker/intune `Devices/Set-LenovoVantage.ps1`, FredrikWall/PowerShell `Windows/Get-ComputerModel.ps1`).
- HP `HP`: `DMI_EXACT_MATCH(DMI_SYS_VENDOR, "HP")` in `drivers/power/supply/axp288_charger.c`; `Hewlett-Packard` (older models):
  `DMI_EXACT_MATCH(DMI_SYS_VENDOR, "Hewlett-Packard")` in `sound/soc/intel/boards/bytcr_rt5640.c`. The name change from `Hewlett-Packard`
  to `HP` is also described in deployment scripts (2Pint DeployR "MakeAlias", MSEndpointMgr `Invoke-CMApplyDriverPackage.ps1` – both patterns).
- Dell `Dell Inc.`: `DMI_EXACT_MATCH(DMI_SYS_VENDOR, "Dell Inc.")` in `drivers/hwmon/dell-smm-hwmon.c`; an example WMI reading
  "Dell Inc." / "Precision M65" in ITPro Today ("How can I create a WMI filter for only certain computer makes?").
  `Dell Computer Corporation` (very old models): `drivers/platform/x86/dell/dell-laptop.c`.
- ASUS `ASUSTeK COMPUTER INC.`: `DMI_EXACT_MATCH(DMI_SYS_VENDOR, "ASUSTeK COMPUTER INC.")` in `drm_panel_orientation_quirks.c`,
  41 entries in `drivers/platform/x86/asus-nb-wmi.c`; msinfo32 "System Manufacturer ASUSTeK COMPUTER INC." on the Tom's Guide forum (X455LJ).
- Acer `Acer`: `DMI_EXACT_MATCH(DMI_SYS_VENDOR, "Acer")` in `drivers/platform/x86/acer-wmi.c` and `drm_panel_orientation_quirks.c`;
  msinfo32 "System Manufacturer Acer" / "Aspire 5315" on the Intel Community forum.
- MSI `Micro-Star International Co., Ltd.`: `DMI_EXACT_MATCH(DMI_SYS_VENDOR, "Micro-Star International Co., Ltd.")` in
  `sound/soc/intel/boards/bytcr_rt5640.c`.
- Microsoft Surface `Microsoft Corporation`: `DMI_EXACT_MATCH(DMI_SYS_VENDOR, "Microsoft Corporation")` in
  `drivers/platform/surface/surface_gpe.c` (15 Surface models). **Note:** a Hyper-V VM has the same string (measurement below), so the
  "virtual machine" rule must be checked before matching Surface (DeployR does the same: Microsoft + `Virtual Machine` = VM).

## Measurement on 26H2 VMs (Home and Pro, build 26300.9457, 2026-10-21)

Script `tests/vm/explore/Invoke-E4A3Exploration.ps1`, raw results outside git. Conclusions for the page rules:

- Hyper-V VM: `Win32_ComputerSystem` = `Microsoft Corporation` / `Virtual Machine`; `Win32_ComputerSystemProduct.Version` =
  `Hyper-V UEFI Release v4.1`; the serial number is in `Win32_ComputerSystemProduct.IdentifyingNumber` and `Win32_BIOS.SerialNumber`
  (the same string, with hyphens, e.g. `7491-1319-6942-4807-0972-0051-44`).
- VM card: `Microsoft Hyper-V Video`, `PNPDeviceID` `VMBUS\{DA0A7802-…}\{5620E0C7-…}` (no `VEN_`), `InfFilename` `wvmbusvideo.inf`.
- The "Microsoft Basic Display Adapter" couldn't be forced on a generation 2 VM (turning off the Hyper-V card gives code 22 with the same
  INF, `pnputil /remove-device` removes the card from `Win32_VideoController`). From the system INF files in the VM: the name comes from `display.inf`
  (`MSBDAName = "Microsoft Basic Display Adapter"`, matching `PCI\CC_0300`/`PCI\CC_0301`), in Polish
  `DriverStore\pl-PL\display.inf_loc`: "Podstawowa karta graficzna Microsoft". Variants from `displayoverride.inf`:
  "Microsoft Basic Display Adapter (Low Resolution)" / "(No ACPI support)", in Polish "Podstawowa karta wideo Microsoft (niska
  rozdzielczość)" / "(bez obsługi interfejsu ACPI)". `InfFilename` in `Win32_VideoController` for this card wasn't observed.
- `Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0` on a clean VM: empty. Code 22 after `Disable-PnpDevice` (Composite Bus Enumerator
  and the Hyper-V card). There was no code 28.
- `SoftwareLicensingProduct` (filter from the plan): `LicenseStatus` 5 on the non-activated Home and Pro VMs.
- Page queries (5 repetitions, as Administrator and as elevated `dso`): the slowest are `Win32_PnPSignedDriver` (median up to 211 ms,
  first reading up to 744 ms) and `SoftwareLicensingProduct` (median up to 117 ms, first reading up to 442 ms), the rest with a median below 25 ms.
  Limit `PrepLoader.DefaultTimeout` = 10 s (no median exceeds 5 s).

## Unconfirmed

- **Addresses with the serial number (`support-number`)**: Lenovo, HP, Dell, ASUS, Acer, MSI, Surface. No official page documents
  an address with the number, and without a real number there's no way to check whether the address opens the product page. The patterns
  going around online (Dell `…/product-support/servicetag/<tag>`, Lenovo `pcsupport.lenovo.com/products/<number>`) come from forums and third-party sites;
  the search engines on dell.com and lenovo.com returned no documentation, and Dell and Lenovo tell you to enter the number in the search field on the support
  page. To be checked on a real laptop with a serial number.
- **Intel, pages in Polish**: `intel.com/content/www/pl/pl/...` redirects to `intel.pl`, and that one to a 404 page (as of 2026-10-21).
- **AMD, page in Polish**: `amd.com/pl/support/download/drivers.html` gives 404.
- **Intel graphics – Arc driver page**: the "Intel® Arc™ Graphics – Windows" page (`/download/785597/...`) today covers
  only Arc and Core Ultra (downloaded page 2026-10-21), not Iris Xe and older ones, so it isn't suitable as one address for every Intel card.
- **Intel Driver & Support Assistant (rejected)**: `https://www.intel.com/content/www/us/en/support/detect.html` exists (downloaded page
  2026-10-21), but it leads to an installed tool that updates drivers, and spec 7.2/8 forbids suggesting such tools
  (Ruling R13). Replaced by the "Support for Intel® Graphics" page.
- **WMI names outside the table**: `ASUS` (substring in `asus-nb-wmi.c`, no exact match), `MICRO-STAR INT…` (old MSI,
  only a substring in `msi-laptop.c`). Without an exact string they don't go in; such a laptop gets "Search for a support page".
