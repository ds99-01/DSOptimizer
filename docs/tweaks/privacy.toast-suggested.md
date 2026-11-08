# privacy.toast-suggested – Wyłącz systemowe powiadomienia z sugestiami

## Werdykt 26H2
Portować: zapis jednej wartości dodaje do listy nadawców powiadomień (System > Powiadomienia) systemowego nadawcę z sugestiami („Suggested” w angielskim Windows) z wyłączonym przełącznikiem; test na VM 26H2 (Home i Pro) potwierdził stan przełącznika. dowód: stan przełącznika w Ustawieniach, treści nie obserwowano.

## Co zmienia
- `HKCU\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\Windows.SystemToast.Suggested` (w silniku `HKU\<SID zalogowanego>`), wartość `Enabled`, **DWord**, ustawiana na `0`; `scope=user`.
- Skutek widoczny w świeżo otwartym oknie Ustawień, bez restartu Eksploratora i bez ponownego logowania.

## Źródła
- Test na VM 26H2 Home i Pro, 2026-11-07 (build 26300.9457, nieaktywowany Windows, konto lokalne): zapis przez API rejestru (.NET) i obserwacja listy nadawców powiadomień w Ustawieniach (UI Automation, zrzuty).
- Win11Debloat: https://github.com/Raphire/Win11Debloat (gałąź master), plik `Regfiles/Disable_Windows_Suggestions.reg`, linie 34–36; dostęp 2026-11-06.
- Brak dokumentacji MS dla tej wartości, dowód: test na VM (obserwacja niżej).

## Wartości domyślne na świeżym 26H2
Odczyt z VM `DSO-EN-Home` i `DSO-EN-Pro`, 2026-11-07: klucza `Windows.SystemToast.Suggested` i wartości `Enabled` **brak** (nadawcy nie ma na liście).

## Detekcja i undo
- Detekcja: `Enabled` = DWORD 0 -> `Applied`; brak wartości lub inna wartość -> `NotApplied`.
- Undo przywraca snapshot sprzed zmiany z journala: poprzednią wartość, a gdy jej nie było – usuwa ją razem z kluczem `Windows.SystemToast.Suggested`, jeśli utworzyło go apply (`KeyAbsentAfterUndo`); `original` z katalogu (`<RemoveEntry>`) to tylko fallback.
- Klucza `Notifications\Settings` na świeżej VM zaraz po zalogowaniu nie było; Windows utworzył go po kilku minutach (w teście na VM razem z pierwszym powiadomieniem systemowym). Gdy istniał przed apply, zostaje po cofnięciu (`KeyPresentAfterUndo`); gdy apply go utworzył i jest pusty, cofnięcie usuwa go razem z kluczem nadawcy.
- Jeśli apply utworzył klucz `Notifications\Settings`, a w chwili cofnięcia są w nim inne wpisy (np. zapisane przez Windows), cofnięcie usuwa wartość i pusty klucz nadawcy, a klucz `Notifications\Settings` zostaje; cofnięcie się udaje, a program zgłasza ostrzeżenie „Klucz … pozostał po cofnięciu (nie jest pusty).”.

## Ryzyka i ostrzeżenia
- `risk=safe`, `suggested=true`: wyłącza jedną kategorię powiadomień, utraty funkcji nie zaobserwowano.
- Bez ostrzeżeń w katalogu.
- Polskiego brzmienia nazwy tego nadawcy nie odczytano (nadawca pojawia się dopiero po zapisie, a na polskiej VM wartości nie zapisywano), dlatego teksty programu po polsku opisują go bez nazwy.

## Test VM
Przypadek `privacy-toast-suggested` (`Flow=Full`) w `tests/vm/cases.psd1`; przebieg VM z 2026-11-08 (build 26300.9457, `Invoke-VmTests.ps1`): **Passed** na `DSO-PL-Pro` (Professional) w pełnym przebiegu (917 zaliczonych, 0 błędów, 25 pominiętych). Na `DSO-EN-Home` (Core) i `DSO-EN-Pro` (Professional) pierwszy pełny przebieg (915 zaliczonych, 2 błędy, 25 pominiętych) dał dla tego przypadku błąd po cofnięciu zmiany. Przyczyną było założenie testu, nie działanie pozycji: test zakładał, że klucz `Notifications\Settings` istnieje przed zmianą, a na świeżej VM zaraz po zalogowaniu go nie było (Windows utworzył go po kilku minutach). Przypadek poprawiono tak, by sam zapewniał ten klucz przed zmianą; powtórzony na `DSO-EN-Home` (oba przypadki powiadomień) przeszedł: **Passed**, 0 błędów. Na `DSO-EN-Pro` poprawionego przypadku osobno nie powtarzano.

## Obserwacja skutku
- **Home** (`DSO-EN-Home`), 2026-11-07. Metoda: strona `ms-settings:notifications` w świeżym oknie Ustawień (UI Automation, zrzuty), lista nadawców, przełącznik `Windows.SystemToast.Suggested2_ToggleSwitch`, przed zapisem, po zapisie i po cofnięciu. Wynik: przed zapisem nadawcy „Suggested” na liście brak; po zapisie jest, przełącznik wyłączony; po cofnięciu znowu go brak. dowód: stan przełącznika w Ustawieniach, treści nie obserwowano.
- **Pro** (`DSO-EN-Pro`), 2026-11-07. Ta sama metoda i ten sam wynik.
- Nie sprawdzono: samego powiadomienia „Suggested”. Na VM nie pojawiło się w żadnej fazie, więc nie porównano „jest/nie ma powiadomienia”; skutek jest pośredni (stan nadawcy na liście).
