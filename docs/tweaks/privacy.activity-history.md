# privacy.activity-history – Historia aktywności (EnableActivityFeed, PublishUserActivities, UploadUserActivities)

## Werdykt 26H2
nie wchodzi: nieobserwowalne na VM (brak strony Historii aktywności). W Ustawieniach 26H2 nie ma strony, na której polityki mogłyby coś zmienić, więc nie dowiedziono ani działania, ani braku działania trzech wartości.

## Co zmienia
Pozycji nie ma w katalogu, nic nie zmienia. Rozważane wpisy:
- `HKLM\SOFTWARE\Policies\Microsoft\Windows\System`, `EnableActivityFeed`, DWord, `0`.
- `HKLM\SOFTWARE\Policies\Microsoft\Windows\System`, `PublishUserActivities`, DWord, `0`.
- `HKLM\SOFTWARE\Policies\Microsoft\Windows\System`, `UploadUserActivities`, DWord, `0`.

## Źródła
- Test na VM 26H2 Home i Pro, 2026-11-07 (build 26300.9457, nieaktywowany Windows, konto lokalne, bez sieci): zapis przez API rejestru (.NET) i obserwacja w Ustawieniach (UI Automation, zrzuty), także z restartem VM.
- Microsoft Learn, Policy CSP Privacy: https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-privacy (dostęp 2026-11-07).
- Win11Debloat: https://github.com/Raphire/Win11Debloat (gałąź master), plik `Regfiles/Disable_Telemetry.reg`, linie 28–30 (plik w UTF-16; numery linii po konwersji do UTF-8, bez pustych linii); dostęp 2026-11-06.
- WinUtil: https://github.com/ChrisTitusTech/winutil (gałąź main), plik `config/tweaks.json`, linie 1–28; dostęp 2026-11-06.

## Wartości domyślne na świeżym 26H2
Odczyt z VM `DSO-EN-Home` i `DSO-EN-Pro`, 2026-11-07: klucz `HKLM\SOFTWARE\Policies\Microsoft\Windows\System` istnieje, wszystkich trzech wartości **brak**.

## Detekcja i undo
Nie dotyczy (pozycja nie jest w katalogu).

## Ryzyka i ostrzeżenia
Nie dotyczy. Pozycja nie weszła do katalogu.

## Test VM
Nie dotyczy (nie wchodzi): pozycji nie ma w katalogu, więc nie ma przypadku VM.

## Obserwacja skutku
- **Home** (`DSO-EN-Home`), 2026-11-07. Metoda: zapis trzech polityk przez API rejestru, strona `ms-settings:privacy-activityhistory` i lista „Privacy & security” w świeżym oknie Ustawień przed zapisem, po zapisie i po cofnięciu (20:28–20:29), potem powtórka z restartem VM po zapisie i po cofnięciu (21:04–21:10). Wynik: **nieobserwowalne**. `ms-settings:privacy-activityhistory` otwiera stronę główną Ustawień, a na liście „Privacy & security” nie ma pozycji „Activity history”; tak samo we wszystkich fazach, także po restarcie VM.
- **Pro** (`DSO-EN-Pro`), 2026-11-07. Ta sama metoda i ten sam wynik.
