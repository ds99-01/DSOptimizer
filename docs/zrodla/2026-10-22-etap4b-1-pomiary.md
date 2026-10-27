# Etap 4b partia 1 – pomiary VM: krótkie naprawy (2026-10-22)

Pomiar na VM Hyper-V `DSO-26H2-Home` i `DSO-26H2-Pro` (Windows 11 26H2, kompilacja 26300.9457, polski interfejs). Każdy przebieg zaczynał się
od checkpointu `czysta-26H2`. Skrypt: `tests/vm/explore/Invoke-E4B1Exploration.ps1` (Pester na mockach: `tests/vm/tests/E4B1Exploration.Tests.ps1`).
Konteksty: **K1** = PowerShell Direct jako wbudowany Administrator; **K2** = zadanie Harmonogramu konta `dso` (Interactive + Highest), czyli
proces jak DSOptimizer po UAC. Surowe wyniki (JSON + zrzuty) leżą poza gitem: zapis pomiaru w archiwum autora.

Przebiegi (oznaczenia w tekście):
- **P1** – Pro, wszystkie fazy (`przebieg1-pro/`). Wyniki sieciowe P1 są nieważne: po resecie sieci karta nie dostała adresu z DHCP.
- **H1** – Home bez sieci: baseline, dns, policies, store, winget, protection, wureset, netreset (`offline/DSO-26H2-Home/`).
- **N1** – Pro z siecią (`Default Switch`): baseline, wuhistory, wureset, network (`network/DSO-26H2-Pro/`).
- **A2** – po poprawkach skryptu, bez sieci (`a2/`). Poprawki: ręczny IP przez `netsh`, okno „Ochrona systemu” na świeżym systemie,
  ponowne włączenie ochrony, odczyt ochrony po restarcie. Pro: dns, policies, store, winget, protection, netreset. Home: protection, netreset.
- **S1** – jednorazowa sonda na Home (`a2/netsh-denied-home.json`): ACL podkluczy `Nsi` i `netsh int ip reset` z plikiem logu.

Wszystkie przebiegi zakończyły się bez błędu (`error` puste, `cleanup.Ok = true`).

## Tabela decyzji

