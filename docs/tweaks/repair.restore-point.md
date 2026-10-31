# repair.restore-point – Utwórz punkt przywracania teraz

## Werdykt 26H2
Portować jako `kind=action`, `risk=safe` (kategoria „Naprawy”, Etap 4b partia 1): punkt na żądanie przez istniejący `IRestorePointService` (ten sam, który tworzy punkty przed zmianami) działa na 26H2 Home i Pro, gdy ochrona systemu jest włączona. Bez cofnięcia.

## Co zmienia
Handler C# `restore-point` (`RestorePointHandler`), bez skryptu: `IRestorePointService.Create("DSOptimizer – punkt ręczny")`. Serwis na czas wywołania zeruje `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore\SystemRestorePointCreationFrequency` (24-godzinny limit), wywołuje `SystemRestore.CreateRestorePoint`, czeka (do 10 s), aż punkt pojawi się na liście, i przywraca poprzednią wartość limitu. Wyniki:
- `Created` → `Executed`: „Utworzono punkt przywracania „DSOptimizer – punkt ręczny” (<czas lokalny z IClock>).”
- `ProtectionDisabled` → `Failed`: „Ochrona systemu na dysku C: jest wyłączona – włącz ją pozycją „Ochrona systemu” w tej kategorii.” Handler niczego nie włącza po cichu.
- `Failed` → `Failed` z komunikatem serwisu i kodem (`(kod N)`), gdy serwis go podał.

## Źródła
- Test na VM 26H2 Home i Pro (build 26300.9457), 2026-10-22: `docs/zrodla/2026-10-22-etap4b-1-pomiary.md`, sekcja 7 (punkt utworzony przez `DSOptimizer.exe --apply vmtest.hklm-value --restore-point required` = `Created`; przy wyłączonej ochronie `CreateRestorePoint` zwraca „Nie można uruchomić określonej usługi, ponieważ jest ona wyłączona…”).
- Brak dokumentacji MS sprawdzonej w tym zadaniu; dowód: test na VM opisany niżej.

## Wartości domyślne na świeżym 26H2
Ochrona systemu wyłączona (`SPP\Clients` bez wpisu), `SystemRestorePointCreationFrequency` brak (odczyt 2026-10-22, Home i Pro), więc na świeżym komputerze akcja kończy się `Failed` z odesłaniem do pozycji „Ochrona systemu”.

## Detekcja i undo
Nie dotyczy: `kind=action` nie ma detekcji ani cofnięcia. Dowodem skutku jest lista punktów (serwis zwraca `Created` dopiero, gdy nowy punkt jest na liście). Utworzony punkt usuwa się ręcznie w oknie „Ochrona systemu” (Konfiguruj → Usuń).

## Ryzyka i ostrzeżenia
- `risk=safe`: punkt przywracania niczego nie zmienia w systemie poza zajęciem miejsca w limicie ochrony systemu.
- Wymaga włączonej ochrony systemu na C:.
- Punkt nie obejmuje plików osobistych; Windows może usunąć najstarsze punkty, gdy skończy się przydzielone miejsce.

## Test VM
Przypadki `repair-restore-point-disabled` (ochrona wyłączona: `Failed` z komunikatem odsyłającym do „Ochrona systemu”) i `repair-restore-point-created` (ochrona włączona w `Setup` przez `Enable-ComputerRestore -Drive 'C:\'`, `Check`: punkt „DSOptimizer – punkt ręczny” na liście) dopisane w `tests/vm/cases.psd1` w Tasku 7.

Przebieg VM (Task 10, 2026-10-22, pełny `tests/vm/Invoke-VmTests.ps1` na gałęzi `etap4b-1`; raporty poza gitem): Home i Pro po 722 zaliczone / 0 błędów / 25 pominiętych (6 przypadków ze `Skip` spoza kategorii Naprawy). `repair-restore-point-disabled`: **Passed** na Home i Pro (`Failed`, kod 1, „Ochrona systemu na dysku C: jest wyłączona – włącz ją pozycją „Ochrona systemu” w tej kategorii.”, punkt nie powstał). `repair-restore-point-created`: **Passed** na Home i Pro (`Executed`, „Utworzono punkt przywracania „DSOptimizer – punkt ręczny” (…)”, punkt na liście `Get-ComputerRestorePoint`).

## Obserwacja skutku
Metoda: utworzenie punktu przez tę samą usługę (`IRestorePointService`) i odczyt `Get-ComputerRestorePoint` oraz `vssadmin list shadows`; surowe wyniki poza gitem. Akcja ręczna wywołuje tę samą usługę z innym opisem, więc pomiar dotyczy usługi, a nie samego handlera.
- **Home** (`DSO-26H2-Home`, 26300.9457), 2026-10-22: po włączeniu ochrony punkt powstaje i jest widoczny na liście oraz jako kopia w `vssadmin list shadows`; przy wyłączonej ochronie punktu nie da się utworzyć (WMI zwraca błąd usługi wyłączonej, co usługa mapuje na `ProtectionDisabled`).
- **Pro** (`DSO-26H2-Pro`, 26300.9457), 2026-10-22: punkt przez `DSOptimizer.exe --apply vmtest.hklm-value --restore-point required` = `Created` („DSOptimizer – przed vmtest.hklm-value”); zachowanie przy wyłączonej ochronie jak na Home.
