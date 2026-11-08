# Wersja 4.0.6 – pomiar połączeń sieciowych programu

Stan: 2026-11-08. Ten sam pomiar co dla wersji 4.0.4, powtórzony na wydaniu 4.0.6 (paczka UX, strażnik ustawień, nowe pozycje
prywatności). Metoda, kontrole i kryteria są takie same i opisuje je [raport wersji 4.0.3](2026-11-01-v4-0-3-pomiar-sieci.md);
zmiany w stosunku do [raportu 4.0.4](2026-11-03-v4-0-4-pomiar-sieci.md) opisano poniżej. Skrypt:
`tests/vm/explore/Invoke-NetExploration.ps1 -Scenario start-on,start-off,cli`, tym razem dla dwóch wariantów dystrybucji:
archiwum przenośnego (`-ArtifactPath`) i instalatora (`-InstallerPath`).

## Warunki pomiaru

- Czyste VM z Hyper-V, punkt kontrolny `czysta-26H2`: `DSO-EN-Home` (Windows 11 Home, angielski), `DSO-EN-Pro` (Windows 11 Pro,
  angielski) i `DSO-PL-Pro` (Windows 11 Pro, polski). Numer kompilacji Windows nie został zapisany w wynikach tego pomiaru.
- Każdy scenariusz na świeżym cofnięciu do `czysta-26H2`. VM startuje bez sieci, `Default Switch` jest podłączany tylko na
  czas scenariusza, a po nim następuje cofnięcie, VM `Off` i karta bez przełącznika (sprawdzone po każdym scenariuszu).
- Konto lokalne `dso`, okno programu uruchamiane w jego sesji interaktywnej. Komputer poza domeną, bez proxy.
- Artefakty z wydania 4.0.6, równe `SHA256SUMS.txt`:
  - `DSOptimizer-4.0.6.zip`, 3 896 223 B, SHA256 `27914c60…dd779`;
  - `DSOptimizer-4.0.6-setup.exe`, 4 864 771 B, SHA256 `630a93d2…9c9e`.
  Program w każdym pomiarze zgłosił wersję 4.0.6.0.
- Ustawienia: jak w pomiarze 4.0.3 (`start-on` bez pola `checkUpdatesAtStart`, czyli domyślnie włączone; `start-off`
  z `"checkUpdatesAtStart": false`; `cli` jak `start-on`). Język programu zgodny z językiem VM.

Scenariusze:

- `start-on`: okno główne, potem przejście po wszystkich 13 pozycjach menu i stronie Ustawienia, bez klikania w akcje
  (na stronach Specyfikacja, Przygotowanie, Aplikacje i Instalacja po 45 s, na pozostałych po 8 s). Liczba pozycji menu
  jest mniejsza niż w raporcie 4.0.4 (15), bo w 4.0.6 naprawy i narzędzia są jedną stroną.
- `start-off`: to samo ze sprawdzaniem wersji wyłączonym w ustawieniach.
- `cli`: `DSOptimizer.exe --list-install` i `--list-apps` jako użytkownik `dso` (oba zakończone kodem 0).

## Wyniki

Wszystkie 18 przebiegów (3 VM × 2 warianty × 3 scenariusze) zaliczonych. W każdym: 0 utraconych buforów i zdarzeń śladu,
drzewo procesów zgodne między śladem a audytem 4688 (28 procesów dla okna, 10 dla `cli`), ruch kontrolny widoczny w śladzie.

| VM | Wariant | Scenariusz | Połączenia drzewa programu | DNS drzewa programu | Zdarzeń w śladzie |
|---|---|---|---|---|---|
| EN-Home | archiwum | start-on | 1: TCP 140.82.121.5:443, `api.github.com` | `api.github.com` | 96 445 |
| EN-Home | archiwum | start-off | 0 | 0 | 147 847 |
| EN-Home | archiwum | cli | 0 | 0 | 15 857 |
| EN-Home | instalator | start-on | 1: TCP 140.82.121.5:443, `api.github.com` | `api.github.com` | 48 227 |
| EN-Home | instalator | start-off | 0 | 0 | 47 200 |
| EN-Home | instalator | cli | 0 | 0 | 183 195 |
| EN-Pro | archiwum | start-on | 1: TCP 140.82.121.5:443, `api.github.com` | `api.github.com` | 491 303 |
| EN-Pro | archiwum | start-off | 0 | 0 | 377 462 |
| EN-Pro | archiwum | cli | 0 | 0 | 173 605 |
| EN-Pro | instalator | start-on | 1: TCP 140.82.121.5:443, `api.github.com` | `api.github.com` | 405 474 |
| EN-Pro | instalator | start-off | 0 | 0 | 272 812 |
| EN-Pro | instalator | cli | 0 | 0 | 13 614 |
| PL-Pro | archiwum | start-on | 1: TCP 140.82.121.5:443, `api.github.com` | `api.github.com` | 90 264 |
| PL-Pro | archiwum | start-off | 0 | 0 | 53 211 |
| PL-Pro | archiwum | cli | 0 | 0 | 14 908 |
| PL-Pro | instalator | start-on | 1: TCP 140.82.121.6:443, `api.github.com` | `api.github.com` | 290 670 |
| PL-Pro | instalator | start-off | 0 | 0 | 54 271 |
| PL-Pro | instalator | cli | 0 | 0 | 15 386 |

Wynik jest taki sam jak w 4.0.4: przy otwartym oknie jedynym połączeniem drzewa programu jest sprawdzenie wersji na
`api.github.com` (jedno połączenie TCP na port 443; adres IP różni się w jednym przebiegu, bo to adres z puli GitHuba), a po
wyłączeniu sprawdzania wersji i w trybie `cli` program nie wykonuje żadnych zapytań DNS ani połączeń. Nowe pozycje prywatności
i strażnik ustawień nie dodały żadnego połączenia.

## Instalator

Poza siecią sprawdzono instalator na wszystkich trzech VM (`Invoke-InstallerExploration.ps1`): 5 faz, 34 sprawdzenia, wszystkie
zaliczone. Fazy: instalacja i odinstalowanie (1/1), instalacja przy działającym programie, w tym odmowa drugiej kopii, kod 7
przy ponownej instalacji i relaunch (18/18), kreator po polsku (6/6) i po angielsku (6/6), znacznik pochodzenia z internetu
(3/3). Faza aktualizacji nie była uruchamiana, bo nie ma wyższej wersji do zainstalowania.

## Ograniczenia

Obowiązują wszystkie ograniczenia z [raportu wersji 4.0.3](2026-11-01-v4-0-3-pomiar-sieci.md#ograniczenia), w szczególności:

- Mierzone jest wyłącznie drzewo procesów programu; ruch zlecony usługom Windows nie jest liczony. Usługi `wuauserv`,
  `UsoSvc` i `BITS` zatrzymano i wyłączono na czas pomiaru, a zatrzymanie `DoSvc` zostało odmówione („Access is denied” /
  „Odmowa dostępu”) w każdym z 18 przebiegów. Skuteczności zatrzymania (ponownych uruchomień usług w czasie śladu) nie analizowano.
- Czas obserwacji (sieć podłączona): około 5–6 minut dla okna i około 45 s dla `cli` (wg osi czasu przebiegów archiwum).
- `cli` to tylko dwa polecenia; pozostałe polecenia wiersza poleceń nie były mierzone.
- Przejście po stronach nie uruchamia akcji.
- Komputer w domenie i sieć z serwerem proxy są niezmierzone. Jeden przebieg każdego scenariusza na każdej kombinacji VM i wariantu;
  Windows 11 26H2 (angielski i polski).