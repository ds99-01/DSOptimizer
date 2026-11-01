# panels.visual-effects – Efekty wizualne (Opcje wydajności)

## Werdykt 26H2
Portować jako `kind=panel`, `risk=safe` (alias z poprzedniej wersji `tool_vis`): w VM 26H2 Home i Pro polecenie otwiera klasyczne okno „Opcje wydajności”, a nie Ustawienia. Pozycja tylko uruchamia program i niczego nie zmienia.

## Co zmienia
Nic: `kind=panel` otwiera okno i nie zapisuje ani rejestru, ani usług, ani plików. Pole `launch`: `file` = `SystemPropertiesPerformance.exe`, `arguments` = puste.
Launcher uruchamia przez CreateProcess `C:\Windows\System32\SystemPropertiesPerformance.exe` (plik tylko z System32, nigdy z katalogu bieżącego ani `PATH`), poza Job Objectem skryptów i bez czekania na okno.

Odrzucone warianty (nie osobne pozycje; dane z VM w sekcji „Źródła”):
- `control.exe sysdm.cpl,,3` (poprzednia wersja `tool_vis`): `control.exe` kończy się kodem 1, a okno należy do `SystemPropertiesAdvanced.exe` (tytuł „Właściwości systemu”, `#32770`) – to jest okno „Właściwości systemu”, nie „Opcje wydajności”. Karta Zaawansowane to wniosek z nazwy procesu (MS nazywa `SystemPropertiesAdvanced.exe` „Advanced system properties”). Efekty wizualne są w tym wariancie o jedno kliknięcie dalej, dlatego wariant odpada dla tej pozycji.

## Źródła
- WinUtil: brak wiersza dla tej pozycji (poprzednia wersja, `core_models.py`: `sysdm.cpl,,3`, odrzucone, patrz wyżej).
- https://learn.microsoft.com/en-us/windows/win32/shell/executing-control-panel-items – MS „Executing Control Panel Items” (dostęp 2026-10-19): „Performance: %windir%\system32\SystemPropertiesPerformance.exe”.
- Odczyt VM 26H2 (build 26300.9457), Home (`DSO-26H2-Home`) i Pro (`DSO-26H2-Pro`), 2026-10-19: Okno „Opcje wydajności” z procesu `SystemPropertiesPerformance.exe` (sesja użytkownika, podniesiony). Przełączników efektów automat nie czytał; według oględzin w VM (2026-10-19) w oknie widać „Efekty wizualne”.
  - Konto Administrator przez PowerShell Direct (sesja 0): po 10 s żyje `SystemPropertiesPerformance.exe` (ta sesja nie pokazuje okna użytkownika, tylko start procesu).
  - Sesja użytkownika `dso` (zadanie podniesione): okno „Opcje wydajności” (`#32770`). Home = Pro we wszystkich polach poza PID-ami.

## Wartości domyślne na świeżym 26H2
Nie dotyczy: pozycja nie czyta ani nie zapisuje ustawień. Stan systemu po uruchomieniu to tylko proces okna (odczyt VM, 2026-10-19, wyżej).

## Detekcja i undo
Nie dotyczy: `kind=panel` nie ma detekcji ani cofnięcia. `Executed` oznacza tylko, że proces wystartował; czy okno się pokazało, ocenia test VM w sesji użytkownika `dso` (zadanie podniesione; smoke z prawdziwym DSOptimizer.exe w sesji dso – nie sprawdzono ręcznie w programie).

## Ryzyka i ostrzeżenia
- `risk=safe`: pozycja niczego nie zmienia w systemie; okno zamykasz sam (DSOptimizer nie czeka na nie i go nie zamyka).
- Zmierzone na VM z polskimi ustawieniami regionalnymi; tytuł okna może się różnić na innej kulturze.

## Test VM
Przebieg VM `20261019-110405` (2026-10-19, build 26300.9457): Home (`DSO-26H2-Home`) i Pro (`DSO-26H2-Pro`), `tests/vm/Invoke-VmTests.ps1`. Raporty lokalne poza gitem. Przebieg łącznie: Home 401 zaliczonych / 0 błędów / 29 pominiętych, Pro 401 / 0 / 29 (`main` 313/0/29 + przebiegi Reboot); w tych przypadkach `CleanupError` puste.
- `panels-visual-effects` (`Flow=Execute`, `Catalog=BuiltIn`, `RestorePoint=skip`): **Passed** na Home i Pro (`ExecuteExit=0`, `Executed`). `Check` odpytuje w pętli: w ciągu 15 s (pętla co 0,5 s) żyje proces `SystemPropertiesPerformance.exe`; `Cleanup` zamyka tylko procesy pasujące do `Check`. **Check dowodzi, że proces z polecenia żyje (konto Administrator przez PowerShell Direct, sesja 0), nie że okno pojawiło się u użytkownika.**
- Eksploracja w sesji użytkownika `dso` (zadanie podniesione, token `Limited`): okno „Opcje wydajności” (zakładka „Efekty wizualne”). Home = Pro.
- Klik ręczny (VM Pro, sesja `dso`, Win+R, 2026-10-19): `SystemPropertiesPerformance.exe` otwiera okno z zakładką „Efekty wizualne”. Win+R z samym `sysdm.cpl,,3` daje błąd „nie można odnaleźć pliku” (poprzednia wersja wołała `control.exe sysdm.cpl,,3`, nie samo `sysdm.cpl,,3`).
- Wariant `control.exe sysdm.cpl,,3` odrzucony na podstawie eksploracji VM (sesje: konto Administrator przez PowerShell Direct i sesja użytkownika `dso`, nie klik ręczny): otwiera Właściwości systemu (`SystemPropertiesAdvanced.exe`), nie efekty wizualne.
- Kliknięte `SystemPropertiesPerformance.exe` z Win+R nie jest dokładnie tym samym co `launch` pozycji (CreateProcess pełnej ścieżki z System32): klik potwierdza panel, a `launch` pozycji potwierdza dopiero eksploracja w sesji użytkownika `dso` (zadanie podniesione).
- **Smoke z prawdziwym `DSOptimizer.exe` w sesji `dso`: nie sprawdzono ręcznie w programie**. Niewykonany; pozycja wchodzi do `main` z tym zastrzeżeniem. Rozbieżność między „proces żyje” (Check) a oknem widocznym dla użytkownika wyjdzie dopiero przy pierwszym użyciu z aplikacji.
