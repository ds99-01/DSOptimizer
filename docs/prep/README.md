# Przygotowanie – sprawdzone adresy producentów

Strona „Przygotowanie” (Etap 4a, partia 3) pokazuje linki tekstowe do oficjalnych stron producentów. Ten plik jest jedynym źródłem
listy adresów w `PrepLinks` (Core). Test `LinksMatchDocsPrepReadme` czyta obie tabele niżej i sprawdza, że kod ma dokładnie te same
wpisy z tą samą datą sprawdzenia.

## Zasady

- Adres wchodzi do tabeli tylko po sprawdzeniu: domena producenta, `https`, strona istnieje i robi to, co mówi kolumna `Cel`.
  W kolumnie `Źródło` jest opis, jak to sprawdzono (curl, pobranie strony, wyszukiwarka na domenie producenta). Wiele stron producentów
  blokuje boty (403, timeout); wtedy dowodem jest ta sama strona w wynikach wyszukiwania na domenie producenta.
- Adres, którego nie dało się potwierdzić, nie wchodzi do tabeli. Trafia do sekcji „Niepotwierdzone” na dole.
- `Cel`: `chipset` (sterowniki chipsetu po producencie procesora), `grafika` (sterowniki karty po producencie karty),
  `wsparcie` (strona wsparcia producenta laptopa), `wsparcie-numer` (to samo z numerem seryjnym; adres ma dosłowne `{serial}`).
  Szablon z numerem wchodzi tylko wtedy, gdy strona producenta go dokumentuje albo adres z prawdziwym numerem w poprawnym
  formacie otwiera stronę produktu.
- `Sprawdzono` w formacie `yyyy-MM-dd`. Po ponownym sprawdzeniu zmienia się data tu i w kodzie (test pilnuje zgodności).
- Program niczego z tych stron nie pobiera; otwiera je w przeglądarce konta użytkownika (`ShellUrlOpener`).
- Nazwy producentów w obu tabelach są te same: AMD, Intel, NVIDIA, Lenovo, HP, Dell, ASUS, Acer, MSI, Microsoft Surface.

## Adresy

| Producent | Cel | Adres | Źródło | Sprawdzono |
|---|---|---|---|---|
| AMD | chipset | https://www.amd.com/en/support/download/drivers.html | curl: 200, tytuł „Drivers and Support for Processors and Graphics”, sekcja „Chipsets” i narzędzie „Auto-Detect and Install Tool” (wersja `/pl/` daje 404) | 2026-10-21 |
| AMD | grafika | https://www.amd.com/en/support/download/drivers.html | jw.: ta sama strona obejmuje sterowniki Radeon („Auto-Detect and Install Driver Updates for AMD Radeon™ Series Graphics and Ryzen™ Chips”) | 2026-10-21 |
| Intel | chipset | https://www.intel.com/content/www/us/en/download/19347/chipset-inf-utility.html | pobrana strona: strona pobierania „Chipset INF Utility”, wersja 10.1.20658.8883 z 2026-01-08 (curl: 403, blokada botów; wariant `/pl/pl/` przekierowuje na intel.pl i kończy się stroną 404) | 2026-10-21 |
| Intel | grafika | https://www.intel.com/content/www/us/en/support/products/80939/graphics.html | pobrana strona: tytuł „Support for Intel® Graphics”, sekcja „List of Drivers for Intel® Graphics”, odnośniki do sterowników Arc, Iris Xe, UHD/HD i starszych kart (curl: 403, blokada botów; wariant `/pl/pl/` przekierowuje na intel.pl) | 2026-10-21 |
| NVIDIA | grafika | https://www.nvidia.com/pl-pl/drivers/ | curl: 200, tytuł „Pobierz oficjalne sterowniki NVIDIA” | 2026-10-21 |
| Lenovo | wsparcie | https://pcsupport.lenovo.com/pl/pl/ | curl: 200, tytuł „Oficjalny laptop Lenovo, akcesoria & wsparcie PC - Lenovo Support PL” (pobranie strony: 403) | 2026-10-21 |
| HP | wsparcie | https://support.hp.com/pl-pl/drivers | curl: 200, aplikacja HP Support (strona bez JS nie ma treści, pobrana strona pusta); strony modeli pod tą ścieżką („… Pobieranie oprogramowania i sterowników”) w wynikach wyszukiwania na support.hp.com | 2026-10-21 |
| Dell | wsparcie | https://www.dell.com/support/home/pl-pl | pobrana strona: tytuł „Strona wsparcia – Dell Polska”, przyciski „Znajdź sterownik” i „Sterowniki i pliki do pobrania” (curl: 403) | 2026-10-21 |
| ASUS | wsparcie | https://www.asus.com/pl/support/ | curl: 200, tytuł „Oficjalne Wsparcie – ASUS Polska” | 2026-10-21 |
| Acer | wsparcie | https://www.acer.com/pl-pl/support/drivers-and-manuals | wyszukiwarka na acer.com: tytuł „Pobierz sterowniki i instrukcje pomocy technicznej firmy Acer”, wyszukiwanie po numerze seryjnym, SNID albo modelu (curl i pobranie strony: brak odpowiedzi) | 2026-10-21 |
| MSI | wsparcie | https://www.msi.com/support | wyszukiwarka na msi.com: strona „Support” z wyszukiwaniem modelu i sterownikami (curl i pobranie strony: 403) | 2026-10-21 |
| Microsoft Surface | wsparcie | https://support.microsoft.com/pl-pl/surface/drivers-firmware/download-drivers-and-firmware-for-surface | curl bez śledzenia przekierowań: 200, tytuł „Pobieranie sterowników i oprogramowania układowego dla urządzenia Surface”; stary adres z GUID (`…/download-drivers-and-firmware-for-surface-09bb2e09-…`) daje 301 na ten | 2026-10-21 |

