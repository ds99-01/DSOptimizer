# repair.system-files – Sprawdź i napraw pliki systemowe

## Werdykt 26H2
Portować jako `kind=action`, `risk=safe` (kategoria „Naprawy”, Etap 4b partia 2, długa naprawa z oknem postępu): `DISM /Online /Cleanup-Image /RestoreHealth`, a po nim `sfc /scannow` – kolejność z dokumentacji MS. Na zdrowej VM 26H2 Home i Pro oba narzędzia kończą się kodem 0, a wynik da się uczciwie odczytać z `CBS.log` (zmierzone). Werdykty „naprawiono”, „nie udało się naprawić części plików” i „naprawa czeka na restart” są **niezmierzone** (bez celowego uszkadzania plików chronionych na VM) i opierają się na dokumentacji MS.

## Co zmienia
Handler C# `system-files` (`SystemFilesHandler`), bez skryptu:
1. Strażnik `ServicingGuard`: działający proces `dism`, `sfc` albo `cleanmgr` = `Refused` „Poprzednia operacja serwisowania jeszcze trwa – spróbuj za chwilę.”, nic nie startuje.
2. Krok 1 z 2 „DISM”: `%SystemRoot%\System32\dism.exe /Online /Cleanup-Image /RestoreHealth` przez `IServicingToolRunner` (`ServicingToolRunner`: `CreateProcessW` pełnej ścieżki, poza Job Objectem, bez zabijania, limit czekania = `timeoutSeconds` = 2700 s). Procent z paska (`[====  45.0%  ]`, strona OEM) idzie do okna postępu („Krok 1 z 2 · Dalej: SFC”). Przed startem odczyt długości `%SystemRoot%\Logs\CBS\CBS.log`, po końcu segment od tego offsetu (`ReadSharedFrom`, najwyżej ostatnie 16 MB).
3. Werdykt DISM (`DismOutcome`): kod 0 + blok „Checking System Update Readiness.” z `Total Detected Corruption` = 0 → zdrowy; > 0 i wszystko naprawione → naprawiony; mniej naprawionych → błąd; kod 0 bez bloku → „zdrowy albo naprawiony”. Kod 3010 → restart. `0x800F081F`, `0x800F0906`, `0x800F0907` → „nie znaleziono plików do naprawy – potrzebny internet (Windows Update) albo nośnik instalacyjny”. Inny kod (także `0x800F0950`) → błąd z kodem szesnastkowo i ścieżką `%SystemRoot%\Logs\DISM\dism.log`. Po porażce DISM SFC i tak startuje (spec 5.1).
4. Między krokami „Zatrzymaj” = wynik z samym DISM i linią „SFC pominięty – zatrzymano.” (`NotConfirmed`, a po porażce DISM `Failed`; nigdy `Executed`).
5. Krok 2 z 2 „SFC”: `%SystemRoot%\System32\sfc.exe /scannow` tak samo (wyjście UTF-16LE bez BOM, procent przez samo `\r`). Odczyt klucza `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending` przed i po SFC.
6. Werdykt SFC (`SfcOutcome`) ze znaczników `[SR]` segmentu `CBS.log` z tego przebiegu: `Cannot repair member file` → nie wszystko naprawiono; `Repairing corrupted file … from store` / `Repaired file … by copying from backup` → naprawiono; ślad naprawy (te znaczniki albo `[SR] Repairing N components` z N > 0) razem z kluczem `RebootPending`, którego nie było przed SFC → naprawa po restarcie; `[SR] Verify complete` bez śladu naprawy i kod 0 → brak naruszeń; reszta (brak segmentu, brak `[SR]`, kod ≠ 0) → niejednoznaczne.
7. Wynik zbiorczy: `Executed` tylko, gdy DISM ∈ {zdrowy, naprawiony, zdrowy albo naprawiony, restart} i SFC ∈ {brak naruszeń, naprawiono, naprawa po restarcie}. DISM brak źródła / błąd albo SFC „nie wszystko” → `Failed`. SFC niejednoznaczne → `NotConfirmed` ze ścieżką `CBS.log`. `RequiresReboot` przy DISM 3010 albo SFC „naprawa po restarcie”. Komunikat: linia DISM, linia SFC i „Logi: %SystemRoot%\Logs\DISM\dism.log, %SystemRoot%\Logs\CBS\CBS.log.”.
8. Limit czasu: DISM po 45 min → `NotConfirmed` „DISM nadal działa w tle po 45 min – program przestał czekać. Wynik sprawdzisz w %SystemRoot%\Logs\DISM\dism.log.”, SFC nie startuje (i tak czekałby na koniec DISM – pomiar 3c). SFC po 45 min → `NotConfirmed` „SFC nadal działa w tle po 45 min – …” ze ścieżką `CBS.log`. Narzędzie pracuje dalej, okno postępu przechodzi do wyniku, a kolejna długa naprawa dostaje `Refused`.

