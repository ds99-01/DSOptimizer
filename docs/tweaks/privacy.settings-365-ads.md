# privacy.settings-365-ads – Karty konta Microsoft na stronie głównej Ustawień (DisableConsumerAccountStateContent)

## Werdykt 26H2
nie wchodzi: brak obserwowanego skutku na Home i Pro. Karty konta Microsoft na stronie głównej Ustawień zostały po zapisie polityki i po restarcie VM; nie sprawdzono edycji Enterprise i Education, dla których Microsoft podaje tę politykę, ani karty „Back up your PC to Microsoft account” po restarcie VM (w tym przebiegu karty nie było w żadnej fazie).

## Co zmienia
Pozycji nie ma w katalogu, nic nie zmienia. Rozważany wpis:
- `HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent`, `DisableConsumerAccountStateContent`, DWord, `1`.

## Źródła
- Test na VM 26H2 Home i Pro, 2026-11-07 (build 26300.9457, nieaktywowany Windows, konto lokalne): zapis przez API rejestru (.NET) i obserwacja strony głównej Ustawień (UI Automation, zrzuty), także z restartem VM.
- Microsoft Learn, Policy CSP Experience: https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-experience (ms.date 2026-10-14, dostęp 2026-11-07): polityka podana tylko dla edycji Enterprise i Education.
- Win11Debloat: https://github.com/Raphire/Win11Debloat (gałąź master), pliki `Config/Features.json`, linie 744–748, i `Regfiles/Disable_Settings_365_Ads.reg`, linie 2–3 (plik w UTF-16; numery linii po konwersji do UTF-8, bez pustych linii); dostęp 2026-11-06.
- RemoveWindowsAI: https://github.com/zoicware/RemoveWindowsAI (gałąź main), plik `RemoveWindowsAi.ps1`, linia 1125; dostęp 2026-11-06.

## Wartości domyślne na świeżym 26H2
Odczyt z VM `DSO-EN-Home` i `DSO-EN-Pro`, 2026-11-07: klucza `HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent` i wartości `DisableConsumerAccountStateContent` **brak**.

## Detekcja i undo
Nie dotyczy (pozycja nie jest w katalogu).

## Ryzyka i ostrzeżenia
Nie dotyczy. Pozycja nie weszła do katalogu.

## Test VM
Nie dotyczy (nie wchodzi): pozycji nie ma w katalogu, więc nie ma przypadku VM.

## Obserwacja skutku
- **Home** (`DSO-EN-Home`), 2026-11-07. Metoda: zapis polityki przez API rejestru, strona główna Ustawień (`ms-settings:` w świeżym oknie) przed zapisem, po zapisie i po cofnięciu (20:21–20:22, z siecią), potem powtórka z restartem VM po zapisie i po cofnięciu (21:32–21:35, bez sieci). Wynik: **brak skutku**. Karty „Back up your PC to Microsoft account” i „It’s all here with Microsoft account” są przed zapisem, po zapisie i po cofnięciu. W powtórce z restartem karta „It’s all here with Microsoft account” jest we wszystkich fazach, a karty „Back up your PC to Microsoft account” nie było w żadnej fazie, więc dla niej wynik po restarcie jest nieodczytany.
- **Pro** (`DSO-EN-Pro`), 2026-11-07. Ta sama metoda i ten sam wynik.
