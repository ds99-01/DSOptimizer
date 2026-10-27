# Etap 3, partia A – źródła danych ekranu „Specyfikacja” (research 2026-10-19)

Zakres: tabela pól z spec Etapu 3 §4.2 (specyfikacja w archiwum autora) i rekordy z Taska 3 (`HardwareModels.cs`).
Dokument jest jedynym wejściem dla Taska 5 (czytnik `WindowsHardwareReader`): czytnik używa **tylko** źródeł oznaczonych „potwierdzone”.
Zasada repo: źródło wchodzi do kodu, gdy jest (1) w dokumentacji MS i (2) odczytane naprawdę na Windows 11 26H2 Home, Pro i na hoście.
Co nie spełnia obu warunków, ma w kolumnie decyzji wyraźne zastrzeżenie albo wypada.

Maszyny i surowe dane (sonda `tests/vm/explore/Invoke-E3AExploration.ps1`, wynik: zapis pomiaru w archiwum autora):

| Skrót | Maszyna | System |
|---|---|---|
| **Home** | VM `DSO-26H2-HOME` (Hyper-V gen 2, checkpoint `czysta-26H2`) | build 26300.9457, `EditionID=Core`, `DisplayVersion=26H2` |
| **Pro** | VM `DSO-26H2-PRO` (Hyper-V gen 2, checkpoint `czysta-26H2`) | build 26300.9457, `EditionID=Professional`, `DisplayVersion=26H2` |
| **host** | `PC-DAWID` (realny sprzęt, sonda `-LocalReadOnly`) | build 26200.9711, `EditionID=Professional`, `DisplayVersion=25H2` |

Wszystkie trzy biegi z uprawnieniami administratora, PowerShell 5.1, proces 64-bitowy. Sonda ma tylko odczyty. Każda klasa przeszła bez błędu
(pole `Error`/`ClassError` puste we wszystkich 3×20 krokach). Data dostępu do URL: 2026-10-19. Wszystkie URL niżej pobrano (pobranie strony HTTP) tego dnia.
Rodzaj źródła: **MS** = strona bieżąca learn.microsoft.com; **MS-archiwum** = strona archiwalna MS (może być nieaktualna); **brak MS** = nie ma dokumentacji MS.

Dane osobowe: numerów seryjnych dysków i płyty hosta tu nie ma – „(zanonimizowany)”.

## 1. Tabela: pole → źródło → wartości → dokumentacja → decyzja

Kolumna „Home / Pro / host” podaje surowe wartości z sondy. „VM” = Home i Pro mają tę samą wartość (nie różnią się).
Typy CIM podano jak zwraca sonda (`CimTypes`), ważne przy rzutowaniu w `System.Management` (`UInt32` → `uint`, `UInt64` → `ulong`).

### 1.1 CpuInfo (`Win32_Processor`, `root\cimv2`) – [MS](https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-processor)

| Pole rekordu | Źródło | Home / Pro / host | Decyzja |
|---|---|---|---|
| `Name` | `Win32_Processor.Name` (String) | VM: `"AMD Ryzen 7 9800X3D 8-Core Processor           "` (VM widzi procesor hosta); host: to samo. **Wartość ma końcowe spacje – `Trim()`.** | potwierdzone |
| `Cores` | `NumberOfCores` (UInt32) | VM: 2; host: 8 | potwierdzone |
| `Threads` | `NumberOfLogicalProcessors` (UInt32) | VM: 4; host: 16 | potwierdzone |

Uwaga: dokumentacja mówi „On a multiprocessor computer, one instance per processor” – przy wielu instancjach sumować `Cores` i `Threads`. Na trzech maszynach była jedna instancja.
Nie używać `ThreadCount` (nie sondowane).

### 1.2 MemoryModule / MemoryInfo (`Win32_PhysicalMemory`, `Win32_PhysicalMemoryArray`) – [MS PhysicalMemory](https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-physicalmemory), [MS PhysicalMemoryArray](https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-physicalmemoryarray)

| Pole rekordu | Źródło | Home / Pro / host | Decyzja |
|---|---|---|---|
| `CapacityBytes` | `Win32_PhysicalMemory.Capacity` (UInt64, bajty) | VM: 4 160 749 568 + 134 217 728 (dynamiczna pamięć Hyper-V); host: 17 179 869 184 ×2 (2×16 GiB) | potwierdzone |
| `SpeedMts` | **`ConfiguredClockSpeed`** (UInt32); fallback `Speed` gdy `ConfiguredClockSpeed` = 0/null | VM: `ConfiguredClockSpeed`=0, `Speed`=null; host: `ConfiguredClockSpeed`=**6000**, `Speed`=4800 | potwierdzone (patrz odp. (a)) |
| `SmbiosType` | `SMBIOSMemoryType` (UInt32, surowy typ SMBIOS) | VM: 2 (DRAM); host: **34** (DDR5) | potwierdzone dla 24/26 (MS) i 34 (host); 29, 30, 35 – patrz „Wypada” |
| `Manufacturer` | `Manufacturer` (String) | VM: `Microsoft Corporation`; host: `G Skill Intl` | potwierdzone |
| `PartNumber` | `PartNumber` (String) | VM: `None`; host: `"F5-6000J3038F16G              "` (**dopełnione spacjami – `Trim()`**) | potwierdzone |
| `Slot` | patrz niżej: `DeviceLocator` + `BankLabel` | VM: `DeviceLocator`=`M0001`/`M0002`, `BankLabel`=`None`; host: `DeviceLocator`=`DIMM 1` **dla obu modułów**, `BankLabel`=`P0 CHANNEL A` / `P0 CHANNEL B` | potwierdzone (reguła niżej) |
| `MemoryInfo.SlotCount` | `Win32_PhysicalMemoryArray.MemoryDevices` (UInt16; „liczba fizycznych gniazd w tablicy”) | VM: 2; host: 4 | potwierdzone |
| liczba modułów | liczba instancji `Win32_PhysicalMemory` | VM: 2; host: 2 | potwierdzone |

**Reguła `Slot`.** `DeviceLocator` samo nie wystarcza: na hoście oba moduły mają `DIMM 1` (płyta numeruje gniazda per kanał). `BankLabel` samo też nie
(VM: `None`). Reguła dla Taska 5:
1. `locator = DeviceLocator.Trim()`, `bank = BankLabel.Trim()`.
2. Gdy `bank` jest niepusty, różny od `locator` i nie jest zaślepką (`None`, `Unknown`, `Not Specified`, bez wielkości liter): `Slot = $"{locator} ({bank})"` → host: `DIMM 1 (P0 CHANNEL A)` i `DIMM 1 (P0 CHANNEL B)`; w przeciwnym razie `Slot = locator` → VM: `M0001`, `M0002`.
3. Gdy `locator` pusty: `Slot = bank`, a gdy też pusty: `null`.

