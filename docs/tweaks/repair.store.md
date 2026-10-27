# repair.store – Napraw Microsoft Store na tym koncie

Dawniej `tools.store-repair` (kategoria „Narzędzia”); od Etapu 4b partii 1 pozycja jest w kategorii „Naprawy” jako `repair.store`, a stare id i alias v3.2 `tool_msstore` działają jako aliasy (`--execute tools.store-repair` dalej rozwiązuje się do tej akcji).

## Werdykt 26H2
Portować jako `kind=action`, `risk=caution` (alias v3.2 `tool_msstore`), ale w zmienionej, uczciwej formie: akcja ponownie rejestruje z manifestu pakiet `Microsoft.WindowsStore`, który już jest zainstalowany dla konta uruchamiającego DSOptimizer (mechanizm z dokumentacji MS), i uznaje sukces dopiero po odczycie zwrotnym. Nie instaluje Microsoft Store i nie obiecuje „Przywraca Microsoft Store na LTSC” z v3.2 (`wsreset.exe -i`): to zawodziło (zmierzone: kod wyjścia 0 przy błędzie).

## Co zmienia
Zmienia rejestrację pakietu aplikacji `Microsoft.WindowsStore` dla jednego konta (tego, które uruchomiło DSOptimizer). Pliki pakietu w `C:\Program Files\WindowsApps\…` zostają bez zmian; nic nie jest pobierane. Skrypt (`script.apply`) działa wyłącznie przez trzy helpery zdefiniowane w skrypcie (testy je mockują):
1. `Get-DsoProcessSid` – SID konta bieżącego procesu (`WindowsIdentity.GetCurrent().User`).
2. `Get-DsoStorePackage` – `Get-AppxPackage -AllUsers -Name 'Microsoft.WindowsStore'` (DOKŁADNA nazwa, nie wzorzec `*Store*`, bo ten łapie także `Microsoft.StorePurchaseApp`), z polami `Name`, `PackageFullName`, `Version`, `InstallLocation`, `Status` oraz `Users` (SID i `InstallState` z `PackageUserInformation`). Przełącznik `-AllUsers` jest potrzebny, bo bez niego `PackageUserInformation` jest puste (zmierzone).
3. `Register-DsoStorePackage` – `Add-AppxPackage -Path "<InstallLocation>\AppxManifest.xml" -Register -DisableDevelopmentMode` (przykład 3 z dokumentacji MS).

Czwarty helper, `Get-DsoAccountName` (`SecurityIdentifier.Translate` → nazwa konta, błąd = sam SID; jak w `repair.winget`), służy tylko do komunikatu odmowy (a).

Przebieg i wyniki (żadna ścieżka nie daje sukcesu bez odczytu zwrotnego):
- (a) SID procesu ≠ `$UserSid` (SID zalogowanego użytkownika): `Failed` „Naprawa Microsoft Store działa tylko dla konta, na którym działa DSOptimizer: uruchom DSOptimizer na koncie <nazwa>. Nic nie zmieniono.” (to samo zdanie co w `repair.winget`; nazwa z `Get-DsoAccountName`, a gdy SID-u nie da się przetłumaczyć – sam SID) BEZ odczytu pakietów i wywołania rejestracji. Powód zmierzony: rejestracja z konta `Administrator` dopisuje pakiet Administratorowi, wpis konta `dso` zostaje bez zmian, więc „naprawa” nie dotknęłaby konta, o które chodzi.
- (b) Brak pakietu `Microsoft.WindowsStore` na dysku (nie ma go na liście `-AllUsers`; np. LTSC): `Failed` „Pakiet Microsoft Store nie jest zainstalowany dla tego konta; ta akcja go nie instaluje.” (decyzja D4), bez rejestracji. Kilka pakietów o tej nazwie dla konta (albo kilka na dysku bez wpisu `Installed` konta): `Failed` (nie wiadomo, który zarejestrować). Pakiet na dysku bez wpisu `Installed` dla konta (R33, niżej): rejestracja i odczyt zwrotny.
- (c) Brak lokalizacji instalacji albo nieczytelny `AppxManifest.xml`: `Failed` z powodem, bez rejestracji.
- (d) Błąd `Add-AppxPackage`: `Failed` z komunikatem błędu.
- (e) Po rejestracji odczyt zwrotny: brak pakietu dla konta albo `Status` ≠ `Ok`: `Failed`.
- (f) Odczyt zwrotny: pakiet dla konta w stanie `Installed` i `Status` = `Ok`: `Executed` z wersją i pełną nazwą pakietu.

