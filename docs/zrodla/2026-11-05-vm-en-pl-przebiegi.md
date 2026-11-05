# Windows 11 26H2 po angielsku i po polsku – przebiegi testów na VM

Stan: 2026-11-05. Pierwszy pełny przebieg testów programu na Windows 11 26H2 w dwóch językach: angielskim (en-US) i polskim
(pl-PL), na edycjach Home i Pro. Wcześniejsze przebiegi (do wersji 4.0.5) szły tylko na polskim Windows (VM `DSO-26H2-Home`
i `DSO-26H2-Pro`); ich wyniki zostają w dokumentach pozycji jako historia.

## Warunki

- Cztery czyste VM z Hyper-V, każda z punktem kontrolnym `czysta-26H2`, każda zbudowana bez klikania z oficjalnego ISO
  Windows 11 26H2 (Consumer editions) w danym języku. Edycja wybrana po `EditionId` obrazu, język obrazu sprawdzony przed
  instalacją; po instalacji odczytane z VM:

  | VM | Edycja (`EditionID`) | Język interfejsu i region | Build |
  |---|---|---|---|
  | `DSO-EN-Home` | Core (Home) | en-US | 26300.9457 |
  | `DSO-EN-Pro` | Professional (Pro) | en-US | 26300.9457 |
  | `DSO-PL-Home` | Core (Home) | pl-PL | 26300.9457 |
  | `DSO-PL-Pro` | Professional (Pro) | pl-PL | 26300.9457 |

- Ten sam build na wszystkich VM, więc różnica wyniku EN–PL nie mogła wynikać z wersji systemu. Windows nieaktywowany,
  konto lokalne `dso`, strefa czasowa CET, bez sieci (poza pomiarem sieci niżej).
- Program: wersja 4.0.5 zbudowana z kodu oznaczonego tagiem `v4.0.5` (bez zmian w programie).
- Na VM EN program działał po angielsku, na VM PL po polsku (język wykryty z Windows). Oczekiwane komunikaty programu
  w testach pochodzą z tabel tłumaczeń programu, nie są pisane ręcznie.

## Wyniki

| VM | Pełny zestaw testów (CLI) | Smoke okna | Pomiar sieci | Instalator |
|---|---|---|---|---|
| `DSO-EN-Home` | 861 zaliczonych, 0 błędów, 25 pominiętych | 23/23 (program po angielsku) | 3/3 | 5/5 faz |
| `DSO-EN-Pro` | 861 / 0 / 25 | 23/23 po angielsku i 23/23 po polsku | 3/3 | 5/5 faz |
| `DSO-PL-Pro` | 861 / 0 / 25 | 23/23 po polsku i 23/23 po angielsku | 3/3 | 5/5 faz |
| `DSO-PL-Home` | 861 / 0 / 25 | 23/23 (program po polsku) | – | – |

- Pominięte przypadki są te same na każdej VM i pomijane z góry: hibernacja (Hyper-V bez S4), zmiana profilu sieci
  i skutek SMB w sieci LAN (VM bez sieci), BitLocker przy OOBE (punkt kontrolny jest po OOBE), menu „Przejmij na własność”
  w klasycznym menu Eksploratora (wymaga kliknięcia i zgody UAC), skutek WinUtil z siecią (pokrywa osobna eksploracja).
- Wykrywanie języka z Windows: w trybie wiersza poleceń bez `--lang` raport jest w języku Windows na każdej VM; w oknie,
  po zamknięciu okna wyboru języka bez wyboru, program wybiera angielski na VM EN i polski na VM PL.
- Pomiar sieci (scenariusze jak w [pomiarze wersji 4.0.4](2026-11-03-v4-0-4-pomiar-sieci.md)): przy starcie okna
  z włączonym sprawdzaniem wersji wyłącznie `api.github.com` na porcie 443 (jedno połączenie); po wyłączeniu sprawdzania
  i w trybie wiersza poleceń zero zapytań DNS i zero połączeń.
- Instalator: smoke instalacji, praca przy działającym programie, kreator po polsku i po angielsku, plik z internetu
  (oznaczenie MotW, okno SmartScreen bez sieci).

## Różnice EN–PL

Błędów programu: brak. Różnic w działaniu pozycji między Windows po angielsku i po polsku: brak. Pierwszy przebieg na EN
pokazał dwa błędy samych testów, poprawione przed wynikami powyżej:

- Okno SmartScreen bez sieci ma po angielsku nagłówek „SmartScreen can't be reached right now”; test znał tylko polski
  odpowiednik.
- Strona „Autostart”: na świeżym koncie wpis `OneDriveSetup` (`HKU\<SID>\Software\Microsoft\Windows\CurrentVersion\Run`)
  znika sam ok. 70 s po pierwszym logowaniu, więc liczba pozycji w oknie i wcześniejszy odczyt CLI potrafiły się różnić
  o jedną. Dotyczy to też VM PL; program liczy pozycje poprawnie. Test porównuje teraz okno z odczytem CLI przed i po.

## Czego te przebiegi nie sprawdzają

- Obserwacji skutku w interfejsie Windows (Ustawienia, wyszukiwanie, menu Start) dla każdej pozycji: te obserwacje
  opisane w dokumentach pozycji zrobiono na polskim Windows i nie powtarzano ich na angielskim.
- Wartości domyślnych na świeżym systemie po angielsku (dokumenty pozycji podają odczyt z polskich VM).
- Windows w innych językach (np. niemieckim, francuskim), ARM64, Windows 10, aktywowanego Windows i komputera w domenie.