**`SpeedMts` – wartość.** Dokumentacja MS opisuje `Speed` jako „nanoseconds”, a realnie host zwraca 4800 (MT/s, prędkość znamionowa JEDEC modułu) – dokumentacja jest tu
nieaktualna/błędna w jednostce. `ConfiguredClockSpeed` wg MS to „configured clock speed… in megahertz (MHz), or 0, if unknown” z SMBIOS Type 17; dla DDR SMBIOS podaje tu
liczbę transferów (MT/s), host: 6000 = ustawienie EXPO/XMP z opisu komputera autora. Kolejność: `ConfiguredClockSpeed` > 0, inaczej `Speed` > 0 (z zastrzeżeniem jednostki), inaczej `null`.
Formatter bierze maksimum po modułach.

### 1.3 BoardInfo (`Win32_BaseBoard`, `Win32_BIOS`, `Win32_ComputerSystem`, `GetFirmwareType`) – [MS BaseBoard](https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-baseboard), [MS BIOS](https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-bios), [MS ComputerSystem](https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-computersystem), [MS GetFirmwareType](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getfirmwaretype), [MS FIRMWARE_TYPE](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ne-winnt-firmware_type)

| Pole rekordu | Źródło | Home / Pro / host | Decyzja |
|---|---|---|---|
| `Manufacturer` | `Win32_BaseBoard.Manufacturer` (String) | VM: `Microsoft Corporation`; host: `ASUSTeK COMPUTER INC.` | potwierdzone |
| `Product` | `Win32_BaseBoard.Product` (String, „Baseboard part number defined by the manufacturer”, SMBIOS Type 2) | VM: `Virtual Machine`; host: `ROG STRIX X670E-F GAMING WIFI` | potwierdzone |
| `BiosVersion` | `Win32_BIOS.SMBIOSBIOSVersion` (String) | VM: `Hyper-V UEFI Release v4.1`; host: `3804` | potwierdzone |
| `BiosDate` | `Win32_BIOS.ReleaseDate` (datetime) – patrz format niżej | VM: 2025-09-25; host: 2026-06-15 | potwierdzone (reguła daty niżej) |
| `Firmware` | P/Invoke `kernel32!GetFirmwareType(out int)`: 1 = `FirmwareTypeBios` → `Legacy`, 2 = `FirmwareTypeUefi` → `Uefi`, 0 lub błąd → `Unknown` | VM Home/Pro: `$env:firmware_type`=`UEFI`; host: `GetFirmwareType` → **2**, `$env:firmware_type`=`UEFI` | potwierdzone (host: odczyt natywny; VM: tylko `$env:firmware_type`, który wywołuje to samo API) |
| `SystemManufacturer` | `Win32_ComputerSystem.Manufacturer` (String) | VM: `Microsoft Corporation`; host: `ASUS` | potwierdzone |
| `SystemModel` | `Win32_ComputerSystem.Model` (String) | VM: `Virtual Machine`; host: **`System Product Name`** (zaślepka płyty ASUS) | potwierdzone; zaślepkę ukrywa formatter (Task 3) |

**Format `BiosDate`.** Dokumentacja: „UTC format of YYYYMMDDHHMMSS.MMMMMM(+-)OOO”. Surowa wartość z `System.Management` (sprawdzone `Get-WmiObject` na hoście):
`"20260615000000.000000+000"`. `Get-CimInstance` (sonda) pokazuje to już po konwersji na czas lokalny: `2026-06-15T02:00:00+02:00`.
**Pułapka:** `ManagementDateTimeConverter.ToDateTime` przelicza na czas lokalny (host: `2026-06-15T02:00:00`), więc w strefie na zachód od UTC data
przesunęłaby się o dzień wstecz. **Reguła:** `BiosDate` = pierwsze 8 znaków surowego napisu (`yyyyMMdd`) sparsowane niezależnie od strefy
(`DateTime.ParseExact(raw[..8], "yyyyMMdd", Invariant)`); pusty/krótszy napis → `null`. (Win32_OperatingSystem.InstallDate ma offset lokalny, tam `ToDateTime` jest poprawne – pkt 1.9.)

### 1.4 GpuInfo (`Win32_VideoController` + rejestr klasy wyświetlania) – [MS VideoController](https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-videocontroller), [MS-archiwum: Setting Hardware Information in the Registry](https://learn.microsoft.com/en-us/windows-hardware/drivers/display/setting-hardware-information-in-the-registry)

| Pole rekordu | Źródło | Home / Pro / host | Decyzja |
|---|---|---|---|
| `Name` | `Win32_VideoController.Name` (String) | VM: `Microsoft Hyper-V Video`; host: `NVIDIA GeForce RTX 5080` | potwierdzone |
| `DriverVersion` | `DriverVersion` (String) | VM: `10.0.26100.1150`; host: `32.0.16.1742` | potwierdzone (host: to wersja pliku sterownika, nie numer „581.xx” z makiety; formatter pokazuje ją wprost) |
| `MemoryBytes` | 1. `HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\NNNN` → `HardwareInformation.qwMemorySize` (**REG_QWORD**); 2. fallback `Win32_VideoController.AdapterRAM` (UInt32) → `GpuMemory.Resolve(qw, adapterRam)` | VM: brak `qwMemorySize` w podkluczu `0000`, `AdapterRAM`=null → `null`; host (NVIDIA, `0000`): `qwMemorySize`=**17 094 934 528** (QWord, `0x3FAF00000` = 15,92 GiB → po zaokrągleniu do całości „16 GB”), `AdapterRAM`=**4 293 918 720** (nasycone, kłamie) | **potwierdzone empirycznie, brak dokumentacji MS** (patrz „Wypada / ograniczenia” pkt 1) |

