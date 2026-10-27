# privacy.widgets-taskbar-da – przycisk Widżetów przez TaskbarDa (użytkownik)

## Werdykt 26H2
nie wchodzi: system blokuje zapis `TaskbarDa` (sterownik UCPD) dla każdego naszego procesu. Widżety wyłącza pozycja `privacy.widgets` (polityka maszynowa).

## Co zmienia
Pozycji nie ma w katalogu, nic nie zmienia. Rozważany wpis: `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced`, `TaskbarDa`, DWord, `0` (`scope=user`). Przełącznik w Ustawieniach zapisuje tę wartość (0/1) i chowa przycisk od razu, ale nasz zapis kończy się `UnauthorizedAccessException`.

## Źródła
- Test na VM 26H2 Home i Pro, 2026-10-21 (build 26300.9457): zapis z `powershell.exe` i z osobnego exe o nazwie `DSOptimizer.exe`.
- Research: `docs/zrodla/2026-10-20-etap4a-zrodla.md`, sekcja „Wyniki VM 26H2 (partia 1)”, uwaga o `TaskbarDa`.

## Wartości domyślne na świeżym 26H2
Odczyt z VM, 2026-10-21: `Explorer\Advanced\TaskbarDa` brak wartości (przycisk widoczny), Home i Pro.

## Detekcja i undo
Nie dotyczy (pozycja nie jest w katalogu).

## Ryzyka i ostrzeżenia
Nie dotyczy. Pozycja nie weszła do katalogu.

## Test VM
Nie dotyczy (nie wchodzi): pozycji nie ma w katalogu, więc nie ma przypadku VM.

## Obserwacja skutku
- **Home** (`DSO-26H2-Home`), 2026-10-21. Metoda: zapis przez .NET `RegistryKey` (jak API aplikacji) i z osobnego exe. Wynik: `UnauthorizedAccessException`, sterownik UCPD działa; przełącznik z Ustawień chowa przycisk, ale to zapis systemowy, którego nie możemy powtórzyć.
- **Pro** (`DSO-26H2-Pro`), 2026-10-21. Ta sama metoda i ten sam wynik.

## Reddit
Do uzupełnienia w części E.
