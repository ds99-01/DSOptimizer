# repair.system-protection – Ochrona systemu

## Werdykt 26H2
Portować jako `kind=tweak`, `risk=safe` (kategoria „Naprawy”, Etap 4b partia 1): na świeżej 26H2 Home i Pro ochrona systemu jest wyłączona, więc żaden punkt przywracania nie powstaje; `Enable-ComputerRestore -Drive 'C:\'` włącza ją tak samo jak okno „Ochrona systemu”, a skutek da się odczytać z rejestru. Cofnięcie wyłącza ochronę, ale nie usuwa istniejących punktów.

## Co zmienia
Skrypt (`script.detect` / `apply` / `undo`), po nazwach poleceń (testy je mockują):
- `Enable-ComputerRestore -Drive 'C:\'` (apply) i `Disable-ComputerRestore -Drive 'C:\'` (undo). Limit miejsca ustawia VSS sam (3% woluminu, ta sama wartość co przy włączeniu z okna); **bez `vssadmin resize`**, więc `vssadmin` w ogóle nie jest wołany.
- Odczyt (tylko odczyt, helpery mockowane w testach): `Get-DsoSystemVolumeId` (CIM `Win32_Volume`, `DriveLetter='C:'`, `DeviceID` = `\\?\Volume{GUID}\`) i `Get-DsoProtectionClients` (wartość REG_MULTI_SZ `{09F7EDC5-294E-4180-AF6A-FB0E6A0E9513}` w `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SPP\Clients`, widok 64-bit).

## Źródła
- Test na VM 26H2 Home i Pro (build 26300.9457), 2026-10-22: `docs/zrodla/2026-10-22-etap4b-1-pomiary.md`, tabela decyzji i sekcja 7.
- Reguła skojarzenia wpisu z woluminem C: jest odtworzeniem `src/DSOptimizer.Windows/Apps/SystemProtectionReader.cs` (Etap 4a, Task 2): wpis włączony, gdy któryś element wartości zawiera GUID woluminu C: z `Win32_Volume`.
- Brak dokumentacji MS sprawdzonej w tym zadaniu; dowód: test na VM opisany niżej.

## Wartości domyślne na świeżym 26H2
Home i Pro (odczyt 2026-10-22): `SPP\Clients` bez wpisu, `vssadmin list shadowstorage` = „No items found”, `SystemRestore\DiskPercent` = 15 (stara wartość, nie odpowiada limitowi), `SystemRestorePointCreationFrequency` brak.

## Detekcja i undo
- Detect: wpis woluminu C: w `SPP\Clients` = `Applied`, brak wpisu (albo klucza/wartości) = `NotApplied`, niezależnie od tego, czy kopie (punkty) nadal istnieją. Nie da się ustalić woluminu C: albo odczytać rejestru = błąd skryptu = stan nieznany (nie `NotApplied`).
- Apply kończy się sukcesem dopiero po odczycie zwrotnym (wpis C: jest); inaczej błąd „Ochrona systemu nie została włączona…”. Już włączona = `AlreadyApplied` (silnik, przed skryptem).
- Undo: `Disable-ComputerRestore`, potem odczyt zwrotny (wpisu nie ma). Nie przywraca żadnego snapshotu (stan to jeden wpis), więc `original` i snapshot nie mają znaczenia.

## Ryzyka i ostrzeżenia
- `risk=safe`.
- Ostrzeżenia w katalogu: „Cofnięcie wyłącza ochronę: nowe punkty nie powstaną, a z istniejących nie przywrócisz systemu (zostają na dysku).” oraz „Samo włączenie nie tworzy punktu – pierwszy powstanie przed zmianą albo po „Utwórz punkt przywracania teraz”.”
- Pierwsze ostrzeżenie to skrót tekstu zmienionego względem specu (pomiar przeczy „usuwa wszystkie punkty”, Ruling R7). Pełny tekst R7: „Wyłączenie ochrony systemu blokuje tworzenie nowych punktów przywracania i przywracanie systemu z istniejących. Istniejące punkty zostają na dysku (zajmują miejsce) i wrócą po ponownym włączeniu ochrony; usunąć je można w oknie »Ochrona systemu« (Konfiguruj → Usuń).”
- Limit miejsca ustawia Windows (3% dysku C. na zmierzonej maszynie); program go nie zmienia.
- Samo włączenie nie tworzy punktu. Wyłączenie ochrony „z kasowaniem punktów” (`vssadmin delete shadows`) to osobna, nieodwracalna operacja, której tu nie ma i której nie mierzono.

## Test VM
Przypadek `repair-system-protection` (`Flow=Full`, `Snapshot`: wartość `{09F7EDC5-…}` w `SPP\Clients`) dopisany w `tests/vm/cases.psd1` w Tasku 7.

Przebieg VM (Task 10, 2026-10-22, pełny `tests/vm/Invoke-VmTests.ps1` na gałęzi `etap4b-1`; raporty poza gitem): Home i Pro po 722 zaliczone / 0 błędów / 25 pominiętych (6 przypadków ze `Skip` spoza kategorii Naprawy). `repair-system-protection`: **Passed** na Home i Pro – `NotApplied` → apply `Applied` → detekcja `Applied` → undo `Undone` → `NotApplied`, `CheckAfterApply`/`CheckAfterUndo` (wpis woluminu C: w `SPP\Clients`) zaliczone, stan końcowy wartości `SPP\Clients` = stan przed. Wyścigu zapisu `SPP\Clients` po `Enable-ComputerRestore` (minor z Tasku 7) nie zaobserwowano.

## Obserwacja skutku
Metoda: odczyt `SPP\Clients`, `vssadmin list shadowstorage` i `Get-ComputerRestorePoint` przed i po zmianie; punkt tworzony po włączeniu; surowe wyniki poza gitem.
- **Home** (`DSO-26H2-Home`, 26300.9457), 2026-10-22: włączenie z okna „Ochrona systemu” (`SystemPropertiesProtection.exe` → Konfiguruj → „Włącz ochronę systemu”) i `Enable-ComputerRestore -Drive 'C:\'` (24–26 ms) dają ten sam stan: wpis w `SPP\Clients`, shadow storage powstaje przy pierwszym punkcie z limitem 2 052 367 196 B = 3,0% woluminu 68 412 239 872 B. `Disable-ComputerRestore -Drive 'C:\'` usuwa wpis, ale punkt zostaje (`Get-ComputerRestorePoint` i `vssadmin list shadows` go widzą po 3 s, 60 s i po restarcie VM); po ponownym `Enable-ComputerRestore` punkt jest znów na liście. Przy wyłączonej ochronie punktu nie da się utworzyć (WMI: usługa wyłączona).
- **Pro** (`DSO-26H2-Pro`, 26300.9457), 2026-10-22: identycznie jak na Home (przebiegi P1 i A2).

## Reddit
Do uzupełnienia w części E.