**Reguła dopasowania `Win32_VideoController` ↔ podklucz klasy wyświetlania** (odp. (b), fakty z hosta):
- Instancje GPU bierz **wyłącznie** z `Win32_VideoController`. Rejestr służy tylko do dociągnięcia pamięci. Host ma 3 podklucze (`0000` NVIDIA, `0001` i `0002` AMD Radeon Graphics – iGPU), ale `Win32_VideoController` zwraca jedną instancję (NVIDIA); podklucze iGPU nie mogą więc dodawać wpisów do listy kart.
- Podklucze to katalogi o nazwie `^\d{4}$` (pomijaj `Configuration`, `Properties`). Dla każdej karty:
  1. **Dopasowanie po identyfikatorze:** `MatchingDeviceId` (REG_SZ) niepuste **i** `PNPDeviceID.StartsWith(MatchingDeviceId, StringComparison.OrdinalIgnoreCase)`.
     Wielkości liter są różne: `MatchingDeviceId` = `pci\ven_10de&dev_2c02` (małe litery, **bez** `SUBSYS`), `PNPDeviceID` = `PCI\VEN_10DE&DEV_2C02&SUBSYS_89D71043&REV_A1\...` (wielkie; sufiks instancji pominięto). Dla Hyper-V: `VMBUS\{da0a7802-e377-4aac-8e77-0558eb1073f8}` jest prefiksem `VMBUS\{DA0A7802-E377-4AAC-8E77-0558EB1073F8}\{5620E0C7-...}`. Tak samo AMD (MatchingDeviceId zawiera tam `SUBSYS`/`REV`: `PCI\VEN_1002&DEV_164E&SUBSYS_88771043&REV_C1` – prefiks pełnego PNPDeviceID, gdyby taka karta była aktywna).
  2. **Fallback:** `DriverDesc` (REG_SZ) równe `Name` (`OrdinalIgnoreCase`). Host: `DriverDesc`=`NVIDIA GeForce RTX 5080` = `Name`; VM: `Microsoft Hyper-V Video` = `Name`. Fallback jest potrzebny, gdy `MatchingDeviceId` ma inny kształt niż prefiks `PNPDeviceID`.
  3. Przy kilku kandydatach (np. dwa podklucze AMD z identycznym `DriverDesc` na hoście) preferuj pasujących **obiema** regułami, potem tego, który ma `qwMemorySize`; remis → pierwszy wg nazwy podklucza. Brak kandydata → `qwMemorySize` = `null`.
- Odczyt: `RegistryKey.OpenBaseKey(LocalMachine, Registry64)`, tylko do odczytu. `GetValueKind` → `QWord` ⇒ `(long)` → `ulong`; dla bezpieczeństwa obsłuż też `byte[8]` (little-endian) i `int`/`long` ≤ 0 → `null`. **Nie** używaj `HardwareInformation.MemorySize` (host: DWord, wartość `-1048576` = `0xFFF00000`, nasycona) ani `HardwareInformation.AdapterString` (host iGPU: REG_BINARY UTF-16, nie potrzebne).
- Gdy `qwMemorySize` = `null`: `GpuMemory.Resolve(null, AdapterRAM)` → wartość `AdapterRAM` tylko gdy nie jest nasycona (4 294 967 295 i `0xFFF00000` odpada), inaczej `null`. Uwaga dla Taska 3: `AdapterRAM` hosta to **4 293 918 720 (= 0xFFF00000)**, nie 4 294 967 295. Test `GpuMemory_Resolve` zakłada nasycenie wyłącznie na `uint.MaxValue`; przy braku `qwMemorySize` na hoście zwróciłby 4 293 918 720 ≈ „4 GB”. W Tasku 3/5 warto traktować `AdapterRAM >= 0xFFF00000` jako nasycone.

### 1.5 DisplayInfo (WMI `root\wmi` + user32) – [MS WmiMonitorID](https://learn.microsoft.com/en-us/windows/win32/wmicoreprov/wmimonitorid), [MS WmiMonitorBasicDisplayParams](https://learn.microsoft.com/en-us/windows/win32/wmicoreprov/wmimonitorbasicdisplayparams), [MS EnumDisplayDevicesW](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumdisplaydevicesw), [MS EnumDisplaySettingsW](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumdisplaysettingsw)

| Pole rekordu | Źródło | Home / Pro / host | Decyzja |
|---|---|---|---|
| `Name` | `WmiMonitorID.UserFriendlyName` (**UInt16[]**, tablica kodów UTF-16 dopełniona zerami; długość w `UserFriendlyNameLength`) | VM: `HyperVMonitor`; host: `T24d-30` (`[84,50,52,100,45,51,48,0,0,0,0,0,0]`) i `LEN LT2223pwC` | potwierdzone (zdekoduj do `char`, odetnij zera) |
| `WidthCm`, `HeightCm` | `WmiMonitorBasicDisplayParams.MaxHorizontalImageSize` / `MaxVerticalImageSize` (UInt8, **centymetry**; 0 = „system nie zakłada rozmiaru”) | VM: 0 × 0; host: 52×32 oraz 48×27 | potwierdzone |
| klucz parowania | `InstanceName` (String) w obu klasach, np. `DISPLAY\LEN63FF\5&20a6e220&0&UID4357_0` | VM: `DISPLAY\MSH062E\5&...&UID5527112_0`; host: dwa wpisy `DISPLAY\LEN63FF\...` i `DISPLAY\LEN60A1\...` | potwierdzone (w obu klasach ten sam `InstanceName`) |
| `Width`, `Height`, `RefreshHz` | user32: `EnumDisplayDevicesW` (adaptery z `DISPLAY_DEVICE_ATTACHED_TO_DESKTOP`) → `EnumDisplaySettingsW(deviceName, ENUM_CURRENT_SETTINGS=-1, ref DEVMODEW)` → `dmPelsWidth`, `dmPelsHeight`, `dmDisplayFrequency` | **nie odczytane** na żadnej maszynie (patrz „Wypada / ograniczenia” pkt 2) | potwierdzone tylko w dokumentacji; wymaga odczytu hosta w Tasku 5 (Step 3) |
| identyfikator do parowania | `EnumDisplayDevicesW(adapterName, i, ref dd, EDD_GET_DEVICE_INTERFACE_NAME=1)` → `DeviceID` = interfejs monitora `\\?\DISPLAY#LEN63FF#5&20a6e220&0&UID4357#{GUID}` | j.w. | potwierdzone tylko w dokumentacji (MS: „link between GDI monitor devices and SetupAPI monitor devices”) |

Przekątna: `round(sqrt(WidthCm² + HeightCm²) / 2.54)`: host 52×32 → 24 cale, 48×27 → 22 cale (zgodne z nazwami modeli). VM 0×0 → pole zniknie, zgodnie z Taskiem 3.
Wiele monitorów: wiersze `WmiMonitorID`/`WmiMonitorBasicDisplayParams` tej samej kolejności nie gwarantują – paruj po `InstanceName` (po obcięciu końcowego `_N`), nie po indeksie.
Format `MonitorIdentity.Matches`: interfejs `\\?\DISPLAY#LEN63FF#5&20a6e220&0&UID4357#{GUID}` → po obcięciu `\\?\`, końcowego `#{GUID}` i zamianie `#`→`\` daje `DISPLAY\LEN63FF\5&20a6e220&0&UID4357`; WMI `...UID4357_0` bez `_0` – to samo.
Uwaga: `UserFriendlyName` bywa pusty/zerowy dla monitorów bez deskryptora nazwy; wtedy `Name = null` (rozmiar i tryb zostają).

