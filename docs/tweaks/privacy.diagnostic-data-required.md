# privacy.diagnostic-data-required – Ogranicz dane diagnostyczne do wymaganych

## Werdykt 26H2
Portować: test na VM 26H2 (Home i Pro) pokazał realny skutek (baner „zarządza organizacja”, wyszarzony przełącznik opcjonalnych danych). Opis mówi „ograniczenie do wymaganych danych diagnostycznych”, nie „wyłączenie telemetrii”.

## Co zmienia
- `HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection`, wartość `AllowTelemetry`, **DWord**, ustawiana na `1`; `scope=machine`.
- Klucz `DataCollection` **istnieje na świeżej instalacji** (wartości `AllowTelemetry` w nim nie ma). Undo usuwa tylko wartość, klucz zostaje (przypadek VM: `KeyPresentAfterUndo`).
- Poziom 1 = dane wymagane. Pozycja nie wyłącza wysyłania danych diagnostycznych.
- `AllowTelemetry=0` **nie wchodzi**: według MS poziom „Security” jest dostępny tylko w Windows Enterprise/Education, a na VM nie ma tych edycji, więc nie ma dowodu (zasada dowodu Etapu 4a). Na Home/Pro MS opisuje 0 jak 1.
- Mechanizm użytkownika (przełącznik „Opcjonalne dane diagnostyczne”: `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection`, `AllowTelemetry` i `MaxTelemetryAllowed`, domyślnie 1/1) nie jest zmieniany; pozycja używa polityki.
- Polityka blokuje przełącznik opcjonalnych danych w Ustawieniach (nie wchodzą polityki `ConfigureTelemetryOptInSettingsUx` ani `DisableDiagnosticDataViewer`).

## Źródła
- Dokumentacja MS: strona Diagnostic Data (poziomy danych), polityka `System/AllowTelemetry` (Windows 10 1507+; Pro, Enterprise, Education), „Manage connections from Windows operating system components to Microsoft services” (rejestr: `HKLM\Software\Policies\Microsoft\Windows\DataCollection\AllowTelemetry`).
- Test na VM 26H2 Home i Pro, 2026-10-21 (build 26300.9457, nieaktywowany Windows, konto lokalne): zapis przez API rejestru (.NET `RegistryKey`) i obserwacja w Ustawieniach (UI Automation, zrzuty).
- Research: `docs/zrodla/2026-10-20-etap4a-zrodla.md`, sekcje „2a. Allow Diagnostic Data (AllowTelemetry)” i „Wyniki VM 26H2 (partia 1)”.

## Wartości domyślne na świeżym 26H2
Odczyt z VM DSO-26H2-Home i DSO-26H2-Pro, 2026-10-21: `HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection` **istnieje**, `AllowTelemetry` **brak wartości**; przełącznik użytkownika `...\CurrentVersion\Policies\DataCollection` `AllowTelemetry`/`MaxTelemetryAllowed` = 1/1 (opcjonalne dane wyłączone).

## Detekcja i undo
- Detekcja: `AllowTelemetry` = DWORD 1 -> `Applied`; brak wartości lub inna wartość -> `NotApplied`.
- Undo przywraca snapshot sprzed zmiany z journala (brak wartości = usunięcie wartości, klucz `DataCollection` zostaje); `original` = `<RemoveEntry>` to tylko fallback.

## Ryzyka i ostrzeżenia
- `risk=caution`, `suggested=true`.
- Ostrzeżenia w katalogu: „To ograniczenie, a nie wyłączenie – Microsoft nadal otrzymuje dane wymagane.” oraz „W Ustawieniach pojawi się komunikat, że niektórymi ustawieniami zarządza organizacja.”
- Poza uwagami w programie (w szczegółach technicznych albo tylko tutaj): przełącznik opcjonalnych danych w Ustawieniach jest wyszarzony do czasu cofnięcia.
- Brak restartu (żaden restart nie jest wymagany, `requiresReboot=false`); Eksploratora nie trzeba restartować.

## Test VM
Przypadek `privacy-diagnostic-data-required` (`Flow=Full`) w `tests/vm/cases.psd1`: `KeyPresentAfterUndo` dla `HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection` (klucz istnieje na świeżej instalacji, wartość po undo nie istnieje). Przebieg na VM 2026-10-21 (`Invoke-VmTests.ps1`, Task 9 partii): **DSO-26H2-Home i DSO-26H2-Pro – zaliczony**, kroki detect `NotApplied` → apply `Applied` → detect `Applied` → undo `Undone` → detect `NotApplied`, stan po apply potwierdzony niezależnym odczytem, stan końcowy = stan przed (`SnapshotDiff` pusty). Klucz `HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection` istniał przed apply i przetrwał undo (`KeyPresentAfterUndo`), a `AllowTelemetry` po undo znowu nie istnieje. Eksploracja z 2026-10-21 przeszła na Home i Pro.

## Obserwacja skutku
- **Home** (`DSO-26H2-Home`, EditionID `Core`), 2026-10-21. Metoda: UI Automation Ustawień (Prywatność i zabezpieczenia > Diagnostyka i opinie) i zrzuty po zapisie przez API rejestru. Wynik: baner „Niektórymi z tych ustawień zarządza Twoja organizacja” i „Twoja organizacja zezwala na wysyłanie tylko wymaganych danych diagnostycznych”, przełącznik opcjonalnych danych diagnostycznych wyszarzony. Po cofnięciu (wartość usunięta) baner znika, przełącznik znowu aktywny.
- **Pro** (`DSO-26H2-Pro`, EditionID `Professional`), 2026-10-21. Ta sama metoda i ten sam wynik.
