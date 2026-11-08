# privacy.edge-diagnostic-data – Wyłącz dane diagnostyczne i opinie w Edge

## Werdykt 26H2
Portować jako pozycję **niesugerowaną**: trzy polityki Edge; test na VM 26H2 (Home i Pro) pokazał skutek w ustawieniach Edge: ustawienie wymaganych danych diagnostycznych wyłączone i zablokowane (`DiagnosticData`) oraz ustawienie opinii zablokowane (`UserFeedbackAllowed`, osobna obserwacja), oba z informacją o zarządzaniu przez organizację. `PersonalizationReportingEnabled` nie miało osobnej obserwacji, jego działanie opisuje strona Microsoftu. Niesugerowana, bo `DiagnosticData` = 0 wyłącza też wymagane dane diagnostyczne Edge, czego Microsoft nie zaleca, a Edge pokazuje ustawienia jako zarządzane przez organizację.

## Co zmienia
- `HKLM\SOFTWARE\Policies\Microsoft\Edge`, trzy wartości **DWord** ustawiane na `0`: `DiagnosticData`, `PersonalizationReportingEnabled`, `UserFeedbackAllowed`; `scope=machine`.
- `UserFeedbackAllowed` to wpis opcjonalny ze źródeł; wchodzi, bo osobna obserwacja pokazała skutek.
- Na świeżym 26H2 nie ma klucza `Edge` (klucz `HKLM\SOFTWARE\Policies\Microsoft` istnieje). Apply tworzy klucz, undo usuwa wartości. Klucz `Edge` znika przy cofnięciu tylko wtedy, gdy powstał przy zmianie i jest pusty; gdy są w nim wartości innej pozycji Edge, zostaje (program zgłasza to ostrzeżeniem).
- Bez restartu Windows. Edge w teście był uruchamiany od nowa w każdej fazie; strony Microsoftu polityk `DiagnosticData` i `UserFeedbackAllowed` podają „Requires browser restart”, więc otwarte okno Edge może nie pokazać zmiany do ponownego uruchomienia przeglądarki.

## Źródła
- Test na VM 26H2 Home i Pro, 2026-11-07 (build 26300.9457, nieaktywowany Windows, konto lokalne, z siecią): zapis przez API rejestru (.NET) i obserwacja w Edge (UI Automation, zrzuty) oraz odczyt tabeli `edge://policy`.
- Strony polityk Microsoft Edge (MS Learn, dostęp 2026-11-07; wszystkie: ścieżka `SOFTWARE\Policies\Microsoft\Edge`, `REG_DWORD`, bez oznaczenia jako przestarzałe, bez ograniczenia do komputerów w domenie albo w MDM):
  - indeks polityk: https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies (ms.date 2026-11-04);
  - `DiagnosticData`: https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/diagnosticdata (ms.date 2026-05-20): typ Integer, „Off (0) = Off (Not recommended)”, „'Off' turns off required and optional diagnostic data collection. This option isn't recommended.”, „Requires browser restart”;
  - `PersonalizationReportingEnabled`: https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/personalizationreportingenabled (ms.date 2026-05-20): tytuł (dostęp 2026-11-08) „Allow personalization of ads, Microsoft Edge, search, news and other Microsoft services by sending browsing history, favorites and collections, usage and other browsing data to Microsoft”, „If you disable this policy, users can't change or override the setting.”, „This setting isn't available for child accounts or enterprise accounts.”;
  - `UserFeedbackAllowed`: https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/userfeedbackallowed (ms.date 2026-09-11): „If you disable this policy, users can't invoke Edge Feedback.”, „Requires browser restart”.
- Win11Debloat: https://github.com/Raphire/Win11Debloat (gałąź master), pliki `Regfiles/Disable_Telemetry.reg` linie 35–39 i `Regfiles/Disable_Edge_Ads_And_Suggestions.reg` linie 2–16 (`UserFeedbackAllowed`); dostęp 2026-11-06.
- privacy.sexy: https://github.com/undergroundwires/privacy.sexy (gałąź master), plik `src/application/collections/windows.yaml`, linie 9301–9330 i 10280–10317; dostęp 2026-11-06.

## Wartości domyślne na świeżym 26H2
Odczyt z VM `DSO-EN-Home` i `DSO-EN-Pro`, 2026-11-07: klucz `HKLM\SOFTWARE\Policies\Microsoft` istnieje, klucza `HKLM\SOFTWARE\Policies\Microsoft\Edge` i wszystkich trzech wartości **brak** (w ustawieniach Edge „Required diagnostic data” jako tekst bez przełącznika, „Send optional diagnostic data…” wyłączone i nieaktywne, „Feedback” wyłączone i aktywne). Stan „Feedback” odczytano tylko na Home; na Pro strona nie zdążyła się wczytać.

## Detekcja i undo
- Detekcja: wszystkie wartości z sekcji „Co zmienia” równe zapisanym -> `Applied`; brak wartości lub inna wartość -> `NotApplied`.
- Undo przywraca snapshot sprzed zmiany z journala (brak wartości = jej usunięcie) i usuwa klucz `Edge`, jeśli utworzyło go apply i jest pusty (`KeyAbsentAfterUndo`); gdy są w nim wartości innej pozycji Edge, klucz zostaje, a program zgłasza to ostrzeżeniem. `original` z katalogu (`<RemoveEntry>`) to tylko fallback.
- Klucz `HKLM\SOFTWARE\Policies\Microsoft` istniał przed apply i zostaje (`KeyPresentAfterUndo`).
- Klucz `Edge` jest wspólny z innymi pozycjami Edge (np. `privacy.edge-shopping-recommendations`); gdy przed apply istniał z wartościami innej pozycji, undo zostawia klucz i te wartości. Ten sam mechanizm sprawdza przypadek VM `privacy-edge-shared-key` (dla `privacy.edge-newtab-content`).