### 1.6 Dźwięk (`Win32_SoundDevice`) – [MS](https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-sounddevice)

| Pole | Źródło | Home / Pro / host | Decyzja |
|---|---|---|---|
| lista nazw | `Win32_SoundDevice.Name` (String) | VM: **0 instancji** (Hyper-V bez urządzenia audio); host: 6 – `AMD Streaming Audio Device`, `Realtek USB Audio`, `NVIDIA Virtual Audio Device (Wave Extensible) (WDM)`, `Sonic Studio Virtual Mixer`, `NVIDIA High Definition Audio`, `Urządzenie audio USB` | potwierdzone (pusta lista na VM = dane, formatter pokaże „brak”) |

Lista zawiera urządzenia wirtualne (NVIDIA Virtual Audio, Sonic Studio). Nie ma w źródle pola odróżniającego je od fizycznych – nie filtrować (filtr po nazwie byłby zgadywaniem).

### 1.7 DiskInfo (`root\Microsoft\Windows\Storage`) – [MS MSFT_PhysicalDisk](https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/msft-physicaldisk), [MS MSFT_StorageReliabilityCounter](https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/msft-storagereliabilitycounter)

| Pole rekordu | Źródło | Home / Pro / host | Decyzja |
|---|---|---|---|
| `Number` | `MSFT_PhysicalDisk.DeviceId` (**String**) → `int.TryParse` | VM: `"0"`; host: `"0"`, `"1"`, `"2"` (zgodne z numerami w Zarządzaniu dyskami) | potwierdzone (gdy `DeviceId` nie jest liczbą, np. dysk wirtualny puli Storage Spaces → `Number = null`; nie widziano na tych maszynach) |
| `Model` | `FriendlyName` (String) | VM: `Msft Virtual Disk`; host: `KINGSTON SKC3000S1024G`, `Samsung SSD 980 PRO 1TB`, `Lexar 1TB SSD` | potwierdzone |
| `SizeBytes` | `Size` (UInt64, bajty) | VM: 68 719 476 736; host: 1 024 209 543 168 / 1 000 204 886 016 / 1 024 209 543 168 | potwierdzone |
| `BusType` | `BusType` (UInt16) | VM: **10** (SAS – Hyper-V SCSI); host: **17** (NVMe) ×3 | potwierdzone; kody MS: 1 SCSI, 3 ATA, 7 USB, 8 RAID, 10 SAS, 11 SATA, 17 NVMe (pełna tabela MS: 0 Unknown, 2 ATAPI, 4 1394, 5 SSA, 6 FC, 9 iSCSI, 12 SD, 13 MMC, 14 MAX, 15 File Backed Virtual, 16 Storage Spaces) |
| `MediaType` | `MediaType` (UInt16) | VM: **0** (Unspecified); host: **4** (SSD) ×3 | potwierdzone; kody MS: 0 Unspecified, 3 HDD, 4 SSD, 5 SCM |
| `HealthStatus` | `HealthStatus` (UInt16) | VM i host: **0** (Healthy) | potwierdzone; kody MS: 0 Healthy, 1 Warning, 2 Unhealthy, **5 Unknown** |
| `SerialNumber` | `SerialNumber` (String) | VM: `null`; host: (zanonimizowany) – format NVMe `xxxx_xxxx_xxxx_xxxx.` z kropką na końcu | potwierdzone; **`Trim()` i obciąć końcową kropkę** to decyzja kosmetyczna Taska 5 (surowa wartość działa) |
| `TemperatureC` | `MSFT_StorageReliabilityCounter.Temperature` (UInt8, °C), dopasowany po `DeviceId` | VM: instancja istnieje, `Temperature`=**0** (czyli „brak”); host: 38, 36, 35 | potwierdzone (patrz niżej) |

**Temperatura.** Sonda użyła `Get-CimAssociatedInstance` (powiązanie `MSFT_PhysicalDisk` → `MSFT_StorageReliabilityCounter`), 1 instancja na dysk. W dokumentacji MS
`MSFT_StorageReliabilityCounter.DeviceId` „When associated with an MSFT_PhysicalDisk … will be the same as its DeviceId member”, a odczyt potwierdza równość
(host i VM: `DeviceId` licznika = `DeviceId` dysku). **Reguła dla Taska 5:** nie polegaj na nieprzetestowanej nazwie klasy asocjacji; wykonaj jedno zapytanie
`SELECT DeviceId, Temperature FROM MSFT_StorageReliabilityCounter` w tej samej przestrzeni nazw i dopasuj po `DeviceId` w kodzie (równość `string`). Zapytanie po `DeviceId` jest zgodne z dokumentacją i z danymi;
samo zapytanie bez asocjacji **nie było** uruchomione w sondzie (używała asocjacji) – Task 5 weryfikuje je na hoście testem `ReadDisks_AtLeastOneWithSize`.
`Temperature == 0` traktuj jako brak (VM: 0; spec i Task 3: „temperatura tylko > 0”). Pozostałe liczniki na hoście: `TemperatureMax` i `Wear` = `null` – nie używać.

### 1.8 NetworkAdapterInfo (`root\StandardCimv2`, `MSFT_NetAdapter`) – [MS](https://learn.microsoft.com/en-us/windows/win32/fwp/wmi/netadaptercimprov/msft-netadapter)

Surowe wartości (wszystkie instancje; `HI` = `HardwareInterface`, `V` = `Virtual`, `CP` = `ConnectorPresent`, `MCS` = `MediaConnectState`, `NPM` = `NdisPhysicalMedium`):

| Maszyna | `InterfaceDescription` | HI | V | CP | MCS | `Speed` [b/s] | NPM |
|---|---|---|---|---|---|---|---|
| Home, Pro | `Microsoft Hyper-V Network Adapter` | true | false | true | 2 | 10 000 000 000 | 0 |
| host | `WireGuard Tunnel` | **false** | **true** | false | 1 | 100 000 000 000 | 0 |
| host | `Intel(R) Ethernet Controller (3) I225-V` | true | false | true | 1 | 1 000 000 000 | **14** |
| host | `Xbox Wireless Adapter for Windows` | true | false | true | 1 | 600 000 000 | **19** |
| host | `Hyper-V Virtual Ethernet Adapter #2` (vEthernet WSL) | **false** | **true** | false | 1 | 10 000 000 000 | 0 |
| host | `Hyper-V Virtual Ethernet Adapter` (vEthernet Default Switch) | **false** | **true** | false | 1 | 10 000 000 000 | 0 |
| host | `MediaTek Wi-Fi 6E MT7922 (RZ616) 160MHz Wireless LAN Card` | true | false | **false** | **0** | 0 | **9** |

