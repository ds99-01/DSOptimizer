# privacy.widgets – Wyłącz Widżety

## Werdykt 26H2
Portować: test na VM 26H2 (Home i Pro) pokazał realny skutek. Brak dokumentacji MS dla tej wartości w naszym źródle, dowód: test na VM (obserwacja niżej).

## Co zmienia
- `HKLM\SOFTWARE\Policies\Microsoft\Dsh`, wartość `AllowNewsAndInterests`, **DWord**, ustawiana na `0`; `scope=machine`.
- Na świeżym 26H2 nie ma ani wartości, ani klucza `Dsh`. Apply tworzy klucz, undo usuwa wartość i pusty klucz `Dsh` utworzony przez apply.
- Apply nie restartuje Eksploratora. Przycisk Widżetów znika z paska zadań po restarcie Eksploratora: użyj akcji `tools.restart-explorer` albo zaloguj się ponownie. Tablica jest zablokowana od razu.
- Rozważany wariant użytkownika `HKCU\...\Explorer\Advanced\TaskbarDa` nie wchodzi, bo zapis blokuje system (patrz `privacy.widgets-taskbar-da.md`).

## Źródła
- Test na VM 26H2 Home i Pro, 2026-10-21 (build 26300.9457): mechanizm odczytany z przełącznika w Ustawieniach i zmiany polityki przez API rejestru (.NET `RegistryKey`).
- Research: `docs/zrodla/2026-10-20-etap4a-zrodla.md`, sekcje „3. Widgets” i „Wyniki VM 26H2 (partia 1)”.

## Wartości domyślne na świeżym 26H2
Odczyt z VM DSO-26H2-Home i DSO-26H2-Pro, 2026-10-21: `HKLM\SOFTWARE\Policies\Microsoft\Dsh` **brak klucza**, `AllowNewsAndInterests` **brak wartości** (Home i Pro identycznie).

## Detekcja i undo
- Detekcja: `AllowNewsAndInterests` = DWORD 0 -> `Applied`; brak wartości lub inna wartość -> `NotApplied`.
- Undo przywraca snapshot sprzed zmiany z journala (brak wartości = usunięcie wartości i klucza `Dsh`, który powstał przy apply); `original` = `<RemoveEntry>` to tylko fallback.

## Ryzyka i ostrzeżenia
- `risk=safe`, `suggested=true`.
- Ostrzeżenie w katalogu: „Przycisk zniknie po ponownym zalogowaniu (albo po restarcie Eksploratora w „Narzędziach”).”
- Poza uwagami w programie (w szczegółach technicznych albo tylko tutaj): restart Eksploratora to pozycja `tools.restart-explorer`; przełącznik „Widżety” w Ustawieniach jest po zmianie wyszarzony (polityka go blokuje i nie da się tego uniknąć).

## Test VM
Przypadek `privacy-widgets` (`Flow=Full`) w `tests/vm/cases.psd1`: `KeyAbsentAfterUndo` dla `HKLM\SOFTWARE\Policies\Microsoft\Dsh`. Przebieg na VM 2026-10-21 (`Invoke-VmTests.ps1`, Task 9 partii): **DSO-26H2-Home i DSO-26H2-Pro – zaliczony**, kroki detect `NotApplied` → apply `Applied` → detect `Applied` → undo `Undone` → detect `NotApplied`, stan po apply potwierdzony niezależnym odczytem, stan końcowy = stan przed (`SnapshotDiff` pusty). Klucz `Dsh` utworzony przez apply po undo nie istnieje (`KeyAbsentAfterUndo`). Eksploracja z 2026-10-21 (zapis przez API i cofnięcie) przeszła na Home i Pro.

## Obserwacja skutku
- **Home** (`DSO-26H2-Home`, EditionID `Core`), 2026-10-21. Metoda: UI Automation Ustawień po `AutomationId`, zrzuty, klawisze Win+W w sesji `dso`. Wynik: po zapisie `AllowNewsAndInterests`=0 skrót Win+W nie otwiera tablicy; przełącznik „Widżety” w Ustawieniach jest wyszarzony i wyłączony; przycisk na pasku znika po restarcie Eksploratora (bez restartu tablica jest zablokowana, przycisk jeszcze widać). Po cofnięciu (wartość i klucz usunięte) Win+W znowu otwiera tablicę, przełącznik aktywny, przycisk wraca po restarcie Eksploratora.
- **Pro** (`DSO-26H2-Pro`, EditionID `Professional`), 2026-10-21. Ta sama metoda i ten sam wynik.