## Ryzyka i ostrzeżenia
- `risk=caution`, `suggested=false`: `DiagnosticData` = 0 wyłącza także wymagane dane diagnostyczne Edge („'Off' turns off required and optional diagnostic data collection”), a Microsoft tego nie zaleca („Off (Not recommended)”). Wartość `0` zostaje, bo tak zapisują ją źródła i z nią test dał skutek; wartości `1` („RequiredData”) nie sprawdzano.
- Ostrzeżenia w katalogu: „Wyłącza też wymagane dane diagnostyczne Edge, czego Microsoft nie zaleca.” oraz „Edge pokaże przy ustawieniach danych diagnostycznych i opinii informację, że zarządza nimi organizacja, i nie pozwoli ich zmienić.”
- Brzmienia ustawień Edge i informacji o organizacji po polsku nie odczytano (polska VM nie miała zapisu), dlatego teksty programu po polsku opisują je bez cytatu; po angielsku: „Required diagnostic data”, „Send optional diagnostic data to improve Microsoft products”, „Feedback”, „This setting is managed by your organization”.

## Test VM
Przypadek `privacy-edge-diagnostic-data` (`Flow=Full`) w `tests/vm/cases.psd1`; przebieg VM z 2026-11-08 (build 26300.9457, `Invoke-VmTests.ps1`): **Passed** na `DSO-EN-Home` (Core), `DSO-EN-Pro` i `DSO-PL-Pro` (Professional). Pełne przebiegi: 915 zaliczonych, 2 błędy, 25 pominiętych na obu VM angielskich oraz 917 zaliczonych, 0 błędów, 25 pominiętych na polskiej. Oba błędy dotyczyły przypadków dwóch pozycji powiadomień (`privacy.toast-suggested`, `privacy.toast-backup-reminder`; opis w ich dokumentacji), nie tej pozycji.

## Obserwacja skutku
Edge uruchamiany od nowa w każdej fazie z flagą `--no-first-run` (przełącznik Chromium, nie polityka; bez niej ekran powitalny zasłaniał stronę), strony `edge://settings/?search=…` (UI Automation, zrzuty) i tabela `edge://policy`, przed zapisem, po zapisie i po cofnięciu.
- **Home** (`DSO-EN-Home`), 2026-11-07.
  - `DiagnosticData` i `PersonalizationReportingEnabled` zapisane razem (20:59–21:02; strona `edge://settings/?search=diagnostic`; Edge 157.0.4341.42). Wynik: przed zapisem „Required diagnostic data” jako tekst bez przełącznika, „Send optional diagnostic data…” wyłączone i nieaktywne; po zapisie pojawia się przełącznik „Required diagnostic data” wyłączony, nieaktywny, z „This setting is managed by your organization”, a przy „Send optional diagnostic data…” też ten napis; po cofnięciu jak przed zapisem. W `edge://policy`: `DiagnosticData` `0`, `PersonalizationReportingEnabled` `false`; obie Platform, Device, Mandatory, status OK.
  - `UserFeedbackAllowed` osobno (21:02–21:04; strona `edge://settings/?search=feedback`; Edge 157.0.4341.42). Wynik: przed zapisem „Feedback” wyłączone i aktywne; po zapisie wyłączone, nieaktywne, z „This setting is managed by your organization.”; po cofnięciu wyłączone i aktywne. Ustawienie jest wyłączone już na czystej VM, więc skutkiem jest jego zablokowanie. W `edge://policy`: `false`, Platform, Device, Mandatory, status OK.
- **Pro** (`DSO-EN-Pro`), 2026-11-07.
  - `DiagnosticData` i `PersonalizationReportingEnabled` (20:59–21:02; Edge 152.0.4191.66 przed zapisem, 157.0.4341.42 po zapisie i po cofnięciu). Ten sam wynik co na Home; `edge://policy` jak na Home.
  - `UserFeedbackAllowed` (21:02–21:04; Edge 157.0.4341.42). Po zapisie i po cofnięciu ten sam wynik co na Home; fazy przed zapisem na Pro nie odczytano (strona nie zdążyła się wczytać). `edge://policy` jak na Home.
- Skutek pokazały ustawienie danych diagnostycznych (`DiagnosticData`) i ustawienie opinii (`UserFeedbackAllowed`). `PersonalizationReportingEnabled` nie miało osobnej obserwacji: wyszukiwanie „personalization” zwracało we wszystkich fazach tylko „Manage your history, privacy and personalization settings.”, a ustawienie „Allow Microsoft to save your browsing activity… to personalize…” było wyłączone już na czystej VM i się nie zmieniło (status OK w `edge://policy`, działanie według strony Microsoftu).
- Pierwsze podejście (20:40–20:44, strona `edge://settings/privacy`) nie pokazało tych ustawień, bo w Edge 157 ta strona jest tylko spisem podstron.
- Nie sprawdzono: samego wysyłania danych diagnostycznych z Edge (ruch sieciowy) ani wartości `1` polityki `DiagnosticData`.