Żaden producent nie ma wpisu `wsparcie-numer` (powód w „Niepotwierdzone”).

## Nazwy producentów w WMI

`Win32_ComputerSystem.Manufacturer` to producent z tablicy SMBIOS (typ 1), ten sam napis co `sys_vendor` w Linuksie
(Microsoft devblogs, „How do I obtain the computer manufacturer's name?”, 2008-12-18: WMI czyta to „by querying the SMBIOS
directly”). Dlatego źródłem nazw są tabele DMI jądra Linux z dokładnym dopasowaniem (`DMI_EXACT_MATCH(DMI_SYS_VENDOR, …)`)
i odczyty z forów. `PrepLinks.Oem` porównuje bez rozróżniania wielkości liter i po `Trim`, więc `ASUSTeK Computer Inc.`
(starsze modele) pasuje do wpisu ASUS.

| Producent | Nazwy producenta w WMI |
|---|---|
| Lenovo | LENOVO |
| HP | HP; Hewlett-Packard |
| Dell | Dell Inc.; Dell Computer Corporation |
| ASUS | ASUSTeK COMPUTER INC. |
| Acer | Acer |
| MSI | Micro-Star International Co., Ltd. |
| Microsoft Surface | Microsoft Corporation |

Źródła (sprawdzone 2026-10-21):
- Lenovo `LENOVO`: `DMI_EXACT_MATCH(DMI_SYS_VENDOR, "LENOVO")` w `drivers/gpu/drm/drm_panel_orientation_quirks.c` i `drivers/firmware/efi/sysfb_efi.c`
  (github.com/torvalds/linux); skrypty administratorów z `(Get-CimInstance -ClassName Win32_ComputerSystem).Manufacturer -eq "LENOVO"`
  (aaronparker/intune `Devices/Set-LenovoVantage.ps1`, FredrikWall/PowerShell `Windows/Get-ComputerModel.ps1`).
- HP `HP`: `DMI_EXACT_MATCH(DMI_SYS_VENDOR, "HP")` w `drivers/power/supply/axp288_charger.c`; `Hewlett-Packard` (starsze modele):
  `DMI_EXACT_MATCH(DMI_SYS_VENDOR, "Hewlett-Packard")` w `sound/soc/intel/boards/bytcr_rt5640.c`. Zmianę nazwy z `Hewlett-Packard`
  na `HP` opisują też skrypty wdrożeniowe (2Pint DeployR „MakeAlias”, MSEndpointMgr `Invoke-CMApplyDriverPackage.ps1` – oba wzorce).
- Dell `Dell Inc.`: `DMI_EXACT_MATCH(DMI_SYS_VENDOR, "Dell Inc.")` w `drivers/hwmon/dell-smm-hwmon.c`; przykład odczytu WMI
  „Dell Inc.” / „Precision M65” w ITPro Today („How can I create a WMI filter for only certain computer makes?”).
  `Dell Computer Corporation` (bardzo stare modele): `drivers/platform/x86/dell/dell-laptop.c`.
- ASUS `ASUSTeK COMPUTER INC.`: `DMI_EXACT_MATCH(DMI_SYS_VENDOR, "ASUSTeK COMPUTER INC.")` w `drm_panel_orientation_quirks.c`,
  41 wpisów w `drivers/platform/x86/asus-nb-wmi.c`; msinfo32 „System Manufacturer ASUSTeK COMPUTER INC.” na forum Tom's Guide (X455LJ).
- Acer `Acer`: `DMI_EXACT_MATCH(DMI_SYS_VENDOR, "Acer")` w `drivers/platform/x86/acer-wmi.c` i `drm_panel_orientation_quirks.c`;
  msinfo32 „System Manufacturer Acer” / „Aspire 5315” na forum Intel Community.
- MSI `Micro-Star International Co., Ltd.`: `DMI_EXACT_MATCH(DMI_SYS_VENDOR, "Micro-Star International Co., Ltd.")` w
  `sound/soc/intel/boards/bytcr_rt5640.c`.
- Microsoft Surface `Microsoft Corporation`: `DMI_EXACT_MATCH(DMI_SYS_VENDOR, "Microsoft Corporation")` w
  `drivers/platform/surface/surface_gpe.c` (15 modeli Surface). **Uwaga:** VM Hyper-V ma ten sam napis (pomiar niżej), więc reguła
  „maszyna wirtualna” musi być sprawdzana przed dopasowaniem do Surface (DeployR robi tak samo: Microsoft + `Virtual Machine` = VM).

