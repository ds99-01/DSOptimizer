# panels.system-restore – Przywracanie systemu

## Werdykt 26H2
Portować jako `kind=panel`, `risk=safe`: w VM 26H2 Home i Pro polecenie otwiera klasyczne okno „Przywracanie systemu”, a nie Ustawienia. Pozycja tylko uruchamia program i niczego nie zmienia.

## Co zmienia
Nic: `kind=panel` otwiera okno i nie zapisuje ani rejestru, ani usług, ani plików. Pole `launch`: `file` = `rstrui.exe`, `arguments` = puste.
Launcher uruchamia przez CreateProcess `C:\Windows\System32\rstrui.exe` (plik tylko z System32, nigdy z katalogu bieżącego ani `PATH`), poza Job Objectem skryptów i bez czekania na okno.
Skutek uboczny (zmierzony na koncie Administrator przez PowerShell Direct (sesja 0), Home i Pro): start usługi `wbengine.exe` (Block Level Backup Engine) towarzyszy uruchomieniu `rstrui.exe`. Kreator otwiera się także przy wyłączonej ochronie systemu i braku punktów przywracania.

## Źródła
- WinUtil `config/feature.json` @ `144d516` (MIT, 2026-07-31): WPFPanelRestore = „Windows Restore”, `InvokeScript`: `rstrui.exe`. Hint co do listy i polecenia, nie dowód skutku – https://raw.githubusercontent.com/ChrisTitusTech/winutil/144d51675190ffcb2553b7ba5313f8d602c8ecf9/config/feature.json
- Microsoft Learn: brak strony z nazwą kanoniczną lub poleceniem dla tej pozycji w Windows 11 (nazwę podaje tylko WinUtil); ogólnie MS potwierdza tylko, że Panel sterowania istnieje dla zgodności, a część ustawień migruje do Ustawień: https://support.microsoft.com/en-us/windows/system-configuration-tools-in-windows-f8a49657-b038-43b8-82d3-28bea0c5666b
- Odczyt VM 26H2 (build 26300.9457), Home (`DSO-26H2-Home`) i Pro (`DSO-26H2-Pro`), 2026-10-19: Okno „Przywracanie systemu” (`#32770`) w `rstrui.exe` (podniesionym). Kreator otwiera się także przy wyłączonej ochronie systemu: na VM `RestorePointCount` = 0, `SRInitDone` = 1, `RPSessionInterval` = 0 (Home i Pro); zgodne z oględzinami. Skutek uboczny zmierzony na koncie Administrator przez PowerShell Direct (sesja 0): start usługi `wbengine.exe`.
  - Konto Administrator przez PowerShell Direct (sesja 0): po 10 s żyje `rstrui.exe` (ta sesja nie pokazuje okna użytkownika, tylko start procesu).
  - Sesja użytkownika `dso` (zadanie podniesione): okno „Przywracanie systemu” (`#32770`). Home = Pro we wszystkich polach poza PID-ami.

## Wartości domyślne na świeżym 26H2
Nie dotyczy: pozycja nie czyta ani nie zapisuje ustawień. Stan systemu po uruchomieniu to tylko proces okna (odczyt VM, 2026-10-19, wyżej).

## Detekcja i undo
Nie dotyczy: `kind=panel` nie ma detekcji ani cofnięcia. `Executed` oznacza tylko, że proces wystartował; czy okno się pokazało, ocenia test VM w sesji użytkownika `dso` (zadanie podniesione; smoke z prawdziwym DSOptimizer.exe w sesji dso – nie sprawdzono ręcznie w programie).

## Ryzyka i ostrzeżenia
- `risk=safe`: pozycja niczego nie zmienia w systemie; okno zamykasz sam (DSOptimizer nie czeka na nie i go nie zamyka).
- Samo uruchomienie kreatora uruchamia też `wbengine.exe` (zmierzone w VM). Kreator może przywrócić system do punktu przywracania – to decyzja użytkownika w otwartym oknie, nie tej pozycji.
- Zmierzone na VM z polskimi ustawieniami regionalnymi; tytuł okna może się różnić na innej kulturze.

## Test VM
Przebieg VM z 2026-11-05 (build 26300.9457) na Windows 11 26H2 po angielsku i po polsku: `DSO-EN-Home` i `DSO-PL-Home` (Core), `DSO-EN-Pro` i `DSO-PL-Pro` (Professional), na każdej **Passed**: przypadek `panels-system-restore`. Na każdej VM pełny przebieg: 861 zaliczonych, 0 błędów, 25 pominiętych. Niżej wcześniejsze przebiegi na polskim Windows.

Przebieg VM `20261019-110405` (2026-10-19, build 26300.9457): Home (`DSO-26H2-Home`) i Pro (`DSO-26H2-Pro`), `tests/vm/Invoke-VmTests.ps1`. Raporty lokalne poza gitem. Przebieg łącznie: Home 401 zaliczonych / 0 błędów / 29 pominiętych, Pro 401 / 0 / 29 (`main` 313/0/29 + przebiegi Reboot); w każdym z przypadków paneli `CleanupError` puste.
- `panels-system-restore` (`Flow=Execute`, `Catalog=BuiltIn`, `RestorePoint=skip`): **Passed** na Home i Pro (`ExecuteExit=0`, `Executed`). `Check` odpytuje w pętli: żyje `rstrui.exe`; `Cleanup` zamyka tylko procesy pasujące do `Check`. **Check dowodzi, że proces z polecenia żyje (konto Administrator przez PowerShell Direct, sesja 0), nie że okno pojawiło się u użytkownika.**
- Pomiar na VM w sesji użytkownika `dso` (zadanie podniesione, token `Limited`): okno „Przywracanie systemu”; skutek uboczny: start usługi `wbengine.exe` (zostaje do revertu checkpointu). Home = Pro.
- Klik ręczny (VM Pro, sesja `dso`, Win+R, 2026-10-19): `rstrui.exe` otwiera kreator przywracania, mimo wyłączonej ochrony systemu.
- Kliknięte `rstrui.exe` z Win+R nie jest dokładnie tym samym co `launch` pozycji (CreateProcess pełnej ścieżki z System32): klik potwierdza panel, a `launch` pozycji potwierdza dopiero pomiar na VM w sesji użytkownika `dso` (zadanie podniesione).
- **Smoke z prawdziwym `DSOptimizer.exe` w sesji `dso`: nie sprawdzono ręcznie w programie**. Niewykonany; pozycja wchodzi do `main` z tym zastrzeżeniem. Rozbieżność między „proces żyje” (Check) a oknem widocznym dla użytkownika wyjdzie dopiero przy pierwszym użyciu z aplikacji.
