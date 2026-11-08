# privacy.edge-newtab-content – Ukryj treści MSN i domyślne kafelki na nowej karcie Edge

## Werdykt 26H2
Portować jako pozycję **niesugerowaną**: dwie polityki Edge, każda z osobno obserwowanym skutkiem; test na VM 26H2 (Home i Pro) pokazał, że z nowej karty znika kanał MSN (`NewTabPageContentEnabled`), a z domyślnych kafelków zostaje tylko Microsoft 365 (`NewTabPageHideDefaultTopSites`). Niesugerowana, bo Edge traktuje te ustawienia jako polityki zarządzane przez organizację.

## Co zmienia
- `HKLM\SOFTWARE\Policies\Microsoft\Edge`, dwie wartości **DWord**: `NewTabPageContentEnabled` ustawiana na `0` i `NewTabPageHideDefaultTopSites` ustawiana na `1`; `scope=machine`.
- Na świeżym 26H2 nie ma klucza `Edge` (klucz `HKLM\SOFTWARE\Policies\Microsoft` istnieje). Apply tworzy klucz, undo usuwa wartości. Klucz `Edge` znika przy cofnięciu tylko wtedy, gdy powstał przy zmianie i jest pusty; gdy są w nim wartości innej pozycji Edge, zostaje (program zgłasza to ostrzeżeniem).
- Bez restartu Windows. Edge w teście był uruchamiany od nowa w każdej fazie; czy otwarte już okno Edge pokazuje zmianę od razu, nie sprawdzono.

## Źródła
- Test na VM 26H2 Home i Pro, 2026-11-07 (build 26300.9457, nieaktywowany Windows, konto lokalne, z siecią): zapis przez API rejestru (.NET) i obserwacja strony `edge://newtab` (UI Automation, zrzuty) oraz odczyt tabeli `edge://policy`.
- Strony polityk Microsoft Edge (MS Learn, dostęp 2026-11-07; obie: ścieżka `SOFTWARE\Policies\Microsoft\Edge`, `REG_DWORD`, bez oznaczenia jako przestarzałe, bez ograniczenia do komputerów w domenie albo w MDM):
  - indeks polityk: https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies (ms.date 2026-11-04);
  - `NewTabPageContentEnabled`: https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/newtabpagecontentenabled (ms.date 2026-05-20): „If you disable this policy, Microsoft Edge doesn't display Microsoft content on the new tab page.”; polityka obejmuje „all profile types, namely unsigned local user profiles, profiles signed in using a Microsoft Account”;
  - `NewTabPageHideDefaultTopSites`: https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/newtabpagehidedefaulttopsites (ms.date 2026-05-20): „If you set this policy to true, the default top site tiles are hidden.”.
- Win11Debloat: https://github.com/Raphire/Win11Debloat (gałąź master), pliki `Config/Features.json` linie 540–544 i `Regfiles/Disable_Edge_Ads_And_Suggestions.reg` linie 2–16; dostęp 2026-11-06.

## Wartości domyślne na świeżym 26H2
Odczyt z VM `DSO-EN-Home` i `DSO-EN-Pro`, 2026-11-07: klucz `HKLM\SOFTWARE\Policies\Microsoft` istnieje, klucza `HKLM\SOFTWARE\Policies\Microsoft\Edge` i obu wartości **brak** (na nowej karcie kanał MSN i sześć domyślnych kafelków).

## Detekcja i undo
- Detekcja: wszystkie wartości z sekcji „Co zmienia” równe zapisanym -> `Applied`; brak wartości lub inna wartość -> `NotApplied`.
- Undo przywraca snapshot sprzed zmiany z journala (brak wartości = jej usunięcie) i usuwa klucz `Edge`, jeśli utworzyło go apply i jest pusty (`KeyAbsentAfterUndo`); gdy są w nim wartości innej pozycji Edge, klucz zostaje, a program zgłasza to ostrzeżeniem. `original` z katalogu (`<RemoveEntry>`) to tylko fallback.
- Klucz `HKLM\SOFTWARE\Policies\Microsoft` istniał przed apply i zostaje (`KeyPresentAfterUndo`).
- Klucz `Edge` jest wspólny z innymi pozycjami Edge (np. `privacy.edge-shopping-recommendations`); gdy przed apply istniał z wartościami innej pozycji, undo zostawia klucz i te wartości. Pilnuje tego osobny przypadek VM `privacy-edge-shared-key`.

