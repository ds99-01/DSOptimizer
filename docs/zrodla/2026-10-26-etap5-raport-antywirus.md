# Etap 5 – raport antywirusowy i wybór formy wydania (Task 6)

Stan: 2026-10-26. Surowe wyniki (bez danych prywatnych w tym pliku): zapis pomiaru w archiwum autora
(`e5.json`, zrzuty `motw.png`, `dpi-150.png`, raporty `--out`, logi). Skrypt: `tests/vm/explore/Invoke-E5Exploration.ps1
-Phase defender,av,motw,startup,dpi -Variant SingleExe|Folder` (HEAD `e6c58d8`).

## Warunki pomiaru

- Czyste VM 26H2 (`DSO-26H2-Pro` Professional, `DSO-26H2-Home` Core, kompilacja 26300.9457), sieć (`Default Switch`) tylko na
  czas faz, na końcu revert do `czysta-26H2`, VM `Off`, karta bez przełącznika, VHDX odłączony (`cleanup.Ok = true` we wszystkich 4).
- Defender w ustawieniach domyślnych świeżej instalacji, nic nie wyłączone, bez wyjątków. Przed pomiarem `Update-MpSignature`:
  1.437.1.0 (2025-09-17) → **1.461.204.0 / 1.461.206.0** (2026-10-25/26). `MAPSReporting` = 2, `SubmitSamplesConsent` = 1
  (ochrona w chmurze i automatyczne przesyłanie próbek włączone), Tamper Protection włączona.
- Program podany jak z pendrive'a: VHDX exFAT `DSO-E5` podłączony do VM, plik w folderze `Narzędzia DSO` (spacja + polska litera).
  Artefakty z `tools/release/Build-Release.ps1`: SingleExe `DSOptimizer-4.0.0.exe` 9 641 984 B (SHA256 `043be790…14f8`),
  Folder `DSOptimizer-4.0.0.zip` 3 689 833 B (SHA256 `4ba71cea…765e`; na nośniku rozpakowany).

## Wyniki – Defender

| Przebieg | Kroki av (18) | Zagrożenia (`Get-MpThreatDetection`) | Zdarzenia Defendera (1006/1007/1015/1116–1119/1121/1122/1126) | Log na nośniku |
|---|---|---|---|---|
| Pro – jeden exe | 18/18 | 0 | 0 | tak |
| Home – jeden exe | 18/18 | 0 | 0 | tak |
| Pro – zip/folder | 18/18 | 0 | 0 | tak |
| Home – zip/folder | 18/18 | 0 | 0 | tak |

Kroki av (każdy przez CLI jako `dso`, Interactive + Highest, z nośnika, kod 0): tweak rejestru C# (`privacy.advertising-id`
apply/undo), tweak usługi (`privacy.diagtrack` apply/undo), tweak skryptem (`repair.system-protection` apply/undo), punkt
przywracania (handler), panel przez `launch` (`panels.computer-management`), skrypt akcji (`repair.dns-flush`), skrypt zatrzymujący
powłokę (`tools.restart-explorer`), krótki handler (`repair.wu-reset`), długi handler (`repair.disk-cleanup`), `--list-apps`,
usunięcie AppX (Clipchamp) i programu Win32 (fixture `DsoTestQuiet`), `--install-apps install.7zip,install.rustdesk` (winget +
RustDesk z GitHuba), **nowa akcja WinUtil** (`--execute tools.winutil`: „Otwarto okno WinUtil 26.10.07 (plik z wydania GitHuba,
SHA256 zgodny).”, okno WinUtil widoczne, pobrany plik bez `Zone.Identifier`), start okna GUI.

**Wniosek: Defender z aktualnymi sygnaturami i ochroną w chmurze nie blokuje żadnej funkcji DSOptimizera, w żadnym wariancie,
na żadnej edycji.**

## SmartScreen (plik oznaczony „pobrany z internetu”, `ZoneId=3`, start przez powłokę jak dwuklik)

Na wszystkich 4 zrzutach `motw.png` po 5 s: okno „System Windows ochronił ten komputer – Filtr Microsoft Defender SmartScreen
uniemożliwił uruchomienie nierozpoznanej aplikacji… Więcej informacji / Nie uruchamiaj”. Dla zipu MotW przechodzi na
rozpakowane pliki (Eksplorator) i okno jest to samo. Z nośnika bez MotW okna nie ma (przebiegi av/startup). Automatyczny detektor
w skrypcie nie rozpoznaje tego okna (w 26H2 hostuje je `explorer.exe`, `ProxyModalWindow`/`ApplicationFrameWindow` bez tytułu) –
ocena po zrzutach (ruling autora). Obejście dla użytkownika: „Więcej informacji” → „Uruchom mimo to” (README).

## Czas startu (z nośnika, do pojawienia się okna głównego „DSOptimizer 4.0.0”)

