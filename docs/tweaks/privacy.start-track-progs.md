# privacy.start-track-progs – Wyłącz poprawianie wyników Start i wyszukiwania

## Werdykt 26H2
Portować: przełącznik „Popraw wyniki uruchamiania i wyszukiwania” (strona `ms-settings:privacy-general`) zmienia się zapisem jednej wartości; test na VM 26H2 (Home i Pro) potwierdził stan przełącznika, a pole „Pokaż najczęściej używane aplikacje” (Personalizacja > Start) robi się nieaktywne. dowód: stan przełącznika w Ustawieniach, treści nie obserwowano.

## Co zmienia
- `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced` (w silniku `HKU\<SID zalogowanego>`), wartość `Start_TrackProgs`, **DWord**, ustawiana na `0`; `scope=user`.
- Mechanizm przełącznika `SystemSettings_Privacy_StoreAppUsage_ToggleSwitch` z różnicy rejestru na VM: wyłączenie zapisuje `Start_TrackProgs` = 0, włączenie zapisuje 1. Na czystej VM wartości nie ma, a przełącznik jest włączony.
- Przełącznik w świeżym oknie Ustawień jest wyłączony od razu, bez restartu.
- Skutek uboczny: pole „Pokaż najczęściej używane aplikacje” w Personalizacja > Start jest po zapisie nieaktywne, więc listy najczęściej używanych aplikacji nie da się włączyć.

## Źródła
- Test na VM 26H2 Home i Pro, 2026-11-07 (build 26300.9457, nieaktywowany Windows, konto lokalne, bez sieci): zapis przez API rejestru (.NET), różnica rejestru po przełączeniu przełącznika w obie strony i obserwacja w Ustawieniach (UI Automation, zrzuty).
- Win11Debloat: https://github.com/Raphire/Win11Debloat (gałąź master), plik `Regfiles/Disable_Telemetry.reg`, linie 25–27; dostęp 2026-11-06.
- WinUtil: https://github.com/ChrisTitusTech/winutil (gałąź main), plik `config/tweaks.json`, linia 543; dostęp 2026-11-06.
- privacy.sexy: https://github.com/undergroundwires/privacy.sexy (gałąź master), plik `src/application/collections/windows.yaml`, linia 7792; dostęp 2026-11-06.
- Brak dokumentacji MS dla tej wartości, dowód: test na VM (obserwacja niżej).

## Wartości domyślne na świeżym 26H2
Odczyt z VM `DSO-EN-Home` i `DSO-EN-Pro`, 2026-11-07: klucz `Explorer\Advanced` istnieje, `Start_TrackProgs` **brak** (przełącznik włączony; pole „Pokaż najczęściej używane aplikacje” wyłączone, ale aktywne).

## Detekcja i undo
- Detekcja: `Start_TrackProgs` = DWORD 0 -> `Applied`; brak wartości lub inna wartość (także 1 zapisane przez przełącznik) -> `NotApplied`.
- Undo przywraca snapshot sprzed zmiany z journala (brak wartości = jej usunięcie, 1 = przełącznik włączony); `original` z katalogu (`<RemoveEntry>`) to tylko fallback.
- Klucz `Explorer\Advanced` istniał przed apply i zostaje (`KeyPresentAfterUndo`).

## Ryzyka i ostrzeżenia
- `risk=caution`, `suggested=false`: pozycja nie jest sugerowana, bo wyłącza funkcję „Pokaż najczęściej używane aplikacje”; kto chce wyłączyć poprawianie wyników, zaznacza ją sam. Po zapisie pole „Pokaż najczęściej używane aplikacje” jest nieaktywne (w odczycie UI Automation `Enabled` = false na Home i Pro), czyli lista najczęściej używanych aplikacji w Start przestaje być dostępna; po cofnięciu pole znowu jest aktywne.
- Ostrzeżenie w katalogu: „Pole „Pokaż najczęściej używane aplikacje” w ustawieniach menu Start będzie nieaktywne – tej listy nie da się wtedy włączyć.”

## Test VM
Przypadek `privacy-start-track-progs` (`Flow=Full`) w `tests/vm/cases.psd1`; przebieg VM z 2026-11-08 (build 26300.9457, `Invoke-VmTests.ps1`): **Passed** na `DSO-EN-Home` (Core), `DSO-EN-Pro` i `DSO-PL-Pro` (Professional). Pełne przebiegi: 915 zaliczonych, 2 błędy, 25 pominiętych na obu VM angielskich oraz 917 zaliczonych, 0 błędów, 25 pominiętych na polskiej. Oba błędy dotyczyły przypadków dwóch pozycji powiadomień (`privacy.toast-suggested`, `privacy.toast-backup-reminder`; opis w ich dokumentacji), nie tej pozycji.

## Obserwacja skutku
- **Home** (`DSO-EN-Home`), 2026-11-07, 20:24–20:26. Metoda: strony `ms-settings:privacy-general` i `ms-settings:personalization-start` w świeżym oknie Ustawień (UI Automation, zrzuty), przełącznik „Improve Start and search results” („Popraw wyniki uruchamiania i wyszukiwania” w polskim Windows) i pole „Show most used apps” („Pokaż najczęściej używane aplikacje”) przed zapisem, po zapisie i po cofnięciu. Wynik: przed zapisem przełącznik włączony (wartości brak), „Show most used apps” wyłączone i aktywne; po zapisie przełącznik wyłączony, „Show most used apps” wyłączone i nieaktywne; po cofnięciu przełącznik włączony, „Show most used apps” wyłączone i aktywne. dowód: stan przełącznika w Ustawieniach, treści nie obserwowano.
- **Pro** (`DSO-EN-Pro`), 2026-11-07, 20:24–20:26. Ta sama metoda i ten sam wynik.
- Cofnięcie w teście przywróciło wartość 1, bo przed zapisem pozycji przełącznik był już raz przełączany w obie strony (odczyt mechanizmu); stan „brak wartości = przełącznik włączony” widać w odczycie sprzed przełączania.
- Nie sprawdzono: samych wyników w menu Start i w wyszukiwaniu ani listy najczęściej używanych aplikacji z włączonym polem.
