# repair.wu-reset-full – Zresetuj Windows Update i usuń blokady

## Werdykt 26H2
Portować jako `kind=action`, `risk=experimental` (kategoria „Naprawy”, Etap 4b partia 1, handler C# `wu-reset-full`): wszystko z `repair.wu-reset` plus eksport i usunięcie polityk Windows Update oraz przywrócenie typów startu usług WU do wartości świeżej 26H2. Na Pro komplet polityk testowych daje w Ustawieniach „Ustawieniami usługi Windows Update zarządza Twoja organizacja.” i nieaktywny przycisk, a po usunięciu klucza komunikat znika.

## Co zmienia
Handler C# (`src/DSOptimizer.Core/Repair/WindowsUpdateReset.cs`, wariant `full`). Kroki ponad `repair.wu-reset`:
1. Najpierw odmowa: `IDomainInfo.IsDomainJoined()` (`Win32_ComputerSystem.PartOfDomain`) = `true` → `Refused` „Komputer jest w domenie: polityki Windows Update ustawia administrator domeny.”; `null` → `Refused` „Nie udało się sprawdzić, czy komputer jest w domenie.” Nic nie jest zmieniane.
2. Odczyt usług obejmuje też `UsoSvc` (typ startu do kroku 5; `UsoSvc` nie jest zatrzymywany).
3. Kopia polityk, przed jakąkolwiek zmianą: gdy klucz `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate` istnieje – `%SystemRoot%\System32\reg.exe export "<klucz>" "<backups>\wu-policies-<rrrrMMdd-HHmmss>.reg" /y` (`System32ToolRunner`, eksport obejmuje podklucz `AU`) i sprawdzenie, że plik ma rozmiar > 0. `<backups>` = `%ProgramData%\DSOptimizer\backups`, tworzony z zaufanym ACL przy pierwszej kopii (`SecureDirectory.Ensure`), nie przy starcie programu. Nieczytelny klucz, eksport z kodem ≠ 0, pusty plik albo niedostępny katalog kopii → `Failed` „… polityki zostają bez zmian. Nic nie zmieniono.” (usługi i foldery nietknięte). Brak klucza → bez pliku, linia „Brak polityk Windows Update.”
4. Zatrzymanie usług i przeniesienie folderów jak w `repair.wu-reset` (wszystko albo nic). Gdy ten etap się nie uda, polityki zostają, a plik `.reg` zostaje w `<backups>` (ścieżka w wyniku).
5. Usunięcie polityk, po przeniesieniu folderów, przed startem usług: `IRegistry.DeleteKeyTree` klucza z podkluczami (tylko gdy kopia powstała), z odczytem zwrotnym (klucza nie ma). Błąd usunięcia albo klucz nadal jest → `Failed` (po przywróceniu usług) ze ścieżką kopii i podpowiedzią `reg import "<plik>"`.
6. Start usług jak w `repair.wu-reset`.
7. Typy startu (tylko gdy wszystko wyżej się udało): zapis `<backups>\wu-services-<rrrrMMdd-HHmmss>.txt` (UTF-8 z BOM, zaufany ACL; nazwa i typ startu sprzed oraz podpowiedź `sc.exe config`), potem `IServiceManager.SetStartType` tylko tam, gdzie typ różni się od tabeli niżej, z odczytem zwrotnym. Nieudany zapis kopii = ostrzeżenie i typy startu bez zmian; odmowa zapisu typu albo rozjazd odczytu = ostrzeżenie z nazwą usługi, bez porażki całości.
8. Stałe ostrzeżenie w wyniku: „Na komputerze zarządzanym przez Intune/MDM usunięte polityki mogą wrócić przy następnej synchronizacji.” (wykrywania MDM w tej partii nie ma).

Gdy przebieg zatrzyma się po kroku 5, a przed krokiem 7 – nie było żadnego folderu do przeniesienia albo nie udało się ponownie uruchomić usług – wynik to `Failed`, a nagłówek kończy się zdaniami „Polityki Windows Update zostały już usunięte (kopia: <plik>; przywrócenie: reg import "<plik>").” (tylko gdy polityki były i zostały usunięte) oraz „Typy startu usług nie zostały zmienione.”; ostrzeżenie MDM jest w wyniku także wtedy.

Tabela typów startu (ta sama na Home i Pro):

| Usługa | Typ startu po resecie |
|---|---|
| `wuauserv` | Manual |
| `bits` | AutomaticDelayed |
| `cryptsvc` | Automatic |
| `UsoSvc` | AutomaticDelayed |

`msiserver`, `WaaSMedicSvc` i `DoSvc` nigdy nie są zmieniane: zapis typu startu kończy się odmową 5 także dla Administratora (pomiar), a `DoSvc` ma różne wartości na Home i Pro. Wynik wspomina o którejś tylko, gdy jest wyłączona (`Disabled`). Bez rejestracji DLL (`regsvr32`), bez modułów spoza systemu, bez ruszania `WaaSMedicSvc`.

## Źródła
- Test na VM 26H2 Home i Pro (build 26300.9457), 2026-10-22: `docs/zrodla/2026-10-22-etap4b-1-pomiary.md`, tabela decyzji pkt 3, 3h i 4, sekcje 3 i 4 (polityki: zapis testowy, widok Ustawień razem i osobno, `reg export`/`delete`/`import`).
- Brak dokumentacji MS sprawdzonej w tym zadaniu (strony polityk Windows Update nie były pobierane); dowód: test na VM opisany niżej. Skutku polityk na sam skan (offline) nie zmierzono, więc diagnoza i opis nie obiecują, że każda wartość blokuje aktualizacje.

## Wartości domyślne na świeżym 26H2
Odczyt 2026-10-22: klucz `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate` – brak (Home i Pro). Typy startu: jak w tabeli wyżej (Home i Pro), `msiserver` Manual, `WaaSMedicSvc` Manual, `DoSvc` Home Manual / Pro AutomaticDelayed. `PartOfDomain` = False na obu VM.

## Detekcja i undo
Nie dotyczy: `kind=action` bez detekcji i cofnięcia (bez journala). Ręczny powrót: `reg import "<backups>\wu-policies-<znacznik>.reg"` (pomiar: przywraca oba klucze 1:1) i typy startu z `wu-services-<znacznik>.txt` (`sc.exe config <nazwa> start= …`). Ścieżki obu plików są w wyniku.

## Ryzyka i ostrzeżenia
- `risk=experimental`: usuwa ustawienia blokujące albo opóźniające aktualizacje, które mógł świadomie ustawić właściciel albo inne narzędzie „optymalizujące”; kopia `.reg` w `%ProgramData%\DSOptimizer\backups`.
- Zmienia typy startu `wuauserv`, `bits`, `cryptsvc`, `UsoSvc` na wartości świeżej 26H2; poprzednie w pliku tekstowym obok kopii.
- Odmowa w domenie; na komputerze z Intune/MDM polityki mogą wrócić przy synchronizacji.
- Wszystkie ostrzeżenia `repair.wu-reset` (historia aktualizacji, chwilowe zatrzymanie usług z zależnymi, kopie folderów `.dso-*`).
- Historia aktualizacji w Ustawieniach zostanie wyczyszczona (zostać mogą tylko wpisy definicji Microsoft Defender); aktualizacje pobiorą się od nowa.

## Test VM
Przypadek `repair-wu-reset-full` (`Flow=Execute`): `Setup` zapisuje 6 polityk testowych z pomiaru (`WUServer`/`WUStatusServer` = `http://dso-wsus.invalid:8530`, `DisableWindowsUpdateAccess`/`SetDisableUXWUAccess` = 1, `AU\NoAutoUpdate` = 1, `AU\UseWUServer` = 1) i ustawia `UsoSvc` na `Disabled`; `Check` wymaga: klucza polityk nie ma, plik `wu-policies-*.reg` w `C:\ProgramData\DSOptimizer\backups` zawiera `WUServer`, typy startu = tabela wyżej, `wuauserv`/`bits`/`cryptsvc` działają. `Cleanup` usuwa klucz polityk i katalog kopii (VM i tak wraca do checkpointu). Dopisany w `tests/vm/cases.psd1` w Tasku 6.

Przebieg VM (Task 10, 2026-10-22, pełny `tests/vm/Invoke-VmTests.ps1` na gałęzi `etap4b-1`; raporty poza gitem): Home i Pro po 722 zaliczone / 0 błędów / 25 pominiętych (6 przypadków ze `Skip` spoza kategorii Naprawy). `repair-wu-reset-full`: **Passed** na Home i Pro (`Executed`; kopia `wu-policies-*.reg` z `WUServer`, `wu-services-*.txt`, klucz polityk usunięty, typy startu jak na świeżym 26H2, usługi działają). Uwaga: w pierwszym przebiegu samych przypadków Napraw (`repair-1`) na Home jeden raz `bits` nie wystartował ponownie (`Failed` „Nie udało się ponownie uruchomić usług: bits.”, `NotRestored`: „Nie można uruchomić usługi bits”) – uczciwy wynik, ale przyczyny nie ustalono: w 5 kolejnych próbach (także zaraz po `repair.wu-reset`, jak w tamtym przebiegu) i w 2 kolejnych przebiegach `bits` startował; do obserwacji.

## Obserwacja skutku
Metoda: sonda eksploracji (`tests/vm/explore/Invoke-E4B1Exploration.ps1`): zapis 6 polityk razem i każdej osobno, zrzut strony Windows Update w Ustawieniach (komunikat organizacji, stan przycisku „Sprawdź aktualizacje”), `reg export` / `reg delete` / `reg import` z porównaniem wartości, `ChangeServiceConfig` dla każdej usługi WU; surowe wyniki poza gitem.
- **Home** (`DSO-26H2-Home`, 26300.9457), 2026-10-22: 6 polityk razem i każda osobno – brak komunikatu w Ustawieniach, „Sprawdź aktualizacje” aktywny (skutku widocznego w UI nie ma; skutku na skan offline nie da się zmierzyć). `reg export` klucza zapisuje też `AU` (jeden plik), `reg delete` usuwa całość, `reg import` przywraca 1:1. `ChangeServiceConfig`: `wuauserv`, `bits`, `cryptsvc`, `UsoSvc` OK; `msiserver`, `WaaSMedicSvc`, `DoSvc` odmowa 5. Reset usług i folderów jak w `repair.wu-reset`.
- **Pro** (`DSO-26H2-Pro`, 26300.9457), 2026-10-22: 6 polityk razem → „Ustawieniami usługi Windows Update zarządza Twoja organizacja.” i nieaktywny przycisk; osobno komunikat i blokadę daje tylko `SetDisableUXWUAccess`, pozostałe 5 bez widocznego skutku. Po usunięciu klucza komunikat znika, `reg import` przywraca stan 1:1. `ChangeServiceConfig` jak Home (`wuauserv` Disabled i powrót – WaaSMedic nie przywraca w 10 s). Reset usług i folderów oraz historia aktualizacji jak w `repair.wu-reset` (N1).
