# ux.classic-context-menu – Przywróć klasyczne menu prawego przycisku myszy

## Werdykt 26H2
Portować: test na VM 26H2 (Home i Pro) pokazał realny skutek. Brak dokumentacji MS dla tego klucza w naszym źródle, dowód: test na VM (obserwacja niżej). Pozycja jest ręczna: `suggested=false`, nigdy nie zaznaczana automatycznie.

## Co zmienia
- `HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32`, **wartość domyślna** (pusta nazwa), **String** (REG_SZ), pusty ciąg; `scope=user` (hive zalogowanego użytkownika, nie konta Administratora).
- Na świeżym 26H2 nie ma ani klucza `{86ca…}`, ani jego podklucza `InprocServer32`. Apply tworzy oba, undo usuwa oba (silnik zapisuje w snapshocie najwyższy brakujący klucz i po przywróceniu brakującej wartości kasuje puste klucze od `InprocServer32` w górę do `{86ca…}`).
- Apply nie restartuje Eksploratora (decyzja R15). Menu zmienia się po restarcie Eksploratora: użyj akcji `tools.restart-explorer` albo zaloguj się ponownie.

## Źródła
- Test na VM 26H2 Home i Pro, 2026-10-21 (build 26300.9457): zapis i cofnięcie przez API rejestru (.NET `RegistryKey`), obserwacja okna menu.
- Research: `docs/zrodla/2026-10-20-etap4a-zrodla.md`, sekcje „Wyniki VM 26H2 (partia 1)” i „Pozycje do katalogu”, Task 8.

## Wartości domyślne na świeżym 26H2
Odczyt z VM DSO-26H2-Home i DSO-26H2-Pro, 2026-10-21: `HKU\<SID>\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}` **brak klucza**, `...\InprocServer32` **brak klucza** (Home i Pro identycznie).

## Detekcja i undo
- Detekcja: wartość domyślna `InprocServer32` = pusty REG_SZ -> `Applied`; brak klucza lub inna wartość -> `NotApplied`.
- Undo przywraca snapshot sprzed zmiany z journala (brak wartości = usunięcie wartości, a potem obu kluczy utworzonych przez apply); `original` = `<RemoveEntry>` to tylko fallback.
- Obserwacja z eksploracji: po cofnięciu oba klucze zostały usunięte, a po restarcie Eksploratora wróciło menu Windows 11. Usunięcie obu kluczy przez silnik potwierdził przypadek VM 2026-10-21 na Home i Pro (Task 9 partii, sekcja „Test VM”).

## Ryzyka i ostrzeżenia
- `risk=caution`, `suggested=false`.
- Ostrzeżenie w katalogu: „Menu zmieni się po ponownym zalogowaniu (albo po restarcie Eksploratora w „Narzędziach”).”
- Poza uwagami w programie (w szczegółach technicznych albo tylko tutaj): DSOptimizer sam Eksploratora nie restartuje (restart to pozycja `tools.restart-explorer`); wpis w HKCU przesłania rejestrację menu Windows 11 dla tego konta, a cofnięcie przywraca stan sprzed zmiany.

## Test VM
Przypadek `ux-classic-context-menu` (`Flow=Full`) w `tests/vm/cases.psd1`: `KeyAbsentAfterUndo` dla `{86ca…}` i `{86ca…}\InprocServer32` (w `HKU\{UserSid}\…`), `NotWrittenTo` dla HKCU Administratora. Przebieg na VM 2026-10-21 (`Invoke-VmTests.ps1`, Task 9 partii): **DSO-26H2-Home i DSO-26H2-Pro – zaliczony**, kroki detect `NotApplied` → apply `Applied` → detect `Applied` → undo `Undone` → detect `NotApplied`, stan po apply potwierdzony niezależnym odczytem, stan końcowy = stan przed (`SnapshotDiff` pusty). Silnik po undo usunął oba klucze utworzone przez apply: `{86ca…}\InprocServer32` i `{86ca…}` (`KeyAbsentAfterUndo`). Po apply niezależny odczyt widział pustą wartość domyślną (`Exists`, `String`, dane puste), a detekcja `Applied`; po undo brak klucza i detekcja `NotApplied` (pusta wartość domyślna i brak klucza są rozróżniane). HKCU Administratora bez zmian.

## Obserwacja skutku
- **Home** (`DSO-26H2-Home`, EditionID `Core`), 2026-10-21. Metoda: prawy klik na pulpicie w sesji `dso` po restarcie Eksploratora, odczyt klasy otwartego okna menu. Wynik: z kluczem prawy klik na pulpicie otwiera okno klasy `#32768` (menu Win32); bez klucza otwiera się `Microsoft.UI.Content.PopupWindowSiteBridge` (menu Windows 11). Po cofnięciu (oba klucze usunięte) i restarcie Eksploratora wraca menu Windows 11.
- **Pro** (`DSO-26H2-Pro`, EditionID `Professional`), 2026-10-21. Ta sama metoda i ten sam wynik.

## Reddit
Do uzupełnienia w części E.
