# repair.dns-flush – Wyczyść pamięć DNS

## Werdykt 26H2
Portować jako `kind=action`, `risk=safe` (nowa kategoria „Naprawy”, Etap 4b partia 1): czyszczenie pamięci podręcznej rozwiązywania nazw (`Clear-DnsClientCache`) działa na 26H2 Home i Pro, a skutek da się potwierdzić odczytem zwrotnym (`Get-DnsClientCache`). Bez cofnięcia: pamięć odbudowuje się sama.

## Co zmienia
Czyści pamięć podręczną klienta DNS (usługa `Dnscache`). Nie zmienia ustawień DNS, pliku `hosts`, rejestru ani konfiguracji kart. Skrypt (`script.apply`), po nazwach poleceń (testy je mockują):
1. `Get-DnsClientCache` – odczyt przed (liczba wpisów do komunikatu).
2. `Clear-DnsClientCache`.
3. `Get-DnsClientCache` – odczyt zwrotny.
4. `Get-DsoHostsText` – helper czytający `%SystemRoot%\System32\drivers\etc\hosts` (tylko odczyt; w testach mock).

Kryterium sukcesu (decyzja z pomiaru, `docs/zrodla/2026-10-22-etap4b-1-pomiary.md`, sekcja 1): każdy wpis, który zostaje w pamięci po czyszczeniu, dotyczy nazwy z pliku `hosts` (słowa po adresie, bez komentarzy `#`, bez kropki na końcu, bez rozróżniania wielkości liter) albo jej nazwy odwrotnej (`*.in-addr.arpa`, `*.ip6.arpa`), dowolnego typu i statusu. Pusta pamięć też jest sukcesem. Inny wpis = `Failed` „Pamięć DNS nie została wyczyszczona: zostało N wpisów.” Sukces: „Wyczyszczono pamięć DNS (przed: N wpisów, po: M – tylko wpisy z pliku hosts).”

Nie liczymy spadku liczby wpisów: `Get-DnsClientCache` nie pokazuje nazw publicznych rozwiązanych przez `Resolve-DnsName` ani przez aplikacje (pomiar N1), więc „przed” bywa mniejsze, niż się wydaje, a spadek nie dowodzi niczego.

## Źródła
- Test na VM 26H2 Home i Pro (build 26300.9457), 2026-10-22: `ipconfig /flushdns` (kod 0) i `Clear-DnsClientCache` dają ten sam stan pamięci (przebiegi H1, P1, A2, N1; `docs/zrodla/2026-10-22-etap4b-1-pomiary.md`, sekcja 1).
- Brak dokumentacji MS sprawdzonej w tym zadaniu (strony cmdletów `Clear-DnsClientCache`/`Get-DnsClientCache` nie były pobierane); dowód: test na VM opisany wyżej. Zachowanie „zostają wpisy z `hosts`” pochodzi wyłącznie z pomiaru.

## Wartości domyślne na świeżym 26H2
Pamięć DNS na świeżej VM po czyszczeniu: Home 2 wpisy (A nazwy z `hosts` i PTR `67.45.123.10.in-addr.arpa`, TTL ok. 604 700 s), Pro tak samo (odczyt 2026-10-22). Z siecią (N1, Pro): 3 wpisy, wszystkie z `hosts` (A, PTR i negatywny AAAA ze statusem 9501); `wpad` i nazwa negatywna znikają.

## Detekcja i undo
Nie dotyczy: `kind=action` nie ma detekcji ani cofnięcia (nie ma journala). Dowodem skutku jest odczyt zwrotny w skrypcie: kod 0 bez niego nie wystarcza. Pamięć odbuduje się sama przy kolejnych zapytaniach.

## Ryzyka i ostrzeżenia
- `risk=safe`: operacja nie zmienia żadnych ustawień, a pamięć odbudowuje się sama.
- Wpisy z `hosts` zostają w pamięci po czyszczeniu (normalne zachowanie Windows) i program liczy je jako wyczyszczone.
- Pierwsze otwarcie stron po czyszczeniu może być chwilę wolniejsze.
- Akcja nie naprawia sieci i nie zmienia serwerów DNS.

## Test VM
Przypadek `repair-dns-flush` (`Flow=Execute`, `Check`: niezależny odczyt `Get-DnsClientCache` i ten sam test „tylko wpisy z `hosts`”): dopisany w `tests/vm/cases.psd1` w Tasku 5.

Przebieg VM (Task 10, 2026-10-22, pełny `tests/vm/Invoke-VmTests.ps1` na gałęzi `etap4b-1`; raporty poza gitem): Home i Pro po 722 zaliczone / 0 błędów / 25 pominiętych (6 przypadków ze `Skip` spoza kategorii Naprawy). `repair-dns-flush`: **Passed** na Home i Pro – wynik `Executed` „Wyczyszczono pamięć DNS (przed: 2 wpisów, po: 2 – tylko wpisy z pliku hosts)”, `Check` zaliczony. W Tasku 10 `Setup` dopisuje do `hosts` wpis testowy `10.123.45.67 dso-test-hosts.invalid` (jak w pomiarze), bo bez sieci same zapytania `localhost`/`.invalid` dawały „przed: 0 wpisów” (pierwszy przebieg); `Cleanup` usuwa wpis z ponowieniem (na Pro plik `hosts` był chwilowo zajęty). Bez sieci zwykłe odpowiedzi DNS nie trafiają do pamięci, więc VM nie pokazuje usunięcia wpisu spoza `hosts` – to zostaje do obserwacji na sprzęcie.

## Obserwacja skutku
Metoda: odczyt `Get-DnsClientCache` przed i po `Clear-DnsClientCache` (i `ipconfig /flushdns`), po rozwiązaniu nazw (`Resolve-DnsName`) przy podłączonej sieci; zrzut wyjścia w surowych wynikach (poza gitem).
- **Home** (`DSO-26H2-Home`, 26300.9457, bez sieci), 2026-10-22: po czyszczeniu (obie metody) zostają tylko 2 wpisy z `hosts` (A nazwy i PTR). Skutek: pamięć wyczyszczona poza wpisami z `hosts`.
- **Pro** (`DSO-26H2-Pro`, 26300.9457), 2026-10-22: bez sieci jak Home (2 wpisy z `hosts`); z siecią (`Default Switch`) 3 wpisy, wszystkie z `hosts`, a `wpad` i wpis negatywny znikają. Nazwy publiczne rozwiązane przez `Resolve-DnsName` (15–19 ms, ponowne zapytanie z pamięci 1 ms) nie były widoczne w `Get-DnsClientCache` ani przed, ani po czyszczeniu – dlatego kryterium nie opiera się na spadku liczby wpisów.

## Reddit
Do uzupełnienia w części E.
