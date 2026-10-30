# privacy.start-recommendations – Wyłącz zalecenia w menu Start

## Werdykt 26H2
Portować: przełącznik zaleceń w Start zmienia się zapisem jednej wartości; test na VM 26H2 (Home i Pro) potwierdził stan przełącznika. dowód: stan przełącznika w Ustawieniach, treści nie obserwowano (w menu Start nie było treści tego typu, więc zmiany w menu nie widać).

## Co zmienia
- `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced` (w silniku `HKU\<SID zalogowanego>`), wartość `Start_IrisRecommendations`, **DWord**, ustawiana na `0`; `scope=user`.
- Ustawienia pokazują nowy stan od razu; widok menu Start (według obserwacji na VM po restarcie Eksploratora i zabiciu SearchHost oraz StartMenuExperienceHost) katalog opisuje jako widoczny po ponownym zalogowaniu. Apply nie restartuje niczego (R15); czy sam `tools.restart-explorer` wystarcza, nie sprawdzano (ostrzeżenie: „sam restart Eksploratora może nie wystarczyć”).
- Uwaga do testów UI: na Home po pierwszym restarcie z siecią włączyła się nowa wersja menu Start (w Ustawieniach `AutomationId` pola to `SystemSettings_Start_IrisRecommendations_CheckBox` zamiast `_ToggleSwitch`); wartość rejestru się nie zmienia.

## Źródła
- Test na VM 26H2 Home i Pro, 2026-10-21 (build 26300.9457, nieaktywowany Windows, konto lokalne): zapis przez API rejestru (.NET `RegistryKey`) i obserwacja w Ustawieniach (UI Automation, zrzuty).
- Research: `docs/zrodla/2026-10-20-etap4a-zrodla.md`, sekcja „Wyniki VM 26H2 (partia 1)”.
- Brak dokumentacji MS dla tej wartości, dowód: test na VM (obserwacja niżej).

## Wartości domyślne na świeżym 26H2
Odczyt z VM DSO-26H2-Home i DSO-26H2-Pro, 2026-10-21: klucz `Explorer\Advanced` istnieje, `Start_IrisRecommendations` **brak** (przełącznik włączony).

## Detekcja i undo
- Detekcja: wszystkie wartości z sekcji „Co zmienia” równe zapisanym -> `Applied`; brak wartości lub inna wartość -> `NotApplied`.
- Undo przywraca snapshot sprzed zmiany z journala (brak wartości = jej usunięcie); `original` z katalogu to tylko fallback.
- Klucz `Explorer\Advanced` istniał przed apply i zostaje.

## Ryzyka i ostrzeżenia
- `risk=safe`, `suggested=true` (R12: zostaje sugerowana).
- Ostrzeżenie w katalogu: „Zmiana zadziała po ponownym zalogowaniu; sam restart Eksploratora może nie wystarczyć.”
- Poza uwagami w programie (w szczegółach technicznych albo tylko tutaj): skutku samego restartu Eksploratora (`tools.restart-explorer`) nie sprawdzano.

## Test VM
Przypadek `privacy-start-recommendations` (`Flow=Full`) w `tests/vm/cases.psd1` (`NotWrittenTo` dla `HKCU` procesu Administratora, bo pozycja jest per użytkownik; `KeyPresentAfterUndo`, bo klucz istnieje na świeżej instalacji). Przebieg na VM 2026-10-21 (`Invoke-VmTests.ps1`, Task 9 partii): **DSO-26H2-Home i DSO-26H2-Pro – zaliczony**, kroki detect `NotApplied` → apply `Applied` → detect `Applied` → undo `Undone` → detect `NotApplied`, stan po apply potwierdzony niezależnym odczytem, stan końcowy = stan przed (`SnapshotDiff` pusty). HKCU Administratora bez zmian, klucz `Explorer\Advanced` został (`KeyPresentAfterUndo`). Eksploracja z 2026-10-21 przeszła na Home i Pro.

## Obserwacja skutku
- **Home** (`DSO-26H2-Home`), 2026-10-21. Metoda: przełącznik zaleceń w Ustawieniach (Personalizacja > Start; UI Automation) po zapisie przez API i restarcie Eksploratora. Wynik: przełącznik „wyłączone”; w menu Start nie było treści tego typu, więc zmiany w menu nie widać. dowód: stan przełącznika w Ustawieniach, treści nie obserwowano.
- **Pro** (`DSO-26H2-Pro`), 2026-10-21. Ta sama metoda i ten sam wynik.

## Reddit
Do uzupełnienia w części E.
