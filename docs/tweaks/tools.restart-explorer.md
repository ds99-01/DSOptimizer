# tools.restart-explorer – Uruchom ponownie pasek zadań i Eksploratora

## Werdykt 26H2
Portować jako `kind=action`, `risk=safe` (alias v3.2 `tool_restart_explorer`): na VM 26H2 Home i Pro zatrzymanie powłoki przez `Stop-Process -Force` kończy się wznowieniem jej przez winlogon w nie więcej niż 1,42 s, z tokenem użytkownika (`Limited`), więc pozycja nie powtarza błędu v3.2 (aplikacja sama uruchamiała `explorer.exe` i dostawała podniesiony Eksplorator).

## Co zmienia
Nie zapisuje rejestru, usług ani plików; zatrzymuje wszystkie powłoki Eksploratora zalogowanego użytkownika (SID `$UserSid`) w jego sesji i sprawdza, czy wróciła nowa. Skrypt (`script.apply`) działa wyłącznie przez trzy helpery zdefiniowane w skrypcie (testy je mockują):
1. `Get-DsoAutoRestartShell` – tylko ODCZYT `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon`, wartość `AutoRestartShell` (64-bitowy widok rejestru). Wartość inna niż 1 (w tym 0) albo jej brak: `Failed` („AutoRestartShell = … – winlogon nie wznowi Eksploratora; nic nie zatrzymano”), bez zatrzymania. Zapisu tej wartości akcja nigdy nie robi.
2. `Get-DsoUserShellProcess` – procesy `explorer.exe` (Win32_Process) z właścicielem o SID równym `$UserSid` (SID zalogowanego użytkownika przekazany skryptowi: właściciel `explorer.exe` w aktywnej sesji konsoli, wg `InteractiveUserContext`; to nie musi być konto, które uruchomiło DSOptimizer, gdy aplikację podniesiono innym kontem admina) i polem `CommandLine`. Skrypt zostawia tylko powłokę: linia poleceń BEZ `/factory`. Procesy `explorer.exe /factory,{…} -Embedding` (rodzic `svchost.exe`) to hosty okien, nie powłoka: ich zatrzymanie nie daje powrotu, więc nigdy nie są zatrzymywane. Brak powłoki: `Failed` („brak Eksploratora użytkownika …; nic nie zatrzymano” + instrukcja Menedżera zadań). Wartość `AutoRestartShell` musi być typu DWORD równego 1 (REG_SZ „1”, QWORD i BINARY są odrzucane z podaniem typu).
3. `Stop-DsoShellProcess` – `Stop-Process -Id <PID> -Force` (zmierzone na VM). `taskkill` nie jest używany (na powłoce nie był mierzony).

Przebieg: odczyt `AutoRestartShell` → wybór powłok `$UserSid` bez `/factory` → ustalenie sesji (wszystkie znalezione powłoki muszą być w jednej sesji, inaczej `Failed` bez zatrzymania: nie wiadomo, którą restartować) → zatrzymanie wszystkich znalezionych powłok tego konta w tej sesji (zwykle jedna; gdyby znaleziono kilka, nie da się wiarygodnie wskazać „tej właściwej”, więc zatrzymanie wszystkich to zabezpieczenie, a nie zmierzony scenariusz) → pętla co 0,5 s, limit 10 s liczony zegarem (Stopwatch), a nie liczbą prób; błąd odczytu procesów w trakcie czekania (np. proces zniknął w trakcie zapytania CIM) oznacza „jeszcze nie ma” i trafia do komunikatu `Failed`: sukces = powłoka tego samego SID w tej samej sesji (bez `/factory`) o PID innym niż zatrzymane → `Executed` „Explorer zrestartowany przez winlogon: PID a → b”. Brak nowej powłoki w 10 s: `Failed` z instrukcją Microsoftu „Menedżer zadań > Uruchom nowe zadanie > explorer.exe” (to krok ręczny, aplikacja niczego nie uruchamia). `timeoutSeconds` pozycji = 30 (większy niż limit 10 s).

Skrypt NIGDY nie uruchamia `explorer.exe` (ani `Start-Process`, ani `&`): podniesiony Eksplorator w v3.2 powstawał właśnie z procesu aplikacji. Test harnessu pilnuje tego bezpiecznikami (`Start-Process`, `explorer`, `taskkill` rzucają przy niezamockowanym wywołaniu).

## Źródła
- MS-archiwum (Windows Server 2003 Technical Reference, „AutoRestartShell: Core Services”, ms.date 2009-10-08, `is_archived`), dostęp 2026-10-19: https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-server-2003/cc776814(v=ws.10) – klucz `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon`, `REG_DWORD`, zakres 0 lub 1, domyślnie 1: „Specifies whether the Windows user interface (typically, Explorer.exe) restarts automatically if it stops unexpectedly.”; 0 – użytkownik musi uruchomić interfejs sam (Menedżer zadań > Plik > Uruchom nowe zadanie). Dotyczy Windows Server 2003; dla Windows 11/26H2 nie ma strony MS Learn (Ruling R7), więc skutek na 26H2 opiera się na odczycie VM poniżej.
- Odczyt VM 26H2 (build 26300.9457), Home i Pro, 2026-10-19 (eksploracja C5, `tests/vm/explore/Invoke-C5Exploration.ps1`; research: `docs/zrodla/2026-10-19-partia-c5-zrodla.md`, „Wyniki VM”, pytania 5–7):
  - `AutoRestartShell` = DWORD 1 (Home i Pro); przypadek „brak wartości” nie wystąpił, zachowanie przy braku nie zostało zmierzone (stąd `Failed` zachowawczo).
  - `Stop-Process -Force` na powłoce (`C:\Windows\Explorer.EXE`) użytkownika `dso`: nowa powłoka po 136–1416 ms (rozdzielczość pętli 500 ms), rodzic `userinit.exe`, ta sama sesja (1), właściciel `dso`, token `Limited` (nie podniesiony).
  - Konto `dso` ma obok powłoki kilka procesów `C:\Windows\explorer.exe /factory,{5BD95610-9434-43C2-886C-57852CC8A120} -Embedding` (rodzic `svchost.exe`). Wybór „najstarszy proces” zawiódł w przebiegu, a zatrzymanie procesu `/factory` nie dało powrotu powłoki – dlatego kryterium to linia poleceń bez `/factory`.
  - v3.2 (`Start-Process C:\Windows\explorer.exe` z procesu admina): w sesji 0 powstał osobny, podniesiony Eksplorator Administratora; w sesji użytkownika uruchomiony proces kończy się kodem 1 i przekazuje okno do istniejącej powłoki.
