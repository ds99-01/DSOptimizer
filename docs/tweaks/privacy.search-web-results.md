# privacy.search-web-results – Wyłącz wyniki z internetu w wyszukiwaniu Windows

## Werdykt 26H2
Portować: test na VM 26H2 (Home i Pro) pokazał realny skutek. Brak dokumentacji MS dla obu wartości, dowód: test na VM (obserwacja niżej).

Od 2026-11-16 (do wydania po 4.0.8) pozycja ustawia dwie wartości. Do 4.0.8 była tylko `IsGlobalWebSearchProviderToggleEnabled`, a ta steruje przełącznikiem, który Windows pokazuje w Europejskim Obszarze Gospodarczym (EOG). Na VM z regionem USA nic nie zmieniała, także po ponownym uruchomieniu. `BingSearchEnabled` = 0 wyłącza wyniki Bing w regionie USA i PL.

## Co zmienia
- `HKCU\Software\Microsoft\Windows\CurrentVersion\Search` (w silniku `HKU\<SID zalogowanego>`), wartość `BingSearchEnabled`, **DWord**, ustawiana na `0`; `scope=user`. Główna wartość: działa w EOG i poza nim. Skutek uboczny: ze strony startowej wyszukiwania znikają wyróżnienia (obrazek dnia, quiz, popularne wyszukiwania).
- `HKCU\Software\Microsoft\Windows\CurrentVersion\SearchSettings` (w silniku `HKU\<SID zalogowanego>`), wartość `IsGlobalWebSearchProviderToggleEnabled`, **DWord**, ustawiana na `0`; `scope=user`. To stan przełącznika „Zezwalaj aplikacjom wyszukiwania na wyświetlanie wyników” (Prywatność > Wyszukaj).
- `IsGlobalWebSearchProviderToggleEnabled` wyłącza w EOG wszystkich dostawców wyników z sieci i zostaje w pozycji, żeby przełącznik w Ustawieniach pokazywał ten sam stan. Żadna z wartości nie blokuje przełącznika i nie rusza pola wyszukiwania Eksploratora.
- Apply nie restartuje Eksploratora. Skutek obserwowano po restarcie Eksploratora i zabiciu SearchHost (zrobiono też StartMenuExperienceHost przy pozycjach Start). Katalog podaje więc skutek „po ponownym zalogowaniu”; czy sam restart Eksploratora (`tools.restart-explorer`) wystarcza, nie sprawdzano, pozycja mówi o tym wprost („sam restart Eksploratora może nie wystarczyć”).

### Działające alternatywy (opisane, bez własnych pozycji katalogu)
Obie dały ten sam efekt (wyniki Bing znikają) na Home i Pro z regionem PL, po restarcie Eksploratora:
- `...\SearchSettings\WebSearchProviders`, wartość `Microsoft.BingSearch_8wekyb3d8bbwe!App` = DWORD 0 (domyślnie 1): węższa, tylko dostawca Bing.
- `HKCU\Software\Policies\Microsoft\Windows\Explorer` `DisableSearchBoxSuggestions` = DWORD 1: polityka; według MS wyłącza też historię pola wyszukiwania Eksploratora.

`BingSearchEnabled` była tu alternatywą do 4.0.8; od 2026-11-16 jest wartością pozycji (wyżej). Na VM z regionem USA (2026-11-16) `DisableSearchBoxSuggestions` zadziałała dopiero po ponownym zalogowaniu.

Nie działają (patrz osobne dokumenty): `ConnectedSearchUseWeb` (`privacy.search-connected-web.md`), `DisableWebSearch` (`privacy.search-disable-web-search.md`).

## Źródła
- Test na VM 26H2 Home i Pro, 2026-10-21 (build 26300.9457): przełącznik w Ustawieniach, zapis przez API rejestru, wyniki wyszukiwania dla frazy „pogoda”.
- Test na VM 26H2 `DSO-EN-Home` z regionem USA i `DSO-PL-Home` z regionem PL, 2026-11-16 (build 26300.9457): zapis przez API rejestru, wyniki wyszukiwania przed i po, także po ponownym uruchomieniu.

## Wartości domyślne na świeżym 26H2
Odczyt z VM DSO-26H2-Home i DSO-26H2-Pro, 2026-10-21: klucz `Search` istnieje, `BingSearchEnabled` **brak wartości**; klucz `SearchSettings` istnieje, `IsGlobalWebSearchProviderToggleEnabled` **brak wartości** (przełącznik włączony); `WebSearchProviders\Microsoft.BingSearch_8wekyb3d8bbwe!App` = 1.

