# tools.winutil – Otwórz WinUtil (Chris Titus)

## Werdykt 26H2
Portować jako `kind=action`, `risk=caution`, z handlerem C# `winutil` (Etap 5, spec 3.3, decyzja K5; wcześniej cienki launcher z partii C5, decyzja D5): pozycja pobiera oficjalny `winutil.ps1` z najnowszego stabilnego wydania na GitHubie, sprawdza SHA256 i otwiera go w osobnym oknie PowerShell. Niczego więcej nie robi. To cudzy kod z internetu uruchamiany jako admin, którego treści DSOptimizer nie ocenia.

To **drugi wyjątek** od zasady „program sam niczego nie pobiera” (pierwszy: RustDesk z GitHuba). Zakres: jeden plik (`winutil.ps1`), jedno repo (`ChrisTitusTech/winutil`), jedno źródło (`releases/latest`); reguły w kodzie (`src/DSOptimizer.Core/Tools/WinUtilSource.cs`), nie w JSON.

## Co zmienia
Samo DSOptimizer nic w systemie nie zmienia poza plikiem skryptu w folderze pobrań. Handler `WinUtilHandler` (`src/DSOptimizer.Core/Tools/WinUtilHandler.cs`):
1. `GET https://api.github.com/repos/ChrisTitusTech/winutil/releases/latest` (limit 15 s, `Accept: application/vnd.github+json`).
2. `WinUtilSource.TryParseRelease`: bez `prerelease`/`draft`, tag z cyfr (`26.10.07`), dokładnie jeden asset `winutil.ps1`, `size` ≤ 5 MB, `digest` `sha256:` (bez niego = odmowa), duplikaty kluczy JSON = zła odpowiedź.
3. `WinUtilSource.IsAllowedDownloadUrl`: https, host `github.com`, ścieżka dokładnie `/ChrisTitusTech/winutil/releases/download/<tag>/winutil.ps1`, bez `%`, zapytania i fragmentu.
4. Pobranie do `%ProgramData%\DSOptimizer\downloads\<guid>.ps1` (zaufany ACL, ten sam folder co RustDesk) przez `WebDownloader`: przekierowania tylko na `release-assets.githubusercontent.com`/`objects.githubusercontent.com` przez https, limit 5 MB i 2 min.
5. SHA256 pliku na dysku liczony tuż przed startem = `digest` z API (różnica = plik usunięty, nic nie startuje).
6. `powershell.exe -NoExit -NoProfile -ExecutionPolicy RemoteSigned -File "<plik>"` przez `ShellProcessLauncher` (CreateProcess stałej ścieżki PowerShell 5.1, poza Job Objectem, bez czekania). Okno dziedziczy podniesienie DSOptimizera (manifest `requireAdministrator`).

Każda porażka po utworzeniu ścieżki usuwa plik. Po starcie plik zostaje (PowerShell czyta go po uruchomieniu, decyzja K4) i jest usuwany przy kolejnym pobraniu (WinUtil albo RustDesk), gdy ma ponad 24 h (sprzątanie osieroconych pobrań w `DownloadFolder.NewDownloadPath`; bez kolejnego pobrania plik zostaje). Log programu dostaje linię INFO: tag, ścieżka, bajty, SHA256. Przed pobraniem handler raportuje (wyjście akcji, `IProgress<string>`) jedną linię postępu „Pobieranie WinUtil <tag> z GitHuba…”.

Wyniki (dokładne teksty w handlerze): sukces „Otwarto okno WinUtil <tag> (plik z wydania GitHuba, SHA256 zgodny).”; bez sieci albo po limicie „Nie wykonano – brak połączenia z GitHubem.”; HTTP 403/429 „Nie wykonano – GitHub chwilowo odmawia (limit zapytań).”; inne kody, odrzucone przekierowanie i adres pobrania spoza wzorca z pkt 3 (nic nie pobrano) „Nie wykonano – GitHub zwrócił nieoczekiwaną odpowiedź.”; odmowa reguł wydania „Nie wykonano – <powód>.”; brak assetu „Nie wykonano – najnowsze wydanie WinUtil nie ma pliku winutil.ps1.”; plik ponad limit albo zły SHA256 „Nie wykonano – pobrany plik nie przeszedł sprawdzenia.”; błąd startu okna „Nie wykonano – nie udało się otworzyć okna PowerShell: <komunikat>.” Wszystkie porażki = `Failed`.

Co zmieni WinUtil, zależy od wydania i od tego, co klikniesz w jego oknie; pozycja tego nie obiecuje.
Nie używamy: `-EncodedCommand`, gałęzi deweloperskiej ani wariantu z presetem (`-Preset`). Kod WinUtil nie jest w repo.

