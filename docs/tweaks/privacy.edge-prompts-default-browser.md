# privacy.edge-prompts-default-browser – Monity Edge o przeglądarce domyślnej (DefaultBrowserSettingEnabled)

## Werdykt 26H2
nie wchodzi: nieobserwowalne na VM (brak monitu o przeglądarkę domyślną). Monitu nie było w żadnej fazie, także bez polityk, więc nie dowiedziono ani działania, ani braku działania obu polityk. Edge był na VM przeglądarką domyślną i był uruchamiany z flagą `--no-first-run`, która sama może tłumić monity.

## Co zmienia
Pozycji nie ma w katalogu, nic nie zmienia. Rozważane wpisy:
- `HKLM\SOFTWARE\Policies\Microsoft\Edge`, `DefaultBrowserSettingEnabled`, DWord, `0`.
- `HKLM\SOFTWARE\Policies\Microsoft\Edge`, `DefaultBrowserSettingsCampaignEnabled`, DWord, `0`.

## Źródła
- Test na VM 26H2 Home i Pro, 2026-11-07 (build 26300.9457, nieaktywowany Windows, konto lokalne, z siecią): zapis przez API rejestru (.NET), obserwacja w Edge (UI Automation, zrzuty) i odczyt tabeli `edge://policy`.
- Strony polityk Microsoft Edge (MS Learn, ms.date 2026-05-20, dostęp 2026-11-07):
  - `DefaultBrowserSettingEnabled`: https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/defaultbrowsersettingenabled: „This policy only works for PCs running Windows 7.”;
  - `DefaultBrowserSettingsCampaignEnabled`: https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/defaultbrowsersettingscampaignenabled.
- Win11Debloat: https://github.com/Raphire/Win11Debloat (gałąź master), plik `Regfiles/Disable_Edge_Ads_And_Suggestions.reg`, linia 13 (plik w UTF-16; numery linii po konwersji do UTF-8, bez pustych linii); dostęp 2026-11-06.

## Wartości domyślne na świeżym 26H2
Odczyt z VM `DSO-EN-Home` i `DSO-EN-Pro`, 2026-11-07: klucza `HKLM\SOFTWARE\Policies\Microsoft\Edge` i obu wartości **brak**. Edge jest przeglądarką domyślną („Microsoft Edge is your default browser”, przycisk „Make default” nieaktywny).

## Detekcja i undo
Nie dotyczy (pozycja nie jest w katalogu).

## Ryzyka i ostrzeżenia
Nie dotyczy. Pozycja nie weszła do katalogu.

## Test VM
Nie dotyczy (nie wchodzi): pozycji nie ma w katalogu, więc nie ma przypadku VM.

## Obserwacja skutku
- **Home** (`DSO-EN-Home`), 2026-11-07. Metoda: zapis obu polityk przez API rejestru; Edge 157.0.4341.42 uruchamiany od nowa w każdej fazie z flagą `--no-first-run` (przełącznik Chromium, nie polityka; bez niej ekran powitalny zasłaniał stronę); strony `edge://settings/defaultBrowser` i `edge://policy` przed zapisem, po zapisie i po cofnięciu (20:39–20:41). Wynik: **nieobserwowalne**. Monitu o przeglądarkę domyślną nie było w żadnej fazie; Edge był na VM przeglądarką domyślną, a skrypt nie rozstrzyga, czy polityki działają. Obie polityki miały w `edge://policy` status OK.
- **Pro** (`DSO-EN-Pro`), 2026-11-07. Ta sama metoda (20:39–20:41, Edge 157.0.4341.42) i ten sam wynik.
