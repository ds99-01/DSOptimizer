# repair.network-reset – Zresetuj ustawienia sieci

## Werdykt 26H2
Portować jako `kind=action`, `risk=caution`, `requiresReboot=true` (Etap 4b partia 1): `netsh winsock reset` i `netsh int ip reset` działają na 26H2 Home i Pro, ale skutek widać dopiero po restarcie, a `netsh int ip reset` na świeżym systemie zawsze kończy się kodem 1 z jedną odmową dostępu przy chronionym kluczu. Dlatego ocena wyniku nie opiera się na „kod ≠ 0 = błąd”, tylko na zmierzonym wzorcu (niżej).

## Co zmienia
Skrypt (`script.apply`), polecenia po nazwie (testy mockują `netsh`, `Get-NetIPConfiguration`, `Clear-DnsClientCache`):
1. `Get-NetIPConfiguration` – konfiguracja IP każdego adaptera trafia do stdout (a więc do logu): alias, IPv4 z maską, brama, DNS, DHCP tak/nie. Błąd odczytu = `Failed` bez żadnej zmiany.
2. `netsh winsock reset` – kod ≠ 0 = `Failed` z wyjściem; następne polecenia nie startują.
3. `netsh int ip reset` – ocena wyniku wg reguły niżej.
4. `Clear-DnsClientCache`.

Błąd po udanym `netsh winsock reset` (kroki 3–4) kończy komunikat zdaniem: „Winsock został już zresetowany – uruchom ponownie komputer; konfiguracja IP sprzed resetu jest w pliku logu.” Przy każdym błędzie akcji skryptowej silnik zapisuje jej stdout w pliku logu (linia „repair.network-reset: wyjście skryptu przed błędem:”), a wynik w oknie niesie tylko stderr – więc kopia konfiguracji IP z kroku 1 jest w logu także przy porażce.

Ostatnia linia stdout: „Polecenia zakończone powodzeniem. Skutek po restarcie komputera. Konfiguracja IP sprzed resetu jest wyżej i w pliku logu.” Skrypt zeruje `$LASTEXITCODE` po przyjęciu kodu 1 z adnotacją, bo opakowanie skryptu kończy się `exit $LASTEXITCODE`.

**Reguła oceny `netsh int ip reset`** (pomiar VM, ruling R9): kod 0 = sukces. Kod 1 **i** dokładnie jedna linia kończąca się na `failed.` **i** w następnej niepustej linii „Odmowa dost…” albo „Access is denied” **i** wszystkie pozostałe pozycje `Resetting …` kończą się `OK!` = sukces z adnotacją „Jedna pozycja chroniona przez system odmówiła dostępu – to zwykłe zachowanie Windows 11, reszta zresetowana.” Każdy inny przypadek (inny kod, więcej niż jedno `failed`, inny komunikat, brak odmowy) = `Failed` z wyjściem. Porównanie bez polskich znaków („Odmowa dost”), bo komunikat przychodzi w złym kodowaniu („dost─Öpu”: bajty UTF-8 czytane jako OEM 852); test pokrywa wariant PL, EN i zniekształcony. Testowy przykład to dosłowne wyjście z VM Home (34 pozycje, 33 `OK!`, jedna `failed.`).

## Źródła
- Test na VM 26H2 Home i Pro (26300.9457), 2026-10-22, konteksty K1 (PowerShell Direct, Administrator) i K2 (proces konta `dso`, Interactive + Highest): `docs/zrodla/2026-10-22-etap4b-1-pomiary.md`, sekcja 2 oraz sonda S1.
- Odczyt ACL (S1, Home): 6 podkluczy `HKLM\SYSTEM\CurrentControlSet\Control\Nsi` nie otwiera się do zapisu dla Administratora (m.in. `{eb004a00-9b1a-11d4-9123-0050047759bc}\26` i `\4`; SDDL `O:BAG:SYD:P(A;OICI;CCDCLCSWRPRC;;;WD)`) – fabryczny stan świeżego systemu. Którego z nich dotyczy odmowa netsh, nie udało się potwierdzić (`netsh int ip reset <plik>` na 26H2 nie tworzy pliku logu).
- Brak dokumentacji MS sprawdzonej w tym zadaniu (strony poleceń `netsh` nie były pobierane); dowód: test na VM. Wymóg restartu potwierdza komunikat samego narzędzia: „You must restart the computer in order to complete the reset.” Zachowanie na 26H2 pochodzi wyłącznie z pomiaru.

