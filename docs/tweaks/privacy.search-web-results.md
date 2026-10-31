# privacy.search-web-results – Wyłącz wyniki z internetu w wyszukiwaniu Windows

## Werdykt 26H2
Portować: test na VM 26H2 (Home i Pro) pokazał realny skutek. Brak dokumentacji MS dla tej wartości, dowód: test na VM (obserwacja niżej).

## Co zmienia
- `HKCU\Software\Microsoft\Windows\CurrentVersion\SearchSettings` (w silniku `HKU\<SID zalogowanego>`), wartość `IsGlobalWebSearchProviderToggleEnabled`, **DWord**, ustawiana na `0`; `scope=user`. To stan przełącznika „Zezwalaj aplikacjom wyszukiwania na wyświetlanie wyników” (Prywatność > Wyszukaj).
- Wyłącza wszystkich dostawców wyników z sieci, nie blokuje przełącznika i nie rusza pola wyszukiwania Eksploratora.
- Apply nie restartuje Eksploratora. Skutek obserwowano po restarcie Eksploratora i zabiciu SearchHost (zrobiono też StartMenuExperienceHost przy pozycjach Start). Katalog podaje więc skutek „po ponownym zalogowaniu”; czy sam restart Eksploratora (`tools.restart-explorer`) wystarcza, nie sprawdzano, pozycja mówi o tym wprost („sam restart Eksploratora może nie wystarczyć”).

### Działające alternatywy (opisane, bez własnych pozycji katalogu)
Wszystkie trzy dały ten sam efekt (wyniki Bing znikają) na Home i Pro, po restarcie Eksploratora:
- `...\SearchSettings\WebSearchProviders`, wartość `Microsoft.BingSearch_8wekyb3d8bbwe!App` = DWORD 0 (domyślnie 1): węższa, tylko dostawca Bing.
- `HKCU\Software\Policies\Microsoft\Windows\Explorer` `DisableSearchBoxSuggestions` = DWORD 1: polityka; według MS wyłącza też historię pola wyszukiwania Eksploratora.
- `HKCU\Software\Microsoft\Windows\CurrentVersion\Search` `BingSearchEnabled` = DWORD 0 (domyślnie brak wartości): działa, bez dokumentacji MS.

Nie działają (patrz osobne dokumenty): `ConnectedSearchUseWeb` (`privacy.search-connected-web.md`), `DisableWebSearch` (`privacy.search-disable-web-search.md`).

## Źródła
- Test na VM 26H2 Home i Pro, 2026-10-21 (build 26300.9457): przełącznik w Ustawieniach, zapis przez API rejestru, wyniki wyszukiwania dla frazy „pogoda”.
- Research: `docs/zrodla/2026-10-20-etap4a-zrodla.md`, sekcje „4. Bing / wyszukiwanie w sieci w menu Start” i „Wyniki VM 26H2 (partia 1)”.

## Wartości domyślne na świeżym 26H2
Odczyt z VM DSO-26H2-Home i DSO-26H2-Pro, 2026-10-21: klucz `SearchSettings` istnieje, `IsGlobalWebSearchProviderToggleEnabled` **brak wartości** (przełącznik włączony); `WebSearchProviders\Microsoft.BingSearch_8wekyb3d8bbwe!App` = 1.

## Detekcja i undo
- Detekcja: wartość = DWORD 0 -> `Applied`; brak wartości lub inna wartość -> `NotApplied`.
- Undo przywraca snapshot sprzed zmiany z journala (brak wartości = jej usunięcie; klucz `SearchSettings` istniał przed apply i zostaje); `original` = `<RemoveEntry>` to tylko fallback.

## Ryzyka i ostrzeżenia
- `risk=safe`, `suggested=true`.
- Ostrzeżenie w katalogu: „Zmiana zadziała po ponownym zalogowaniu; sam restart Eksploratora może nie wystarczyć.”
- Poza uwagami w programie (w szczegółach technicznych albo tylko tutaj): skutku samego restartu Eksploratora (`tools.restart-explorer`) nie sprawdzano; pozycja wyłącza wszystkich dostawców wyników z sieci.

## Test VM
Przypadek `privacy-search-web-results` (`Flow=Full`) w `tests/vm/cases.psd1` (`NotWrittenTo` dla `HKCU` procesu Administratora, bo pozycja jest per użytkownik). Przebieg na VM 2026-10-21 (`Invoke-VmTests.ps1`, Task 9 partii): **DSO-26H2-Home i DSO-26H2-Pro – zaliczony**, kroki detect `NotApplied` → apply `Applied` → detect `Applied` → undo `Undone` → detect `NotApplied`, stan po apply potwierdzony niezależnym odczytem, stan końcowy = stan przed (`SnapshotDiff` pusty). HKCU Administratora bez zmian (`NotWrittenTo`), klucz `SearchSettings` został. Eksploracja z 2026-10-21 przeszła na Home i Pro.

## Obserwacja skutku
- **Home** (`DSO-26H2-Home`), 2026-10-21. Metoda: zrzuty ekranu wyników wyszukiwania (Win+S) dla frazy „pogoda” w sesji `dso`, po zapisie przez API rejestru i restarcie Eksploratora oraz SearchHost. Wynik: znika sekcja „Microsoft Bing sugestie internetowe” i karta „Microsoft Bing” w wynikach; po cofnięciu sekcja wraca.
- **Pro** (`DSO-26H2-Pro`), 2026-10-21. Ta sama metoda i ten sam wynik.
