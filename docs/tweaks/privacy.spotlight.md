# privacy.spotlight – Wyłącz Windows Spotlight (zdjęcia dnia)

## Werdykt 26H2
Portować jako pozycję **niesugerowaną** z ostrzeżeniem (decyzja autora 2026-10-21, pytanie 1 po partii 1). Skutek jest: tło pulpitu przestaje być zdjęciem dnia. Cofnięcie usuwa politykę, ale tła „W centrum uwagi Windows” nie przywraca (dlatego w partii 1 pozycja wypadła). Ostrzeżenie w katalogu mówi, że zdjęcia dnia trzeba wybrać ponownie w Ustawienia > Personalizacja > Tło.

## Co zmienia
- `HKCU\Software\Policies\Microsoft\Windows\CloudContent` (w silniku `HKU\<SID zalogowanego>`), wartość `DisableWindowsSpotlightFeatures`, **DWord**, ustawiana na `1`; `scope=user`.
- Skutek widoczny od razu, bez restartu Eksploratora i bez ponownego logowania.

## Źródła
- Test na VM 26H2 Home i Pro, 2026-10-21 (build 26300.9457, nieaktywowany Windows, konto lokalne): zapis przez API rejestru (.NET `RegistryKey`) i obserwacja w Ustawieniach (UI Automation, zrzuty).
- Research: `docs/zrodla/2026-10-20-etap4a-zrodla.md`, sekcja „Wyniki VM 26H2 (partia 1)” (wiersz „Spotlight, wszystko”).
- MS (ADMX/CSP `Experience/AllowWindowsSpotlight`) podaje tę politykę jako ❌ Pro, ✅ Enterprise/Education; na VM 26H2 skutek na pulpicie był jednak taki sam na Home i Pro, więc liczy się obserwacja (zasada Etapu 4a).

## Wartości domyślne na świeżym 26H2
Odczyt z VM, 2026-10-21: klucz `...\Policies\Microsoft\Windows\CloudContent` użytkownika i wartość brak (Home i Pro); tło pulpitu = „W centrum uwagi Windows”.

## Detekcja i undo
- Detekcja: `DisableWindowsSpotlightFeatures` = DWORD 1 -> `Applied`; brak wartości lub inna wartość -> `NotApplied`.
- Undo przywraca snapshot sprzed zmiany z journala (brak wartości = jej usunięcie) i usuwa klucz `CloudContent`, jeśli utworzyło go apply (pusty).
- **Undo nie przywraca tła**: po cofnięciu tło zostaje „Obraz”. Rejestr wraca do stanu sprzed zmiany, ale wybór tła w Ustawieniach już nie.

## Ryzyka i ostrzeżenia
- `risk=caution` (cofnięcie nie przywraca całego skutku), `suggested=false`.
- Ostrzeżenie w katalogu: „Cofnięcie nie przywraca zdjęć dnia: wybierz je ponownie w Ustawienia > Personalizacja > Tło.”
- Poza uwagami w programie (w szczegółach technicznych albo tylko tutaj): skutku na ekranie blokady nie sprawdzano.
- Ponownego wyboru „W centrum uwagi Windows” po cofnięciu nie sprawdzano: nieaktywowany Windows na VM blokuje personalizację.

## Test VM
Przypadek `privacy-spotlight` (`Flow=Full`) w `tests/vm/cases.psd1` (`NotWrittenTo` dla `HKCU` procesu Administratora, bo pozycja jest per użytkownik; `KeyAbsentAfterUndo`, bo klucz `CloudContent` użytkownika nie istnieje na świeżej instalacji). Przebieg na VM 2026-10-21 (`Invoke-VmTests.ps1` z samym tym przypadkiem): **DSO-26H2-Home i DSO-26H2-Pro – zaliczony (12/0)**, kroki detect `NotApplied` → apply `Applied` → detect `Applied` → undo `Undone` → detect `NotApplied`, stan po apply potwierdzony niezależnym odczytem, zapis trafił do HKU zalogowanego, nie do HKCU procesu, klucz `CloudContent` po undo nie istnieje, stan końcowy = stan przed. Przypadek sprawdza rejestr, nie tło (tło: obserwacja niżej).

## Obserwacja skutku
- **Home** (`DSO-26H2-Home`), 2026-10-21. Metoda: zrzuty tła pulpitu i Ustawień przed zapisem polityki, po zapisie i po cofnięciu. Wynik: tło przełącza się z „W centrum uwagi Windows” na „Obraz” (znika ikona „Dowiedz się więcej o tym zdjęciu”); **po cofnięciu tło zostaje „Obraz”**. Ekranu blokady nie dało się sprawdzić (nieaktywowany Windows blokuje personalizację).
- **Pro** (`DSO-26H2-Pro`), 2026-10-21. Ta sama metoda i ten sam wynik.
