# privacy.welcome-experience – „Pokaż środowisko powitalne” i „Zaproponuj sposoby…” (usunięte ustawienia)

## Werdykt 26H2
nie wchodzi: brak tych ustawień w 26H2: w „Ustawieniach dodatkowych” (System > Powiadomienia) jest tylko pole porad (`privacy.windows-tips`). Nie ma więc czego przełączać ani czego obserwować.

## Co zmienia
Pozycji nie ma w katalogu, nic nie zmienia. Rozważany wpis: ustawienia „Pokaż środowisko powitalne” i „Zaproponuj sposoby…” (w 26H2 ich nie ma, więc nie ma wartości do zapisu).

## Źródła
- Test na VM 26H2 Home i Pro, 2026-10-21 (build 26300.9457, nieaktywowany Windows, konto lokalne): zapis przez API rejestru (.NET `RegistryKey`) i obserwacja w Ustawieniach (UI Automation, zrzuty).
- Research: `docs/zrodla/2026-10-20-etap4a-zrodla.md`, sekcja „Wyniki VM 26H2 (partia 1)”.
- Brak dokumentacji MS dla tej wartości, dowód: test na VM (obserwacja niżej).

## Wartości domyślne na świeżym 26H2
Odczyt z VM, 2026-10-21: w Ustawieniach brak obu pól (Home i Pro).

## Detekcja i undo
Nie dotyczy (pozycja nie jest w katalogu).

## Ryzyka i ostrzeżenia
Nie dotyczy. Pozycja nie weszła do katalogu.

## Test VM
Nie dotyczy (nie wchodzi): pozycji nie ma w katalogu, więc nie ma przypadku VM.

## Obserwacja skutku
- **Home** (`DSO-26H2-Home`), 2026-10-21. Metoda: przegląd System > Powiadomienia > Ustawienia dodatkowe (UI Automation). Wynik: jest tylko pole porad i sugestii; brak tych dwóch ustawień.
- **Pro** (`DSO-26H2-Pro`), 2026-10-21. Ta sama metoda i ten sam wynik.

## Reddit
Do uzupełnienia w części E.