| # | Pomiar | Home | Pro | DECYZJA (wartości do Tasków 5–9) |
|---|---|---|---|---|
| 1 | DNS: `Get-DnsClientCache` po `ipconfig /flushdns` i po `Clear-DnsClientCache` | H1: po obu czyszczeniach zostają tylko 2 wpisy z `hosts` (A nazwy + PTR `67.45.123.10.in-addr.arpa`) | P1/A2 jak Home. N1 (z siecią): zostają 3 wpisy, wszystkie z `hosts` (A, PTR, negatywny AAAA dla nazwy z `hosts`, status 9501); `wpad` i nazwa negatywna znikają | **Kryterium sukcesu:** każdy wpis pamięci po czyszczeniu dotyczy nazwy z `hosts` albo jej nazwy odwrotnej (`*.in-addr.arpa`/`*.ip6.arpa`) – dowolny typ i status. Pusta pamięć też = sukces. Nie liczyć spadku liczby wpisów (patrz uwaga o nazwach publicznych). |
| 2 | Reset sieci: `netsh winsock reset` + `netsh int ip reset`, ręczny IP/DNS przed i po restarcie | Winsock: kod 0. `int ip reset`: **kod 1**, jedna pozycja „Resetting , failed.” + „Odmowa dostępu.”, reszta OK. Z procesu `dso` (K2) tak samo. Ręczny IP/maska/brama: znikają z rejestru od razu, adres działa do restartu, po restarcie go nie ma. Ręczne DNS **zostają** | Identycznie (P1, A2) | **Reguła oceny wyjścia** (niżej) zamiast „kod ≠ 0 = Failed”. `requiresReboot = true` potwierdzone (oba polecenia same proszą o restart). **Treść ostrzeżenia:** niżej. |
| 3 | WU: usługi, zatrzymanie, foldery, typy startu | Typy startu: wuauserv Manual, bits AutomaticDelayed, cryptsvc Automatic, UsoSvc AutomaticDelayed, msiserver Manual, WaaSMedicSvc Manual, **DoSvc Manual**. `catroot2` przeniesiony dopiero po zatrzymaniu cryptsvc razem z zależnymi | Te same, ale **DoSvc AutomaticDelayed** (P1, N1). Reszta jak Home | **Zatrzymywane usługi: `wuauserv`, `bits`, `cryptsvc`. Bez `UsoSvc` i `msiserver`** (nie trzymają żadnego folderu, ich zatrzymanie nic nie zmienia). **catroot2: tak** – patrz 3b. Domyślne typy startu i lista „zmienialnych” – tabela w sekcji 3. |
| 3h | Historia aktualizacji po resecie (sieć) | – (bez sieci: historia pusta przed i po) | N1: przed resetem 2 wpisy (sterownik + definicje Defendera). Po resecie API historii zwraca 0, w Ustawieniach znika wpis sterownika, a wpis „Aktualizacje definicji (1)” zostaje. Po podłączeniu sieci WU sam zainstalował 2 aktualizacje (wynik Succeeded), strona WU bez błędu | Ostrzeżenie potwierdzone z korektą: „Historia aktualizacji w Ustawieniach zostanie wyczyszczona (zostać mogą tylko wpisy definicji Microsoft Defender); aktualizacje pobiorą się od nowa.” |
| 4 | Polityki WU: widok „zarządza Twoja organizacja” | 6 wartości łącznie i każda osobno: **brak komunikatu**, „Sprawdź aktualizacje” aktywny | 6 łącznie: „Ustawieniami usługi Windows Update zarządza Twoja organizacja.”, przycisk nieaktywny. Osobno komunikat i blokadę daje **tylko `SetDisableUXWUAccess`**. Pozostałe 5 bez widocznego skutku. `reg export` klucza `WindowsUpdate` zapisuje też `AU` (jeden plik). Po `delete` komunikat znika, `reg import` przywraca stan 1:1 | **Lista do diagnozy:** w `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate`: `WUServer`, `WUStatusServer`, `DisableWindowsUpdateAccess`, `SetDisableUXWUAccess`; w `…\WindowsUpdate\AU`: `NoAutoUpdate`, `UseWUServer`. Wariant rozszerzony eksportuje i usuwa **cały klucz `WindowsUpdate` z podkluczami** (`reg export` + delete), nie pojedyncze wartości. Adnotacja: widoczny skutek zmierzono tylko dla `SetDisableUXWUAccess` (Pro); skutku pozostałych na skan offline nie da się sprawdzić. |
| 5 | Sklep / R33 (stan Staged dla `dso`) | H1: `Remove-AppxPackage` jako dso → dla konta brak wpisu, pakiet Staged (S-1-5-18) i w obrazie; start Sklepu nie daje okna (71 s). Rejestracja z manifestu jako dso (0,35 s) → `Installed`, okno „Microsoft Store” | P1 i A2 identycznie (rejestracja 0,4 s); po usunięciu start otwiera Eksplorator „Dokumenty” (P1) albo nic (A2) | **R33 wchodzi.** Polecenie `Add-AppxPackage -Register "<InstallLocation>\AppxManifest.xml" -DisableDevelopmentMode` jako zalogowany. Przygotowanie do testu VM opisane w sekcji 5. |
| 6 | winget (jako dso) | App Installer **1.26.509.0** (w obrazie 2025.926.104.0) `Installed`, alias jest, `winget --version` = `v1.11.510`, kod 0. `Remove-AppxPackage` dla konta: odmowa 0x80073CFA („part of Windows”). „Zepsucie” = usunięcie aliasu `winget.exe` → brak pliku. `-RegisterByFamilyName` przywraca alias → `v1.11.510`, kod 0 | Identycznie (A2: rejestracja 0,53 s). N1 z siecią: `winget source list` kod 0 (msstore, winget), `winget search` pobrał źródło (3,63 MB) i zwrócił wynik, kod 0 | **Kryterium sukcesu:** pakiet `Microsoft.DesktopAppInstaller` `Installed` dla konta **i** alias `%LOCALAPPDATA%\Microsoft\WindowsApps\winget.exe` konta istnieje **i** `winget --version` z procesu konta kończy się kodem 0 z wersją `v<cyfry>` w wyjściu. Minimalna wersja: niezmierzona (patrz sekcja 6). |
| 6i | Id „Instalatora aplikacji” w Sklepie | – | – | **`9NBLGGH4NNS1` potwierdzone** (pobranie strony z hosta: `https://apps.microsoft.com/detail/9NBLGGH4NNS1` = „App Installer”; strona MS „Use WinGet” podaje ten sam id). Link: `ms-windows-store://pdp/?ProductId=9NBLGGH4NNS1`. |
| 7 | Ochrona systemu | Świeża: wyłączona (`SPP\Clients` bez wpisu, brak shadow storage). Włączenie z okna i `Enable-ComputerRestore` (24 ms) dają ten sam stan: wpis w `SPP\Clients`, **brak skojarzenia shadow storage do pierwszego punktu**. Po pierwszym punkcie MaxSpace = 2 052 367 196 B = **3,0%** z 68,4 GB. `Disable-ComputerRestore`: punkt **nie znika** (3 s, 60 s, po restarcie VM) | Identycznie (P1, A2) | **Detect:** wpis woluminu C: w `SPP\Clients` (`SystemProtectionReader`). **Apply:** `Enable-ComputerRestore -Drive 'C:\'` (albo WMI `SystemRestore.Enable("C:\")`), **bez `vssadmin resize`**. Limit 3% to wartość Windows, ta sama co z okna. **Undo:** `Disable-ComputerRestore -Drive 'C:\'`. Ostrzeżenie trzeba zmienić – sekcja 7. |

