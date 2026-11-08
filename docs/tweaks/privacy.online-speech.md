# privacy.online-speech – Wyłącz rozpoznawanie mowy online

## Werdykt 26H2
Portować jako pozycję **niesugerowaną**, na podstawie dowodu odwrotnego: na czystej VM 26H2 (Home i Pro) przełącznik rozpoznawania mowy online jest już wyłączony, więc zapis `0` niczego nie zmienia na ekranie; test z wartością przeciwną pokazał, że `HasAccepted` steruje przełącznikiem w obie strony (`1` = włączony, `0` = wyłączony). Pozycja ma sens, gdy użytkownik wcześniej włączył rozpoznawanie mowy online. dowód: stan przełącznika w Ustawieniach, treści nie obserwowano.

## Co zmienia
- `HKCU\Software\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy` (w silniku `HKU\<SID zalogowanego>`), wartość `HasAccepted`, **DWord**, ustawiana na `0`; `scope=user`.
- Mechanizm przełącznika z różnicy rejestru na VM: przełączenie z wyłączonego na włączony tworzy klucze `Settings` i `OnlineSpeechPrivacy` i zapisuje `HasAccepted` = 1; przełączenie z włączonego na wyłączony zapisuje `HasAccepted` = 0.
- Na czystej VM nie ma kluczy `Speech_OneCore\Settings` ani `Settings\OnlineSpeechPrivacy` (klucz `Speech_OneCore` istnieje). Apply tworzy oba klucze; undo usuwa wartość i klucz `Settings` razem z `OnlineSpeechPrivacy`, jeśli utworzyło je apply.
- Bez restartu: stan przełącznika widać w świeżym oknie Ustawień.

## Źródła
- Test na VM 26H2 Home i Pro, 2026-11-07 (build 26300.9457, nieaktywowany Windows, konto lokalne, bez sieci): zapis przez API rejestru (.NET), różnica rejestru po przełączeniu przełącznika w obie strony, zapis wartości przeciwnej (`HasAccepted` = 1) i obserwacja w Ustawieniach (UI Automation, zrzuty).
- Win11Debloat: https://github.com/Raphire/Win11Debloat (gałąź master), plik `Regfiles/Disable_Telemetry.reg`, linie 8–10; dostęp 2026-11-06.
- WinUtil: https://github.com/ChrisTitusTech/winutil (gałąź main), plik `config/tweaks.json`, wpis `OnlineSpeechPrivacy` (okolice linii 480–490, numer przybliżony); dostęp 2026-11-06.
- Brak dokumentacji MS dla tej wartości, dowód: test na VM (obserwacja niżej).

## Wartości domyślne na świeżym 26H2
Odczyt z VM `DSO-EN-Home` i `DSO-EN-Pro`, 2026-11-07: klucz `HKCU\Software\Microsoft\Speech_OneCore` istnieje, kluczy `Speech_OneCore\Settings` i `Settings\OnlineSpeechPrivacy` oraz wartości `HasAccepted` **brak** (przełącznik rozpoznawania mowy online wyłączony).

## Detekcja i undo
- Detekcja: `HasAccepted` = DWORD 0 -> `Applied`; brak wartości lub inna wartość (także 1 zapisane przez przełącznik) -> `NotApplied`.
- Na świeżym Windows przełącznik jest wyłączony, ale wartości i kluczy nie ma, więc detekcja daje `NotApplied` (w programie pozycja jest niezastosowana), a zastosowanie zapisze `0`, utworzy oba klucze i niczego nie zmieni na ekranie. `Applied` pojawia się też wtedy, gdy użytkownik sam wyłączył przełącznik po wcześniejszym włączeniu (Windows zapisuje wtedy `HasAccepted` = 0).
- Undo przywraca snapshot sprzed zmiany z journala (brak wartości = jej usunięcie, 1 = przełącznik włączony) i usuwa klucz `Settings` z `OnlineSpeechPrivacy`, jeśli utworzyło je apply (`KeyAbsentAfterUndo`); `original` z katalogu (`<RemoveEntry>`) to tylko fallback.
- Klucz `Speech_OneCore` istniał przed apply i zostaje (`KeyPresentAfterUndo`).

