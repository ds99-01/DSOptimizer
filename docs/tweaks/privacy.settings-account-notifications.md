# privacy.settings-account-notifications – Wyłącz powiadomienia o koncie w Ustawieniach

## Werdykt 26H2
Portować: przełącznik „Pokaż powiadomienia w ustawieniach” zmienia się zapisem jednej wartości; test na VM 26H2 (Home i Pro) potwierdził stan przełącznika. dowód: stan przełącznika w Ustawieniach, treści nie obserwowano.

## Co zmienia
- `HKCU\Software\Microsoft\Windows\CurrentVersion\SystemSettings\AccountNotifications` (w silniku `HKU\<SID zalogowanego>`), wartość `EnableAccountNotifications`, **DWord**, ustawiana na `0`; `scope=user`.

## Źródła
- Test na VM 26H2 Home i Pro, 2026-10-21 (build 26300.9457, nieaktywowany Windows, konto lokalne): zapis przez API rejestru (.NET `RegistryKey`) i obserwacja w Ustawieniach (UI Automation, zrzuty).
- Research: `docs/zrodla/2026-10-20-etap4a-zrodla.md`, sekcja „Wyniki VM 26H2 (partia 1)”.
- Brak dokumentacji MS dla tej wartości, dowód: test na VM (obserwacja niżej).

## Wartości domyślne na świeżym 26H2
Odczyt z VM DSO-26H2-Home i DSO-26H2-Pro, 2026-10-21: klucz `AccountNotifications` istnieje, `EnableAccountNotifications` **brak** (przełącznik włączony).

## Detekcja i undo
- Detekcja: wszystkie wartości z sekcji „Co zmienia” równe zapisanym -> `Applied`; brak wartości lub inna wartość -> `NotApplied`.
- Undo przywraca snapshot sprzed zmiany z journala (brak wartości = jej usunięcie); `original` z katalogu to tylko fallback.
- Klucz `AccountNotifications` istniał przed apply i zostaje.

## Ryzyka i ostrzeżenia
- `risk=safe`, `suggested=true`.
- Bez ostrzeżeń w katalogu.

## Test VM
Przypadek `privacy-settings-account-notifications` (`Flow=Full`) w `tests/vm/cases.psd1` (`NotWrittenTo` dla `HKCU` procesu Administratora, bo pozycja jest per użytkownik; `KeyPresentAfterUndo`, bo klucz istnieje na świeżej instalacji). Przebieg na VM 2026-10-21 (`Invoke-VmTests.ps1`, Task 9 partii): **DSO-26H2-Home i DSO-26H2-Pro – zaliczony**, kroki detect `NotApplied` → apply `Applied` → detect `Applied` → undo `Undone` → detect `NotApplied`, stan po apply potwierdzony niezależnym odczytem, stan końcowy = stan przed (`SnapshotDiff` pusty). HKCU Administratora bez zmian, klucz `AccountNotifications` został (`KeyPresentAfterUndo`). Eksploracja z 2026-10-21 przeszła na Home i Pro.

## Obserwacja skutku
- **Home** (`DSO-26H2-Home`), 2026-10-21. Metoda: przełącznik „Pokaż powiadomienia w ustawieniach” w Ustawieniach (UI Automation) po zapisie przez API. Wynik: przełącznik „wyłączone”; po cofnięciu „włączone”. dowód: stan przełącznika w Ustawieniach, treści nie obserwowano.
- **Pro** (`DSO-26H2-Pro`), 2026-10-21. Ta sama metoda i ten sam wynik.

## Reddit
Do uzupełnienia w części E.
