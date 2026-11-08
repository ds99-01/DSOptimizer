# privacy.explorer-sync-provider-notifications – Wyłącz powiadomienia dostawcy synchronizacji

## Werdykt 26H2
Portować: pole „Pokaż powiadomienia dostawcy synchronizacji” w Opcjach Eksploratora plików przechodzi zapisem jednej wartości na odznaczone; test na VM 26H2 (Home i Pro) potwierdził stan pola. Samych powiadomień w Eksploratorze nie obserwowano.

## Co zmienia
- `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced` (w silniku `HKU\<SID zalogowanego>`), wartość `ShowSyncProviderNotifications`, **DWord**, ustawiana na `0`; `scope=user`.
- Pole w Opcjach Eksploratora plików jest odznaczone od razu, także przed restartem Eksploratora.

## Źródła
- Test na VM 26H2 Home i Pro, 2026-11-07 (build 26300.9457, nieaktywowany Windows, konto lokalne): zapis przez API rejestru (.NET) i obserwacja w Opcjach Eksploratora plików (komunikaty okna i MSAA, zrzuty).
- Win11Debloat: https://github.com/Raphire/Win11Debloat (gałąź master), plik `Regfiles/Disable_Windows_Suggestions.reg`, linie 28–30 (sekcja „Sync provider ads”); dostęp 2026-11-06.
- privacy.sexy: https://github.com/undergroundwires/privacy.sexy (gałąź master), plik `src/application/collections/windows.yaml`, linia 30489; dostęp 2026-11-06.
- Brak dokumentacji MS dla tej wartości, dowód: test na VM (obserwacja niżej).

## Wartości domyślne na świeżym 26H2
Odczyt z VM `DSO-EN-Home` i `DSO-EN-Pro`, 2026-11-07: klucz `Explorer\Advanced` istnieje, `ShowSyncProviderNotifications` **brak** (pole zaznaczone).

## Detekcja i undo
- Detekcja: `ShowSyncProviderNotifications` = DWORD 0 -> `Applied`; brak wartości lub inna wartość -> `NotApplied`.
- Undo przywraca snapshot sprzed zmiany z journala (brak wartości = jej usunięcie); `original` z katalogu (`<RemoveEntry>`) to tylko fallback.
- Klucz `Explorer\Advanced` istniał przed apply i zostaje (`KeyPresentAfterUndo`).

## Ryzyka i ostrzeżenia
- `risk=safe`, `suggested=true`: zmienia jedno pole Opcji Eksploratora plików, utraty funkcji nie zaobserwowano.
- Bez ostrzeżeń w katalogu.

## Test VM
Przypadek `privacy-explorer-sync-provider-notifications` (`Flow=Full`) w `tests/vm/cases.psd1`; przebieg VM z 2026-11-08 (build 26300.9457, `Invoke-VmTests.ps1`): **Passed** na `DSO-EN-Home` (Core), `DSO-EN-Pro` i `DSO-PL-Pro` (Professional). Pełne przebiegi: 915 zaliczonych, 2 błędy, 25 pominiętych na obu VM angielskich oraz 917 zaliczonych, 0 błędów, 25 pominiętych na polskiej. Oba błędy dotyczyły przypadków dwóch pozycji powiadomień (`privacy.toast-suggested`, `privacy.toast-backup-reminder`; opis w ich dokumentacji), nie tej pozycji.

## Obserwacja skutku
- **Home** (`DSO-EN-Home`), 2026-11-07. Metoda: Opcje Eksploratora plików, zakładka Widok, stan pola „Show sync provider notifications” („Pokaż powiadomienia dostawcy synchronizacji” w polskim Windows) przed zapisem, po zapisie, po restarcie Eksploratora i po cofnięciu. Wynik: przed zapisem pole zaznaczone (wartości brak); po zapisie odznaczone, tak samo po restarcie Eksploratora; po cofnięciu znowu zaznaczone.
- **Pro** (`DSO-EN-Pro`), 2026-11-07. Ta sama metoda i ten sam wynik.
- Nie sprawdzono: samych powiadomień dostawcy synchronizacji w oknie Eksploratora. Na VM OneDrive nie był używany i takich komunikatów nie było, więc dowodem jest stan pola w Opcjach Eksploratora plików.