Pełne zdania narzędzi (bez linii pasków postępu) trafiają do logu programu, a wszystkie linie do wyjścia akcji.

## Źródła
- Test na VM 26H2 Home i Pro (build 26300.9457, pl-PL), 2026-10-22: `docs/zrodla/2026-10-22-etap4b-2-pomiary.md`, pkt 1, 1z, 1b, 1n, 2, 2r, 2k, 3, 3c, 6 oraz sekcje 1, 2 i 6; próbki wyjścia i segmentów `CBS.log` w `tests/DSOptimizer.Tests.Unit/Repair/Samples/`.
- DISM, kolejność DISM → SFC i komunikaty SFC: https://support.microsoft.com/en-us/topic/use-the-system-file-checker-tool-to-repair-missing-or-corrupted-system-files-79aa86cb-ca52-166a-92a3-966e85d4094e (dostęp 2026-10-22, MS Support).
- Blok podsumowania `CBS.log` i kody braku źródła: https://learn.microsoft.com/en-us/troubleshoot/windows-server/installing-updates-features-roles/fix-windows-update-errors (KB 947821), https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/configure-a-windows-repair-source (dostęp 2026-10-22, MS Learn).
- Znaczniki `[SR]`: https://learn.microsoft.com/en-us/troubleshoot/windows-client/deployment/analyze-sfc-program-log-file-entries (KB 928228, dostęp 2026-10-22, MS Learn).

## Wartości domyślne na świeżym 26H2
Magazyn składników zdrowy (`Total Detected Corruption: 0`), SFC bez naruszeń, klucz `RebootPending` brak – odczyt 2026-10-22, Home i Pro. Procesy `dism`, `sfc`, `cleanmgr` nie działają na bezczynnej VM (baseline 4 × 36 próbek).

## Detekcja i undo
Nie dotyczy: `kind=action` bez detekcji i cofnięcia. Odczytem zwrotnym skutku są kody wyjścia oraz segmenty `CBS.log` z tego przebiegu (blok podsumowania DISM i znaczniki `[SR]` SFC); bez nich wynik to `NotConfirmed`, nigdy „wykonano”. Naprawy plików systemowych nie da się cofnąć – przywracają stan z magazynu składników.

## Ryzyka i ostrzeżenia
- `risk=safe`: DISM i SFC naprawiają wyłącznie z magazynu składników i (DISM) Windows Update; ustawień nie zmieniają.
- Czas: zwykle 2–3 min na VM, z aktywnym Windows Update DISM ok. 10 min, z realnym uszkodzeniem dłużej (niezmierzone). Limit czekania 45 min na narzędzie; po nim narzędzie pracuje dalej w tle.
- „Zatrzymaj” działa tylko między krokami.
- Bez internetu DISM może zgłosić brak plików źródłowych (niezmierzone – zdrowy magazyn nie sięga po źródło).
- Znane ograniczenie strażnika: nie wykrywa instalacji aktualizacji przez Windows Update (`TiWorker` wykluczony – fałszywe alarmy); drugie narzędzie serwisowania w takiej sytuacji czeka na koniec pierwszego (pomiar 3c), więc naprawa trwa dłużej, ale nie psuje się.
- Zamknięcie programu po limicie czasu nie przerywa DISM ani SFC (zmierzone – sekcja „Test VM”); niezmierzone: uruchomienie DSOptimizera przez cudzy program w Job Objekcie z `KILL_ON_JOB_CLOSE`.

