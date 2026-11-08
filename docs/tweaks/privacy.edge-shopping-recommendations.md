# privacy.edge-shopping-recommendations – Wyłącz zakupy i rekomendacje w Edge

## Werdykt 26H2
Portować jako pozycję **niesugerowaną**: trzy polityki Edge zapisane razem; test na VM 26H2 (Home i Pro) pokazał skutek w ustawieniach Edge (ustawienie zakupów wyłączone i zablokowane, z informacją o zarządzaniu przez organizację). Osobny skutek widać tylko dla `EdgeShoppingAssistantEnabled`; `ShowRecommendationsEnabled` i `SpotlightExperiencesAndRecommendationsEnabled` nie miały osobnej obserwacji, ich działanie opisują strony Microsoftu. Niesugerowana, bo Edge pokazuje ustawienie jako zarządzane przez organizację.

## Co zmienia
- `HKLM\SOFTWARE\Policies\Microsoft\Edge`, trzy wartości **DWord** ustawiane na `0`: `EdgeShoppingAssistantEnabled`, `ShowRecommendationsEnabled`, `SpotlightExperiencesAndRecommendationsEnabled`; `scope=machine`.
- Na świeżym 26H2 nie ma klucza `Edge` (klucz `HKLM\SOFTWARE\Policies\Microsoft` istnieje). Apply tworzy klucz, undo usuwa wartości. Klucz `Edge` znika przy cofnięciu tylko wtedy, gdy powstał przy zmianie i jest pusty; gdy są w nim wartości innej pozycji Edge, zostaje (program zgłasza to ostrzeżeniem).
- Bez restartu Windows. Edge w teście był uruchamiany od nowa w każdej fazie; strona Microsoftu polityki `SpotlightExperiencesAndRecommendationsEnabled` podaje „Requires browser restart”, więc otwarte okno Edge może nie pokazać zmiany do ponownego uruchomienia przeglądarki.

## Źródła
- Test na VM 26H2 Home i Pro, 2026-11-07 (build 26300.9457, nieaktywowany Windows, konto lokalne, z siecią): zapis przez API rejestru (.NET) i obserwacja w Edge (UI Automation, zrzuty) oraz odczyt tabeli `edge://policy`.
- Strony polityk Microsoft Edge (MS Learn, dostęp 2026-11-07; wszystkie: ścieżka `SOFTWARE\Policies\Microsoft\Edge`, `REG_DWORD`, bez oznaczenia jako przestarzałe, bez ograniczenia do komputerów w domenie albo w MDM):
  - indeks polityk: https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies (ms.date 2026-11-04);
  - `EdgeShoppingAssistantEnabled`: https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/edgeshoppingassistantenabled (ms.date 2026-05-20): „If you disable this policy, shopping features such as price comparison, coupons, rebates, and express checkout aren't automatically found for retail domains.”;
  - `ShowRecommendationsEnabled`: https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/showrecommendationsenabled (ms.date 2026-05-20): „If you disable this setting, users won't receive any recommendations or notifications from Microsoft Edge.”;
  - `SpotlightExperiencesAndRecommendationsEnabled`: https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/spotlightexperiencesandrecommendationsenabled (ms.date 2026-05-20): „If you disable this setting, spotlight experiences and recommendations are turned off.”, „Requires browser restart”, tylko Windows.
- Win11Debloat: https://github.com/Raphire/Win11Debloat (gałąź master), pliki `Config/Features.json` linie 540–544 i `Regfiles/Disable_Edge_Ads_And_Suggestions.reg` linie 2–16; dostęp 2026-11-06.
- privacy.sexy: https://github.com/undergroundwires/privacy.sexy (gałąź master), plik `src/application/collections/windows.yaml`, linie 10145–10277; dostęp 2026-11-06.

## Wartości domyślne na świeżym 26H2
Odczyt z VM `DSO-EN-Home` i `DSO-EN-Pro`, 2026-11-07: klucz `HKLM\SOFTWARE\Policies\Microsoft` istnieje, klucza `HKLM\SOFTWARE\Policies\Microsoft\Edge` i wszystkich trzech wartości **brak** (ustawienie zakupów w Edge włączone i aktywne).

