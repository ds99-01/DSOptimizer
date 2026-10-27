# privacy.soft-landing – Porady Windows, polityka (DisableSoftLanding)

## Werdykt 26H2
nie wchodzi: brak skutku: pole porad dalej aktywne i zaznaczone. Porady wyłącza pozycja `privacy.windows-tips` (pole w ustawieniach powiadomień).

## Co zmienia
Pozycji nie ma w katalogu, nic nie zmienia. Rozważany wpis: `HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent`, `DisableSoftLanding`, DWord, `1`.

## Źródła
- Test na VM 26H2 Home i Pro, 2026-10-21 (build 26300.9457, nieaktywowany Windows, konto lokalne): zapis przez API rejestru (.NET `RegistryKey`) i obserwacja w Ustawieniach (UI Automation, zrzuty).
- Research: `docs/zrodla/2026-10-20-etap4a-zrodla.md`, sekcja „Wyniki VM 26H2 (partia 1)”.
- Brak dokumentacji MS dla tej wartości, dowód: test na VM (obserwacja niżej).

## Wartości domyślne na świeżym 26H2
Odczyt z VM, 2026-10-21: wartość `DisableSoftLanding` brak (Home i Pro).

## Detekcja i undo
Nie dotyczy (pozycja nie jest w katalogu).

## Ryzyka i ostrzeżenia
Nie dotyczy. Pozycja nie weszła do katalogu.

## Test VM
Nie dotyczy (nie wchodzi): pozycji nie ma w katalogu, więc nie ma przypadku VM.

## Obserwacja skutku
- **Home** (`DSO-26H2-Home`), 2026-10-21. Metoda: zapis polityki, przegląd pola porad w Ustawieniach. Wynik: **brak skutku**, pole „Uzyskaj porady i sugestie…” dalej aktywne i zaznaczone.
- **Pro** (`DSO-26H2-Pro`), 2026-10-21. Ta sama metoda i ten sam wynik.

## Reddit
Do uzupełnienia w części E.
