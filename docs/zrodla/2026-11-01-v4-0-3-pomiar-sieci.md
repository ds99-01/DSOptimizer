# Wersja 4.0.3 – pomiar połączeń sieciowych programu

Stan: 2026-11-01. Pomiar odpowiada na jedno pytanie: z kim łączy się sam program, gdy okno się otwiera, gdy sprawdzanie
wersji jest wyłączone i w trybie wiersza poleceń. Skrypt: `tests/vm/explore/Invoke-NetExploration.ps1 -Scenario start-on,start-off,cli`
(kod programu z HEAD `4a210c0`; plik wydania zbudowany z `tools/release/Build-Release.ps1 -Variant Folder -Tag v4.0.3`).

## Warunki pomiaru

- Czyste VM 26H2 z Hyper-V: `DSO-26H2-Pro` (Windows 11 Pro) i `DSO-26H2-Home` (Windows 11 Home). Kompilacja 26300 (program podaje
  `build` 26300 w obu VM; numer poprawki 9457 jak we wcześniejszych pomiarach tego samego punktu kontrolnego).
- Każdy scenariusz na świeżym cofnięciu do `czysta-26H2`. VM startuje bez sieci, `Default Switch` jest podłączany tylko na
  czas scenariusza, a po nim następuje cofnięcie, VM `Off` i karta bez przełącznika.
- Konto lokalne `dso`, okno programu uruchamiane w jego sesji interaktywnej. Komputer poza domeną, bez proxy,
  z siecią tylko przez `Default Switch`.
- Artefakt: `DSOptimizer-4.0.3.zip`, 3 792 569 B, SHA256 `a57c0741…ffca5`, suma sprawdzana na hoście i w VM.
- Ustawienia: `settings.json` z językiem polskim (okno wyboru języka się nie pojawia). W `start-on` bez pola
  `checkUpdatesAtStart` (domyślnie włączone), w `start-off` z `"checkUpdatesAtStart": false`, w `cli` jak w `start-on`.

## Metoda

Ślad zdarzeń Windows (ETW) z trzech źródeł: tworzenie procesów (Kernel-Process), połączenia (Kernel-Network) i zapytania
DNS (DNS-Client). Parser buduje **drzewo procesów programu**: `DSOptimizer.exe` i wszyscy jego potomkowie. Liczy się
tylko ruch tego drzewa. Ruch zlecony usługom Windows (Windows Update, Delivery Optimization, BITS) nie jest liczony, bo
wykonują go ich własne procesy, a nie program.

Scenariusze:

- `start-on`: okno główne programu, potem przejście po wszystkich 14 stronach menu i stronie Ustawienia (czytanie
  struktury okna, bez klikania w akcje; na stronach Specyfikacja, Przygotowanie, Aplikacje i Instalacja po 45 s, na pozostałych po 8 s).
- `start-off`: to samo ze sprawdzaniem wersji wyłączonym w ustawieniach.
- `cli`: `DSOptimizer.exe --list-install` i `--list-apps` jako użytkownik `dso`, z zapisem raportów (`--out`).

Kontrole, które muszą przejść, żeby wynik się liczył:

- ruch kontrolny: tuż przed końcem śladu osobny proces w VM wykonuje zapytanie DNS i połączenie TCP 443 do `example.com`;
  ślad musi go zawierać poza drzewem programu (dowód, że ślad widzi sieć);
- liczba utraconych buforów i zdarzeń w śladzie (odczyt z `ControlTraceW`) musi wynosić 0;
- kontrola krzyżowa 4688: drzewo procesów z dziennika zabezpieczeń (audyt tworzenia procesów, włączony na czas
  scenariusza) musi być równe drzewu procesów ze śladu, w obie strony;
- sieć musi być gotowa przed startem programu (trasa domyślna, brama odpowiada), żeby brak ruchu nie wynikał z braku sieci.

Kryterium: `start-on` – co najmniej jedno połączenie, każde na TCP 443 z hostem `api.github.com`, a DNS drzewa tylko
o `api.github.com`; `start-off` i `cli` – zero zapytań DNS i zero połączeń w drzewie programu.

## Wyniki

| VM | Scenariusz | Wynik | Połączenia drzewa programu | DNS drzewa programu | Zdarzeń w śladzie | Utracone bufory / zdarzenia | 4688 | Ruch kontrolny |
|---|---|---|---|---|---|---|---|---|
| Pro | start-on | zaliczony | 1: TCP 140.82.121.6:443, `api.github.com` | `api.github.com` (A i AAAA) | 53 403 | 0 / 0 | zgodne (26 procesów) | widoczny |
| Pro | start-off | zaliczony | 0 | 0 | 344 890 | 0 / 0 | zgodne (25 procesów) | widoczny |
| Pro | cli | zaliczony | 0 | 0 | 11 078 | 0 / 0 | zgodne (10 procesów) | widoczny |
| Home | start-on | zaliczony | 1: TCP 140.82.121.5:443, `api.github.com` | `api.github.com` (A i AAAA) | 300 188 | 0 / 0 | zgodne (26 procesów) | widoczny |
| Home | start-off | zaliczony | 0 | 0 | 414 969 | 0 / 0 | zgodne (26 procesów) | widoczny |
| Home | cli | zaliczony | 0 | 0 | 11 125 | 0 / 0 | zgodne (10 procesów) | widoczny |

W `start-on` jedyne połączenie drzewa programu: jedno otwarcie TCP na 443 do GitHuba, około 0,7 kB wysłanych i 14,6 kB
odebranych. Nie było innego hosta, innego portu ani serwera certyfikatów w drzewie programu. W `cli` oba polecenia
zakończyły się kodem 0 i zapisały niepuste raporty.

Pierwszy przebieg na Pro został odrzucony przez błąd samego narzędzia (kontrola 4688 brała ponownie użyte numery
procesów za potomków programu). Po poprawce narzędzia wszystkie trzy scenariusze na Pro powtórzono od nowa; wyniki powyżej
pochodzą z tego powtórzenia i z przebiegu na Home.

## Ograniczenia

- Mierzone jest wyłącznie drzewo procesów programu. Ruch zlecony usługom Windows (Windows Update, Delivery Optimization,
  BITS) nie jest liczony.
- Usługi `wuauserv`, `UsoSvc`, `DoSvc` i `BITS` próbowano zatrzymać na czas pomiaru, bo pobierają w tle i zalewały ślad
  ruchem, który nie należy do programu. Udało się to dla `wuauserv` i `UsoSvc` (zatrzymane i wyłączone do cofnięcia VM)
  oraz dla `BITS` na Home w każdym scenariuszu i na Pro w `cli`. Na Pro w `start-on` i `start-off` zatrzymanie `BITS`
  się nie powiodło, a `DoSvc` na obu edycjach odmówiła zmiany („Odmowa dostępu”), więc działała normalnie. Nie ma to
  wpływu na wynik, bo liczy się tylko drzewo programu, ale nie można mówić, że wszystkie cztery usługi były wyłączone.
- Komputer w domenie i sieć z serwerem proxy są niezmierzone. Pomiar objął komputer poza domeną z bezpośrednim
  dostępem do internetu.
- Przejście po stronach nie uruchamia akcji (zastosuj, cofnij, instalacja). Akcje, które z założenia łączą się z siecią
  (WinUtil, RustDesk, winget), nie wchodzą do tego pomiaru.
- Jeden przebieg każdego scenariusza na każdej edycji; wersja Windows 26H2, polski interfejs.
