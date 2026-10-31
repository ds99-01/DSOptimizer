# panels.devices-printers – Urządzenia i drukarki

## Werdykt 26H2
Portować jako `kind=panel`, `risk=safe`: w VM 26H2 Home i Pro polecenie otwiera klasyczne okno „Urządzenia i drukarki — Eksplorator plików”, a nie Ustawienia. Wchodzi z zastrzeżeniem: wymaga Eksploratora tego konta w bieżącej sesji (strażnik); poza taką sesją odmawia, zamiast zostawić podniesiony Eksplorator.

## Co zmienia
Nic: `kind=panel` otwiera okno i nie zapisuje ani rejestru, ani usług, ani plików. Pole `launch`: `file` = `shell:::{A8A91A66-3A7D-4424-8D24-04E180695C7A}`, `arguments` = puste.
Launcher uruchamia przez CreateProcess `%SystemRoot%\explorer.exe` (stała ścieżka z `Environment.GetFolderPath(Windows)`, nie System32) z argumentem `shell:::{A8A91A66-3A7D-4424-8D24-04E180695C7A}`, bez ShellExecute i bez czekania na okno. Przed startem sprawdza, że w bieżącej sesji działa `explorer.exe` tego samego konta (strażnik powłoki, spec A6); inaczej `Failed` „Brak Eksploratora tego konta w bieżącej sesji – nie uruchamiam podniesionego Eksploratora.” i nic nie startuje.

Odrzucone warianty (nie osobne pozycje; dane z VM w sekcji „Źródła”):
- `Start-Process 'shell:::{A8A91A66-3A7D-4424-8D24-04E180695C7A}'` (WinUtil, ShellExecute): w K1 blokuje ok. 108 s i zwraca błąd „Wykonanie serwera nie powiodło się”, w K2 zwraca błąd po 49 ms mimo otwartego okna – fałszywie negatywny wynik. ShellExecute nie istnieje w launcherze.
- `control.exe /name Microsoft.DevicesAndPrinters` (nazwa kanoniczna MS): przekierowuje do aplikacji Ustawienia (`SystemSettings.exe` + `ApplicationFrameHost.exe`), nie do klasycznego okna; fallback `ms-settings:` odrzucony (decyzja D2).

## Źródła
- WinUtil `config/feature.json` @ `144d516` (MIT, 2026-07-31): WPFPanelPrinter = „Printer Panel”, `InvokeScript`: `Start-Process 'shell:::{A8A91A66-3A7D-4424-8D24-04E180695C7A}'`. Hint co do listy i polecenia, nie dowód skutku – https://raw.githubusercontent.com/ChrisTitusTech/winutil/144d51675190ffcb2553b7ba5313f8d602c8ecf9/config/feature.json
- https://learn.microsoft.com/en-us/windows/win32/shell/controlpanel-canonical-names – MS „Canonical Names of Control Panel Items”: `Microsoft.DevicesAndPrinters` = `{A8A91A66-3A7D-4424-8D24-04E180695C7A}` („Devices and Printers”).
- Odczyt VM 26H2 (build 26300.9457), Home (`DSO-26H2-Home`) i Pro (`DSO-26H2-Pro`), 2026-10-19, K1 = PS Direct jako Administrator (sesja 0), K2 = zadanie interaktywne podniesione w sesji użytkownika `dso`: Sonda uzupełniająca (CreateProcess `C:\Windows\explorer.exe shell:::{A8A91A66-…}`, K2): uruchomiony `explorer.exe` kończy się kodem 1 przed 3 s, a okno „Urządzenia i drukarki — Eksplorator plików” (`CabinetWClass`) należy do nowego procesu `explorer.exe /factory,{75dff2b7-…} -Embedding` konta użytkownika (sesja 1, token `Limited`, `IsUserShell` = false).
  - K1 (sesja 0): CreateProcess bez strażnika zostawił żywy, podniesiony `explorer.exe` Administratora (token `Default`, `Elevated` = true) bez okna dla użytkownika. Dlatego strażnik w K1 odmawia (brak Eksploratora Administratora w sesji 0), a przypadek VM przez PS Direct nie sprawdza okna.
  - K2: okno „Urządzenia i drukarki — Eksplorator plików” (`CabinetWClass`). Home = Pro we wszystkich polach poza PID-ami.
- Research i wyniki: `docs/zrodla/2026-10-19-partia-c5-zrodla.md` (sekcje „Wyniki VM”, „Sonda uzupełniająca”).

## Wartości domyślne na świeżym 26H2
Nie dotyczy: pozycja nie czyta ani nie zapisuje ustawień. Stan systemu po uruchomieniu to tylko proces okna (odczyt VM, 2026-10-19, wyżej).

## Detekcja i undo
Nie dotyczy: `kind=panel` nie ma detekcji ani cofnięcia. `Executed` oznacza tylko, że proces wystartował; czy okno się pokazało, ocenia test VM (K2; smoke z prawdziwym DSOptimizer.exe w sesji dso – do potwierdzenia przez autora).

