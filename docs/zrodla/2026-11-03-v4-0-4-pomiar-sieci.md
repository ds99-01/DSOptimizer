# Wersja 4.0.4 – pomiar połączeń sieciowych programu

Stan: 2026-11-03. Ten sam pomiar co dla wersji 4.0.3, powtórzony na wydaniu 4.0.4, które dodaje stronę „Autostart”. Metoda,
kontrole i kryteria są takie same i opisuje je [raport wersji 4.0.3](2026-11-01-v4-0-3-pomiar-sieci.md). Skrypt:
`tests/vm/explore/Invoke-NetExploration.ps1 -Scenario start-on,start-off,cli`.

## Warunki pomiaru

- Czyste VM 26H2 z Hyper-V: `DSO-26H2-Pro` (Windows 11 Pro) i `DSO-26H2-Home` (Windows 11 Home), punkt kontrolny `czysta-26H2`.
- Każdy scenariusz na świeżym cofnięciu do `czysta-26H2`. VM startuje bez sieci, `Default Switch` jest podłączany tylko na
  czas scenariusza, a po nim następuje cofnięcie, VM `Off` i karta bez przełącznika.
- Konto lokalne `dso`, okno programu uruchamiane w jego sesji interaktywnej. Komputer poza domeną, bez proxy.
- Artefakt: `DSOptimizer-4.0.4.zip` pobrany z wydania na GitHubie, 3 871 329 B, SHA256 `c4136ee0…d12999`, równy
  `SHA256SUMS.txt`. Program w VM zgłosił wersję 4.0.4.0.
- Ustawienia: jak w pomiarze 4.0.3 (`start-on` bez pola `checkUpdatesAtStart`, czyli domyślnie włączone; `start-off`
  z `"checkUpdatesAtStart": false`; `cli` jak `start-on`).

Scenariusze:

- `start-on`: okno główne, potem przejście po wszystkich 15 stronach menu (w tym nowej stronie „Autostart”, 8 s) i stronie
  Ustawienia, bez klikania w akcje.
- `start-off`: to samo ze sprawdzaniem wersji wyłączonym w ustawieniach.
- `cli`: `DSOptimizer.exe --list-install` i `--list-apps` jako użytkownik `dso`.

## Wyniki

| VM | Scenariusz | Wynik | Połączenia drzewa programu | DNS drzewa programu | Zdarzeń w śladzie | Utracone bufory / zdarzenia | 4688 | Ruch kontrolny |
|---|---|---|---|---|---|---|---|---|
| Pro | start-on | zaliczony | 1: TCP 140.82.121.5:443, `api.github.com` | `api.github.com` | 379 072 | 0 / 0 | zgodne (28 procesów) | widoczny |
| Pro | start-off | zaliczony | 0 | 0 | 321 249 | 0 / 0 | zgodne (28 procesów) | widoczny |
| Pro | cli | zaliczony | 0 | 0 | 101 800 | 0 / 0 | zgodne (10 procesów) | widoczny |
| Home | start-on | zaliczony | 1: TCP 140.82.121.5:443, `api.github.com` | `api.github.com` | 413 128 | 0 / 0 | zgodne (28 procesów) | widoczny |
| Home | start-off | zaliczony | 0 | 0 | 161 739 | 0 / 0 | zgodne (28 procesów) | widoczny |
| Home | cli | zaliczony | 0 | 0 | 254 143 | 0 / 0 | zgodne (10 procesów) | widoczny |

Strona „Autostart” nie zmieniła wyniku: przy otwartym oknie jedynym połączeniem drzewa programu było sprawdzenie wersji
na `api.github.com`, a po jego wyłączeniu program nie łączył się z niczym.

## Ograniczenia

Obowiązują wszystkie ograniczenia z [raportu wersji 4.0.3](2026-11-01-v4-0-3-pomiar-sieci.md#ograniczenia), w szczególności:

- Mierzone jest wyłącznie drzewo procesów programu; ruch zlecony usługom Windows nie jest liczony. Zatrzymanie `wuauserv`,
  `UsoSvc` i `BITS` przed śladem nie zgłosiło błędu, wyłączenie `DoSvc` zostało odmówione („Odmowa dostępu”) na obu
  edycjach. Skuteczności zatrzymania (ponownych uruchomień usług w czasie śladu) w tym pomiarze nie analizowano.
- Czas obserwacji (sieć podłączona): 6,2–7,7 minuty dla okna i 1,4–2,2 minuty dla `cli`.
- `cli` to tylko dwa polecenia; polecenia autostartu (`--list-autostart`, `--set-autostart`, `--undo-autostart`) nie były
  mierzone.
- Przejście po stronach nie uruchamia akcji, w tym wyłączania pozycji autostartu.
- Komputer w domenie i sieć z serwerem proxy są niezmierzone. Jeden przebieg każdego scenariusza na każdej edycji; polski
  Windows 11 26H2.
