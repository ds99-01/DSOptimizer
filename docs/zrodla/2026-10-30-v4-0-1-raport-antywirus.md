# Wersja 4.0.1 – raport antywirusowy

Stan: WYNIK-DO-UZUPEŁNIENIA (data pomiaru). Raport uzupełnia pomiar z wersji 4.0
([2026-10-26-etap5-raport-antywirus.md](2026-10-26-etap5-raport-antywirus.md)); 4.0.1 to pierwsze wydanie z angielskim
interfejsem i dokumentacją, więc sprawdzono, czy nowe teksty i okno wyboru języka niczego nie zmieniają w zachowaniu
programu wobec Defendera. Skrypt: `tests/vm/explore/Invoke-E5Exploration.ps1 -Phase defender,av,motw -Variant Folder`
(HEAD WYNIK-DO-UZUPEŁNIENIA).

## Warunki pomiaru

- Czyste VM 26H2 `DSO-26H2-Pro` (kompilacja WYNIK-DO-UZUPEŁNIENIA), sieć (`Default Switch`) tylko na czas faz, na końcu revert
  do `czysta-26H2`, VM `Off`, karta bez przełącznika, VHDX odłączony (`cleanup.Ok` = WYNIK-DO-UZUPEŁNIENIA).
- Defender w ustawieniach domyślnych świeżej instalacji, nic nie wyłączone, bez wyjątków. Sygnatury przed pomiarem i po
  `Update-MpSignature`: WYNIK-DO-UZUPEŁNIENIA. Ochrona w chmurze, przesyłanie próbek, Tamper Protection:
  WYNIK-DO-UZUPEŁNIENIA.
- Artefakt z `tools/release/Build-Release.ps1 -Variant Folder -Tag v4.0.1`: `DSOptimizer-4.0.1.zip`, rozmiar
  WYNIK-DO-UZUPEŁNIENIA B, SHA256 WYNIK-DO-UZUPEŁNIENIA. Przy pierwszym starcie na czystym profilu pomiar wybiera język
  w oknie wyboru (polski), czas startu liczony bez obsługi tego okna.

## Wyniki – Defender

| Przebieg | Kroki av | Zagrożenia (`Get-MpThreatDetection`) | Zdarzenia Defendera (1006/1007/1015/1116–1119/1121/1122/1126) | Log na nośniku |
|---|---|---|---|---|
| Pro – zip/folder | WYNIK-DO-UZUPEŁNIENIA | WYNIK-DO-UZUPEŁNIENIA | WYNIK-DO-UZUPEŁNIENIA | WYNIK-DO-UZUPEŁNIENIA |

Wniosek: WYNIK-DO-UZUPEŁNIENIA.

## SmartScreen (plik oznaczony „pobrany z internetu”, `ZoneId=3`, start przez powłokę jak dwuklik)

WYNIK-DO-UZUPEŁNIENIA (zrzut `motw.png` po 5 s: co się pokazało).

## Czas startu (z nośnika, do pojawienia się okna głównego, bez okna wyboru języka)

| Przebieg | Pierwszy (zimny, po starcie VM) | Kolejne dwa | Mediana z 3 |
|---|---|---|---|
| Pro – zip/folder | WYNIK-DO-UZUPEŁNIENIA | WYNIK-DO-UZUPEŁNIENIA | WYNIK-DO-UZUPEŁNIENIA |

Porównanie z wersją 4.0 (Pro, folder): pierwszy 9,1 s, mediana 0,75 s.

## VirusTotal

WYNIK-DO-UZUPEŁNIENIA (pliki z wnętrza zipu, wykrycia, data).

## Do potwierdzenia

Antywirusy innych firm, prawdziwy pendrive USB, NAS (UNC), polityki firmowe i reguły ASR – bez zmian względem raportu 4.0.
Program sprawdzano wyłącznie na polskim Windows 11 26H2; na Windows w innych językach część pozycji może nie wykryć stanu poprawnie.
