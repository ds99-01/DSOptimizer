# panels.date-time – Data i godzina

## Werdykt 26H2
Portować jako `kind=panel`, `risk=safe`: w VM 26H2 Home i Pro polecenie otwiera klasyczne okno „Data i godzina”, a nie Ustawienia. Pozycja tylko uruchamia program i niczego nie zmienia.

## Co zmienia
Nic: `kind=panel` otwiera okno i nie zapisuje ani rejestru, ani usług, ani plików. Pole `launch`: `file` = `control.exe`, `arguments` = `/name Microsoft.DateAndTime`.
Launcher uruchamia przez CreateProcess `C:\Windows\System32\control.exe /name Microsoft.DateAndTime` (plik tylko z System32, nigdy z katalogu bieżącego ani `PATH`), poza Job Objectem skryptów i bez czekania na okno.

Odrzucone warianty (nie osobne pozycje; dane z VM w sekcji „Źródła”):
- `timedate.cpl` (WinUtil): w VM to samo okno; remis rozstrzygnięty na korzyść nazwy kanonicznej MS.

## Źródła
- WinUtil `config/feature.json` @ `144d516` (MIT, 2026-07-31): WPFPanelTimedate = „Time and Date”, `InvokeScript`: `timedate.cpl`. Hint co do listy i polecenia, nie dowód skutku – https://raw.githubusercontent.com/ChrisTitusTech/winutil/144d51675190ffcb2553b7ba5313f8d602c8ecf9/config/feature.json
- https://learn.microsoft.com/en-us/windows/win32/shell/controlpanel-canonical-names – MS „Canonical Names of Control Panel Items”: `Microsoft.DateAndTime` (`timedate.cpl,-51`).
- Odczyt VM 26H2 (build 26300.9457), Home (`DSO-26H2-Home`) i Pro (`DSO-26H2-Pro`), 2026-10-19, K1 = PS Direct jako Administrator (sesja 0), K2 = zadanie interaktywne podniesione w sesji użytkownika `dso`: Okno „Data i godzina” (`#32770`) w `rundll32.exe` (podniesionym); `control.exe` kończy się kodem 1 mimo otwartego okna.
  - K1: po 10 s żyje `rundll32.exe` (`…\timedate.cpl`) (sesja 0; K1 nie pokazuje okna użytkownika, tylko start procesu).
  - K2: okno „Data i godzina” (`#32770`). Home = Pro we wszystkich polach poza PID-ami.
- Research i wyniki: `docs/zrodla/2026-10-19-partia-c5-zrodla.md` (sekcje „Wyniki VM”, „Sonda uzupełniająca”).

## Wartości domyślne na świeżym 26H2
Nie dotyczy: pozycja nie czyta ani nie zapisuje ustawień. Stan systemu po uruchomieniu to tylko proces okna (odczyt VM, 2026-10-19, wyżej).

## Detekcja i undo
Nie dotyczy: `kind=panel` nie ma detekcji ani cofnięcia. `Executed` oznacza tylko, że proces wystartował; czy okno się pokazało, ocenia test VM (K2; smoke z prawdziwym DSOptimizer.exe w sesji dso – do potwierdzenia przez Dawida (R30)).

## Ryzyka i ostrzeżenia
- `risk=safe`: pozycja niczego nie zmienia w systemie; okno zamykasz sam (DSOptimizer nie czeka na nie i go nie zamyka).
- Zmierzone na VM z polskimi ustawieniami regionalnymi; tytuł okna może się różnić na innej kulturze.
- Scenariusz „DSOptimizer podniesiony innym kontem administratora niż zalogowany użytkownik” nie był mierzony na VM dla tego panelu (host `control.exe`/`explorer.exe` sam używa asocjacji i COM w procesie podniesionym; DSOptimizer kontroluje tylko ścieżkę uruchamianego pliku).

## Test VM
Przebieg VM `20261019-110405` (2026-10-19, HEAD `fd320ed`, build 26300.9457): Home (`DSO-26H2-Home`) i Pro (`DSO-26H2-Pro`), `tests/vm/Invoke-VmTests.ps1`. Raporty lokalne poza gitem. Przebieg łącznie: Home 401 zaliczonych / 0 błędów / 29 pominiętych, Pro 401 / 0 / 29 (`main` 313/0/29 + przebiegi Reboot); w przypadkach C5 `CleanupError` puste.
- `panels-date-time` (`Flow=Execute`, `Catalog=BuiltIn`, `RestorePoint=skip`): **Passed** na Home i Pro (`ExecuteExit=0`, `Executed`). `Check` odpytuje w pętli: żyje `rundll32.exe` z linią poleceń zawierającą `timedate.cpl`; `Cleanup` zamyka tylko procesy pasujące do `Check`. **Check dowodzi, że proces z polecenia żyje (K1, PS Direct jako Administrator, sesja 0), nie że okno pojawiło się u użytkownika.**
- Eksploracja K2 (zadanie interaktywne w sesji `dso`, token `Limited`): okno „Data i godzina”. Home = Pro. Research i wyniki eksploracji K2: `docs/zrodla/2026-10-19-partia-c5-zrodla.md` (sekcje „Wyniki VM”, „Sonda uzupełniająca”).
- Klik ręczny Dawida (VM Pro, sesja `dso`, Win+R, 2026-10-19): `timedate.cpl` otwiera klasyczne okno.
- Kliknięte polecenie (`timedate.cpl`) nie jest tym samym co `launch` pozycji (`control.exe /name Microsoft.DateAndTime`): klik potwierdza panel, a `launch` pozycji potwierdza dopiero eksploracja K2.
- **Smoke z prawdziwym `DSOptimizer.exe` w sesji `dso`: do potwierdzenia przez Dawida** (Ruling R30). Nie wykonany, bo Dawid wyjechał; pozycja wchodzi do `main` z tym zastrzeżeniem (jak D2 w partii C4). Rozbieżność między „proces żyje” (Check) a oknem widocznym dla użytkownika wyjdzie dopiero przy pierwszym użyciu z aplikacji.

## Reddit
Do uzupełnienia w części E.
