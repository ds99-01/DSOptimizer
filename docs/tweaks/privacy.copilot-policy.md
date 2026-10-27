# privacy.copilot-policy – polityka TurnOffWindowsCopilot

## Werdykt 26H2
nie wchodzi: polityka `TurnOffWindowsCopilot` nie daje skutku na 26H2 (brak dowodu). Usunięcie aplikacji `Microsoft.Copilot` to partia 2 Etapu 4a (Aplikacje), nie ta pozycja.

## Co zmienia
Pozycji nie ma w katalogu, nic nie zmienia. Rozważany wpis: `HKCU\Software\Policies\Microsoft\Windows\WindowsCopilot`, wartość `TurnOffWindowsCopilot`, DWord, `1` (`scope=user`; na VM `HKU\<SID>\...`).

## Źródła
- Test na VM 26H2 Home i Pro, 2026-10-21 (build 26300.9457).
- Research: `docs/zrodla/2026-10-20-etap4a-zrodla.md`, sekcje „1. Microsoft Copilot” i „Wyniki VM 26H2 (partia 1)”.

## Wartości domyślne na świeżym 26H2
Odczyt z VM, 2026-10-21: wszystkie wartości `Software\Policies\...` brak (Home i Pro). Aplikacja `Microsoft.Copilot`: Home zainstalowana i przypięta, Pro brak.

## Detekcja i undo
Nie dotyczy (pozycja nie jest w katalogu).

## Ryzyka i ostrzeżenia
Nie dotyczy. Pozycja nie weszła do katalogu. Powrót możliwy, gdy test na VM pokaże skutek.

## Test VM
Nie dotyczy (nie wchodzi): pozycji nie ma w katalogu, więc nie ma przypadku VM.

## Obserwacja skutku
- **Home** (`DSO-26H2-Home`), 2026-10-21. Metoda: zapis polityki przez API rejestru, restart Eksploratora, klawisz Win+C i przycisk na pasku w sesji `dso`. Wynik: **brak skutku**, aplikacja Copilot (preinstalowana i przypięta) dalej otwiera się przez Win+C, a przycisk zostaje.
- **Pro** (`DSO-26H2-Pro`), 2026-10-21. Wynik: **brak skutku do pokazania**, bo na świeżym Pro nie ma aplikacji Copilot (nie ma czego wyłączać).

## Reddit
Do uzupełnienia w części E.