## Detekcja i undo
- Detekcja: wszystkie wartości z sekcji „Co zmienia” równe zapisanym -> `Applied`; brak wartości lub inna wartość -> `NotApplied`.
- Undo przywraca snapshot sprzed zmiany z journala (brak wartości = jej usunięcie) i usuwa klucz `Edge`, jeśli utworzyło go apply i jest pusty (`KeyAbsentAfterUndo`); gdy są w nim wartości innej pozycji Edge, klucz zostaje, a program zgłasza to ostrzeżeniem. `original` z katalogu (`<RemoveEntry>`) to tylko fallback.
- Klucz `HKLM\SOFTWARE\Policies\Microsoft` istniał przed apply i zostaje (`KeyPresentAfterUndo`).
- Klucz `Edge` jest wspólny z innymi pozycjami Edge; gdy przed apply istniał z innymi wartościami, undo zostawia klucz i te wartości.

## Ryzyka i ostrzeżenia
- `risk=safe`, `suggested=false`: wyłącza funkcje zakupowe Edge; utraty innej funkcji nie zaobserwowano.
- Ostrzeżenie w katalogu: „Edge pokaże przy ustawieniu zakupów informację, że zarządza nim organizacja, i nie pozwoli go zmienić do czasu cofnięcia.”
- Polityki widać w `edge://policy` jako ustawione dla urządzenia. Brzmienia tej informacji i ustawienia zakupów po polsku nie odczytano (polska VM nie miała zapisu), dlatego teksty programu po polsku opisują je bez cytatu; po angielsku: „This setting is managed by your organization.” przy „Save time and money with Shopping in Microsoft Edge”.
- `ShowRecommendationsEnabled` według strony Microsoftu wyłącza wszystkie rekomendacje i powiadomienia Edge; na VM nie było monitów, banerów ani osobnego ustawienia, więc tego skutku nie obserwowano.

## Test VM
Przypadek `privacy-edge-shopping-recommendations` (`Flow=Full`) w `tests/vm/cases.psd1`; przebieg VM z 2026-11-08 (build 26300.9457, `Invoke-VmTests.ps1`): **Passed** na `DSO-EN-Home` (Core), `DSO-EN-Pro` i `DSO-PL-Pro` (Professional). Pełne przebiegi: 915 zaliczonych, 2 błędy, 25 pominiętych na obu VM angielskich oraz 917 zaliczonych, 0 błędów, 25 pominiętych na polskiej. Oba błędy dotyczyły przypadków dwóch pozycji powiadomień (`privacy.toast-suggested`, `privacy.toast-backup-reminder`; opis w ich dokumentacji), nie tej pozycji.

## Obserwacja skutku
- **Home** (`DSO-EN-Home`), 2026-11-07, 20:56–20:59. Metoda: Edge uruchamiany od nowa w każdej fazie z flagą `--no-first-run` (przełącznik Chromium, nie polityka; bez niej ekran powitalny zasłaniał stronę), strona `edge://settings/?search=shopping` (UI Automation, zrzuty) i tabela `edge://policy`, przed zapisem, po zapisie i po cofnięciu. Edge 152.0.4191.66 przed zapisem, 157.0.4341.42 po zapisie i po cofnięciu (aktualizacja w trakcie, z siecią). Wynik: przed zapisem „Save time and money with Shopping in Microsoft Edge” włączone i aktywne; po zapisie wyłączone, nieaktywne, obok „This setting is managed by your organization.”; po cofnięciu znowu włączone i aktywne. W `edge://policy` każda z trzech polityk: `false`, Platform, Device, Mandatory, status OK.
- **Pro** (`DSO-EN-Pro`), 2026-11-07, 20:56–20:59. Ta sama metoda i ten sam wynik; Edge 152.0.4191.66 we wszystkich fazach.
- Skutek pokazało tylko ustawienie zakupów, czyli `EdgeShoppingAssistantEnabled`. Dla `ShowRecommendationsEnabled` i `SpotlightExperiencesAndRecommendationsEnabled` nie było monitów, banerów ani osobnego ustawienia, więc ich skutku nie obserwowano (status OK w `edge://policy`, działanie według stron Microsoftu).
- Pierwsze podejście (20:32–20:35, strony `edge://settings` i `edge://newtab`) nie pokazało różnic, bo strona główna ustawień w tej wersji Edge nie ma ustawienia zakupów; polityki miały już wtedy status OK.
- Nie sprawdzono: samych podpowiedzi zakupowych na stronie sklepu (porównania cen, kupony).
