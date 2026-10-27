# privacy.limit-diagnostic-log-collection – LimitDiagnosticLogCollection

## Werdykt 26H2
nie wchodzi: brak powtarzalnego skutku.

## Co zmienia
Pozycji nie ma w katalogu, nic nie zmienia. Rozważany wpis: `HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection`, `LimitDiagnosticLogCollection`, DWord, `1` (polityka MS dotyczy tylko ścieżki opcjonalnych danych diagnostycznych).

## Źródła
- Dokumentacja MS: polityka `System/LimitDiagnosticLogCollection` (Windows 11 21H2+, Pro). Nazwa wartości rejestru w kluczu `DataCollection` była hipotezą do sprawdzenia na VM.
- Test na VM 26H2 Home i Pro, 2026-10-21 (build 26300.9457, nieaktywowany Windows, konto lokalne): zapis przez API rejestru (.NET `RegistryKey`) i obserwacja w Ustawieniach (UI Automation, zrzuty).
- Research: `docs/zrodla/2026-10-20-etap4a-zrodla.md`, sekcje „2c. Limit Diagnostic Log / Dump collection” i „Wyniki VM 26H2 (partia 1)”.

## Wartości domyślne na świeżym 26H2
Odczyt z VM, 2026-10-21: wartość `LimitDiagnosticLogCollection` brak (Home i Pro).

## Detekcja i undo
Nie dotyczy (pozycja nie jest w katalogu).

## Ryzyka i ostrzeżenia
Nie dotyczy. Pozycja nie weszła do katalogu.

## Test VM
Nie dotyczy (nie wchodzi): pozycji nie ma w katalogu, więc nie ma przypadku VM.

## Obserwacja skutku
- **Home** (`DSO-26H2-Home`), 2026-10-21. Metoda: zapis polityki i przegląd strony Diagnostyka i opinie w Ustawieniach. Wynik: **brak powtarzalnego skutku** (w odczycie „po” nic; jeden niejednoznaczny odczyt po cofnięciu: wyszarzony przełącznik).
- **Pro** (`DSO-26H2-Pro`), 2026-10-21. Ta sama metoda. Wynik: **brak skutku**.

## Reddit
Do uzupełnienia w części E.
