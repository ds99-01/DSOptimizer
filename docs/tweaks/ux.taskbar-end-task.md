# ux.taskbar-end-task – Dodaj „Zakończ zadanie” do menu na pasku zadań

## Werdykt 26H2
Portować: test na VM 26H2 (Home i Pro) pokazał realny skutek. Źródło MS dla przełącznika: MS Learn „Advanced Windows Settings” (System > Zaawansowane, pozycja Taskbar – End Task); nazwy wartości rejestru strona nie podaje, ją potwierdza test na VM. Pozycja jest ręczna: `suggested=false`, nigdy nie zaznaczana automatycznie.

## Co zmienia
- `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings`, wartość `TaskbarEndTask`, **DWord**, ustawiana na `1`; `scope=user` (hive zalogowanego użytkownika).
- Na świeżym 26H2 nie ma ani wartości, ani klucza `TaskbarDeveloperSettings` (tworzy go dopiero przełącznik w Ustawieniach). Apply tworzy klucz, undo usuwa wartość i pusty klucz utworzony przez apply.
- Apply nie restartuje Eksploratora (decyzja R15). Pozycja pojawia się w menu po restarcie Eksploratora: użyj akcji `tools.restart-explorer` albo zaloguj się ponownie.

## Źródła
- https://learn.microsoft.com/en-us/windows/advanced-settings/ – MS Learn „Advanced Windows Settings” (2026-01-22): przełącznik „Enable end task in taskbar by right click” w System > Zaawansowane (25H2 i nowsze; wcześniej Dla deweloperów).
- Test na VM 26H2 Home i Pro, 2026-10-21 (build 26300.9457): mechanizm odczytany z przełącznika w Ustawieniach i zmiana przez API rejestru.
- Research: `docs/zrodla/2026-10-20-etap4a-zrodla.md`, sekcje „Wyniki VM 26H2 (partia 1)” i „Pozycje do katalogu”, Task 8.

## Wartości domyślne na świeżym 26H2
Odczyt z VM DSO-26H2-Home i DSO-26H2-Pro, 2026-10-21: `TaskbarEndTask` **brak wartości** (wyłączone), klucz `TaskbarDeveloperSettings` **brak** (Home i Pro identycznie). Przełącznik w Ustawieniach przełączony tam i z powrotem zostawia wartość jawną `0` zamiast braku.

## Detekcja i undo
- Detekcja: `TaskbarEndTask` = DWORD 1 -> `Applied`; brak wartości lub inna wartość -> `NotApplied`.
- Undo przywraca snapshot sprzed zmiany z journala (brak wartości = usunięcie wartości i klucza `TaskbarDeveloperSettings`, jeśli powstał przy apply); `original` = `<RemoveEntry>` to tylko fallback.
- Obserwacja z eksploracji: po cofnięciu i restarcie Eksploratora pozycja znika z menu przycisku. W teście klucz założył wcześniej przełącznik Ustawień, więc usunięcia samego klucza przez cofnięcie nie obserwowano; dowiódł tego przypadek VM 2026-10-21 na Home i Pro (Task 9 partii, sekcja „Test VM”).

## Ryzyka i ostrzeżenia
- `risk=safe`, `suggested=false`.
- Ostrzeżenia w katalogu: „Pozycja pojawi się po ponownym zalogowaniu (albo po restarcie Eksploratora w „Narzędziach”).” oraz „„Zakończ zadanie” kończy proces programu; niezapisane dane w tym programie mogą przepaść.”
- Poza uwagami w programie (w szczegółach technicznych albo tylko tutaj): DSOptimizer sam Eksploratora nie restartuje (restart to pozycja `tools.restart-explorer`); uwaga o utracie danych opisuje zakończenie procesu, nie pomiar.

## Test VM
Przypadek `ux-taskbar-end-task` (`Flow=Full`) w `tests/vm/cases.psd1`: `KeyAbsentAfterUndo` dla `TaskbarDeveloperSettings` (w `HKU\{UserSid}\…`), `NotWrittenTo` dla HKCU Administratora. Przebieg na VM 2026-10-21 (`Invoke-VmTests.ps1`, Task 9 partii): **DSO-26H2-Home i DSO-26H2-Pro – zaliczony**, kroki detect `NotApplied` → apply `Applied` → detect `Applied` → undo `Undone` → detect `NotApplied`, stan po apply potwierdzony niezależnym odczytem, stan końcowy = stan przed (`SnapshotDiff` pusty). Klucz `TaskbarDeveloperSettings` utworzony przez apply po undo nie istnieje (`KeyAbsentAfterUndo`), HKCU Administratora bez zmian.

## Obserwacja skutku
- **Home** (`DSO-26H2-Home`, EditionID `Core`), 2026-10-21. Metoda: menu przycisku programu na pasku zadań w sesji `dso` po restarcie Eksploratora, odczyt pozycji menu. Wynik: z `TaskbarEndTask`=1 menu przycisku ma pozycję „Zakończ zadanie”; po cofnięciu i restarcie Eksploratora pozycji nie ma.
- **Pro** (`DSO-26H2-Pro`, EditionID `Professional`), 2026-10-21. Ta sama metoda i ten sam wynik.

## Reddit
Do uzupełnienia w części E.
