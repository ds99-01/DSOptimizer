# privacy.search-disable-web-search – polityka DisableWebSearch

## Werdykt 26H2
nie wchodzi: brak skutku na 26H2. Po ustawieniu `DisableWebSearch` = 1 Bing dalej jest w wynikach wyszukiwania. Działające mechanizmy opisuje `privacy.search-web-results.md`.

## Co zmienia
Pozycji nie ma w katalogu, nic nie zmienia. Rozważany wpis: `HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Search`, `DisableWebSearch`, DWord, `1`.

## Źródła
- Test na VM 26H2 Home i Pro, 2026-10-21 (build 26300.9457).
- Research: `docs/zrodla/2026-10-20-etap4a-zrodla.md`, sekcje „4. Bing / wyszukiwanie w sieci w menu Start” i „Wyniki VM 26H2 (partia 1)”.

## Wartości domyślne na świeżym 26H2
Odczyt z VM, 2026-10-21: wartość `DisableWebSearch` brak (Home i Pro).

## Detekcja i undo
Nie dotyczy (pozycja nie jest w katalogu).

## Ryzyka i ostrzeżenia
Nie dotyczy. Pozycja nie weszła do katalogu.

## Test VM
Nie dotyczy (nie wchodzi): pozycji nie ma w katalogu, więc nie ma przypadku VM.

## Obserwacja skutku
- **Home** (`DSO-26H2-Home`), 2026-10-21. Metoda: zapis polityki przez API rejestru, restart Eksploratora i SearchHost, wyniki wyszukiwania dla frazy „pogoda”. Wynik: **brak skutku**, Bing dalej w wynikach.
- **Pro** (`DSO-26H2-Pro`), 2026-10-21. Ta sama metoda i ten sam wynik.

## Reddit
Do uzupełnienia w części E.
