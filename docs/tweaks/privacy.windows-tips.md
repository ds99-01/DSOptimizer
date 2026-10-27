# privacy.windows-tips – Wyłącz porady i sugestie Windows

## Werdykt 26H2
Portować: pole „Uzyskaj porady i sugestie podczas korzystania z systemu Windows” zmienia się zapisem jednej wartości; test na VM 26H2 (Home i Pro) potwierdził stan pola. dowód: stan przełącznika w Ustawieniach, treści nie obserwowano.

## Co zmienia
- `HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager` (w silniku `HKU\<SID zalogowanego>`), wartość `SubscribedContent-338389Enabled`, **DWord**, ustawiana na `0`; `scope=user`.
- Pole jest w System > Powiadomienia > Ustawienia dodatkowe.
- Polityka `DisableSoftLanding` nie dała skutku (pole dalej zaznaczone), patrz `privacy.soft-landing.md`.

## Źródła
- Test na VM 26H2 Home i Pro, 2026-10-21 (build 26300.9457, nieaktywowany Windows, konto lokalne): zapis przez API rejestru (.NET `RegistryKey`) i obserwacja w Ustawieniach (UI Automation, zrzuty).
- Research: `docs/zrodla/2026-10-20-etap4a-zrodla.md`, sekcja „Wyniki VM 26H2 (partia 1)”.
- Brak dokumentacji MS dla tej wartości, dowód: test na VM (obserwacja niżej).

## Wartości domyślne na świeżym 26H2
Odczyt z VM DSO-26H2-Home i DSO-26H2-Pro, 2026-10-21: `SubscribedContent-338389Enabled` = **1** (pole zaznaczone); `original` w katalogu = 1.

## Detekcja i undo
- Detekcja: wszystkie wartości z sekcji „Co zmienia” równe zapisanym -> `Applied`; brak wartości lub inna wartość -> `NotApplied`.
- Undo przywraca snapshot sprzed zmiany z journala (brak wartości = jej usunięcie); `original` z katalogu to tylko fallback.
- Klucz `ContentDeliveryManager` istniał przed apply i zostaje.

## Ryzyka i ostrzeżenia
- `risk=safe`, `suggested=true`.
- Bez ostrzeżeń w katalogu.

## Test VM
Przypadek `privacy-windows-tips` (`Flow=Full`) w `tests/vm/cases.psd1` (`NotWrittenTo` dla `HKCU` procesu Administratora, bo pozycja jest per użytkownik; `KeyPresentAfterUndo`, bo klucz istnieje na świeżej instalacji). Przebieg na VM 2026-10-21 (`Invoke-VmTests.ps1`, Task 9 partii): **DSO-26H2-Home i DSO-26H2-Pro – zaliczony**, kroki detect `NotApplied` → apply `Applied` → detect `Applied` → undo `Undone` → detect `NotApplied`, stan po apply potwierdzony niezależnym odczytem, stan końcowy = stan przed (`SnapshotDiff` pusty). Wartość 1 sprzed apply wróciła ze snapshotu, HKCU Administratora bez zmian. Eksploracja z 2026-10-21 przeszła na Home i Pro.

## Obserwacja skutku
- **Home** (`DSO-26H2-Home`), 2026-10-21. Metoda: pole „Uzyskaj porady i sugestie podczas korzystania z systemu Windows” w Ustawieniach (UI Automation) po zapisie przez API. Wynik: pole odznaczone; po cofnięciu zaznaczone. dowód: stan przełącznika w Ustawieniach, treści nie obserwowano.
- **Pro** (`DSO-26H2-Pro`), 2026-10-21. Ta sama metoda i ten sam wynik.

## Reddit
Do uzupełnienia w części E.
