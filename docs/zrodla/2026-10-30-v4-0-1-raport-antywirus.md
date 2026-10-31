# Wersja 4.0.1 – raport antywirusowy

Stan: 2026-10-30. Raport uzupełnia pomiar z wersji 4.0
([2026-10-26-etap5-raport-antywirus.md](2026-10-26-etap5-raport-antywirus.md)); 4.0.1 to pierwsze wydanie z angielskim
interfejsem i dokumentacją, więc sprawdzono, czy nowe teksty i okno wyboru języka niczego nie zmieniają w zachowaniu
programu wobec Defendera. Skrypt: `tests/vm/explore/Invoke-E5Exploration.ps1 -Phase defender,av,motw,startup -Variant Folder`
(HEAD `90fe2ae`). Plik wydania buduje workflow z tagu; od mierzonego kodu różni go tylko ten raport.

## Warunki pomiaru

- Czyste VM 26H2 `DSO-26H2-Pro` (kompilacja 26300.9457), sieć (`Default Switch`) tylko na czas faz, na końcu revert
  do `czysta-26H2`, VM `Off`, karta bez przełącznika, VHDX odłączony (`cleanup.Ok` = true).
- Defender w ustawieniach domyślnych świeżej instalacji, nic nie wyłączone, bez wyjątków. Sygnatury przed pomiarem i po
  `Update-MpSignature`: 1.437.1.0 (2025-09-17) → **1.463.43.0** (2026-10-30). `MAPSReporting` = 2, `SubmitSamplesConsent` = 1
  (ochrona w chmurze i automatyczne przesyłanie próbek włączone), Tamper Protection włączona.
- Artefakt z `tools/release/Build-Release.ps1 -Variant Folder -Tag v4.0.1`: `DSOptimizer-4.0.1.zip`, rozmiar
  3 785 276 B, SHA256 `945b8359…3139`. Przy pierwszym starcie na czystym profilu pomiar wybiera język
  w oknie wyboru (polski), czas startu liczony bez obsługi tego okna.

## Wyniki – Defender

| Przebieg | Kroki av | Zagrożenia (`Get-MpThreatDetection`) | Zdarzenia Defendera (1006/1007/1015/1116–1119/1121/1122/1126) | Log na nośniku |
|---|---|---|---|---|
| Pro – zip/folder | 18/18 | 0 | 0 | tak |

Wniosek: Defender z aktualnymi sygnaturami i ochroną w chmurze nie zablokował żadnego z 18 sprawdzanych kroków. Pomiar objął wyłącznie Pro
i wariant zip/folder, czyli wariant wydania; edycję Home sprawdzono w tej wersji testami funkcji, bez pomiaru Defendera.

## SmartScreen (plik oznaczony „pobrany z internetu”, `ZoneId=3`, start przez powłokę jak dwuklik)

Na zrzucie `motw.png` po 5 s: okno „System Windows ochronił ten komputer – Filtr Microsoft Defender SmartScreen uniemożliwił
uruchomienie nierozpoznanej aplikacji… Więcej informacji / Nie uruchamiaj”, tak samo jak w wersji 4.0. Automatyczny detektor w skrypcie
tego okna nie rozpoznaje (w 26H2 hostuje je `explorer.exe`, `ApplicationFrameWindow` bez tytułu) i zgłasza „nic”, więc faza
kończy się kodem błędu; ocena po zrzucie, jak w raporcie 4.0. Zagrożenia 0, zdarzenia 0. Obejście dla użytkownika:
„Więcej informacji” → „Uruchom mimo to” (README).

## Czas startu (z nośnika, do pojawienia się okna głównego, bez okna wyboru języka)

| Przebieg | Pierwszy (zimny, po starcie VM) | Kolejne dwa | Mediana z 3 |
|---|---|---|---|
| Pro – zip/folder | 12,5 s | 0,94 s, 0,67 s | 0,94 s |

Pierwszy przebieg 4.0.1 na czystym profilu ma okno wyboru języka, którego pomiar 4.0 nie miał; podane wartości mają czas widoczności
tego okna odjęty, więc porównanie z 4.0 jest przybliżone.

Porównanie z wersją 4.0 (Pro, folder): pierwszy 9,1 s, mediana 0,75 s.

## VirusTotal

Plików wersji 4.0.1 nie wysyłano na VirusTotal. Ostatni wynik z tego serwisu (0 wykryć, raport 4.0) dotyczy plików z wydania roboczego sprzed poprawek 4.0; opublikowanych zipów 4.0 ani 4.0.1 nie skanowano.

## Do potwierdzenia

Antywirusy innych firm, prawdziwy pendrive USB, NAS (UNC), polityki firmowe i reguły ASR – bez zmian względem raportu 4.0.
Program sprawdzano wyłącznie na polskim Windows 11 26H2; na Windows w innych językach część pozycji może nie wykryć stanu poprawnie.