### R33 (Etap 4b, partia 1): pakiet na dysku, ale niezarejestrowany dla konta
Rozszerzenie mechanizmu o stan, który wcześniej kończył się odmową. Skrypt rozróżnia dwa stany (funkcja `Select-DsoUserStore`):
- `Installed`: pakiet ma wpis konta w stanie `Installed` – dotychczasowe zachowanie (ponowna rejestracja z manifestu). Ten wpis wygrywa, nawet gdy na dysku są też inne wersje pakietu.
- `OnDisk`: pakiet `Microsoft.WindowsStore` jest na dysku (np. `Staged` dla `S-1-5-18`, wpis tylko innego konta, brak wpisów), ale konto nie ma wpisu `Installed` – rejestracja `Add-AppxPackage -Path <InstallLocation>\AppxManifest.xml -Register -DisableDevelopmentMode` i odczyt zwrotny: `Installed` dla SID konta i `Status` = `Ok`. Wiele takich pakietów (niejednoznaczne), brak lokalizacji lub nieczytelny manifest: `Failed` bez rejestracji.
- Brak pakietu na dysku: dotychczasowa odmowa („nie jest zainstalowany dla tego konta; ta akcja go nie instaluje”), bez pobierania.
- Odmowa przy SID procesu ≠ SID zalogowanego użytkownika działa jak dotąd, przed jakąkolwiek zmianą.

**Werdykt R33: wchodzi** (pomiar VM 26H2 Home i Pro, 2026-10-22, 3 przebiegi; szczegóły w „Obserwacji skutku”).

Skrypt NIGDY nie woła `wsreset.exe` (także `wsreset -i`) ani `Remove-AppxPackage` (bezpieczniki harnessu rzucają przy niezamockowanym wywołaniu).

## Źródła
- MS: `Add-AppxPackage` (ms.date 2023-05-15), dostęp 2026-10-19: https://learn.microsoft.com/en-us/powershell/module/appx/add-appxpackage – przykład 3 „Add a disabled app package in development mode” (`Get-AppxPackage … InstallLocation`, `Add-AppxPackage -Path $ManifestPath -Register -DisableDevelopmentMode`); `-DisableDevelopmentMode` rejestruje „an existing app package installation that has been disabled, didn't register, or has become corrupted”.
- MS: `Get-AppxPackage` (ms.date 2023-05-15), dostęp 2026-10-19: https://learn.microsoft.com/en-us/powershell/module/appx/get-appxpackage – parametr `-AllUsers`.
- MS: „What's new in Windows 11 Enterprise LTSC 2024” (ms.date 2026-07-15), dostęp 2026-10-19: https://learn.microsoft.com/en-us/windows/whats-new/ltsc/whats-new-windows-11-2024 – „Support for LTSC by apps and tools, such as in-box apps and Microsoft Store, that are designed for the general availability channel release of Windows might be limited.” (jedyny dosłowny cytat MS o Store na LTSC; mówi „wsparcie może być ograniczone”, nie „sklepu nie ma”).
- Pomiar VM 26H2 Home i Pro (build 26300.9457), 2026-10-22 (Etap 4b partia 1, research: `docs/zrodla/2026-10-22-etap4b-1-pomiary.md`, sekcja 5): stan `Staged`, rejestracja z manifestu, odczyt zwrotny (R33).
- Odczyt VM 26H2 Home i Pro (build 26300.9457), 2026-10-19 (eksploracja C5, research: `docs/zrodla/2026-10-19-partia-c5-zrodla.md`, pytania 8–11), Ruling R19:
  - Pakiet: `Name` = `Microsoft.WindowsStore`, wersja 22506.1400.2.0 (`Microsoft.WindowsStore_22506.1400.2.0_x64__8wekyb3d8bbwe`), `Status` = `Ok`, `InstallLocation` = `C:\Program Files\WindowsApps\Microsoft.WindowsStore_22506.1400.2.0_x64__8wekyb3d8bbwe`, `AppxManifest.xml` istnieje. Filtr `*Store*` łapie też `Microsoft.StorePurchaseApp` (22509.1400.2.0).
  - Konto `Administrator` (PS Direct, K1): przed rejestracją 0 pakietów dla bieżącego konta (pusty wynik bez wyjątku); rejestracja kończy się bez błędu w ok. 2 s i dopisuje Store kontu Administrator, wpis konta `dso` bez zmian.
  - Konto `dso` (podniesiona sesja, K2): rejestracja kończy się `Ok` w ok. 2 s, `Status` po = `Ok`; zmiany nie widać, bo pakiet był już zarejestrowany.
  - `PackageUserInformation` jest puste w odczycie bez `-AllUsers`.
  - `wsreset.exe -i`: kod wyjścia 0 przy błędzie 0x80070520 (K1) i 0x80072ee7 (K2), więc kod 0 nie dowodzi powodzenia; akcja go nie używa.
  - Nie testowane na VM: brak pakietu na dysku (LTSC; pokrywają mocki w testach) i działanie Store po rejestracji (VM bez sieci). `Remove-AppxPackage` jako `dso` zmierzono dopiero 2026-10-22 (przygotowanie stanu `Staged`, sekcja „Obserwacja skutku”).