## Pomiar na VM 26H2 (Home i Pro, build 26300.9457, 2026-10-21)

Skrypt `tests/vm/explore/Invoke-E4A3Exploration.ps1`, surowe wyniki poza gitem. Wnioski dla reguł strony:

- VM Hyper-V: `Win32_ComputerSystem` = `Microsoft Corporation` / `Virtual Machine`; `Win32_ComputerSystemProduct.Version` =
  `Hyper-V UEFI Release v4.1`; numer seryjny jest w `Win32_ComputerSystemProduct.IdentifyingNumber` i `Win32_BIOS.SerialNumber`
  (ten sam napis, z myślnikami, np. `7491-1319-6942-4807-0972-0051-44`).
- Karta VM: `Microsoft Hyper-V Video`, `PNPDeviceID` `VMBUS\{DA0A7802-…}\{5620E0C7-…}` (bez `VEN_`), `InfFilename` `wvmbusvideo.inf`.
- „Podstawowej karty graficznej Microsoft” nie dało się wymusić na VM generacji 2 (wyłączenie karty Hyper-V daje kod 22 przy tym samym
  INF, `pnputil /remove-device` usuwa kartę z `Win32_VideoController`). Z plików INF systemu w VM: nazwa pochodzi z `display.inf`
  (`MSBDAName = "Microsoft Basic Display Adapter"`, dopasowanie `PCI\CC_0300`/`PCI\CC_0301`), po polsku
  `DriverStore\pl-PL\display.inf_loc`: „Podstawowa karta graficzna Microsoft”. Warianty z `displayoverride.inf`:
  „Microsoft Basic Display Adapter (Low Resolution)” / „(No ACPI support)”, po polsku „Podstawowa karta wideo Microsoft (niska
  rozdzielczość)” / „(bez obsługi interfejsu ACPI)”. `InfFilename` w `Win32_VideoController` dla tej karty nie został zaobserwowany.
- `Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0` na czystej VM: pusto. Kod 22 po `Disable-PnpDevice` (Composite Bus Enumerator
  i karta Hyper-V). Kodu 28 nie było.
- `SoftwareLicensingProduct` (filtr z planu): `LicenseStatus` 5 na nieaktywowanej VM Home i Pro.
- Zapytania strony (5 powtórzeń, jako Administrator i jako podniesiony `dso`): najwolniejsze `Win32_PnPSignedDriver` (mediana do 211 ms,
  pierwszy odczyt do 744 ms) i `SoftwareLicensingProduct` (mediana do 117 ms, pierwszy odczyt do 442 ms), reszta z medianą poniżej 25 ms.
  Limit `PrepLoader.DefaultTimeout` = 10 s (żadna mediana nie przekracza 5 s).

## Niepotwierdzone

- **Adresy z numerem seryjnym (`wsparcie-numer`)**: Lenovo, HP, Dell, ASUS, Acer, MSI, Surface. Żadna oficjalna strona nie dokumentuje
  adresu z numerem, a bez prawdziwego numeru nie da się sprawdzić, czy adres otwiera stronę produktu. Krążące w sieci wzorce
  (Dell `…/product-support/servicetag/<tag>`, Lenovo `pcsupport.lenovo.com/products/<numer>`) pochodzą z forów i serwisów trzecich;
  wyszukiwarki na dell.com i lenovo.com nie zwróciły dokumentacji, a Dell i Lenovo każą wpisać numer w polu wyszukiwania na stronie
  wsparcia. Do sprawdzenia na prawdziwym laptopie z numerem seryjnym.
- **Intel, strony po polsku**: `intel.com/content/www/pl/pl/...` przekierowuje na `intel.pl`, a ta na stronę 404 (stan 2026-10-21).
- **AMD, strona po polsku**: `amd.com/pl/support/download/drivers.html` daje 404.
- **Intel grafika – strona sterownika Arc**: strona „Intel® Arc™ Graphics – Windows” (`/download/785597/...`) obejmuje
  dziś tylko Arc i Core Ultra (pobrana strona 2026-10-21), nie Iris Xe i starsze, więc nie nadaje się jako jeden adres dla każdej karty Intel.
- **Intel Driver & Support Assistant (odrzucone)**: `https://www.intel.com/content/www/us/en/support/detect.html` istnieje (pobrana strona
  2026-10-21), ale prowadzi do instalowanego narzędzia aktualizującego sterowniki, a spec 7.2/8 zabrania podsuwania takich narzędzi
 . Zastąpione stroną „Support for Intel® Graphics”.
- **Nazwy WMI spoza tabeli**: `ASUS` (podciąg w `asus-nb-wmi.c`, bez dokładnego dopasowania), `MICRO-STAR INT…` (stare MSI,
  tylko podciąg w `msi-laptop.c`). Bez dokładnego napisu nie wchodzą; taki laptop dostanie „Szukaj strony wsparcia”.