## Wartości domyślne na świeżym 26H2
- `netsh winsock reset`: kod 0, „Sucessfully reset the Winsock Catalog. You must restart the computer in order to complete the reset.”
- `netsh int ip reset`: kod 1, 34 pozycje, 33 × „Resetting …, OK!” i jedna „Resetting , failed.” z „Odmowa dostępu.” w następnej linii, na końcu „Restart the computer to complete this action.” Od 20. pozycji nazwy są puste.
- Odczyt 2026-10-22, Home i Pro, K1 i K2 identycznie.

## Detekcja i undo
Nie dotyczy: `kind=action` nie ma detekcji ani cofnięcia (nie ma journala). **Odczytu zwrotnego skutku przed restartem nie ma:** do restartu karta nadal ma stary adres, a reset katalogu Winsock i stosu IP działa dopiero po restarcie. Wynik mówi to wprost („Skutek po restarcie komputera”), a silnik przenosi `requiresReboot` do wyniku `Executed` (baner „Wymagany restart”). Konfigurację IP sprzed resetu zapisuje stdout, więc dało się ją odtworzyć ręcznie.

## Ryzyka i ostrzeżenia
- `risk=caution`: usuwa ręczną konfigurację IP i zmienia stos sieciowy; brak cofnięcia.
- Ostrzeżenia w katalogu (pokazywane też w oknie potwierdzenia; treść oparta na pomiarze): „Wymaga restartu; ręcznie ustawiony adres IP, maska i brama znikną – karta pobierze adres z DHCP (DNS zostają).” oraz „Bez cofania – obecne ustawienia IP DSOptimizer zapisze w logu; VPN i programy sieciowe mogą wymagać ponownej konfiguracji.”
- Zastrzeżenie pomiaru: karta była rozłączona (netsh zapisał `DisableDhcpOnConnect=1` przy `EnableDHCP=1`); przypadek „karta podłączona, `EnableDHCP=0`” nie został zmierzony.
- Zabronione w tej pozycji (Global Constraints): `netcfg -d`, `ipconfig /release|/renew`, `netsh winhttp reset proxy`.

## Test VM
Przypadek `repair-network-reset` (`Flow=Execute`, `Check`: `netsh winsock show catalog` działa; raport ma `requiresReboot=true` dzięki `Expect.RequiresReboot` w runnerze): dopisany w `tests/vm/cases.psd1` w Tasku 5.

Przebieg VM (Task 10, 2026-10-22, pełny `tests/vm/Invoke-VmTests.ps1` na gałęzi `etap4b-1`; raporty poza gitem): Home i Pro po 722 zaliczone / 0 błędów / 25 pominiętych (6 przypadków ze `Skip` spoza kategorii Naprawy). `repair-network-reset`: **Passed** na Home i Pro – `Executed` z kodem 0, `requiresReboot=true` w raporcie, w komunikacie konfiguracja IP sprzed resetu (APIPA, bez sieci), `netsh winsock reset: kod 0` i `netsh int ip reset: kod 1` z adnotacją (R9); `Check` (`netsh winsock show catalog` działa) zaliczony. VM nie była restartowana po resecie i nie ma sieci ani ręcznego IP na podłączonej karcie – do potwierdzenia na sprzęcie.

## Obserwacja skutku
Metoda: kody wyjścia i pełne wyjście `netsh`, odczyt rejestru `HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{GUID}` (`IPAddress`, `SubnetMask`, `DefaultGateway`, `NameServer`) przed resetem, zaraz po nim i po restarcie VM; ręczny IP ustawiony przez `netsh interface ipv4 set address … source=static` i `Set-DnsClientServerAddress`.
- **Home** (`DSO-26H2-Home`, 26300.9457, bez sieci), 2026-10-22: `winsock reset` kod 0; `int ip reset` kod 1 (jedna odmowa dostępu, reszta OK). Wartości `IPAddress`, `SubnetMask`, `DefaultGateway` znikają z rejestru od razu; ręczny adres działa do restartu, po restarcie karta jest na DHCP (APIPA, bo bez sieci). Ręczne serwery DNS (`NameServer`) przetrwały reset i restart.
- **Pro** (`DSO-26H2-Pro`, 26300.9457), 2026-10-22 (przebiegi P1 i A2): identyczny wynik jak Home. Wyniki sieciowe P1 są nieważne (po resecie karta nie dostała adresu z DHCP); wynik opiera się na A2.

## Reddit
Do uzupełnienia w części E.
