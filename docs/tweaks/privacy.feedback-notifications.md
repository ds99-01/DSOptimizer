# privacy.feedback-notifications – Wyłącz powiadomienia o opiniach

## Werdykt 26H2
Portować: test na VM 26H2 (Home i Pro) pokazał zmianę stanu strony „Diagnostyka i opinie” w Ustawieniach (baner, lista „Częstotliwość opinii” znika). Samych powiadomień o opinie nie obserwowano.

## Co zmienia
- `HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection`, wartość `DoNotShowFeedbackNotifications`, **DWord**, ustawiana na `1`; `scope=machine`.
- Klucz `DataCollection` **istnieje na świeżej instalacji**; undo usuwa tylko wartość, klucz zostaje (przypadek VM: `KeyPresentAfterUndo`).
- Wspólny klucz z `privacy.diagnostic-data-required`; pozycje są niezależne.

## Źródła
- Dokumentacja MS: polityka `Experience/DoNotShowFeedbackNotifications` (Windows 10 1607+; „użytkownicy nie będą widzieć powiadomień o opiniach w aplikacji Windows Feedback”), GPO „Do not show feedback notifications”, rejestr z „Manage connections…”: `HKLM\Software\Policies\Microsoft\Windows\DataCollection\DoNotShowFeedbackNotifications` = 1.
- Test na VM 26H2 Home i Pro, 2026-10-21 (build 26300.9457, nieaktywowany Windows, konto lokalne): zapis przez API rejestru (.NET `RegistryKey`) i obserwacja w Ustawieniach (UI Automation, zrzuty).
- Research: `docs/zrodla/2026-10-20-etap4a-zrodla.md`, sekcje „2d. Feedback frequency” i „Wyniki VM 26H2 (partia 1)”.

## Wartości domyślne na świeżym 26H2
Odczyt z VM DSO-26H2-Home i DSO-26H2-Pro, 2026-10-21: klucz `DataCollection` istnieje, `DoNotShowFeedbackNotifications` **brak wartości** (lista „Częstotliwość opinii” widoczna).

## Detekcja i undo
- Detekcja: `DoNotShowFeedbackNotifications` = DWORD 1 -> `Applied`; brak wartości lub inna wartość -> `NotApplied`.
- Undo przywraca snapshot sprzed zmiany z journala (brak wartości = usunięcie wartości, klucz zostaje); `original` = `<RemoveEntry>` to tylko fallback.

## Ryzyka i ostrzeżenia
- `risk=safe`, `suggested=true`.
- Ostrzeżenie w katalogu: „W Ustawieniach pojawi się komunikat, że niektórymi ustawieniami zarządza organizacja.” Dowód: stan strony w Ustawieniach, samych powiadomień nie obserwowano.

## Test VM
Przypadek `privacy-feedback-notifications` (`Flow=Full`) w `tests/vm/cases.psd1`: `KeyPresentAfterUndo` dla `HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection`. Przebieg na VM 2026-10-21 (`Invoke-VmTests.ps1`, Task 9 partii): **DSO-26H2-Home i DSO-26H2-Pro – zaliczony**, kroki detect `NotApplied` → apply `Applied` → detect `Applied` → undo `Undone` → detect `NotApplied`, stan po apply potwierdzony niezależnym odczytem, stan końcowy = stan przed (`SnapshotDiff` pusty). Klucz `DataCollection` przetrwał undo (`KeyPresentAfterUndo`), `DoNotShowFeedbackNotifications` po undo nie istnieje. Eksploracja z 2026-10-21 przeszła na Home i Pro.

## Obserwacja skutku
- **Home** (`DSO-26H2-Home`, EditionID `Core`), 2026-10-21. Metoda: UI Automation Ustawień (Diagnostyka i opinie) i zrzuty po zapisie przez API rejestru. Wynik: baner „zarządza organizacja”, lista „Częstotliwość opinii” znika ze strony; samych powiadomień nie obserwowano. Po cofnięciu lista wraca, baner znika.
- **Pro** (`DSO-26H2-Pro`, EditionID `Professional`), 2026-10-21. Ta sama metoda i ten sam wynik.

## Reddit
Do uzupełnienia w części E.