## Wartości domyślne na świeżym 26H2
Pakiet `Microsoft.WindowsStore` 22506.1400.2.0, `Status` = `Ok`, zainstalowany dla konta `dso` (`Installed`) na VM 26H2 Home i Pro (odczyt `Get-AppxPackage -AllUsers` z 2026-10-19). Konto `Administrator` nie miało Store do czasu rejestracji z jego sesji.

## Detekcja i undo
Nie dotyczy: `kind=action` nie ma detekcji ani cofnięcia (nie ma journala). Rejestracja pakietu nie ma operacji odwrotnej, dlatego `risk=caution` i ostrzeżenie „Brak cofnięcia”. Dowodem skutku jest odczyt zwrotny w skrypcie (pakiet dla konta w stanie `Installed`, `Status` = `Ok`); kod wyjścia 0 bez niego nie jest możliwy.

## Ryzyka i ostrzeżenia
- `risk=caution`: zmienia rejestrację pakietu systemowego; nieudana próba może zostawić Microsoft Store niezarejestrowany dla konta; brak cofnięcia.
- Akcja nie instaluje Microsoft Store. Na LTSC Microsoft pisze, że wsparcie dla aplikacji wbudowanych i Microsoft Store „might be limited” (cytat wyżej); gdy pakietu nie ma, akcja kończy się błędem, bez nieoficjalnych skryptów instalujących Store.
- Sprawdzenie, czy Store się uruchamia i loguje po rejestracji, wymaga sieci; akcja potwierdza tylko stan pakietu (`Status` = `Ok`), nie działanie sklepu.
- Działa tylko dla konta, które uruchomiło DSOptimizer. Przy „uruchom jako inny administrator” (konto admina ≠ zalogowany użytkownik) akcja odmawia i niczego nie zmienia (uczciwie: rejestracja trafiłaby do niewłaściwego konta).
- Sklep w użyciu (VM Home i Pro, Task 10, 2026-10-22): tuż po zalogowaniu `Add-AppxPackage -Register` zwraca `0x80073D02` („aplikacje trzeba zamknąć: Microsoft.WindowsStore”), choć nie widać procesu z pakietu Sklepu; ta sama rejestracja ok. 76–80 s po starcie `explorer.exe` przechodzi. Akcja kończy się wtedy błędem z komunikatem „Microsoft Store jest teraz w użyciu … zamknij Sklep, odczekaj minutę i uruchom naprawę ponownie” (szczegóły oryginalnego błędu w tym samym komunikacie). Program nie zamyka Sklepu sam (bez `-ForceApplicationShutdown`).

