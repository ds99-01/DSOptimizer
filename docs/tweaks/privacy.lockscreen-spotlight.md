# privacy.lockscreen-spotlight – Wyłącz Windows Spotlight na ekranie blokady

## Werdykt 26H2
Portować jako pozycję **niesugerowaną** (`risk=caution`): zapis jednej wartości przełącza listę „Personalizuj ekran blokady” z „W centrum uwagi Windows” na zwykły obraz; test na VM 26H2 (Home i Pro) potwierdził stan listy, a cofnięcie przywróciło „W centrum uwagi Windows”. Samego obrazu ekranu blokady nie obserwowano.

## Co zmienia
- `HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager` (w silniku `HKU\<SID zalogowanego>`), wartość `SubscribedContent-338387Enabled`, **DWord**, ustawiana na `0`; `scope=user`.
- Skutek widoczny w świeżo otwartym oknie Ustawień, bez restartu Eksploratora i bez ponownego logowania.
- Druga wartość z tych samych źródeł, `RotatingLockScreenOverlayEnabled` = 0, nie wchodzi: zapisana sama nie zmieniła niczego na Home ani na Pro (lista przed zapisem, po zapisie i po cofnięciu = Windows spotlight). Na czystej VM ma wartość 1.

## Źródła
- Test na VM 26H2 Home i Pro, 2026-11-07 (build 26300.9457, nieaktywowany Windows, konto lokalne): zapis przez API rejestru (.NET) i obserwacja strony ekranu blokady w Ustawieniach (UI Automation, zrzuty).
- Win11Debloat: https://github.com/Raphire/Win11Debloat (gałąź master), plik `Config/Features.json`, linie 487–492 (`DisableLockscreenTips`), oraz `Regfiles/Disable_Lockscreen_Tips.reg`, linie 2–5; dostęp 2026-11-06.
- Brak dokumentacji MS dla tej wartości, dowód: test na VM (obserwacja niżej).

## Wartości domyślne na świeżym 26H2
Odczyt z VM `DSO-EN-Home` i `DSO-EN-Pro`, 2026-11-07: klucz `ContentDeliveryManager` istnieje, `SubscribedContent-338387Enabled` **brak** (lista = Windows spotlight); `RotatingLockScreenOverlayEnabled` = 1 (tej wartości pozycja nie zmienia).

## Detekcja i undo
- Detekcja: `SubscribedContent-338387Enabled` = DWORD 0 -> `Applied`; brak wartości lub inna wartość -> `NotApplied`.
- Undo przywraca snapshot sprzed zmiany z journala (brak wartości = jej usunięcie); `original` z katalogu (`<RemoveEntry>`) to tylko fallback. Po cofnięciu lista wróciła do „W centrum uwagi Windows” (Windows spotlight).
- Klucz `ContentDeliveryManager` istniał przed apply i zostaje (`KeyPresentAfterUndo`).

## Ryzyka i ostrzeżenia
- `risk=caution`, `suggested=false`: ekran blokady traci Windows Spotlight (lista przechodzi na zwykły obraz).
- Ostrzeżenie w katalogu: „Wyłącza Spotlight na ekranie blokady – zamiast zdjęć dnia będzie zwykły obraz.”
- Związek z `privacy.spotlight` (tło pulpitu): polityka tamtej pozycji (`DisableWindowsSpotlightFeatures` = 1) nie zmieniła listy „Personalizuj ekran blokady” na Home ani na Pro, także po restarcie VM. Obie pozycje zapisują różne wartości i działają na różne miejsca: ta na ekran blokady, tamta na tło pulpitu.
- Pole „Get fun facts, tips, tricks, and more on your lock screen” pojawia się po zapisie jako wyłączone; jego brzmienia po polsku nie odczytano.

## Test VM
Przypadek `privacy-lockscreen-spotlight` (`Flow=Full`) w `tests/vm/cases.psd1`; przebieg VM z 2026-11-08 (build 26300.9457, `Invoke-VmTests.ps1`): **Passed** na `DSO-EN-Home` (Core), `DSO-EN-Pro` i `DSO-PL-Pro` (Professional). Pełne przebiegi: 915 zaliczonych, 2 błędy, 25 pominiętych na obu VM angielskich oraz 917 zaliczonych, 0 błędów, 25 pominiętych na polskiej. Oba błędy dotyczyły przypadków dwóch pozycji powiadomień (`privacy.toast-suggested`, `privacy.toast-backup-reminder`; opis w ich dokumentacji), nie tej pozycji.

## Obserwacja skutku
- **Home** (`DSO-EN-Home`), 2026-11-07. Metoda: strona `ms-settings:lockscreen` w świeżym oknie Ustawień (UI Automation, zrzuty) przed zapisem samej wartości `SubscribedContent-338387Enabled`, po zapisie i po cofnięciu. Wynik: przed zapisem lista „Personalize your lock screen” („Personalizuj ekran blokady” w polskim Windows) = „Windows spotlight” („W centrum uwagi Windows”), pola „fun facts” brak; po zapisie lista = „Picture”, pojawiają się „Choose a photo”, „Browse photos” i pole „Get fun facts…” wyłączone; po cofnięciu znowu „Windows spotlight”, pola „fun facts” brak. Wcześniejszy przebieg z obiema wartościami naraz dał ten sam wynik.
- **Pro** (`DSO-EN-Pro`), 2026-11-07. Ta sama metoda i ten sam wynik.
- Windows na VM jest nieaktywowany, więc wszystkie pola strony były wyszarzone: to odczyt stanu listy, nie kliknięcie.
- Nie sprawdzono: zmiany samego obrazu ekranu blokady. Podgląd na stronie był taki sam przed zapisem i po zapisie (domyślny obraz Windows), a ekranu blokady nie wywołano, bo zablokowałby sesję obserwacji. Na aktywowanym Windows strona może wyglądać inaczej. Polskiego brzmienia pozycji „Picture” nie odczytano.