## Detekcja i undo
- Detekcja: obie wartości = DWORD 0 -> `Applied`; brak którejś wartości lub inna wartość -> `NotApplied`. Kto zastosował pozycję w 4.0.8, zobaczy ją po aktualizacji jako niezastosowaną, bo brakuje `BingSearchEnabled`.
- Undo przywraca snapshot sprzed zmiany z journala (brak wartości = jej usunięcie; klucze `Search` i `SearchSettings` istniały przed apply i zostają); `original` = `<RemoveEntry>` to tylko fallback.

## Ryzyka i ostrzeżenia
- `risk=safe`, `suggested=true`.
- Ostrzeżenia w katalogu: „Zmiana zadziała po ponownym zalogowaniu; sam restart Eksploratora może nie wystarczyć.” oraz „Z ekranu wyszukiwania znikają też wyróżnienia: obrazek dnia, quiz i popularne wyszukiwania.”
- Poza uwagami w programie (w szczegółach technicznych albo tylko tutaj): skutku samego restartu Eksploratora (`tools.restart-explorer`) nie sprawdzano; w regionie USA wyniki znikały od razu po zapisie `BingSearchEnabled`, ale ostrzeżenie zostaje ostrożne. W EOG pozycja wyłącza wszystkich dostawców wyników z sieci. Nie sprawdzano: konta Microsoft i regionu USA na edycji Pro.

## Test VM
Przebieg VM z 2026-11-16 (build 26300.9457) po dodaniu `BingSearchEnabled`: `DSO-EN-Pro`, `DSO-PL-Pro` i `DSO-EN-Home`, na każdej **Passed** (12 z 12): przypadek `privacy-search-web-results` z obiema wartościami, klucze `Search` i `SearchSettings` zostają po undo, HKCU Administratora bez zmian. Przebieg obejmował tylko ten przypadek.

Przebieg VM z 2026-11-05 (build 26300.9457) na Windows 11 26H2 po angielsku i po polsku: `DSO-EN-Home` i `DSO-PL-Home` (Core), `DSO-EN-Pro` i `DSO-PL-Pro` (Professional), na każdej **Passed**: przypadek `privacy-search-web-results`. Na każdej VM pełny przebieg: 861 zaliczonych, 0 błędów, 25 pominiętych. Niżej wcześniejsze przebiegi na polskim Windows.

Przypadek `privacy-search-web-results` (`Flow=Full`) w `tests/vm/cases.psd1` (`NotWrittenTo` dla `HKCU` procesu Administratora, bo pozycja jest per użytkownik). Przebieg na VM 2026-10-21 (`Invoke-VmTests.ps1`): **DSO-26H2-Home i DSO-26H2-Pro – zaliczony**, kroki detect `NotApplied` → apply `Applied` → detect `Applied` → undo `Undone` → detect `NotApplied`, stan po apply potwierdzony niezależnym odczytem, stan końcowy = stan przed (`SnapshotDiff` pusty). HKCU Administratora bez zmian (`NotWrittenTo`), klucz `SearchSettings` został. Osobny pomiar na VM z 2026-10-21 przeszedł na Home i Pro.

## Obserwacja skutku
- **Home** (`DSO-26H2-Home`), 2026-10-21. Metoda: zrzuty ekranu wyników wyszukiwania (Win+S) dla frazy „pogoda” w sesji `dso`, po zapisie przez API rejestru i restarcie Eksploratora oraz SearchHost. Wynik: znika sekcja „Microsoft Bing sugestie internetowe” i karta „Microsoft Bing” w wynikach; po cofnięciu sekcja wraca.
- **Pro** (`DSO-26H2-Pro`), 2026-10-21. Ta sama metoda i ten sam wynik.
- **Region USA** (`DSO-EN-Home`), 2026-11-16. Metoda: zrzuty wyników wyszukiwania przed i po zapisie. Wynik: `IsGlobalWebSearchProviderToggleEnabled` = 0 nic nie zmienia, także po ponownym uruchomieniu; po `BingSearchEnabled` = 0 wyniki z internetu znikają od razu i nie wracają po ponownym uruchomieniu. Ten sam wynik dla `BingSearchEnabled` w regionie PL (`DSO-PL-Home`).
