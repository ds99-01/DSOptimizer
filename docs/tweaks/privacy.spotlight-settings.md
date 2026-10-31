# privacy.spotlight-settings – Spotlight w Ustawieniach (DisableWindowsSpotlightOnSettings)

## Werdykt 26H2
nie wchodzi: brak skutku do pokazania.

## Co zmienia
Pozycji nie ma w katalogu, nic nie zmienia. Rozważany wpis: `HKCU\Software\Policies\Microsoft\Windows\CloudContent`, `DisableWindowsSpotlightOnSettings`, DWord, `1`.

## Źródła
- Test na VM 26H2 Home i Pro, 2026-10-21 (build 26300.9457, nieaktywowany Windows, konto lokalne): zapis przez API rejestru (.NET `RegistryKey`) i obserwacja w Ustawieniach (UI Automation, zrzuty).
- Research: `docs/zrodla/2026-10-20-etap4a-zrodla.md`, sekcja „Wyniki VM 26H2 (partia 1)”.
- Brak dokumentacji MS dla tej wartości, dowód: test na VM (obserwacja niżej).

## Wartości domyślne na świeżym 26H2
Odczyt z VM, 2026-10-21: wartość `DisableWindowsSpotlightOnSettings` brak (Home i Pro).

## Detekcja i undo
Nie dotyczy (pozycja nie jest w katalogu).

## Ryzyka i ostrzeżenia
Nie dotyczy. Pozycja nie weszła do katalogu.

## Test VM
Nie dotyczy (nie wchodzi): pozycji nie ma w katalogu, więc nie ma przypadku VM.

## Obserwacja skutku
- **Home** (`DSO-26H2-Home`), 2026-10-21. Metoda: zapis polityki, przegląd Ustawień, restart VM. Wynik: **brak skutku**, także po restarcie VM.
- **Pro** (`DSO-26H2-Pro`), 2026-10-21. Ta sama metoda i ten sam wynik.