## Ryzyka i ostrzeżenia
- `risk=safe`: pozycja niczego nie zmienia w systemie; okno zamykasz sam (DSOptimizer nie czeka na nie i go nie zamyka).
- Panel startuje przez `%SystemRoot%\explorer.exe` i wymaga uruchomionego Eksploratora tego samego konta w bieżącej sesji. Bez niego (np. aplikacja podniesiona innym kontem administratora niż zalogowany użytkownik) pozycja kończy się błędem i niczego nie uruchamia: podniesiony Eksplorator zostałby osobnym procesem (zmierzone w VM).
- Zmierzone na VM z polskimi ustawieniami regionalnymi; tytuł okna może się różnić na innej kulturze.
- Brak powłoki konta w sesji (explorer.exe bez `/factory`; same procesy `/factory` nie liczą się, bo przeżywają śmierć powłoki): `Failed` bez startu. Strażnik nie sprawdza tokena znalezionej powłoki (świadome ograniczenie). Przypadek VM `panels-devices-printers-no-shell` (PS Direct jako Administrator, sesja 0) właśnie to sprawdza: oczekuje `Failed` z komunikatem „Brak Eksploratora tego konta” i braku nowego explorer.exe z `shell:::{A8A91A66` w linii poleceń; okno sprawdzi smoke z prawdziwym DSOptimizer.exe w sesji dso – do potwierdzenia przez autora.
- Scenariusz „DSOptimizer podniesiony innym kontem administratora niż zalogowany użytkownik” nie był mierzony na VM dla tego panelu (host `control.exe`/`explorer.exe` sam używa asocjacji i COM w procesie podniesionym; DSOptimizer kontroluje tylko ścieżkę uruchamianego pliku).

## Test VM
Przebieg VM `20261019-110405` (2026-10-19, HEAD `fd320ed`, build 26300.9457): Home (`DSO-26H2-Home`) i Pro (`DSO-26H2-Pro`), `tests/vm/Invoke-VmTests.ps1`. Raporty lokalne poza gitem. Przebieg łącznie: Home 401 zaliczonych / 0 błędów / 29 pominiętych, Pro 401 / 0 / 29 (`main` 313/0/29 + przebiegi Reboot); w przypadkach C5 `CleanupError` puste.
- `panels-devices-printers-no-shell` (`Flow=Execute`, `ExecuteExit=1`, oczekiwany `Failed`, `MessageContains` „Brak Eksploratora tego konta”): **Passed** na Home i Pro. Wynik wykonania: `OperationFailed` / `Failed`, komunikat „Brak Eksploratora tego konta w bieżącej sesji – nie uruchamiam podniesionego Eksploratora.” Strażnik odmawia, bo PS Direct jako Administrator (sesja 0) nie ma powłoki tego konta. `Check` czeka 3 s i wymaga braku `explorer.exe` z `shell:::{A8A91A66` w linii poleceń (nic nie wystartowało); `Cleanup` zamyka tylko taki proces. Przypadek **nie jest `Skip`**, ale też **nie sprawdza okna**: sprawdza wyłącznie odmowę bez powłoki.
- Eksploracja K2 (sesja `dso`, token `Limited`): okno „Urządzenia i drukarki — Eksplorator plików” (`CabinetWClass`), należy do procesu `explorer.exe /factory,{…} -Embedding` konta. Home = Pro. Research i wyniki eksploracji K2: `docs/zrodla/2026-10-19-partia-c5-zrodla.md` (sekcje „Wyniki VM”, „Sonda uzupełniająca”).
- Klik ręczny autora (VM Pro, sesja `dso`, Win+R, 2026-10-19): `shell:::{A8A91A66-…}` otwiera klasyczne okno „Urządzenia i drukarki”.
- Wariant `control.exe /name Microsoft.DevicesAndPrinters` odrzucony na podstawie eksploracji VM (K1/K2, nie klik autora): przekierowuje do Ustawień (`SystemSettings.exe`). Wariant `control.exe /name Microsoft.System` dotyczy `panels.system-properties` (eksploracja: brak okna), nie tej pozycji.
- Kliknięte `shell:::{A8A91A66-…}` z Win+R idzie przez powłokę, a `launch` pozycji przez CreateProcess `explorer.exe shell:::{GUID}` (za strażnikiem), więc to nie jest ta sama ścieżka: klik potwierdza panel, a `launch` pozycji potwierdza dopiero eksploracja K2.
- **Smoke z prawdziwym `DSOptimizer.exe` w sesji `dso`: do potwierdzenia przez autora**. Nie wykonany, bo autor wyjechał; pozycja wchodzi do `main` z tym zastrzeżeniem (jak D2 w partii C4). Rozbieżność między „proces żyje” (Check) a oknem widocznym dla użytkownika wyjdzie dopiero przy pierwszym użyciu z aplikacji.
- Pozytywna ścieżka (okno przy powłoce konta) nie ma przypadku w runnerze (runner jest Administratorem w sesji 0); sprawdza ją dopiero smoke w sesji `dso`.