## 1. DNS – szczegóły

- `ipconfig /flushdns`: kod 0, „Successfully flushed the DNS Resolver Cache.”. `Clear-DnsClientCache` działa tak samo.
- Wpisy z `hosts` zostają w pamięci z TTL ok. 604 700 s (sekcja 1 = Answer). To normalne zachowanie, nie błąd czyszczenia.
- **Uwaga (N1, z siecią):** `Resolve-DnsName www.microsoft.com` (i 2 inne nazwy publiczne) odpowiedziało z sieci (15–19 ms), a ponowne zapytanie z pamięci w 1 ms.
  Mimo to **`Get-DnsClientCache` nie pokazał tych nazw** – ani przed czyszczeniem, ani po nim. Widać tylko `wpad`, wpisy `hosts` i wpis negatywny.
  Dlatego detekcja nie może polegać na „nazwy publiczne zniknęły” ani na spadku liczby wpisów. Zostaje kryterium „tylko `hosts`”.
- Odczyt zwrotny w kodzie: `Get-DnsClientCache` (moduł DnsClient, jest na świeżym 26H2) przez `IScriptRunner`. Nazwy z `hosts` czytać
  z `%SystemRoot%\System32\drivers\etc\hosts`: linie bez `#`, każde słowo po adresie to nazwa.

## 2. Reset sieci – szczegóły

- Kolejność i kody (Home i Pro, K1 i K2 identycznie): `netsh winsock reset` → kod 0, „Sucessfully reset the Winsock Catalog. You must restart
  the computer in order to complete the reset.”. `netsh int ip reset` → **kod 1**: 34 pozycje, z czego 33 „Resetting …, OK!” i dokładnie jedna „Resetting , failed.”
  i w następnej linii „Odmowa dostępu.”, na końcu „Restart the computer to complete this action.”. Na 26H2 pozycje od 20. mają pustą nazwę.
  Komunikat odmowy przychodzi w innym kodowaniu niż reszta (OEM 852 daje „dost─Öpu”, czyli bajty UTF-8 czytane jako 852).
