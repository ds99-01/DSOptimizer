# privacy.start-account-notifications – Wyłącz powiadomienia o koncie w menu Start

## Werdykt 26H2
Portować: przełącznik „Pokaż powiadomienia związane z kontem” (Personalizacja > Start) zmienia się zapisem jednej wartości; test na VM 26H2 (Home i Pro) potwierdził stan przełącznika. dowód: stan przełącznika w Ustawieniach, treści nie obserwowano.

## Co zmienia
- `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced` (w silniku `HKU\<SID zalogowanego>`), wartość `Start_AccountNotifications`, **DWord**, ustawiana na `0`; `scope=user`.
- Mechanizm przełącznika `SystemSettings_Start_AccountNotifications_ToggleSwitch` z różnicy rejestru na VM: wyłączenie zapisuje `Start_AccountNotifications` = 0, włączenie zapisuje 1. Na czystej VM wartości nie ma, a przełącznik jest włączony.
- Przełącznik w Ustawieniach jest wyłączony od razu, także przed restartem Eksploratora.

## Źródła
- Test na VM 26H2 Home i Pro, 2026-11-07 (build 26300.9457, nieaktywowany Windows, konto lokalne): zapis przez API rejestru (.NET), różnica rejestru po przełączeniu przełącznika w obie strony i obserwacja w Ustawieniach (UI Automation, zrzuty).
- Win11Debloat: https://github.com/Raphire/Win11Debloat (gałąź master), plik `Regfiles/Disable_Windows_Suggestions.reg`, linie 40–42; dostęp 2026-11-06.
- Brak dokumentacji MS dla tej wartości, dowód: test na VM (obserwacja niżej).

## Wartości domyślne na świeżym 26H2
Odczyt z VM `DSO-EN-Home` i `DSO-EN-Pro`, 2026-11-07: klucz `Explorer\Advanced` istnieje, `Start_AccountNotifications` **brak** (przełącznik włączony).

## Detekcja i undo
- Detekcja: `Start_AccountNotifications` = DWORD 0 -> `Applied`; brak wartości lub inna wartość (także 1 zapisane przez przełącznik) -> `NotApplied`.
- Undo przywraca snapshot sprzed zmiany z journala (brak wartości = jej usunięcie, 1 = przełącznik włączony); `original` z katalogu (`<RemoveEntry>`) to tylko fallback.
- Klucz `Explorer\Advanced` istniał przed apply i zostaje (`KeyPresentAfterUndo`).

## Ryzyka i ostrzeżenia
- `risk=safe`, `suggested=true`: wyłącza powiadomienia o koncie w Start, utraty funkcji nie zaobserwowano.
- Bez ostrzeżeń w katalogu.

## Test VM
Przypadek `privacy-start-account-notifications` (`Flow=Full`) w `tests/vm/cases.psd1`; przebieg VM z 2026-11-08 (build 26300.9457, `Invoke-VmTests.ps1`): **Passed** na `DSO-EN-Home` (Core), `DSO-EN-Pro` i `DSO-PL-Pro` (Professional). Pełne przebiegi: 915 zaliczonych, 2 błędy, 25 pominiętych na obu VM angielskich oraz 917 zaliczonych, 0 błędów, 25 pominiętych na polskiej. Oba błędy dotyczyły przypadków dwóch pozycji powiadomień (`privacy.toast-suggested`, `privacy.toast-backup-reminder`; opis w ich dokumentacji), nie tej pozycji.

## Obserwacja skutku
- **Home** (`DSO-EN-Home`), 2026-11-07. Metoda: strona `ms-settings:personalization-start` w świeżym oknie Ustawień (UI Automation, zrzuty), przełącznik „Show account-related notifications” („Pokaż powiadomienia związane z kontem” w polskim Windows) przed zapisem, po zapisie, po restarcie Eksploratora i po cofnięciu. Wynik: przed zapisem włączony (wartości brak); po zapisie wyłączony, tak samo po restarcie Eksploratora; po cofnięciu włączony. dowód: stan przełącznika w Ustawieniach, treści nie obserwowano.
- **Pro** (`DSO-EN-Pro`), 2026-11-07. Ta sama metoda i ten sam wynik.
- Cofnięcie w teście przywróciło wartość 1, bo przed zapisem pozycji przełącznik był już raz przełączany w obie strony (odczyt mechanizmu); stan „brak wartości = przełącznik włączony” widać w odczycie sprzed przełączania.
- Nie sprawdzono: powiadomień o koncie w samym menu Start (konto lokalne; zrzuty menu Start bez oceny różnicy).