| Pole rekordu | Źródło / reguła | Decyzja |
|---|---|---|
| `Name` | `InterfaceDescription` (String, unikalna nazwa sterownika; nie `Name`, które jest nazwą połączenia typu „Ethernet”) | potwierdzone |
| lista „tylko fizyczne” | `HardwareInterface == true && Virtual == false` | potwierdzone (odp. (c)) |
| `Connected` | `MediaConnectState == 1` (MS: 0 Unknown, 1 Connected, 2 Disconnected) | potwierdzone |
| `SpeedBps` | `Speed` (UInt64, b/s); formatter używa tylko przy `Connected` | potwierdzone |
| `Kind` | `NdisPhysicalMedium`: **14** (802.3) → `Ethernet`; **9** (Native 802.11) lub **1** (Wireless LAN) → `WiFi`; inne → `Other` | potwierdzone (14 i 9 widziano na hoście, 1 tylko w dokumentacji) |

**Reguła „tylko fizyczne” (odp. (c)).** `HardwareInterface` rozróżnia karty fizyczne od wirtualnych: false dla WireGuard i obu vEthernet (Hyper-V), true dla Intel I225-V, Wi-Fi MediaTek,
Xbox Wireless Adapter i dla syntetycznej karty w VM. Dokumentacja: `HardwareInterface` – „interfejs jest dostarczany przez urządzenie sprzętowe”, `Virtual` – „adapter emuluje kartę fizyczną”.
Obie flagi razem są spójne na wszystkich 9 instancjach (HI = !V). Reguła: **zachowaj kartę tylko gdy `HardwareInterface` jest true i `Virtual` jest false.**
**Nie używaj `ConnectorPresent`** do tego celu: Wi-Fi hosta ma `ConnectorPresent=false`, choć jest fizyczne.
**Nie używaj `Speed`/`MediaConnectState` do filtrowania** (WireGuard zgłasza 100 Gb/s, połączony).
**Xbox Wireless Adapter** (klucz sprzętowy USB do pada) przechodzi filtr jako fizyczny (`NPM=19` Other) → trafi na listę jako `Karta sieciowa` z `Kind=Other`. To poprawne zachowanie wg źródła;
ukrycie go wymagałoby filtra po nazwie, którego nie robimy.
**Wi-Fi na hoście jest rozłączone** (`MCS=0`, `Speed=0`): `Connected=false`, prędkość pominięta.
**VM:** karta Hyper-V ma `NPM=0` (Unspecified) → `Kind=Other` (klucz „Karta sieciowa”), `MCS=2` (Disconnected w obu VM w chwili sondy), `Speed` 10 Gb/s – bez znaczenia przy rozłączonej.
Wartości `NdisPhysicalMedium` wg MS: 0 Unspecified, 1 Wireless LAN, 2 Cable Modem, 3 Phone Line, 4 Power Line, 5 DSL, 6 FC, 7 1394, 8 Wireless WAN, 9 Native 802.11, 10 BlueTooth, 11 Infiniband,
12 WiMAX, 13 UWB, 14 802.3, 15 802.5, 16 IRDA, 17 Wired WAN, 18 Wired Connection Oriented WAN, 19 Other.
Sonda zwróciła też `MissingProperties=["ifIndex"]` – nazwa właściwości to `InterfaceIndex` (MS), `ifIndex` istnieje tylko w cmdletach; nie używać.

### 1.9 WindowsInfo (rejestr `CurrentVersion`, `Win32_OperatingSystem`) – [MS Win32_OperatingSystem](https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-operatingsystem)

Klucz: `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion`. Rejestr nie ma strony MS opisującej te wartości (**brak MS**); potwierdzone odczytem na trzech maszynach. `DisplayVersion`, `EditionID`, `CurrentBuild`, `UBR` już są używane w istniejącym `WindowsSystemInfo`.

| Pole rekordu | Źródło | Home / Pro / host | Decyzja |
|---|---|---|---|
| `Edition` | rejestr `EditionID` (REG_SZ) → `WindowsEditions.DisplayName` | Home: `Core`; Pro: `Professional`; host: `Professional` | potwierdzone |
| `DisplayVersion` | rejestr `DisplayVersion` (REG_SZ) | VM: `26H2`; host: `25H2` | potwierdzone |
| `Build` | rejestr `CurrentBuild` (**REG_SZ**, np. `"26300"`) → `int` | VM: 26300; host: 26200 | potwierdzone |
| `Revision` | rejestr `UBR` (**REG_DWORD**) | VM: 9457; host: 9711 | potwierdzone |
| `InstallDate` | `Win32_OperatingSystem.InstallDate` (datetime; surowo `20250808200557.000000+120` – z offsetem lokalnym, `ToDateTime` poprawne) | Home: 2026-10-17 08:35; Pro: 2026-10-16 23:04; host: 2025-08-08 20:05 | potwierdzone |
| **nie używać** | rejestr `ProductName` | Home: `Windows 10 Home`; Pro: `Windows 10 Pro`; host: **`Windows 10 Pro`** (build 26200!) | **wypada** (patrz pkt 5 niżej) |

### 1.10 TpmInfo (`root\CIMV2\Security\MicrosoftTpm`, `Win32_Tpm`) – [MS](https://learn.microsoft.com/en-us/windows/win32/secprov/win32-tpm)

| Pole rekordu | Źródło | Home / Pro / host | Decyzja |
|---|---|---|---|
| `Enabled` | `IsEnabled_InitialValue` (boolean) | true na wszystkich trzech | potwierdzone |
| `Activated` | `IsActivated_InitialValue` (boolean) | true na wszystkich trzech | potwierdzone |
| `SpecVersion` | `SpecVersion` (String) | VM: `2.0, 0, 1.16`; host: `2.0, 0, 1.59` | potwierdzone |

Znaczenie wg MS: `IsEnabled_InitialValue` – „TPM is enabled”, `IsActivated_InitialValue` – „TPM is activated”; obie wartości są zapisane w chwili utworzenia instancji (stan na moment odczytu, wystarcza).
**„Włączony” w UI = `Enabled && Activated`** (jak w Tasku 3). `SpecVersion` = „major.minor specyfikacji TCG, poziom rewizji, poziom errata, wszystko w hex”; przykład MS: `1.2, 2, 0`.
Tu: `2.0, 0, 1.16` → TPM 2.0, rewizja 0, errata 1.16 (hex). Do ekranu bierz **pierwszy człon przed przecinkiem** (`"2.0"`), jak w Tasku 3. Gdy dane niedostępne, MS zwraca napis `"Not Supported"`
– traktuj go jak brak wersji (nie pokazuj „Not Supported” jako wersji).
Brak TPM: klasa/namespace nie była sondowana na maszynie bez TPM – patrz „Wypada / ograniczenia” pkt 5.