- **Który klucz (S1):** `netsh int ip reset <plik>` na 26H2 **nie tworzy pliku logu** (sprawdzone ścieżką bezwzględną `C:\`), więc nazwy klucza
  nie da się odczytać z logu. Odczyt ACL na świeżym Home: dokładnie 6 podkluczy `HKLM\SYSTEM\CurrentControlSet\Control\Nsi` nie otwiera się
  do zapisu dla Administratora: `{eb004a00-9b1a-11d4-9123-0050047759bc}\26` i `\4`, `{eb004a01-…}\25`, `\26` i `\4`, `{eb004a1c-…}\0`.
  SDDL pierwszych pięciu: `O:BAG:SYD:P(A;OICI;CCDCLCSWRPRC;;;WD)`, czyli chronione, tylko Everyone, bez prawa usuwania dla nikogo.
  To fabryczny stan świeżego systemu, nie uszkodzenie. Odmowa netsh dotyczy jednego z tych kluczy. Źródła spoza MS wskazują
  `{eb004a00-…}\26`, ale bez logu nie da się tego potwierdzić pomiarem.
- **Reguła oceny wyjścia `netsh int ip reset`:**
  - kod 0 → sukces;
  - kod 1 **i** w wyjściu dokładnie jedna linia kończąca się na `failed.` **i** zaraz po niej linia z „Odmowa dost” albo „Access is denied”
    **i** wszystkie pozostałe pozycje `Resetting` kończą się `OK!` → sukces z adnotacją. Adnotacja: „Jedna pozycja chroniona przez system
    odmówiła dostępu – na świeżym Windows 11 26H2 tak jest zawsze, reszta zresetowana.”;
  - każdy inny przypadek (inny kod, więcej niż jedno `failed`, inny komunikat) → `Failed` z wyjściem.

  Porównywać bez polskich znaków (np. „Odmowa dost”), bo komunikat przychodzi w innym kodowaniu.
  Dla `netsh winsock reset` i `ipconfig /flushdns` zostaje „kod ≠ 0 = Failed”.
- **Ręczny IP/DNS:** ustawienie przez `netsh interface ipv4 set address name=<idx> source=static …` + `Set-DnsClientServerAddress` na karcie bez sieci
  (A2, Home i Pro). Po resecie wartości `IPAddress`, `SubnetMask`, `DefaultGateway` znikają z
  `HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{GUID}` od razu. Adres działa jeszcze do restartu, a po restarcie karta jest
  na DHCP (tu APIPA). `NameServer` (ręczne DNS) przetrwał reset i restart.
  Zastrzeżenie: karta była rozłączona, więc netsh zapisał `DisableDhcpOnConnect=1` przy `EnableDHCP=1`. Przypadku „karta podłączona,
  `EnableDHCP=0`” nie zmierzono. Wynik „adres znika z rejestru” dotyczy kluczy adresu, a nie samej flagi DHCP.
  Pierwsza próba (H1: `Set-NetIPInterface -Dhcp Disabled` + `New-NetIPAddress`) na rozłączonej karcie zapisała adres tylko w ActiveStore.
  Skończyła się błędem „Inconsistent parameters PolicyStore PersistentStore and Dhcp Enabled”, więc pomiar z H1 nie liczy się dla IP.
  DNS w H1 też przetrwał.
- **Treść ostrzeżenia (potwierdzenie):** „Wymaga restartu komputera. Ręcznie ustawiony adres IP, maska i brama zostaną usunięte – po restarcie karta
  pobierze adres automatycznie (DHCP). Ręcznie ustawione serwery DNS zostają. Niektóre VPN i programy sieciowe mogą wymagać ponownej
  konfiguracji. Bieżąca konfiguracja IP zostanie zapisana w logu, żeby dało się ją odtworzyć.”
- **Wynik:** `requiresReboot = true`. Odczytu zwrotnego skutku przed restartem nie ma, bo do restartu karta nadal ma stary adres.

## 3. Windows Update – szczegóły

**Domyślne typy startu świeżej 26H2** (`Start` / `DelayedAutostart`):

| Usługa | Home | Pro | `ChangeServiceConfig` (Administrator) |
|---|---|---|---|
| `wuauserv` | Manual (3) | Manual (3) | OK (Disabled i powrót, WaaSMedic nie przywraca w 10 s) |
| `bits` | AutomaticDelayed (2/1) | AutomaticDelayed (2/1) | OK |
| `cryptsvc` | Automatic (2) | Automatic (2) | OK |
| `UsoSvc` | AutomaticDelayed (2/1) | AutomaticDelayed (2/1) | OK |
| `msiserver` | Manual (3) | Manual (3) | **odmowa 5** (Access denied) |
| `WaaSMedicSvc` | Manual (3) | Manual (3) | **odmowa 5** |
| `DoSvc` | **Manual (3)** | **AutomaticDelayed (2/1)** | **odmowa 5** |

Decyzja dla `repair.wu-reset-full`: przywracane są tylko typy `wuauserv`, `bits`, `cryptsvc` i `UsoSvc` (te same wartości na Home i Pro).
`msiserver`, `WaaSMedicSvc` i `DoSvc` są chronione: zapis kończy się odmową 5, więc ich nie ruszamy. Wynik ma o tym powiedzieć tylko wtedy,
gdy ich typ startu różni się od tabeli. `DoSvc` różni się między edycjami (Home Manual, Pro AutomaticDelayed), więc dla `DoSvc` nie ma
jednej „wartości świeżej”. Kolejny argument, żeby go nie ruszać.

**3a. Zatrzymanie i foldery (H1, N1, P1 – identycznie):**
- `wuauserv` zatrzymuje się w ok. 1,3 s, `bits` w ok. 0,25 s.
- `cryptsvc` przez `ControlService(STOP)` (jak `WindowsServiceManager.StopService`) kończy się kodem **1051**: działają zależne `AppIDSvc` i sterownik
  `applockerfltr`. Po 30 s nadal Running. Zależne niedziałające: `IsolationSession`, `IsoEnvBroker`.
- Restart Manager: `SoftwareDistribution` trzyma tylko `wuauserv`, a `catroot2` trzymają `CryptSvc`, `AppIDSvc` i `applockerfltr`.
- `SoftwareDistribution` daje się przenieść po zatrzymaniu `wuauserv` i `bits`. Na świeżym Home folderu w ogóle nie ma (`Exists=false`,
  dopóki `wuauserv` nie wystartuje). `catroot2` po zatrzymaniu wuauserv/bits (cryptsvc dalej działa) daje 0x80070005, także po dodatkowym
  zatrzymaniu `UsoSvc` (0,25 s) i `msiserver` (nie działał, 1062).
- **3b. catroot2 z zależnymi:** `ServiceController('cryptsvc').Stop()` zatrzymuje też zależne. Na Home i Pro w ok. 0,5 s stają `cryptsvc`, `AppIDSvc`
  i `applockerfltr`. Potem nikt nie trzyma `catroot2`, a przeniesienie się udaje. Przywrócenie: `Start` cryptsvc, potem zależnych, które
  działały wcześniej. `applockerfltr` startuje sam razem z `AppIDSvc` (jego jawny `Start` zgłasza „Jedno wystąpienie usługi już działa”).
  Stan końcowy: wszystkie trzy Running, jak przed resetem.
- **Decyzja catroot2: przenosimy.** `cryptsvc` zatrzymujemy razem z działającymi usługami zależnymi. Na świeżym 26H2 są to `AppIDSvc` i sterownik
  `applockerfltr`. Zależne odczytujemy przez `DependentServices`, nie z listy na sztywno. Zapamiętujemy, które działały, i po przeniesieniu uruchamiamy
  `cryptsvc` oraz te zależne. Sterownik, który wystartował razem z usługą, liczymy jako przywrócony (sprawdza to odczyt stanu, nie kod `Start`).
  Uzasadnienie: zatrzymanie trwa ok. 0,5 s, nie dotyka usług chronionych (`WaaSMedicSvc`, `msiserver`), a wszystko wraca do stanu sprzed operacji.
  Na czas resetu (ok. 1 s) AppLocker nie egzekwuje reguł, co na prywatnym PC bez AppLockera nie ma znaczenia.
  Jeśli zależnej usługi nie da się zatrzymać, `catroot2` zostaje zablokowany, a zasada „wszystko albo nic” ze specu cofa przeniesienie
  `SoftwareDistribution`.
  Do `IServiceManager` trzeba dodać zatrzymanie z zależnymi (albo zatrzymanie zależnych po kolei przed `cryptsvc`) i uruchomienie w odwrotnej kolejności.
- **Kiedy Windows tworzy foldery:** od razu. `SoftwareDistribution` (DataStore, Download, ReportingEvents.log) jest w sekundzie 0 po starcie `wuauserv`,
  a nowy `catroot2` (39 plików bazy `edb*`) w sekundzie 0 po starcie `cryptsvc`. Odczyt zwrotny może sprawdzać: kopie `.dso-*` istnieją,
  nowe foldery istnieją, usługi w stanie sprzed.
- Przywracanie: `cryptsvc` (0 ms, już działał po próbie z zależnymi) → `bits` (4,3 s) → `wuauserv` (0,5 s) → `UsoSvc` (jeśli zatrzymany).
  Po przywróceniu `WaaSMedicSvc` był Running (uruchomił się sam, bez udziału sondy).

## 4. Polityki WU – szczegóły

- Zapis testowy: `WUServer`/`WUStatusServer` = `http://dso-wsus.invalid:8530` (REG_SZ), `DisableWindowsUpdateAccess`/`SetDisableUXWUAccess` = 1,
  `AU\NoAutoUpdate` = 1, `AU\UseWUServer` = 1.
- Pro: 6 wartości razem → komunikat organizacji i nieaktywny przycisk. Osobno (każda sama, po usunięciu klucza) komunikat i blokadę daje tylko
  `SetDisableUXWUAccess`. Home: brak widocznego skutku w Ustawieniach dla wszystkich 6, razem i osobno.
  Skutku na sam skan (np. `WUServer` + `UseWUServer` kieruje skan do WSUS) offline nie da się zmierzyć. Diagnoza wypisuje więc wszystkie 6 jako
  „mogą być przyczyną”, bez obietnicy, że każda blokuje.
- `reg export HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate <plik> /y` zapisuje też podklucz `AU`. `reg delete … /f` usuwa całość.
  `reg import` przywraca oba klucze 1:1 (porównanie wartości po imporcie = przed).
- `PartOfDomain` na VM = False (odmowa „domena” niezmierzona na prawdziwej domenie – działa z `Win32_ComputerSystem.PartOfDomain`).
  Wykrywania MDM/Intune nie mierzono (VM nie są zapisane do MDM): zostaje stałe ostrzeżenie „na komputerze zarządzanym polityki mogą wrócić”.

## 5. Sklep / R33 – szczegóły i przygotowanie testu VM

- **Werdykt R33: wchodzi** (Home i Pro, 3 przebiegi). Po rejestracji `InstallState` dla konta = `Installed`, `Status = Ok`, okno „Microsoft Store”
  otwiera się (strona offline). Start przez `explorer.exe shell:AppsFolder\Microsoft.WindowsStore_8wekyb3d8bbwe!App` jako dso.
- Pakiet: `Microsoft.WindowsStore_22506.1400.2.0_x64__8wekyb3d8bbwe`, `InstallLocation` = `C:\Program Files\WindowsApps\<PackageFullName>`,
  manifest istnieje. W obrazie (provisioned): `Microsoft.WindowsStore_22506.1400.2.0_neutral_~_8wekyb3d8bbwe`.
- **Przygotowanie stanu Staged do przypadku VM:** w procesie konta `dso` (zadanie Harmonogramu Interactive + Highest, jak `RunAs='dso'`)
  `Remove-AppxPackage -Package <PackageFullName Sklepu>` (0,8 s). Potem `Get-AppxPackage -AllUsers` pokazuje dla pakietu tylko
  `S-1-5-18 = Staged`, bez wpisu dla SID `dso`, a `Get-AppxProvisionedPackage -Online` nadal ma Sklep. Sprawdzenie po naprawie: `InstallState`
  dla SID `dso` = `Installed`. Naprawa musi działać jako zalogowany (SID procesu = zalogowany), tak jak dziś.
- Bez sieci: start Sklepu w stanie Staged nie otwiera okna (H1: brak okna w 71 s; P1: otwiera się Eksplorator „Dokumenty”).

## 6. winget – szczegóły

- Świeży stan dla `dso`: `Microsoft.DesktopAppInstaller_1.26.509.0_x64__8wekyb3d8bbwe` `Installed`, a w obrazie `…_2025.926.104.0_neutral_~_…`.
  Alias `C:\Users\dso\AppData\Local\Microsoft\WindowsApps\winget.exe` (ReparsePoint, długość 0). `winget --version` = `v1.11.510`, `winget --info`
  podaje `Package: Microsoft.DesktopAppInstaller v1.26.509.0`.
- `Remove-AppxPackage` App Installera dla konta kończy się odmową 0x80073CFA („This app is part of Windows and cannot be uninstalled on a per-user basis”).
  Stanu Staged dla App Installera **nie da się** przygotować tak jak dla Sklepu.
- **„Zepsucie” do testu VM:** usunięcie aliasu `Remove-Item -LiteralPath "$env:LOCALAPPDATA\Microsoft\WindowsApps\winget.exe" -Force` w procesie `dso`.
  Potem `winget` = „Nie można odnaleźć określonego pliku”, a pakiet dalej `Installed`. Naprawa
  `Add-AppxPackage -RegisterByFamilyName -MainPackage Microsoft.DesktopAppInstaller_8wekyb3d8bbwe` jako dso (0,4–0,5 s) przywraca alias,
  a `winget --version` = `v1.11.510`, kod 0.
- **Wersja minimalna:** niezmierzona. Obraz 26H2 ma tylko jedną wersję (1.26.509.0 → winget v1.11.510), a starszego pakietu nie pobieramy (anty-cel).
  Kryterium nie porównuje więc wersji, tylko wymaga kodu 0 i wersji w wyjściu. Gdy pakiet jest `Installed`, a `winget --version` nie działa
  także po rejestracji, wynik proponuje stronę Sklepu `9NBLGGH4NNS1`.
- Z siecią (N1): `winget source list` kod 0 (msstore `https://storeedgefd.dsx.mp.microsoft.com/v9.0`, winget `https://cdn.winget.microsoft.com/cache`).
  `winget search PowerToys` pobrał źródło i zwrócił `Microsoft.PowerToys`, kod 0. Sam `source list` nie dowodzi połączenia (offline też kod 0).

## 7. Ochrona systemu – szczegóły

- Świeża VM (Home i Pro): `SPP\Clients` bez wpisu, `vssadmin list shadowstorage` = „No items found”, `SystemRestore\DiskPercent` = 15
  (stara wartość, **nie** odpowiada limitowi). `SystemRestorePointCreationFrequency` brak.
- **Włączenie z okna** (A2, Home i Pro): `SystemPropertiesProtection.exe` jako dso → „Konfiguruj…” → „Włącz ochronę systemu” → OK, bez ruszania suwaka.
  Suwak „Maksymalne użycie” stał na 0 (trackbar 0/0–100). Skutek: wpis w `SPP\Clients`, bez shadow storage. Po pierwszym punkcie
  MaxSpace = 2 052 367 196 B = 3,0% woluminu 68 412 239 872 B. `Enable-ComputerRestore -Drive 'C:\'` (24–26 ms) daje dokładnie to samo.
  **Limit = 3% (domyślny VSS, ten sam przy włączeniu z okna). Apply nie woła `vssadmin resize`.**
  UIA nie widzi kontrolek okna Win32 (wszystko jako Pane). Okno obsłużono przez `SysListView32` + VK_HOME i BM_CLICK.
- Punkt przez `DSOptimizer.exe --apply vmtest.hklm-value --restore-point required` = Created („DSOptimizer – przed vmtest.hklm-value”).
- **Prawda o usuwaniu punktów:** `Disable-ComputerRestore -Drive 'C:\'` (4 ms) usuwa wpis `SPP\Clients`, ale **punkt zostaje**.
  `Get-ComputerRestorePoint` dalej go zwraca, `vssadmin list shadows` pokazuje kopię, a shadow storage zostaje. Tak jest po 3 s, po 60 s i po restarcie VM
  (A2, Home i Pro). Przy wyłączonej ochronie nie da się utworzyć punktu: WMI `CreateRestorePoint` = „Nie można uruchomić określonej usługi,
  ponieważ jest ona wyłączona…”, a przycisk „Przywracanie systemu…” w oknie jest nieaktywny. Po ponownym `Enable-ComputerRestore` stary punkt jest
  znów na liście (pts=1, ta sama kopia). Okno „Ochrona systemu” przy „Wyłącz ochronę systemu” sam usuwa punkty (to okno, nie cmdlet).
- **Decyzja undo:** `Disable-ComputerRestore -Drive 'C:\'` bez usuwania punktów. Treść ostrzeżenia w potwierdzeniu cofnięcia (zmiana względem specu,
  bo pomiar przeczy „usuwa wszystkie punkty”): „Wyłączenie ochrony systemu blokuje tworzenie nowych punktów przywracania i przywracanie
  systemu z istniejących. Istniejące punkty zostają na dysku (zajmują miejsce) i wrócą po ponownym włączeniu ochrony; usunąć je można
  w oknie »Ochrona systemu« (Konfiguruj → Usuń).” Jeśli Dawid woli „wyłączenie = usunięcie punktów”, to osobna, nieodwracalna operacja
  (`vssadmin delete shadows /for=C: /all /quiet`). Tego nie mierzono i nie jest w zakresie.
- Detekcja po undo: brak wpisu C: w `SPP\Clients` = NotApplied, niezależnie od pozostałych kopii.

## Pomiary niewykonalne / zastępcze

- Log `netsh int ip reset <plik>` – na 26H2 plik nie powstaje. Zamiast tego: ACL podkluczy `Nsi` (6 chronionych, SDDL wyżej).
- Ręczny IP na **podłączonej** karcie z `EnableDHCP=0` – niezmierzony. Zmierzono kartę rozłączoną (netsh static → `DisableDhcpOnConnect=1`).
- Kliknięcie „Sprawdź aktualizacje” po resecie WU z siecią – przycisku nie było, bo strona pokazywała „Dostępne aktualizacje do pobrania”
  (stan sprzed resetu trzyma `UsoSvc`). Zamiast tego: API historii po podłączeniu sieci – WU sam zainstalował 2 aktualizacje (wynik 2 = Succeeded).
- Minimalna wersja App Installera z działającym aliasem – w obrazie jest jedna wersja. Kryterium bez progu wersji.
- Skutek polityk WU na sam skan (offline) – niezmierzony. Diagnoza bez obietnicy blokady.
- MDM/Intune – brak środowiska. Stałe ostrzeżenie.

## Stan VM na końcu

Po ostatnim przebiegu (A2 Pro) i sondzie S1 (Home) odczyt z hosta: `DSO-26H2-Home` Off, karta bez przełącznika (`Connected=False`);
`DSO-26H2-Pro` Off, karta bez przełącznika. Każdy przebieg i sonda kończyły się revertem do `czysta-26H2`. Checkpoint `czysta-26H2` obu VM jest na miejscu.