- Decyzja projektowa R18 (2026-10-19).

## Wartości domyślne na świeżym 26H2
`AutoRestartShell` = 1 (DWORD) w `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon` na VM 26H2 Home i Pro (odczyt 2026-10-19). Powłoka użytkownika: jeden proces `C:\Windows\Explorer.EXE` w sesji użytkownika, obok procesy `/factory`.

## Detekcja i undo
Nie dotyczy: `kind=action` nie ma detekcji ani cofnięcia (nic nie jest zapisywane, nie ma journala). Dowodem skutku jest nowa powłoka (inny PID) tego samego konta w tej samej sesji, sprawdzona w skrypcie; sam kod wyjścia 0 bez niej nie jest możliwy.

## Ryzyka i ostrzeżenia
- `risk=safe`: niczego nie zapisuje; skutek uboczny to możliwe zamknięcie okien Eksploratora plików hostowanych w powłoce (operacje kopiowania w nich przerwane; okna `CabinetWClass` bywają w procesach `/factory`, których akcja nie zatrzymuje – dane VM) oraz zniknięcie paska zadań i pulpitu na kilka sekund (zmierzone do ok. 1,4 s).
- Akcja nie zmienia `AutoRestartShell`; przy wartości 0 albo braku wartości odmawia zatrzymania (pusty pulpit bez automatycznego powrotu byłby gorszy niż brak akcji).
- Token: pomiar VM pokazał, że wznowiona przez winlogon powłoka ma token `Limited` konta użytkownika, więc pozycja nie wymaga ostrzeżenia o podniesionym tokenie (w przeciwieństwie do v3.2).
- Ręczne „Zakończ zadanie” w Menedżerze zadań na „Eksplorator Windows” NIE dało powrotu pulpitu (klik Dawida, 2026-10-19, VM Pro). To inny mechanizm niż `Stop-Process -Force` użyty w akcji; przyczyny dane nie wyjaśniają, więc dokument nie obiecuje powrotu po ręcznym zakończeniu z Menedżera. Gdy powłoka nie wraca (także po akcji): Menedżer zadań > Uruchom nowe zadanie > `explorer.exe`.
- Na wolniejszym sprzęcie powrót może trwać dłużej niż limit 10 s: akcja zgłosi wtedy `Failed`, choć powłoka może wrócić chwilę później (komunikat z instrukcją to łagodzi).

## Test VM
Przebieg VM `20261019-110405` (2026-10-19, HEAD `fd320ed`, build 26300.9457): Home (`DSO-26H2-Home`) i Pro (`DSO-26H2-Pro`), `tests/vm/Invoke-VmTests.ps1`. Raporty lokalne poza gitem. Przebieg łącznie: Home 401 zaliczonych / 0 błędów / 29 pominiętych, Pro 401 / 0 / 29 (`main` 313/0/29 + przebiegi Reboot); w przypadkach C5 `CleanupError` puste.
- `tools-restart-explorer` (`Flow=Execute`, `Catalog=BuiltIn`): **Passed** na Home i Pro (`Executed`). `Setup` zapisuje PID jedynej powłoki `dso` (bez `/factory`) i sesję do `C:\dso\restart-explorer-pid.txt` (rzuca, gdy powłok jest inna liczba niż 1); `Check` w pętli (15 s co 0,5 s) szuka powłoki `dso` bez `/factory` o innym PID w tej samej sesji; `Cleanup` usuwa plik.
- Eksploracja K2 (R18): `Stop-Process -Force` na powłoce `dso` → nowa powłoka w maks. 1,42 s, rodzic `userinit.exe`, sesja 1, token `Limited`; `AutoRestartShell` = DWORD 1. Runner nie widzi pulpitu, więc wznowienie paska i pulpitu wzrokowo nie jest zaliczone w Check. Research i wyniki eksploracji K2: `docs/zrodla/2026-10-19-partia-c5-zrodla.md` (sekcje „Wyniki VM”, „Sonda uzupełniająca”).
- Klik ręczny Dawida (VM Pro, sesja `dso`, 2026-10-19): Menedżer zadań > „Eksplorator Windows” > „Zakończ zadanie” **nie przywrócił** paska ani pulpitu (trzeba było Uruchom nowe zadanie > `explorer.exe`). To inny mechanizm niż `Stop-Process -Force` w akcji; dokument nie obiecuje powrotu po ręcznym zakończeniu z Menedżera (zob. „Ryzyka i ostrzeżenia”).
- **Smoke z prawdziwym `DSOptimizer.exe` w sesji `dso`: do potwierdzenia przez Dawida** (Ruling R30). Nie wykonany, bo Dawid wyjechał; pozycja wchodzi do `main` z tym zastrzeżeniem (jak D2 w partii C4).
- Do smoke należy też wzrokowe sprawdzenie, że po akcji wraca pasek i pulpit.

## Reddit
Do uzupełnienia w części E.
