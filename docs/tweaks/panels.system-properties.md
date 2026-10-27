# panels.system-properties – Właściwości systemu

## Werdykt 26H2
Portować jako `kind=panel`, `risk=safe`: w VM 26H2 Home i Pro polecenie otwiera klasyczne okno „Właściwości systemu”, a nie Ustawienia. Karta w oknie to wniosek z nazwy procesu (`SystemPropertiesComputerName.exe`), nie odczyt tytułu karty.

## Co zmienia
Nic: `kind=panel` otwiera okno i nie zapisuje ani rejestru, ani usług, ani plików. Pole `launch`: `file` = `control.exe`, `arguments` = `sysdm.cpl`.
Launcher uruchamia przez CreateProcess `C:\Windows\System32\control.exe sysdm.cpl` (plik tylko z System32, nigdy z katalogu bieżącego ani `PATH`), poza Job Objectem skryptów i bez czekania na okno.

Odrzucone warianty (nie osobne pozycje; dane z VM w sekcji „Źródła”):
- `sysdm.cpl` przez ShellExecute (WinUtil): w głównej sondzie bez nowego procesu i okna – nierozstrzygnięte, bo wcześniejszy wariant `control.exe sysdm.cpl,,3` zostawił otwarte `SystemPropertiesAdvanced.exe` (okno jednoinstancyjne – hipoteza, bez bezpośredniego odczytu). Sonda uzupełniająca w czystym stanie potwierdziła okno dla `control.exe sysdm.cpl`.
- `control.exe sysdm.cpl,,3`: otwiera `SystemPropertiesAdvanced.exe` (karta Zaawansowane wg nazwy procesu); zbędny wariant.
- `control.exe /name Microsoft.System`: w K2 powstaje tylko nowy `explorer.exe /factory` bez okna – odrzucony.

## Źródła
- WinUtil `config/feature.json` @ `144d516` (MIT, 2026-07-31): WPFPanelSystem = „System Properties”, `InvokeScript`: `sysdm.cpl`. Hint co do listy i polecenia, nie dowód skutku – https://raw.githubusercontent.com/ChrisTitusTech/winutil/144d51675190ffcb2553b7ba5313f8d602c8ecf9/config/feature.json
- https://learn.microsoft.com/en-us/windows/win32/shell/executing-control-panel-items – MS „Executing Control Panel Items”: przykład `control.exe sysdm.cpl,,3` (zakładki; numeracja zmieniała się między wersjami).
- Odczyt VM 26H2 (build 26300.9457), Home (`DSO-26H2-Home`) i Pro (`DSO-26H2-Pro`), 2026-10-19, K1 = PS Direct jako Administrator (sesja 0), K2 = zadanie interaktywne podniesione w sesji użytkownika `dso`: Sonda uzupełniająca (CreateProcess `control.exe sysdm.cpl`, czysty stan): okno „Właściwości systemu” (`#32770`) w procesie `SystemPropertiesComputerName.exe` (sesja użytkownika). Którą kartę pokazuje, jest wnioskiem z nazwy procesu (domyślna: „Nazwa komputera”); tytuł okna tego nie rozstrzyga, a cytatu MS o tym pliku brak.
  - K1: po 10 s żyje `SystemPropertiesComputerName.exe` (sesja 0; K1 nie pokazuje okna użytkownika, tylko start procesu).
  - K2: okno „Właściwości systemu” (`#32770`). Home = Pro we wszystkich polach poza PID-ami.
- Research i wyniki: `docs/zrodla/2026-10-19-partia-c5-zrodla.md` (sekcje „Wyniki VM”, „Sonda uzupełniająca”).

## Wartości domyślne na świeżym 26H2
Nie dotyczy: pozycja nie czyta ani nie zapisuje ustawień. Stan systemu po uruchomieniu to tylko proces okna (odczyt VM, 2026-10-19, wyżej).

## Detekcja i undo
Nie dotyczy: `kind=panel` nie ma detekcji ani cofnięcia. `Executed` oznacza tylko, że proces wystartował; czy okno się pokazało, ocenia test VM (K2; smoke z prawdziwym DSOptimizer.exe w sesji dso – do potwierdzenia przez Dawida (R30)).

## Ryzyka i ostrzeżenia
- `risk=safe`: pozycja niczego nie zmienia w systemie; okno zamykasz sam (DSOptimizer nie czeka na nie i go nie zamyka).
- Okno „Właściwości systemu” jest prawdopodobnie jednoinstancyjne (hipoteza): jeśli inne okno tego typu jest już otwarte, kolejne uruchomienie może tylko je aktywować.
- Zmierzone na VM z polskimi ustawieniami regionalnymi; tytuł okna może się różnić na innej kulturze.
- Scenariusz „DSOptimizer podniesiony innym kontem administratora niż zalogowany użytkownik” nie był mierzony na VM dla tego panelu (host `control.exe`/`explorer.exe` sam używa asocjacji i COM w procesie podniesionym; DSOptimizer kontroluje tylko ścieżkę uruchamianego pliku).

## Test VM
Przebieg VM `20261019-110405` (2026-10-19, HEAD `fd320ed`, build 26300.9457): Home (`DSO-26H2-Home`) i Pro (`DSO-26H2-Pro`), `tests/vm/Invoke-VmTests.ps1`. Raporty lokalne poza gitem. Przebieg łącznie: Home 401 zaliczonych / 0 błędów / 29 pominiętych, Pro 401 / 0 / 29 (`main` 313/0/29 + przebiegi Reboot); w przypadkach C5 `CleanupError` puste.
- `panels-system-properties` (`Flow=Execute`, `Catalog=BuiltIn`, `RestorePoint=skip`): **Passed** na Home i Pro (`ExecuteExit=0`, `Executed`). `Check` odpytuje w pętli: żyje `SystemPropertiesComputerName.exe` (Setup zamyka przed startem wszystkie `SystemProperties*.exe`, żeby stary proces nie zaliczył Check); `Cleanup` zamyka tylko procesy pasujące do `Check`. **Check dowodzi, że proces z polecenia żyje (K1, PS Direct jako Administrator, sesja 0), nie że okno pojawiło się u użytkownika.**
- Eksploracja K2 (zadanie interaktywne w sesji `dso`, token `Limited`): okno „Właściwości systemu”. Home = Pro. Research i wyniki eksploracji K2: `docs/zrodla/2026-10-19-partia-c5-zrodla.md` (sekcje „Wyniki VM”, „Sonda uzupełniająca”).
- Klik ręczny Dawida (VM Pro, sesja `dso`, Win+R, 2026-10-19): `sysdm.cpl` otwiera klasyczne okno „Właściwości systemu”.
- Kliknięte polecenie (`sysdm.cpl`) nie jest tym samym co `launch` pozycji (`control.exe sysdm.cpl`): klik potwierdza panel, a `launch` pozycji potwierdza dopiero eksploracja K2.
- **Smoke z prawdziwym `DSOptimizer.exe` w sesji `dso`: do potwierdzenia przez Dawida** (Ruling R30). Nie wykonany, bo Dawid wyjechał; pozycja wchodzi do `main` z tym zastrzeżeniem (jak D2 w partii C4). Rozbieżność między „proces żyje” (Check) a oknem widocznym dla użytkownika wyjdzie dopiero przy pierwszym użyciu z aplikacji.

## Reddit
Do uzupełnienia w części E.