### 1.11 SecureBootState – rejestr + `GetFirmwareType` + `Confirm-SecureBootUEFI` – [MS Confirm-SecureBootUEFI](https://learn.microsoft.com/en-us/powershell/module/secureboot/confirm-securebootuefi)

| Źródło | Home / Pro / host | Decyzja |
|---|---|---|
| `HKLM\SYSTEM\CurrentControlSet\Control\SecureBoot\State` → `UEFISecureBootEnabled` (**REG_DWORD**) | VM Home/Pro: 1; host: **0** | potwierdzone odczytem, **brak MS** (rejestr nieudokumentowany) |
| `Confirm-SecureBootUEFI` (cmdlet, do porównania) | VM: True / True; host: **False** | zgodne z rejestrem na wszystkich trzech |
| `GetFirmwareType` / `$env:firmware_type` | wszędzie UEFI (2) | potwierdzone |

**Reguła (Task 5):** `GetFirmwareType == 1 (Bios)` → `LegacyBios`; `== 2 (Uefi)` → odczytaj `UEFISecureBootEnabled`: 1 → `On`, 0 → `Off`, brak klucza/wartości → `Unknown`;
`GetFirmwareType == 0` lub błąd → `Unknown`. Rejestr i cmdlet dały identyczny wynik na trzech maszynach, więc cmdlet (wymaga admina, ładuje moduł, 50–3300 ms) nie jest potrzebny.
Zaskoczenie hosta: **Secure Boot jest wyłączony** (`UEFISecureBootEnabled=0`, `Confirm-SecureBootUEFI=False`) mimo UEFI – ekran pokaże „Wyłączony”, to prawda, nie błąd czytnika.
Zachowanie na maszynie z BIOS Legacy (brak klucza `State`) nie było sondowane – reguła nie czyta rejestru, gdy firmware = Bios.

### 1.12 VbsInfo (`root\Microsoft\Windows\DeviceGuard`, `Win32_DeviceGuard`) – [MS: Enable memory integrity](https://learn.microsoft.com/en-us/windows/security/hardware-security/enable-virtualization-based-protection-of-code-integrity)

| Pole rekordu | Źródło | Home / Pro / host | Decyzja |
|---|---|---|---|
| `Status` | `VirtualizationBasedSecurityStatus` (UInt32) | VM Home/Pro: **0**; host: **2** | potwierdzone |
| `HvciRunning` | `SecurityServicesRunning` (UInt32[]) **zawiera 2** | VM: `[0]`; host: `[0]` | potwierdzone (odp. – poniżej) |

Dokumentacja MS (strona „Enable memory integrity”, zaktualizowana 2026-10-08): `VirtualizationBasedSecurityStatus`: 0 = VBS nie jest włączone, 1 = włączone, ale nie działa, 2 = włączone i działa.
`SecurityServicesRunning` (tablica): 0 = żadna usługa nie działa, 1 = Credential Guard, **2 = memory integrity (HVCI)**, 3 = System Guard Secure Launch, 4 = SMM Firmware Measurement,
5 = Kernel-mode Hardware-enforced Stack Protection, 6 = ta sama w trybie audytu, 7 = Hypervisor-Enforced Paging Translation. Tak, **2 = HVCI** i tablica może zawierać kilka wartości.
Host: VBS działa (2), ale `SecurityServicesRunning=[0]` → HVCI **nie** działa → ekran „VBS działa, HVCI nie działa” (zgodnie z Taskiem 3). VM: VBS wyłączone (0).
Odczyt działa na Home i Pro (Home: `VirtualizationBasedSecurityStatus=0`, `SecurityServicesRunning=[0]`).
`Status` bez instancji klasy (inna edycja/uprawnienia) → `Failed`/`null` wg Tasku 5 („brak danych”).

### 1.13 BitLockerVolume (`root\CIMV2\Security\MicrosoftVolumeEncryption`, `Win32_EncryptableVolume`) – [MS](https://learn.microsoft.com/en-us/windows/win32/secprov/win32-encryptablevolume)

| Pole rekordu | Źródło | Home / Pro / host | Decyzja |
|---|---|---|---|
| `DriveLetter` | `DriveLetter` (String, np. `"C:"`; NULL gdy wolumin bez litery) | Home/Pro: `C:`; host: `C:`, `D:`, `E:` | potwierdzone (pomijaj woluminy bez litery) |
| `ConversionStatus` | `ConversionStatus` (UInt32) | Home: **2**; Pro: **2**; host: 0, 0, 0 | potwierdzone (odp. (d)) |
| `ProtectionStatus` | `ProtectionStatus` (UInt32) | Home: 0; Pro: 0; host: 0, 0, 0 | potwierdzone |

**Odp. (d):** tak – `ConversionStatus` jest właściwością `Win32_EncryptableVolume` i **zwraca wartość na Windows 11 Home (VM)**, na Pro (VM) i na hoście (Pro). Dokumentacja MS wymienia ją w sekcji właściwości
(w bloku `Syntax` jest tylko `ProtectionStatus`, ale tabela wartości `ConversionStatus` jest w dokumencie): 0 Fully decrypted, 1 Fully encrypted, 2 Encryption in progress, 3 Decryption in progress, 4 Encryption paused, 5 Decryption paused.
`ProtectionStatus` wg MS: 0 = ochrona wyłączona (wolumin niezaszyfrowany, częściowo zaszyfrowany lub klucz dostępny jawnie), 1 = włączona, **2 = nieznany** (np. wolumin zablokowany).
Obie VM (Home i Pro) pokazują `ConversionStatus=2` (szyfrowanie w toku) przy `ProtectionStatus=0` – to prawdziwe dane z checkpointu `czysta-26H2`, więc na takiej VM ekran powie „C: szyfrowanie w toku, ochrona wyłączona”.
Mapowanie w Tasku 3 (1 → włączona, 0 → wyłączona) nie obejmuje `ProtectionStatus=2`; patrz „Wypada / ograniczenia” pkt 6.
Wymaga uprawnień administratora (sekcja „Security Considerations” MS) – sonda i program działają jako administrator.

## 2. Odpowiedzi na pytania (a)–(d) z zadania 1, krok 4

**(a) Czy `ConfiguredClockSpeed` na hoście daje 6000?** Tak. Host: oba moduły (G Skill, DDR5, `SMBIOSMemoryType=34`) mają `ConfiguredClockSpeed=6000` (UInt32), `Speed=4800`. VM: `ConfiguredClockSpeed=0`, `Speed=null`.
Czyli „6000 MT/s” z makiety = `ConfiguredClockSpeed`; `Speed` (4800) to prędkość znamionowa i to nie jest wartość do pokazania, gdy jest `ConfiguredClockSpeed`.

