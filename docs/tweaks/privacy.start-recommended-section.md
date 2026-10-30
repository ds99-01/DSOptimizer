# privacy.start-recommended-section – Ukryj sekcję „Proponowane” w menu Start

## Werdykt 26H2
Portować: polityka `HideRecommendedSection` chowa sekcję „Proponowane” w Start; test na VM 26H2 (Home i Pro) pokazał realny skutek. Pozycja niesugerowana (R12): sekcja pokazuje także ostatnio otwierane pliki, z których można korzystać na co dzień.

## Co zmienia
- `HKCU\Software\Policies\Microsoft\Windows\Explorer` (w silniku `HKU\<SID zalogowanego>`), wartość `HideRecommendedSection`, **DWord**, ustawiana na `1`; `scope=user`.
- Sekcja znika z menu Start po ponownym zalogowaniu i po restarcie komputera (obserwacja na VM była po restarcie Eksploratora i zabiciu SearchHost oraz StartMenuExperienceHost); po cofnięciu wraca. Apply nie restartuje niczego (R15); czy sam `tools.restart-explorer` wystarcza, nie sprawdzano (ostrzeżenie: „sam restart Eksploratora może nie wystarczyć”).
- Ta sama wartość w `HKLM\SOFTWARE\Policies\Microsoft\Windows\Explorer` daje ten sam skutek (opisane, bez własnej pozycji); pozycja używa zakresu użytkownika.
- `HideRecommendedPersonalizedSites` zawiera się w tej pozycji (patrz `privacy.recommended-personalized-sites.md`).

## Źródła
- Test na VM 26H2 Home i Pro, 2026-10-21 (build 26300.9457, nieaktywowany Windows, konto lokalne): zapis przez API rejestru (.NET `RegistryKey`) i obserwacja w Ustawieniach (UI Automation, zrzuty).
- Research: `docs/zrodla/2026-10-20-etap4a-zrodla.md`, sekcja „Wyniki VM 26H2 (partia 1)”.
- Brak dokumentacji MS dla tej wartości, dowód: test na VM (obserwacja niżej).

## Wartości domyślne na świeżym 26H2
Odczyt z VM DSO-26H2-Home i DSO-26H2-Pro, 2026-10-21: wartość `HideRecommendedSection` **brak** i **brak klucza** `...\Policies\Microsoft\Windows\Explorer` (klucz nadrzędny `...\Policies\Microsoft\Windows` istnieje).

## Detekcja i undo
- Detekcja: `HideRecommendedSection` = DWORD 1 -> `Applied`; brak wartości lub inna wartość -> `NotApplied`.
- Undo przywraca snapshot sprzed zmiany z journala: wartość i klucz `Explorer`, których nie było przed apply, po undo znów nie istnieją (Review Focus 4); `original` = `<RemoveEntry>` to tylko fallback.

## Ryzyka i ostrzeżenia
- `risk=safe`, `suggested=false` (R12).
- Ostrzeżenia w katalogu: „Razem z sekcją znikają z menu Start ostatnio otwierane pliki.” oraz „Zmiana zadziała po ponownym zalogowaniu; sam restart Eksploratora może nie wystarczyć.”
- Poza uwagami w programie (w szczegółach technicznych albo tylko tutaj): skutku samego restartu Eksploratora (`tools.restart-explorer`) nie sprawdzano.

## Test VM
Przypadek `privacy-start-recommended-section` (`Flow=Full`) w `tests/vm/cases.psd1` (`KeyAbsentAfterUndo` dla klucza `Policies\...\Explorer` użytkownika, `NotWrittenTo` dla `HKCU` procesu Administratora). Przebieg na VM 2026-10-21 (`Invoke-VmTests.ps1`, Task 9 partii): **DSO-26H2-Home i DSO-26H2-Pro – zaliczony**, kroki detect `NotApplied` → apply `Applied` → detect `Applied` → undo `Undone` → detect `NotApplied`, stan po apply potwierdzony niezależnym odczytem, stan końcowy = stan przed (`SnapshotDiff` pusty). Klucz `Policies\Microsoft\Windows\Explorer` użytkownika utworzony przez apply po undo nie istnieje (`KeyAbsentAfterUndo`), HKCU Administratora bez zmian. Eksploracja z 2026-10-21 przeszła na Home i Pro.

## Obserwacja skutku
- **Home** (`DSO-26H2-Home`), 2026-10-21. Metoda: zrzut menu Start przed i po zapisie polityki oraz restarcie Eksploratora, a także po restarcie VM. Wynik: sekcja „Proponowane” znika z Start (po restarcie Eksploratora i po restarcie VM); po cofnięciu wraca.
- **Pro** (`DSO-26H2-Pro`), 2026-10-21. Ta sama metoda i ten sam wynik.

## Reddit
Do uzupełnienia w części E.
