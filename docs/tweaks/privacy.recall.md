# privacy.recall – Recall: AllowRecallEnablement i DisableAIDataAnalysis (migawki)

## Werdykt 26H2
nie wchodzi: nie dowiedziono (VM bez NPU, funkcja niedostępna). Dotyczy obu wartości: `AllowRecallEnablement` i `DisableAIDataAnalysis` (migawki). To nie jest dowód braku skutku na sprzęcie z NPU, tylko brak możliwości sprawdzenia na naszych VM.

## Co zmienia
Pozycji nie ma w katalogu, nic nie zmienia. Rozważane wpisy:
- `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI`, `AllowRecallEnablement`, DWord, `0`.
- `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI` i `HKCU\Software\Policies\Microsoft\Windows\WindowsAI` (na VM `HKU\<SID>\...`), `DisableAIDataAnalysis`, DWord, `1` (wyłączenie zapisywania migawek).

Gdyby pozycja kiedyś weszła (po teście na sprzęcie z NPU): `suggested=false` i ostrzeżenie „usuwa zapisane migawki”.

## Źródła
- Test na VM 26H2 Home i Pro, 2026-10-21 (build 26300.9457).
- Research: `docs/zrodla/2026-10-20-etap4a-zrodla.md`, sekcje „2. Recall i Click to Do” i „Wyniki VM 26H2 (partia 1)”.

## Wartości domyślne na świeżym 26H2
Odczyt z VM, 2026-10-21: funkcja Windows `Recall` = `DisabledWithPayloadRemoved` (Home i Pro); wartości polityk `WindowsAI` brak.

## Detekcja i undo
Nie dotyczy (pozycja nie jest w katalogu).

## Ryzyka i ostrzeżenia
Nie dotyczy. Pozycja nie weszła do katalogu.

## Test VM
Nie dotyczy (nie wchodzi): pozycji nie ma w katalogu, więc nie ma przypadku VM.

## Obserwacja skutku
- **Home** (`DSO-26H2-Home`), 2026-10-21. Metoda: odczyt stanu funkcji `Recall`, strony w Ustawieniach, zapis polityk przez API rejestru i restart VM. Wynik: **brak skutku do pokazania**, funkcja `Recall` = `DisabledWithPayloadRemoved` przed zmianą i po restarcie VM, w Ustawieniach nie ma strony Recall (VM bez NPU).
- **Pro** (`DSO-26H2-Pro`), 2026-10-21. Ta sama metoda i ten sam wynik. Migawki (`DisableAIDataAnalysis`): także brak skutku do pokazania, bo funkcja jest niedostępna.