**(b) Typ `qwMemorySize` i który podklucz klasy pasuje?** `HardwareInformation.qwMemorySize` jest **REG_QWORD** (`Kind=QWord`) – host: NVIDIA `0000` = 17 094 934 528 (`0x3FAF00000`), AMD `0001` i `0002` = 536 870 912 (512 MiB).
Na VM nie istnieje (Hyper-V Video, podklucz `0000`). Pasujący podklucz: po `MatchingDeviceId` ⊂ `PNPDeviceID` (porównanie bez wielkości liter, `StartsWith`), z fallbackiem `DriverDesc == Name`; szczegóły w pkt 1.4 i pkt 3.1.

**(c) Czy `MSFT_NetAdapter.HardwareInterface` odróżnia karty fizyczne od wirtualnych?** Tak, razem z `Virtual`. Host: WireGuard (`HI=false`, `V=true`), `vEthernet (Default Switch)` i `vEthernet (WSL ...)` (oba `HI=false`, `V=true`) odpadają; Intel I225-V, MediaTek Wi-Fi i Xbox Wireless Adapter przechodzą (`HI=true`, `V=false`).
VM: syntetyczna karta Hyper-V ma `HI=true`, `V=false` (z perspektywy gościa jest „fizyczna”). `ConnectorPresent` do filtrowania się nie nadaje (Wi-Fi hosta: false).

**(d) Czy `ConversionStatus` jest właściwością `Win32_EncryptableVolume` na Home i Pro?** Tak, na obu (Home VM: `C:` ConversionStatus=2, ProtectionStatus=0; Pro VM: to samo; host: C:, D:, E: = 0/0).

## 3. Reguły dla Taska 5 (zebrane)

### 3.1 GPU ↔ podklucz klasy wyświetlania
Zob. pkt 1.4 (reguła w 3 krokach, odczyt `Registry64`, `QWord` → `ulong`, fallback `GpuMemory.Resolve`). Podsumowanie faktów hosta, na których stoi reguła:
`MatchingDeviceId` ma małe litery i bywa bez `SUBSYS` (NVIDIA), `PNPDeviceID` ma wielkie litery i `SUBSYS`/`REV`/sufiks instancji; `DriverDesc` = `Win32_VideoController.Name`;
iGPU AMD ma podklucze (`0001`, `0002`), lecz brak instancji `Win32_VideoController` (nie dodawać karty z samego rejestru); `qwMemorySize` jest REG_QWORD; `MemorySize` DWord nasycone (`0xFFF00000`).

### 3.2 Sieć
Pkt 1.8: filtr `HardwareInterface && !Virtual`; `Kind` z `NdisPhysicalMedium` (14 → Ethernet; 9, 1 → Wi-Fi; reszta → Other); `Connected = MediaConnectState == 1`; `Name = InterfaceDescription`.

### 3.3 TPM
`Enabled = IsEnabled_InitialValue`, `Activated = IsActivated_InitialValue`; „włączony” = obie true. `SpecVersion` = pierwszy człon przed przecinkiem (`"2.0, 0, 1.16"` → `"2.0"`); `"Not Supported"` = brak wersji.

### 3.4 Secure Boot
Rejestr `...\SecureBoot\State\UEFISecureBootEnabled` (DWord) tylko gdy `GetFirmwareType == 2` (Uefi); 1 → On, 0 → Off, brak → Unknown; Bios (1) → LegacyBios. Wartości zgodne z `Confirm-SecureBootUEFI` na wszystkich trzech maszynach.

### 3.5 VBS / HVCI
`Status = VirtualizationBasedSecurityStatus` (0/1/2), `HvciRunning = SecurityServicesRunning.Contains(2)` (2 = memory integrity wg MS). Host: `Status=2`, `HvciRunning=false`.

### 3.6 BitLocker
Wszystkie instancje z niepustym `DriveLetter`; `ConversionStatus` 0–5, `ProtectionStatus` 0–2 (MS). Dostępne na Home i Pro.

### 3.7 Dyski
`Number = int.Parse(DeviceId)`, temperatura z `MSFT_StorageReliabilityCounter` po `DeviceId`; VM: licznik jest, ale `Temperature=0` → pole znika; host: 38/36/35 °C. Kody `BusType`, `MediaType`, `HealthStatus` – pkt 1.7.

## 4. Wypada / ograniczenia

Nie wypadło żadne **pole** z tabeli §4.2 z powodu braku źródła; poniżej zastrzeżenia, które Task 5 musi znać. Pustki VM (brak EDID, brak audio, zerowa temperatura, brak `qwMemorySize`) to **dane**
(Hyper-V nie ma tych urządzeń), a nie powód do usunięcia pola potwierdzonego na hoście i w dokumentacji.