| Przebieg | Pierwszy (zimny, po starcie VM) | Kolejne dwa | Mediana z 3 |
|---|---|---|---|
| Pro – jeden exe | 10,0 s | 1,9 s, 0,8 s | 1,93 s |
| Home – jeden exe | 3,6 s | 0,8 s, 2,8 s | 2,79 s |
| Pro – zip/folder | 9,1 s | 0,75 s, 0,70 s | 0,75 s |
| Home – zip/folder | 6,3 s | 0,79 s, 0,67 s | 0,79 s |

Porównanie: wersja 3.2 potrzebowała 15–20 s.

## Skalowanie 150%

Wszystkie 4 przebiegi: `LogPixels` = 144, okno główne pojawia się (1024×696 na ekranie 1024×768), zrzut `dpi-150.png` – układ
czytelny, nic nie ucięte w widocznej części.

## VirusTotal

Plik wysłany ręcznie przez stronę VirusTotal 2026-10-26. Zip z wydania roboczego
VirusTotal przyjmuje, ale nie skanuje jego zawartości (ponad 3 MB i więcej niż jeden plik), więc wysłane osobno pliki z wnętrza zipu
z wydania roboczego (SHA256 zipu `6f3077a4…40da`) i jeden exe z archiwum autora:

| Plik | SHA256 | Wykrycia |
|---|---|---|
| `DSOptimizer.exe` (zip) | `19a997c4…fcd7` | 0 / 69 |
| `DSOptimizer.Core.dll` (zip) | `3f83819c…701b` | 0 / 69 |
| `DSOptimizer.Windows.dll` (zip) | `2f33e3b4…4d26` | 0 / 70 |
| `DSOptimizer.Catalog.dll` (zip) | `95268fe3…e90b` | 0 / 70 |
| `DSOptimizer-4.0.0.exe` (jeden exe) | `043be790…14f8` | 0 / 69 |

Zakładka „Behavior” (piaskownice CAPE, Zenbox, VirusTotal Jujubox) dla obu exe: brak werdyktu złośliwości; sygnatury MITRE tylko
MEDIUM/LOW/INFO (folder: 8 MEDIUM, 15 LOW, 6 INFO; jeden exe: 9 INFO), zero HIGH, zero reguł IDS i Sigma. Biblioteki zewnętrzne
(Wpf.Ui, Newtonsoft.Json, System.*, CommunityToolkit.Mvvm) nie wysyłane – to niezmienione pakiety NuGet. Pliki na VirusTotal są
widoczne dla subskrybentów i firm antywirusowych (warunek serwisu, zgoda Dawida).

## Decyzja formy (reguła spec 3.1 / plan Task 6 Step 7)

Wykrycia: remis (0 = 0). Mediana startu: jeden exe przekracza medianę folderu o 1,18 s (Pro) i 2,0 s (Home) – więcej niż próg
1 s. **Wydanie = Folder (zip).** VirusTotal (2026-10-26) potwierdza remis: 0 wykryć w obu wariantach, więc decyzja jest
ostateczna. Koszt dla użytkownika: zip trzeba rozpakować (Dawid: „tak będę robił”).

## Podpis cyfrowy – rekomendacja

Na Defenderze nie ma wykryć, więc podpis nie jest potrzebny do działania. Jedyny realny zysk to okno SmartScreen przy pliku
pobranym z internetu. Zasady reputacji SmartScreen dla certyfikatów OV, EV i usługi Trusted Signing (Azure) zmieniały się
(m.in. w 2024 r.) i trzeba je sprawdzić w aktualnych źródłach przed decyzją – stan niesprawdzony na 2026-10-26.
Rekomendacja: nie kupować przed publikacją; wrócić do tematu przy uruchomieniu strony i publicznych wydań
(mapa drogowa autora, „Później”), gdy pobierać będą obcy użytkownicy. Ceny i warunki certyfikatów – niesprawdzone w tym etapie.

## Plik z wydania roboczego v4.0.0 (2026-10-26)

Workflow `release.yml` z tagu `v4.0.0` (run 40262874168): testy, `Build-Release.ps1 -Variant Folder`, sprawdzenie SHA256, wydanie
robocze `DSOptimizer 4.0.0` z `DSOptimizer-4.0.0.zip` (3 689 669 B, SHA256 `6f3077a4…40da`, zgodny z `SHA256SUMS.txt` po pobraniu).
Kontrola na VM Pro (bez sieci): zip rozpakowany, `Zone.Identifier` `ZoneId=3` na wszystkich 16 plikach (jak po pobraniu z internetu
i rozpakowaniu Eksploratorem), `DSOptimizer.exe --list-install` kod 0, 34 pozycje, okno GUI uruchomione jako `dso` (Highest) działa –
Specyfikacja wczytana, dziennik 0 błędów. Biblioteki oznaczone MotW ładują się normalnie; samo kliknięcie „Uruchom mimo to” w oknie
SmartScreen to standardowa droga Windows (nie automatyzowana).

## Do potwierdzenia u klientów

Antywirusy innych firm (ESET, Avast, Norton itd. – dynamicznie niezmierzone), prawdziwy pendrive USB (FAT32/exFAT/NTFS), NAS
(UNC), polityki firmowe i reguły ASR.