## Ryzyka i ostrzeżenia
- `risk=safe`, `suggested=false`: znika treść MSN i domyślne kafelki; utraty funkcji nie zaobserwowano.
- Ostrzeżenie w katalogu: „Edge będzie traktować te ustawienia nowej karty jako narzucone przez organizację, aż cofniesz tę pozycję.”
- Podstawą ostrzeżenia jest tabela `edge://policy`: obie polityki jako Platform, Device, Mandatory, status OK. Informacji o zarządzaniu przez organizację przy ustawieniach nowej karty nie odczytano (brzmienia ustawień nowej karty nie odczytano ani po angielsku, ani po polsku); przy innych politykach Edge taka informacja pojawia się obok zablokowanego ustawienia (`privacy.edge-shopping-recommendations`).
- Z kafelków zostaje Microsoft 365. Kafelków dodanych przez użytkownika nie sprawdzano.

## Test VM
Przypadek `privacy-edge-newtab-content` (`Flow=Full`) w `tests/vm/cases.psd1`; przebieg VM z 2026-11-08 (build 26300.9457, `Invoke-VmTests.ps1`): oba przypadki **Passed** na `DSO-EN-Home` (Core), `DSO-EN-Pro` i `DSO-PL-Pro` (Professional). Pełne przebiegi: 915 zaliczonych, 2 błędy, 25 pominiętych na obu VM angielskich oraz 917 zaliczonych, 0 błędów, 25 pominiętych na polskiej. Oba błędy dotyczyły przypadków dwóch pozycji powiadomień (`privacy.toast-suggested`, `privacy.toast-backup-reminder`; opis w ich dokumentacji), nie tej pozycji. Przypadek `privacy-edge-shared-key` (`Flow=Full`) sprawdza, że cofnięcie tej pozycji zostawia klucz `Edge` i wartość `EdgeShoppingAssistantEnabled` zapisaną wcześniej (`KeyPresentAfterUndo`, `SnapshotDiff` pusty).

## Obserwacja skutku
Edge uruchamiany od nowa w każdej fazie z flagą `--no-first-run` (przełącznik Chromium, nie polityka; bez niej ekran powitalny zasłaniał stronę), strona `edge://newtab` (UI Automation, zrzuty) i tabela `edge://policy`, przed zapisem, po zapisie i po cofnięciu. Treść MSN i nazwy kafelków były po polsku na obu VM (lokalizacja po adresie IP).
- **Home** (`DSO-EN-Home`), 2026-11-07.
  - `NewTabPageContentEnabled` (20:35–20:37; Edge 152.0.4191.66 przed i po zapisie, 157.0.4341.42 po cofnięciu). Wynik: przed zapisem kanał MSN (m.in. „Odkrywaj”, „Ustawienia kanału informacyjnego”, artykuły, reklamy produktów); po zapisie kanału nie ma (elementy UI Automation 219 → 72); po cofnięciu kanał wraca. W `edge://policy`: `false`, Platform, Device, Mandatory, status OK.
  - `NewTabPageHideDefaultTopSites` (20:37–20:39; Edge 157.0.4341.42). Wynik: przed zapisem kafelki Microsoft 365, Booking.com, Allegro, Rtv Euro Agd, Mediamarkt, LinkedIn; po zapisie tylko Microsoft 365. W `edge://policy`: `true`, Platform, Device, Mandatory, status OK. Fazy po cofnięciu na Home nie odczytano: okno logowania OneDrive i powiadomienie zasłoniły Edge, a UI Automation nie zwróciło kafelków ani tabeli polityk.
- **Pro** (`DSO-EN-Pro`), 2026-11-07.
  - `NewTabPageContentEnabled` (20:35–20:37; Edge 152.0.4191.66 przed zapisem, 157.0.4341.42 po zapisie i po cofnięciu). Ten sam wynik co na Home (elementy UI Automation 218 → 71); w `edge://policy` jak na Home.
  - `NewTabPageHideDefaultTopSites` (powtórka 21:12–21:14; Edge 152.0.4191.66). Wynik: po zapisie tylko Microsoft 365; po cofnięciu Microsoft 365, Booking.com, Allegro, Rtv Euro Agd, Mediamarkt, LinkedIn; w `edge://policy` jak na Home. Pierwsze podejście na Pro (20:37–20:39) pokazało przed zapisem sześć kafelków i po zapisie jeden, ale okno Ustawień zasłoniło wtedy `edge://policy`, dlatego powtórka.
- Nie sprawdzono: brzmienia ustawień nowej karty w Edge i tego, czy przy nich pojawia się informacja o zarządzaniu przez organizację.