## Test VM
Przypadek `repair-system-files` w `tests/vm/cases.psd1` (Task 6): `Flow='Execute'`, wbudowany katalog, `CliTimeoutSeconds = 1200`, `Expect`: `Executed`, kod 0, `RequiresReboot = $false`; `Check`: w `CBS.log` jest segment `[SR]` z tego przebiegu (po znaczniku czasu sprzed startu) i w VM nie działa `dism.exe` ani `sfc.exe`. Przypadek `repair-system-files-repaired` z planu wypadł (bez uszkadzania plików chronionych).

Przebieg VM (Task 6, 2026-10-23, `tests/vm/Invoke-VmTests.ps1 -CasesPath <tylko ten przypadek>` na gałęzi `etap4b-2`; raporty poza gitem): **Passed** na Home i Pro (po 7 zaliczonych / 0 błędów). Home: `Executed`, kod 0, `requiresReboot=false`, CLI 146,5 s (DISM 87 s, SFC 60 s); Pro: to samo, 149 s (DISM 85 s, SFC 63 s). Komunikat na obu: „DISM: magazyn składników jest zdrowy – nie wykryto uszkodzeń.” / „SFC: nie znaleziono naruszeń integralności plików systemowych.” Przebieg trwa poniżej 30 min, więc przypadek jest w domyślnym przebiegu (bez `Skip`).

Pomiar (2026-10-23, Home i Pro, `docs/zrodla/2026-10-22-etap4b-2-pomiary.md` sekcja 7): po limicie czekania CLI kończy się (`NotConfirmed`, kod 4), a DISM i SFC z zamkniętym potokiem pracują dalej i kończą się kodem 0 z pełnym wynikiem w `CBS.log` – z PS Direct i z zadania Harmonogramu konta `dso`.

## Obserwacja skutku
Metoda: uruchomienie obu narzędzi z pełnej ścieżki System32 z przekierowanym wyjściem (jak `ServicingToolRunner`), zapis bajtów wyjścia, segmentów `CBS.log` i `dism.log` od offsetu sprzed startu, próbki procesów co 5 s (`tests/vm/explore/Invoke-E4B2Exploration.ps1`).
- **Home** (`DSO-26H2-Home`, 26300.9457), 2026-10-22: DISM `/RestoreHealth` bez sieci kod 0 w 74,5 s, `CBS.log`: `Total Detected Corruption: 0`, `Operation result: 0x0`; SFC `/scannow` kod 0 w 52,4 s, „Windows Resource Protection did not find any integrity violations.”, 498 linii `[SR]` bez znaczników naprawy (`Repairing 0 components`). Restart nie jest potrzebny (`RebootPending` brak).
- **Pro** (`DSO-26H2-Pro`, 26300.9457), 2026-10-22: DISM kod 0 w 72,1 s, ten sam blok podsumowania; SFC kod 0 w 53,9 s, 525 linii `[SR]`, te same szablony.
- Sam handler (2026-10-23, Home i Pro): `DSOptimizer.exe --execute repair.system-files` = `Executed`, a niezależny odczyt w VM potwierdza blok podsumowania DISM i znaczniki `[SR]` tego przebiegu w `CBS.log` oraz brak działającego `dism.exe`/`sfc.exe` po powrocie CLI (przypadek `repair-system-files`).
- Naprawa uszkodzonego pliku, „nie udało się naprawić” i „naprawa czeka na restart”: **niezmierzone** – znaczniki z KB 928228, kody z KB 947821.