1. **`HardwareInformation.qwMemorySize` – brak dokumentacji MS.** Wyszukiwanie (2026-10-19) nie znalazło strony MS opisującej `qwMemorySize`. MS opisuje tylko `HardwareInformation.MemorySize` jako ULONG w MB
   ([archiwum](https://learn.microsoft.com/en-us/windows-hardware/drivers/display/setting-hardware-information-in-the-registry)), a host zwraca w nim DWord w bajtach (`-1048576` = `0xFFF00000`, wartość nasycona), więc ta dokumentacja jest nieaktualna.
   Wartość `qwMemorySize` jest potwierdzona odczytem na hoście (NVIDIA REG_QWORD 17 094 934 528; 2×AMD REG_QWORD 536 870 912), ale spełnia tylko jeden z dwóch warunków repo. Spec §4.2 i zadanie 5 przewidują to źródło wprost
   (alternatywy brak – `AdapterRAM` jest 32-bitowe i kłamie). **Decyzja:** używać, bo to jedyne źródło dające „16 GB”; gdy autor odmówi, pole „GB” przy karcie wypada, a pozostają nazwa i sterownik.
   Fallback `AdapterRAM` jest bezpieczny tylko przy wartościach poniżej nasycenia.
2. **user32 (`EnumDisplayDevicesW`, `EnumDisplaySettingsW`): potwierdzone dokumentacją MS, bez odczytu.** Sonda nie wywołuje user32. Próba odczytu z procesu automatu pomiarowego na hoście zwróciła zero adapterów (proces bez interaktywnego pulpitu),
   więc rozdzielczość, odświeżanie i identyfikator interfejsu monitora nie zostały zmierzone na żadnej maszynie. Autor weryfikuje to w Tasku 5, Step 3 (`HostReadout_PrintsAllParts` na hoście w sesji użytkownika, oczekiwane: 2 monitory, ich rozdzielczość i Hz).
   Bez wyniku z hosta pola `Width`/`Height`/`RefreshHz` zostają nieoparte na odczycie; `GetFirmwareType` natomiast potwierdzony (host: 2).
   Uwaga techniczna: `DEVMODEW` ma 220 bajtów, `dmSize` (offset 68) trzeba ustawić na `sizeof(DEVMODE)`; pole `dmDisplayFrequency` ma offset 184, `dmPelsWidth` 172, `dmPelsHeight` 176.
3. **Typy SMBIOS 29, 30, 35.** Strona MS wymienia dla `MemoryType`/`SMBIOSMemoryType` tylko 24 (DDR3), 25, 26 (DDR4); DDR5 (34) potwierdzone odczytem hosta. Wartości 29 (LPDDR3), 30 (LPDDR4), 35 (LPDDR5) z Taska 3 pochodzą ze specyfikacji
   SMBIOS (DMTF), której tu nie pobierano, i nie były widziane na żadnej maszynie. Nieznany typ i tak jest pomijany przez formatter, więc ryzyko to najwyżej brak etykiety LPDDR; **nie** potwierdzone w tym dokumencie.
4. **`Win32_PhysicalMemory.Speed` – jednostka w dokumentacji MS („nanoseconds”) nie zgadza się z danymi (4800).** Dlatego `Speed` to tylko fallback, nie źródło główne (pkt 1.2).
5. **Brak TPM i BIOS Legacy – nie sondowane.** Na trzech maszynach TPM istnieje (VM: wirtualny TPM 2.0; host: fizyczny/firmware), a firmware to UEFI. Nie wiemy, czy brak TPM daje pustą kolekcję instancji, czy wyjątek
   `ManagementException` (np. `InvalidNamespace`/`NotFound`) przy łączeniu z `root\CIMV2\Security\MicrosoftTpm`. Task 5: złap `ManagementException` o tych kodach i zwróć `Done(null)` („brak”), inne błędy → `Failed`. To reguła ostrożności, nie zmierzony fakt.
6. **`ProtectionStatus=2` (MS: nieznany, np. wolumin zablokowany) nie ma mapowania w Tasku 3** (tam 1 → włączona, wszystko inne → wyłączona). Na trzech maszynach nie wystąpił. Zalecenie dla Taska 3/5: traktować 2 jak „brak danych” zamiast „ochrona wyłączona”.
7. **Win32_VideoController i niezgodne sterowniki.** MS: „Hardware that is not compatible with WDDM returns inaccurate property values”. Na trzech maszynach (WDDM) nie dotyczy; nie mierzono na starych kartach.
8. **Zdarzenie sieciowe VM.** Obie VM miały `MediaConnectState=2` (rozłączona) – nie wiadomo, czy to cecha checkpointu (brak przełącznika), czy chwilowy stan. Wystarcza dla formatowania („rozłączona”); reguła niezmieniona.
9. **Rejestr – brak stron MS.** Wartości `CurrentVersion\*`, `SecureBoot\State\UEFISecureBootEnabled` i klucze klasy wyświetlania nie mają opisu w MS Learn (poza archiwalnym `MemorySize`). Potwierdzone wyłącznie odczytem na 3 maszynach (+ zgodność `UEFISecureBootEnabled` z udokumentowanym
   `Confirm-SecureBootUEFI`). `CurrentVersion\EditionID`, `DisplayVersion`, `CurrentBuild`, `UBR` są już w użyciu w produkcie.
10. **Warunki testu.** Wyniki dla Home i Pro pochodzą z jednego checkpointu `czysta-26H2` każda (build 26300.9457); host to build 26200.9711 (25H2), nie 26H2. Rozbieżności między maszynami wynikają z różnicy sprzętu, nie wersji, w żadnej klasie odczytu nie zaobserwowano różnicy Home↔Pro poza `EditionID`/`ProductName`.

## 5. Zaskoczenie hosta: `ProductName` kłamie

Rejestr `ProductName` zwraca **`Windows 10 Pro`** na hoście (build **26200**, czyli Windows 11 25H2) i `Windows 10 Home` / `Windows 10 Pro` na obu VM z buildem 26300 (26H2). Wartość jest niezmieniona od Windows 10 i nie nadaje się
do rozpoznania wersji ani edycji. **Reguła:** nigdy nie używać `ProductName`. Edycja pochodzi z `EditionID` (`Core` → „Home”, `Professional` → „Pro” przez `WindowsEditions.DisplayName`),
„Windows 11” vs „Windows” wynika z numeru builda (≥ 22000), wersja z `DisplayVersion`.

## 6. Źródła (wszystkie pobrane 2026-10-19)

- [Win32_Processor](https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-processor) – MS
- [Win32_PhysicalMemory](https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-physicalmemory) – MS
- [Win32_PhysicalMemoryArray](https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-physicalmemoryarray) – MS
- [Win32_BaseBoard](https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-baseboard) – MS
- [Win32_BIOS](https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-bios) – MS
- [Win32_ComputerSystem](https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-computersystem) – MS
- [Win32_VideoController](https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-videocontroller) – MS (uwaga o WDDM, `AdapterRAM` uint32)
- [Setting Hardware Information in the Registry](https://learn.microsoft.com/en-us/windows-hardware/drivers/display/setting-hardware-information-in-the-registry) – MS-archiwum
- [WmiMonitorID](https://learn.microsoft.com/en-us/windows/win32/wmicoreprov/wmimonitorid), [WmiMonitorBasicDisplayParams](https://learn.microsoft.com/en-us/windows/win32/wmicoreprov/wmimonitorbasicdisplayparams) – MS
- [Win32_SoundDevice](https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-sounddevice) – MS
- [MSFT_PhysicalDisk](https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/msft-physicaldisk), [MSFT_StorageReliabilityCounter](https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/msft-storagereliabilitycounter) – MS
- [MSFT_NetAdapter](https://learn.microsoft.com/en-us/windows/win32/fwp/wmi/netadaptercimprov/msft-netadapter) – MS
- [Win32_OperatingSystem](https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-operatingsystem) – MS
- [Win32_Tpm](https://learn.microsoft.com/en-us/windows/win32/secprov/win32-tpm) – MS
- [Confirm-SecureBootUEFI](https://learn.microsoft.com/en-us/powershell/module/secureboot/confirm-securebootuefi) – MS
- [GetFirmwareType](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getfirmwaretype), [FIRMWARE_TYPE](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ne-winnt-firmware_type) – MS
- [Enable memory integrity (Win32_DeviceGuard)](https://learn.microsoft.com/en-us/windows/security/hardware-security/enable-virtualization-based-protection-of-code-integrity) – MS
- [Win32_EncryptableVolume](https://learn.microsoft.com/en-us/windows/win32/secprov/win32-encryptablevolume) – MS
- [EnumDisplayDevicesW](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumdisplaydevicesw), [EnumDisplaySettingsW](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumdisplaysettingsw) – MS
