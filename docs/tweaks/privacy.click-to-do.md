# privacy.click-to-do – polityka DisableClickToDo

## Werdykt 26H2
nie wchodzi: nie dowiedziono (VM bez NPU, funkcja niedostępna). To nie jest dowód braku skutku na sprzęcie z NPU, tylko brak możliwości sprawdzenia na naszych VM.

## Co zmienia
Pozycji nie ma w katalogu, nic nie zmienia. Rozważany wpis: `DisableClickToDo`, DWord, `1`, w `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI` i `HKCU\Software\Policies\Microsoft\Windows\WindowsAI` (na VM `HKU\<SID>\...`).

## Źródła
- Test na VM 26H2 Home i Pro, 2026-10-21 (build 26300.9457).
- Research: `docs/zrodla/2026-10-20-etap4a-zrodla.md`, sekcje „2. Recall i Click to Do” i „Wyniki VM 26H2 (partia 1)”.

## Wartości domyślne na świeżym 26H2
Odczyt z VM, 2026-10-21: wartości polityk `WindowsAI` brak (Home i Pro). Click to Do nie ma punktu wejścia, procesu ani strony w Ustawieniach.

## Detekcja i undo
Nie dotyczy (pozycja nie jest w katalogu).

## Ryzyka i ostrzeżenia
Nie dotyczy. Pozycja nie weszła do katalogu.

## Test VM
Nie dotyczy (nie wchodzi): pozycji nie ma w katalogu, więc nie ma przypadku VM.

## Obserwacja skutku
- **Home** (`DSO-26H2-Home`), 2026-10-21. Metoda: poszukiwanie punktu wejścia, procesu i strony w Ustawieniach przed i po zapisie polityki oraz restarcie Eksploratora. Wynik: **brak skutku do pokazania**, funkcja jest niedostępna już przed zmianą (VM bez NPU).
- **Pro** (`DSO-26H2-Pro`), 2026-10-21. Ta sama metoda i ten sam wynik.

## Reddit
Do uzupełnienia w części E.
