# panels.power-options – Opcje zasilania

## Werdykt 26H2
Portować jako `kind=panel`, `risk=safe` (alias z poprzedniej wersji `tool_pow`): w VM 26H2 Home i Pro polecenie otwiera klasyczne okno „Opcje zasilania”, a nie Ustawienia. Pozycja tylko uruchamia program i niczego nie zmienia.

## Co zmienia
Nic: `kind=panel` otwiera okno i nie zapisuje ani rejestru, ani usług, ani plików. Pole `launch`: `file` = `control.exe`, `arguments` = `/name Microsoft.PowerOptions`.
Launcher uruchamia przez CreateProcess `C:\Windows\System32\control.exe /name Microsoft.PowerOptions` (plik tylko z System32, nigdy z katalogu bieżącego ani `PATH`), poza Job Objectem skryptów i bez czekania na okno.

Odrzucone warianty (nie osobne pozycje; dane z VM w sekcji „Źródła”):
- `powercfg.cpl` (WinUtil) i `control.exe powercfg.cpl` (poprzednia wersja): w VM to samo okno „Opcje zasilania”; remis rozstrzygnięty na korzyść nazwy kanonicznej MS.

## Źródła
- WinUtil `config/feature.json` @ `144d516` (MIT, 2026-07-31): WPFPanelPower = „Power Panel”, `InvokeScript`: `powercfg.cpl`. Hint co do listy i polecenia, nie dowód skutku – https://raw.githubusercontent.com/ChrisTitusTech/winutil/144d51675190ffcb2553b7ba5313f8d602c8ecf9/config/feature.json
- https://learn.microsoft.com/en-us/windows/win32/shell/executing-control-panel-items – MS „Executing Control Panel Items”: „control.exe /name *canonicalName*”.
- https://learn.microsoft.com/en-us/windows/win32/shell/controlpanel-canonical-names – MS „Canonical Names of Control Panel Items”: `Microsoft.PowerOptions` (`powercpl.dll`, `{025A5937-A6BE-4686-A844-36FE4BEC8B6D}`).
- Odczyt VM 26H2 (build 26300.9457), Home (`DSO-26H2-Home`) i Pro (`DSO-26H2-Pro`), 2026-10-19: Okno „Opcje zasilania” (`CabinetWClass`) w `explorer.exe` użytkownika (`/factory`, token `Limited`); `control.exe` kończy się kodem 1 mimo otwartego okna.
  - Konto Administrator przez PowerShell Direct (sesja 0): po 10 s żyje `control.exe` z linią `/name Microsoft.PowerOptions` (ta sesja nie pokazuje okna użytkownika, tylko start procesu).
  - Sesja użytkownika `dso` (zadanie podniesione): okno „Opcje zasilania” (`CabinetWClass`). Home = Pro we wszystkich polach poza PID-ami.

## Wartości domyślne na świeżym 26H2
Nie dotyczy: pozycja nie czyta ani nie zapisuje ustawień. Stan systemu po uruchomieniu to tylko proces okna (odczyt VM, 2026-10-19, wyżej).

## Detekcja i undo
Nie dotyczy: `kind=panel` nie ma detekcji ani cofnięcia. `Executed` oznacza tylko, że proces wystartował; czy okno się pokazało, ocenia test VM w sesji użytkownika `dso` (zadanie podniesione; smoke z prawdziwym DSOptimizer.exe w sesji dso – nie sprawdzono ręcznie w programie).

## Ryzyka i ostrzeżenia
- `risk=safe`: pozycja niczego nie zmienia w systemie; okno zamykasz sam (DSOptimizer nie czeka na nie i go nie zamyka).
- Pozycja nie tworzy ani nie zmienia planów zasilania. Opis w poprzedniej wersji (`tool_pow`) obiecywał plan „Ultimate Performance” – ta pozycja tego nie robi (przeniesione z opisu w programie przy porządkowaniu opisów).
- Zmierzone na VM z polskimi ustawieniami regionalnymi; tytuł okna może się różnić na innej kulturze.
- Scenariusz „DSOptimizer podniesiony innym kontem administratora niż zalogowany użytkownik” nie był mierzony na VM dla tego panelu (host `control.exe`/`explorer.exe` sam używa asocjacji i COM w procesie podniesionym; DSOptimizer kontroluje tylko ścieżkę uruchamianego pliku).

## Test VM
Przebieg VM `20261019-110405` (2026-10-19, build 26300.9457): Home (`DSO-26H2-Home`) i Pro (`DSO-26H2-Pro`), `tests/vm/Invoke-VmTests.ps1`. Raporty lokalne poza gitem. Przebieg łącznie: Home 401 zaliczonych / 0 błędów / 29 pominiętych, Pro 401 / 0 / 29 (`main` 313/0/29 + przebiegi Reboot); w każdym z przypadków paneli `CleanupError` puste.
- `panels-power-options` (`Flow=Execute`, `Catalog=BuiltIn`, `RestorePoint=skip`): **Passed** na Home i Pro (`ExecuteExit=0`, `Executed`). `Check` odpytuje w pętli: żyje `control.exe` z linią poleceń zawierającą `Microsoft.PowerOptions`; `Cleanup` zamyka tylko procesy pasujące do `Check`. **Check dowodzi, że proces z polecenia żyje (konto Administrator przez PowerShell Direct, sesja 0), nie że okno pojawiło się u użytkownika.**
- Pomiar na VM w sesji użytkownika `dso` (zadanie podniesione, token `Limited`): okno „Opcje zasilania” (w `explorer.exe`; `control.exe` kończy się kodem 1, więc Check patrzy na proces z konta Administrator przez PowerShell Direct (sesja 0), nie na okno). Home = Pro.
- Klik ręczny (VM Pro, sesja `dso`, Win+R, 2026-10-19): `powercfg.cpl` oraz `control.exe /name Microsoft.PowerOptions` otwierają klasyczne okno.
- Kliknięte polecenia (`powercfg.cpl`, `control.exe /name Microsoft.PowerOptions`) są tylko częściowo zgodne z `launch` pozycji (`control.exe /name Microsoft.PowerOptions`; sam `powercfg.cpl` to inne polecenie): klik potwierdza panel, a `launch` pozycji potwierdza dopiero pomiar na VM w sesji użytkownika `dso` (zadanie podniesione).
- **Smoke z prawdziwym `DSOptimizer.exe` w sesji `dso`: nie sprawdzono ręcznie w programie**. Niewykonany; pozycja wchodzi do `main` z tym zastrzeżeniem. Rozbieżność między „proces żyje” (Check) a oknem widocznym dla użytkownika wyjdzie dopiero przy pierwszym użyciu z aplikacji.
