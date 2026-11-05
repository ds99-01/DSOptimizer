# privacy.advertising-id – Wyłącz identyfikator reklamowy

## Werdykt 26H2
Portować: przełącznik identyfikatora reklamowego zmienia się dwiema wartościami; test na VM 26H2 (Home i Pro) potwierdził stan przełącznika (zapis `1`/`1` włącza go, `0`/`0` wyłącza). dowód: stan przełącznika w Ustawieniach, treści nie obserwowano.

## Co zmienia
- `HKCU\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo` (w silniku `HKU\<SID zalogowanego>`), wartość `Enabled`, **DWord**, `0`.
- `HKCU\Software\Microsoft\Windows\CurrentVersion\CPSS\Store\AdvertisingInfo`, wartość `Value`, **DWord**, `0`; `scope=user`.
- Pozycja zapisuje tylko te dwie wartości i **nie usuwa** wartości `Id` (REG_SZ) z `AdvertisingInfo\Id`. Przełącznik w Ustawieniach przy wyłączaniu usuwa dodatkowo `AdvertisingInfo\Id`. W osobnym pomiarze na VM zapis zawierał to usunięcie, więc samych wartości 0/0 bez usunięcia `Id` w nim nie sprawdzano. Sprawdził je test na VM (2026-10-21, Home i Pro): przy istniejącym `Id` przełącznik po apply tej pozycji (tylko 0/0) jest wyłączony, a po undo włączony (szczegóły w „Test VM”).

### Działająca alternatywa (opisana, bez własnej pozycji katalogu)
`HKLM\SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo`, `DisabledByGroupPolicy` = DWORD 1: na Home i Pro pokazuje baner „Niektórymi z tych ustawień zarządza Twoja organizacja” i wyszarza przełącznik. Nie wchodzi do katalogu, bo odbiera użytkownikowi możliwość przełączenia (nie odbieramy wyboru).

## Źródła
- Test na VM 26H2 Home i Pro, 2026-10-21 (build 26300.9457, nieaktywowany Windows, konto lokalne): zapis przez API rejestru (.NET `RegistryKey`) i obserwacja w Ustawieniach (UI Automation, zrzuty).
- Brak dokumentacji MS dla tej wartości, dowód: test na VM (obserwacja niżej).

## Wartości domyślne na świeżym 26H2
Odczyt z VM DSO-26H2-Home i DSO-26H2-Pro, 2026-10-21: `Enabled` = 0, `Value` = 0, **brak** wartości `Id` (wyłączony po konfiguracji z pliku odpowiedzi); oba klucze istnieją. Na świeżej instalacji pozycja może więc od razu wykrywać się jako `Applied`. `original` w katalogu = 0 i 0 (tylko fallback).

## Detekcja i undo
- Detekcja: wszystkie wartości z sekcji „Co zmienia” równe zapisanym -> `Applied`; brak wartości lub inna wartość -> `NotApplied`.
- Undo przywraca snapshot sprzed zmiany z journala (brak wartości = jej usunięcie); `original` z katalogu to tylko fallback.
- Oba klucze istniały przed apply i zostają.

## Ryzyka i ostrzeżenia
- `risk=safe`, `suggested=true`.
- Bez ostrzeżeń w katalogu.

## Test VM
Przebieg VM z 2026-11-05 (build 26300.9457) na Windows 11 26H2 po angielsku i po polsku: `DSO-EN-Home` i `DSO-PL-Home` (Core), `DSO-EN-Pro` i `DSO-PL-Pro` (Professional), na każdej **Passed**: przypadek `privacy-advertising-id`. Na każdej VM pełny przebieg: 861 zaliczonych, 0 błędów, 25 pominiętych. Niżej wcześniejsze przebiegi na polskim Windows.

Przypadek `privacy-advertising-id` (`Flow=Full`) w `tests/vm/cases.psd1` (`NotWrittenTo` dla `HKCU` procesu Administratora, bo pozycja jest per użytkownik; `KeyPresentAfterUndo`, bo klucz istnieje na świeżej instalacji). Przebieg na VM 2026-10-21 (`Invoke-VmTests.ps1`): **DSO-26H2-Home i DSO-26H2-Pro – zaliczony**, kroki detect `NotApplied` → apply `Applied` → detect `Applied` → undo `Undone` → detect `NotApplied`, stan po apply potwierdzony niezależnym odczytem, stan końcowy = stan przed (`SnapshotDiff` pusty). Testowa wartość `Id` (String `dso-test`) istniała po apply i po undo bez zmian (snapshot przed = po), `Enabled` i `Value` wróciły do 1 ze snapshotu, oba klucze zostały (`KeyPresentAfterUndo`). Osobne sprawdzenie stanu przełącznika (2026-10-21, Home i Pro, CLI `--apply`/`--undo` + UI Automation strony `ms-settings:privacy-general`, `AutomationId` `SystemSettings_Privacy_AdvertisingIdEnabled_ToggleSwitch`, Ustawienia zamykane przed każdym odczytem): przed apply (1/1, `Id`=`dso-test`) przełącznik `On`, po apply (0/0) `Off`, po undo (1/1) `On`; detekcja po apply `Applied`, `user.isCertain`=true. Na Pro Windows sam zastąpił testowe `Id` nowym identyfikatorem (32 znaki hex) między odczytem przed apply (z otwartymi Ustawieniami) a odczytem po apply; DSOptimizer `Id` nie zapisuje, a w przebiegu `Invoke-VmTests` (bez otwierania Ustawień) `Id` na obu VM zostało bez zmian. Surowy wynik: poza gitem. Osobny pomiar na VM z 2026-10-21 przeszedł na Home i Pro.

## Obserwacja skutku
- **Home** (`DSO-26H2-Home`), 2026-10-21. Metoda: przełącznik identyfikatora reklamowego w Ustawieniach (UI Automation) po zapisie przez API. Wynik: domyślnie już wyłączony (0/0, bez `Id`); zapis 1/1 włącza przełącznik, zapis 0/0 wyłącza. dowód: stan przełącznika w Ustawieniach, treści nie obserwowano.
- **Pro** (`DSO-26H2-Pro`), 2026-10-21. Ta sama metoda i ten sam wynik.