## Ryzyka i ostrzeżenia
- `risk=caution`, `suggested=false`: wyłącza rozpoznawanie mowy online; Windows pisze przy przełączniku wprost, że z aplikacji z tą technologią nie można wtedy korzystać.
- Ostrzeżenie w katalogu: „Nie będzie można korzystać z aplikacji używających rozpoznawania mowy online firmy Microsoft (tak opisuje to Windows przy przełączniku).”
- Podstawą ostrzeżenia jest opis Windows przy przełączniku: po angielsku „If you turn off online speech recognition, you won’t be able to use apps that use Microsoft’s online speech recognition technology. …”, po polsku „Jeśli wyłączysz rozpoznawanie mowy online, nie będzie można korzystać z aplikacji używających technologii rozpoznawania mowy online firmy Microsoft. …”.
- Dyktowanie (Win+H): nie sprawdzono, czy przestaje działać. Ostrzeżenie w katalogu tego nie obiecuje.

## Test VM
Przypadek `privacy-online-speech` (`Flow=Full`) w `tests/vm/cases.psd1`; przebieg VM z 2026-11-08 (build 26300.9457, `Invoke-VmTests.ps1`): **Passed** na `DSO-EN-Home` (Core), `DSO-EN-Pro` i `DSO-PL-Pro` (Professional). Pełne przebiegi: 915 zaliczonych, 2 błędy, 25 pominiętych na obu VM angielskich oraz 917 zaliczonych, 0 błędów, 25 pominiętych na polskiej. Oba błędy dotyczyły przypadków dwóch pozycji powiadomień (`privacy.toast-suggested`, `privacy.toast-backup-reminder`; opis w ich dokumentacji), nie tej pozycji.

## Obserwacja skutku
Strona `ms-settings:privacy-speech` w świeżym oknie Ustawień (UI Automation, zrzuty). Przełącznik ma w UI Automation nazwę równą opisowi pod nim; nagłówek po angielsku: „Online speech recognition” (polskiego nagłówka nie odczytano jako osobnego elementu), opis strony: „Use your voice for apps using Microsoft’s online speech recognition technology.” („Używaj głosu w aplikacjach, korzystając z technologii rozpoznawania mowy online firmy Microsoft.” w polskim Windows).
- **Home** (`DSO-EN-Home`), 2026-11-07. Metoda: przebieg główny (20:26–20:27) z zapisem `0`: przed zapisem, po zapisie i po cofnięciu; potem dowód odwrotny (20:54–20:55): zapis `HasAccepted` = 1 (z wartościami ubocznymi z tej samej różnicy rejestru, potem cofniętymi) i cofnięcie do `HasAccepted` = 0. Wynik: w przebiegu głównym przełącznik wyłączony we wszystkich fazach (przed = po = cofnięte), bo był wyłączony już na czystej VM; w dowodzie odwrotnym zapis `1` włącza przełącznik i pokazuje sekcję „Start contributing my voice clips”, a powrót do `0` wyłącza go z powrotem. dowód: stan przełącznika w Ustawieniach, treści nie obserwowano.
- **Pro** (`DSO-EN-Pro`), 2026-11-07, przebieg główny 20:26–20:28, dowód odwrotny 20:54–20:55. Ta sama metoda i ten sam wynik.
- Wniosek: `HasAccepted` steruje przełącznikiem w obie strony, a `0` = wyłączony.
- Nie sprawdzono: czy dyktowanie (Win+H) przestaje działać. Okno „Windows Input Experience” otworzyło się tylko w fazie przed zapisem i nie zależało od wartości, która się nie zmieniła. Nie sprawdzono też samych aplikacji korzystających z rozpoznawania mowy online.