## Test VM
Przebieg VM `20261019-110405` (2026-10-19, HEAD `fd320ed`, build 26300.9457): Home (`DSO-26H2-Home`) i Pro (`DSO-26H2-Pro`), `tests/vm/Invoke-VmTests.ps1`. Raporty lokalne poza gitem. Przebieg łącznie: Home 401 zaliczonych / 0 błędów / 29 pominiętych, Pro 401 / 0 / 29 (`main` 313/0/29 + przebiegi Reboot); w przypadkach C5 `CleanupError` puste.
- `repair-store-other-account` (dawniej `tools-store-repair-other-account`) (`Flow=Execute`, `Catalog=BuiltIn`): **Passed** na Home i Pro (`ExecuteExit=1`, `Failed`, komunikat zawierał wtedy „Rejestracja działa tylko dla konta”; od poprawek po końcowym review partii 1 przypadek sprawdza „uruchom DSOptimizer na koncie”). Runner jest Administratorem, a `$UserSid` to konto `dso`, więc akcja odmawia bez rejestracji. `Setup` zapisuje odczyt `Get-AppxPackage -AllUsers -Name Microsoft.WindowsStore` (rzuca, gdy brak pakietu lub brak linii z `|S-1-5-21-`, co potwierdza kształt `UserSecurityId.Sid`; zapisuje też odczyt manifestu, tylko do odczytu); `Check` wymaga, by odczyt `-AllUsers` po akcji był identyczny jak przed (brak nowego wpisu Administratora); `Cleanup` usuwa pliki pomocnicze.
- `tools-store-repair-register-dso` (przebieg 2026-10-19): **Skipped** (runner nie umiał `RunAs`). Zastąpiony aktywnym `repair-store-register-dso` (`RunAs='dso'`, Task 9): stan `Installed` bez zmian, akcja rejestruje ponownie i odczyt zwrotny zostaje `Installed` + `Status` = `Ok`.
- `repair-store-staged-dso` (`RunAs='dso'`, R33): `Setup` przygotowuje stan `Staged` jak w pomiarze (`Remove-AppxPackage -Package <PackageFullName>` w procesie `dso` przez zadanie Harmonogramu, Interactive + Highest), `Check`: `InstallState` dla SID `dso` = `Installed`. Działanie Store po naprawie wymaga sieci, której VM nie ma.
- Eksploracja K2 (R19, sesja `dso`): rejestracja z manifestu kończy się `Ok` w ok. 2 s, `Status` po = `Ok`; K1 rejestruje pakiet dla Administratora, a wpis `dso` zostaje bez zmian. Research i wyniki eksploracji K2: `docs/zrodla/2026-10-19-partia-c5-zrodla.md` (sekcje „Wyniki VM”, „Sonda uzupełniająca”).
- **Nie testowane na VM:** pozytywna ścieżka z `DSOptimizer.exe` w GUI (sukces rejestracji w sesji konta, które uruchomiło aplikację; CLI jako `dso` pokrywają `repair-store-register-dso` i `repair-store-staged-dso`) i działanie Store po naprawie (brak sieci). Nie ma też instalacji Store (także na LTSC) ani `wsreset -i`.
- **Smoke z prawdziwym `DSOptimizer.exe` w sesji `dso`: do potwierdzenia przez Dawida** (Ruling R30). Nie wykonany, bo Dawid wyjechał; pozycja wchodzi do `main` z tym zastrzeżeniem (jak D2 w partii C4).

Przebieg VM (Task 10, 2026-10-22, pełny `tests/vm/Invoke-VmTests.ps1` na gałęzi `etap4b-1`; raporty poza gitem): Home i Pro po 722 zaliczone / 0 błędów / 25 pominiętych (6 przypadków ze `Skip` spoza kategorii Naprawy). `repair-store-other-account`, `repair-store-register-dso` i `repair-store-staged-dso`: **Passed** na Home i Pro (odmowa dla innego konta; `Executed` „zarejestrowany ponownie”; R33 `Executed` „był na dysku bez wpisu Installed dla tego konta”, `Check` = `Installed` + `Status Ok` dla SID `dso`). `repair-store-register-dso` w pierwszym przebiegu padał na obu VM z `0x80073D02` (Sklep „w użyciu” tuż po zalogowaniu, patrz „Ryzyka”), więc `Setup` czeka, aż od startu `explorer.exe` konta `dso` minie 120 s.

## Obserwacja skutku
Metoda (2026-10-22, 3 przebiegi na `DSO-26H2-Home` i `DSO-26H2-Pro`, build 26300.9457; research: `docs/zrodla/2026-10-22-etap4b-1-pomiary.md`, sekcja 5): w procesie konta `dso` (zadanie Harmonogramu Interactive + Highest, jak DSOptimizer po UAC) `Remove-AppxPackage -Package <PackageFullName Sklepu>` (0,8 s), odczyt `Get-AppxPackage -AllUsers`, `Add-AppxPackage -Register <InstallLocation>\AppxManifest.xml -DisableDevelopmentMode`, odczyt ponowny.
- Przed: `Get-AppxPackage -AllUsers` pokazuje dla `Microsoft.WindowsStore_22506.1400.2.0_x64__8wekyb3d8bbwe` tylko `S-1-5-18` = `Staged`, bez wpisu SID `dso`; `Get-AppxProvisionedPackage -Online` nadal ma Sklep (`Microsoft.WindowsStore_22506.1400.2.0_neutral_~_8wekyb3d8bbwe`).
- Po rejestracji: `InstallState` dla SID `dso` = `Installed`, `Status` = `Ok`; okno „Microsoft Store” otwiera się (strona offline; start przez `explorer.exe shell:AppsFolder\Microsoft.WindowsStore_8wekyb3d8bbwe!App`).
- Bez sieci i bez rejestracji: start Sklepu w stanie `Staged` nie otwiera okna (H1: brak okna w 71 s; P1: otwiera się Eksplorator „Dokumenty”).
- Wniosek: R33 wchodzi. Naprawa musi działać jako zalogowany (SID procesu = SID zalogowanego użytkownika), tak jak dotąd.

## Reddit
Do uzupełnienia w części E.
