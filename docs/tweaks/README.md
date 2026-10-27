# Dokumentacja pozycji katalogu

Każda pozycja katalogu ma plik `docs/tweaks/<id>.md`, gdzie `<id>` to dokładnie `id` z JSON-a w `src/DSOptimizer.Catalog/catalog/`
(test `Definitions_HaveDocsFile` pilnuje, żeby żadna pozycja nie została bez dokumentu). Układ pliku jest stały: dziewięć sekcji
poniżej, w tej kolejności. Ten plik jest szablonem: skopiuj nagłówki do nowego `<id>.md` i zastąp jednolinijkowe instrukcje treścią.

## Werdykt 26H2
Jedno zdanie: czy pozycja ma sens na Windows 11 26H2 (portować / nie portować / placebo) i dlaczego.

## Co zmienia
Wypisz dokładnie, co jest dotykane: klucze rejestru (ścieżka, nazwa, typ, wartość), usługi, komendy i skrypty.
Panel albo launcher z polem `launch`: plik i argumenty dosłownie (reguły pliku: spec, sekcja 4, A6).

## Źródła
URL-e z datą dostępu i rodzajem źródła (MS Learn, dokumentacja producenta, test na VM 26H2, kod WinUtil).

## Wartości domyślne na świeżym 26H2
Wartości odczytane z czystej VM 26H2 Home i Pro, z datą odczytu (brak klucza zapisz wprost jako "brak").

## Detekcja i undo
Jak katalog wykrywa stan (`Applied` / `NotApplied`) i jak działa cofnięcie; co jest snapshotem, a co tylko fallbackiem `original`.

## Ryzyka i ostrzeżenia
Poziom ryzyka, skutki uboczne i ostrzeżenia pokazywane użytkownikowi (zgodne z polami `risk` i `warnings` w katalogu).

## Test VM
Wynik przebiegu na VM: data, wersja (Home/Pro), `Passed` albo `Skipped` z powodem.

## Obserwacja skutku
Dowód realnego skutku (zasada Etapu 4a): Home i Pro osobno, każda z metodą obserwacji (zrzut, UIA, proces, Ustawienia),
wynikiem i datą. Sam zapis wartości nie jest dowodem. Pozycja bez obserwowanego skutku na danej edycji dostaje `editions`
(`NotSupported` na pozostałych); bez żadnego dowodu wypada z katalogu.

## Reddit
Zgłoszenia problemów i opinie o placebo na 24H2/25H2/26H2 (uzupełniane w części E); do tego czasu: "do uzupełnienia".
