# repair.wu-reset – Zresetuj Windows Update

## Werdykt 26H2
Portować jako `kind=action`, `risk=caution` (kategoria „Naprawy”, Etap 4b partia 1, handler C# `wu-reset`): na 26H2 Home i Pro usługi Windows Update dają się zatrzymać i przywrócić, oba foldery robocze (`SoftwareDistribution`, `catroot2`) dają się przenieść, a Windows od razu tworzy nowe. Skutek widać w Ustawieniach (historia aktualizacji wyczyszczona, aktualizacje pobierają się od nowa i instalują).

## Co zmienia
Handler C# (`src/DSOptimizer.Core/Repair/WindowsUpdateReset.cs`), bez skryptu PowerShell. Kroki:
1. Odczyt usług `wuauserv`, `bits`, `cryptsvc` (`IServiceManager.Query`). Brak usługi = pominięta z ostrzeżeniem; brak dostępu albo błąd odczytu = `Failed` „… Nic nie zmieniono.” przed zmianami.
2. Zatrzymanie działających, w tej kolejności, limit 30 s każda: `wuauserv` i `bits` przez `StopService`, `cryptsvc` przez `StopServiceWithDependents` razem z działającymi usługami zależnymi odczytanymi z systemu (`GetRunningDependents` = `EnumDependentServices`; na świeżej 26H2: `AppIDSvc` i sterownik `applockerfltr`). Program zapamiętuje, które działały. `UsoSvc` i `msiserver` nie są zatrzymywane (Restart Manager: nie trzymają żadnego z folderów, a ich zatrzymanie nic nie zmienia). Błąd zatrzymania = start zatrzymanych z powrotem i `Failed` „Nie udało się zatrzymać usługi <nazwa>: … Nic nie zmieniono.”
3. Przeniesienie `%SystemRoot%\SoftwareDistribution`, potem `%SystemRoot%\System32\catroot2` na `<folder>.dso-<rrrrMMdd-HHmmss>` (czas lokalny z `IClock`; kolizja nazwy = `-2`, `-3`…). Brak folderu = krok pominięty z adnotacją (na świeżym Home `SoftwareDistribution` nie istnieje, dopóki `wuauserv` nie wystartuje). Błąd drugiego przeniesienia = cofnięcie pierwszego, przywrócenie usług, `Failed` z nazwą blokującego folderu; błąd cofnięcia trafia do `NotRestored` („Folder … jest pod nazwą …. Windows mógł już utworzyć nowy folder … – najpierw go przenieś albo usuń, potem przywróć nazwę. Kolejny reset w DSOptimizerze usunie kopię … jako starszą.”).
4. Start zatrzymanych w odwrotnej kolejności: `cryptsvc`, jego zależne (w kolejności startu), `bits`, `wuauserv`. O przywróceniu decyduje odczyt stanu (`Query`), nie kod `Start`: sterownik, który wystartował sam razem z `AppIDSvc`, liczy się jako przywrócony. Usługa, która nie działa po starcie = `NotRestored` i `Failed`.
5. Diagnoza polityk (tylko odczyt): wartości `WUServer`, `WUStatusServer`, `DisableWindowsUpdateAccess`, `SetDisableUXWUAccess` w `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate` oraz `NoAutoUpdate`, `UseWUServer` w podkluczu `AU`, jeśli istnieją, trafiają do wyniku jako „Polityka: <nazwa> = <wartość>” + „Mogą być przyczyną problemu; usuwa je pozycja »Zresetuj Windows Update i usuń blokady« (z kopią).” Nic nie jest zmieniane.
6. Odczyt zwrotny: usługi działające przed resetem działają, kopie z tego przebiegu istnieją. Rozjazd = `Failed` z listą. Sukces = `Executed` z listą kroków, ścieżkami kopii i ostrzeżeniem o historii aktualizacji.
7. Dopiero po sukcesie: usunięcie starszych kopii `<folder>.dso-*` obok oryginału (`ListSiblingDirectories`, tylko nazwy pasujące do wzorca `<folder>.dso-rrrrMMdd-HHmmss[-N]`, bez kopii z tego przebiegu); zostaje jedna najnowsza. Błąd usunięcia = ostrzeżenie.

Usługi chronione przez system: `msiserver`, `WaaSMedicSvc`, `DoSvc` nigdy nie są zmieniane; wynik wspomina o którejś tylko, gdy jest wyłączona (`Disabled`).

## Źródła
- Test na VM 26H2 Home i Pro (build 26300.9457), 2026-10-22, przebiegi H1, N1, P1: `docs/zrodla/2026-10-22-etap4b-1-pomiary.md`, sekcja 3 (3a, 3b, 3h), tabela decyzji pkt 3 i 3h.
- Brak dokumentacji MS sprawdzonej w tym zadaniu (strony o resecie składników Windows Update nie były pobierane); dowód: test na VM opisany niżej.

## Wartości domyślne na świeżym 26H2
Odczyt 2026-10-22 (Home i Pro): `wuauserv` Manual, `bits` AutomaticDelayed, `cryptsvc` Automatic, `UsoSvc` AutomaticDelayed, `msiserver` Manual, `WaaSMedicSvc` Manual; `DoSvc` Home Manual, Pro AutomaticDelayed. Zależne `cryptsvc`: działające `AppIDSvc` i sterownik `applockerfltr`, niedziałające `IsolationSession`, `IsoEnvBroker`. `SoftwareDistribution` na świeżym Home: brak (powstaje przy starcie `wuauserv`), na Pro jest. Polityk Windows Update: brak klucza.

## Detekcja i undo
Nie dotyczy: `kind=action` nie ma detekcji ani cofnięcia (nie ma journala). Ślad: plik logu i wynik z listą kroków. Stare foldery zostają jako kopie `.dso-*` (ścieżki w wyniku), więc ręczny powrót jest możliwy przy zatrzymanych usługach.

## Ryzyka i ostrzeżenia
- `risk=caution`: na kilka sekund zatrzymuje usługi Windows Update, BITS i Usługi kryptograficzne (z zależnymi); na czas resetu (ok. 1 s) AppLocker nie egzekwuje reguł – bez znaczenia na prywatnym PC bez AppLockera.
- Historia aktualizacji w Ustawieniach zostanie wyczyszczona (zostać mogą tylko wpisy definicji Microsoft Defender); aktualizacje pobiorą się od nowa.
- Kopie folderów zostają obok oryginału (jedna najnowsza); ścieżki w wyniku.
- Polityki Windows Update tylko wypisywane, nie zmieniane (usuwa je `repair.wu-reset-full`).

## Test VM
Przypadek `repair-wu-reset` (`Flow=Execute`): `Setup` tworzy starą kopię `C:\Windows\SoftwareDistribution.dso-20000101-000000`; `Check` wymaga, by `wuauserv`, `bits`, `cryptsvc` działały, istniała dokładnie jedna kopia `SoftwareDistribution.dso-*` i dokładnie jedna `catroot2.dso-*`, a stara kopia zniknęła. Dopisany w `tests/vm/cases.psd1` w Tasku 6. W Tasku 10 `Check` porównuje też stan `AppIDSvc` (`Win32_Service`) i sterownika `applockerfltr` (`Win32_SystemDriver`) ze stanem zapisanym w `Setup` (R8: zależne wracają do stanu sprzed).

Przebieg VM (Task 10, 2026-10-22, pełny `tests/vm/Invoke-VmTests.ps1` na gałęzi `etap4b-1`; raporty poza gitem): Home i Pro po 722 zaliczone / 0 błędów / 25 pominiętych (6 przypadków ze `Skip` spoza kategorii Naprawy). `repair-wu-reset`: **Passed** na Home i Pro (`Executed`; zatrzymane `wuauserv`, `bits`, `cryptsvc` razem z `applockerfltr` i `AppIDSvc`, foldery przeniesione na `.dso-<znacznik>`, stara kopia usunięta, usługi i zależne z powrotem w stanie sprzed).

## Obserwacja skutku
Metoda: sonda eksploracji (`tests/vm/explore/Invoke-E4B1Exploration.ps1`), zatrzymanie usług przez `ControlService` i `ServiceController.Stop()`, Restart Manager dla obu folderów, przeniesienie folderów, odczyt plików po starcie usług, API historii aktualizacji i strona Ustawień (zrzuty w surowych wynikach poza gitem).
- **Home** (`DSO-26H2-Home`, 26300.9457, bez sieci), 2026-10-22: `cryptsvc` przez `ControlService(STOP)` = kod 1051 (działają `AppIDSvc` i `applockerfltr`); `ServiceController.Stop()` zatrzymuje wszystkie trzy w ok. 0,5 s, potem `catroot2` daje się przenieść. `SoftwareDistribution` nie istnieje do startu `wuauserv`. Po starcie usług nowy `catroot2` (39 plików bazy `edb*`) jest w sekundzie 0; wszystkie usługi wracają do stanu sprzed (`applockerfltr` startuje sam razem z `AppIDSvc`, jego jawny start zgłasza „już działa”). Historia bez sieci: pusta przed i po.
- **Pro** (`DSO-26H2-Pro`, 26300.9457), 2026-10-22: zatrzymanie i przeniesienie jak Home (P1, N1); nowy `SoftwareDistribution` (DataStore, Download, ReportingEvents.log) w sekundzie 0 po starcie `wuauserv`. Z siecią (N1): przed resetem 2 wpisy historii (sterownik + definicje Defendera); po resecie API historii zwraca 0, w Ustawieniach znika wpis sterownika, zostaje „Aktualizacje definicji (1)”. Po podłączeniu sieci WU sam zainstalował 2 aktualizacje (wynik Succeeded), strona Windows Update bez błędu.

## Reddit
Do uzupełnienia w części E.