## Źródła
- Wydania WinUtil na GitHubie: https://github.com/ChrisTitusTech/winutil/releases (API `releases/latest`); w dniu pomiaru (2026-10-26) tag `26.10.07`, `winutil.ps1` 927 798 B, pole `digest` z SHA256, plik bez podpisu Authenticode (`Get-AuthenticodeSignature` = `NotSigned`), więc podpisu nie weryfikujemy – zostaje SHA256 z API.
- README WinUtil (https://raw.githubusercontent.com/ChrisTitusTech/winutil/main/README.md), sekcja „Quick Start”: „WinUtil must be run as Administrator because it performs system-wide changes.” Dawne polecenie z README (`irm … | iex`, partia C5) zastąpione plikiem z wydania; tekst polecenia zniknął z katalogu (spec 3.3 pkt 4 – znany wzorzec łapany przez skanery).
- Licencja WinUtil: MIT, „Copyright (c) 2022 CT Tech Group LLC”. Nie kopiujemy kodu, więc bez wpisu w `THIRD_PARTY_NOTICES.md`.
- v3.2: `tool_winutil` (poprzednia, niepubliczna wersja 3.2); porada v3.2 „Unikaj zakładki 'Tweaks' na LTSC!” nie ma źródła, więc nie jest przeniesiona.
- Pomiar Etapu 5, Task 1 (2026-10-26, VM Home i Pro z siecią i aktualnymi sygnaturami Defendera 1.461.204.0): `docs/zrodla/2026-10-26-etap5-pomiar-winutil.md`. Wariant B (`winutil.ps1` z wydania, SHA256 = `digest`, `-ExecutionPolicy RemoteSigned -File`) otwiera okno „WinUtil 26.10.07” z GUI na obu edycjach, zero detekcji Defendera; stąd `WinUtilSource.ExecutionPolicy = "RemoteSigned"`. Dawne `irm … | iex` też nie było blokowane w tych warunkach – zmiana zostaje, bo usuwa znany wzorzec i daje uczciwy komunikat bez sieci.
- Partia C5 (dawny launcher): `docs/zrodla/2026-10-19-partia-c5-zrodla.md`.

## Wartości domyślne na świeżym 26H2
Nie dotyczy: pozycja nie czyta ani nie zapisuje ustawień. Stan systemu po uruchomieniu to proces okna PowerShell i plik `<guid>.ps1` w `%ProgramData%\DSOptimizer\downloads` (folderu na czystej VM nie ma; powstaje przy pierwszym pobraniu).

## Detekcja i undo
Nie dotyczy: `kind=action` bez detekcji i cofnięcia. `Executed` oznacza, że plik przeszedł sprawdzenie i proces PowerShell wystartował; skutku WinUtil DSOptimizer nie sprawdza i nie cofa.

## Ryzyka i ostrzeżenia
- Ostrzeżenia w katalogu (sens pierwszego: uruchamiasz cudzy kod z internetu jako admin): „Uruchamiasz cudzy kod z internetu jako admin – DSOptimizer nie sprawdza treści skryptu.” oraz „DSOptimizer nie śledzi i nie cofa zmian zrobionych w oknie WinUtil.”
- DSOptimizer nie sprawdza treści – SHA256 potwierdza tylko zgodność z wydaniem GitHuba, nie bezpieczeństwo kodu; każde nowe wydanie może działać inaczej.
- Wymaga sieci; bez niej nic się nie otwiera (wcześniej okno otwierało się z błędem `irm`).
- Bez presetów i bez gałęzi deweloperskiej. Zmiany robione w oknie WinUtil nie są cofane przez DSOptimizer.

## Test VM
- `tools-winutil-offline` (`Flow=Execute`, `Catalog=BuiltIn`, VM bez sieci): oczekiwane `Failed` z kodem 1 i komunikatem „brak połączenia z GitHubem”; `Check` = brak `powershell.exe` z `-File` wskazującym `C:\ProgramData\DSOptimizer\downloads\*.ps1` i brak plików `*.ps1` w `downloads`. Wynik przebiegu (`Invoke-VmTests.ps1`, VM bez sieci, HEAD `fb4bb9c`, 2026-10-26): **Passed na obu edycjach** – Pro 799 / 0 / 25 pominiętych, Home 799 / 0 / 25 (`tools-winutil-offline` zielony na Pro i Home).
- `tools-winutil-effect`: **Skipped** (powód: skutek z siecią pokrywa faza `av` eksploracji E5). Okno i GUI WinUtil z siecią zmierzone w Task 1 (wyżej, wariant B).
- Dawny przypadek `tools-winutil-launch` (partia C5, 2026-10-19, Passed na Home i Pro) usunięty razem z poleceniem `irm … | iex`.
