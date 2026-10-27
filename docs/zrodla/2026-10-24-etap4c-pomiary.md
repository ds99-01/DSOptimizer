# Etap 4c – pomiary VM z siecią: instalacja przez winget (2026-10-24, Task 2)

## Podsumowanie

- **Cała lista działa.** Na Pro uruchomiłem instalację wszystkich 33 pozycji ze spec pkt 4 (`winget show` = 0 dla każdej).
  28 z nich zakończyło się w fazie `install-all` kodem 0, a 5 dało `0x8A150061` („już zainstalowane”): Teams (AppX na świeżym
  26H2, przez winget nie instalowany), VC++ x64 (zależność LibreOffice) oraz Firefox, LibreOffice i 7-Zip (zainstalowane
  kodem 0 już w fazie `network`). Każda pozycja zainstalowana kodem 0 ma wpis Uninstall w odczycie tuż po instalacji, z
  wyjątkiem Discorda (wpis z opóźnieniem, niżej). Najdłuższa pojedyncza instalacja trwała 230 s (VLC na Home), więc limit
  30 min ma duży zapas.
- **`--locale pl-PL` szkodzi. Proponuję go usunąć.** 29 z 31 prób z `--locale` skończyło się `0x8A150010` (brak instalatora
  `pl-PL`). Teams i VC++ x64 nie doszły do wyboru instalatora (`0x8A150061`), a `pl` mają tylko Firefox i WinRAR. Próba
  kosztuje do 83 s na pozycję (na Pro razem 392 s). Gorzej z Firefoksem: z `--locale pl-PL` winget wybrał **starą wersję
  129.0.2** (ostatnią z instalatorem `pl`) zamiast aktualnej 157.0.1. To przestarzała przeglądarka z lukami. Polski
  instalator dostał tylko WinRAR.
- **Brak sieci ma jeszcze jeden kod.** Gdy źródło było już pobrane, instalacja offline kończy się `0x80072EE7` („InternetOpenUrl()
  failed”). Tego kodu nie ma w spec 5.3, a musi trafić do grupy „brak połączenia”.
- **Discord zapisuje wpis Uninstall z opóźnieniem.** Zaraz po kodzie 0 wpisu jeszcze nie ma (Home i Pro). Odczyt zwrotny musi
  chwilę poczekać, inaczej Discord dostanie „Nie potwierdzono”.
- **Administrator Protection da się rozpoznać.** Po `TypeOfAdminApprovalMode = 2` podniesiony proces działa jako ukryte konto
  `ADMIN_dso` (inny SID, profil `C:\Users\ADMIN_dso`). Tam nie ma aliasu winget, więc start się nie udaje. Obecna odmowa
  „inne konto” zadziała, a wartość 2 w rejestrze pozwala podać przyczynę.
- **Winget aktualizuje się sam.** W trakcie serii na Pro App Installer zaktualizował się ze Sklepu (v1.11.510 → v1.29.380).
  Nowa wersja wybrała inne instalatory (MSI zamiast EXE dla VLC i Notepad++) i nie rysuje paska postępu. Reguły `match`
  nie mogą więc zależeć od typu instalatora.
- **Z listy wypadają Teams i DirectX.** Teams jest już na świeżym 26H2 (AppX `MSTeams`), a jego instalacji przez winget nie
  sprawdziłem (MSIX). DirectX to dziś pakiet MSIX, czyli też bez pomiaru. RustDesk potwierdzony jako nieobecny
  (`0x8A150014`), Spotify bez zmian (`0x8A150056` ze spike'a). Kandydat spoza listy do przebiegu Home (T11):
  `winget:PuTTY.PuTTY`.

## Przebieg

Skrypt `tests/vm/explore/Invoke-E4CExploration.ps1` (commit `06f7c66`), VM `DSO-26H2-Home` (Core) i `DSO-26H2-Pro`
(Professional), build 26300.9457, pl-PL. Obie VM równolegle, każda od `czysta-26H2`. `Default Switch` był podpięty tylko
w fazach `network` i `install-all`. Na końcu revert, karta odłączona i VM `Off` (sprawdzone, `cleanup.Ok = true` w obu JSON).
Winget startował jako `dso` z zadania Harmonogramu (Interactive + Highest), z aliasu
`%LOCALAPPDATA%\Microsoft\WindowsApps\winget.exe`. Stdout i stderr szły bajtowo do plików. Instalacja zawsze pełnym poleceniem
z Global Constraints, z `--locale pl-PL` i jedną ponowną próbą bez `--locale` przy `0x8A150010`.

| VM | Start | Koniec | Fazy |
|---|---|---|---|
| Pro | 00:34:57 | 01:15:26 | baseline, offline-fresh, network (6,5 min), install-all (30 min), adminprotection (2 restarty, 1,5 min), final |
| Home | 00:34:57 | 00:54:46 | baseline, offline-fresh, network, install-all (8 pozycji), final |
| Home (dodatkowy) | 00:56:40 | 01:06:46 | baseline, network: dodatkowe wyszukiwania (`redistributable`, `visual c++`, `windows desktop runtime`, `microsoft`), bo trzy zapytania z zadania nie dały obcięcia „…” |

Surowe wyniki (poza gitem): zapis pomiaru w archiwum autora + `raw/` oraz
zapis pomiaru w archiwum autora. Stderr winget był pusty we wszystkich wywołaniach.

## Środowisko (baseline, bez sieci)

| | Home | Pro |
|---|---|---|
| `winget --version` | v1.11.510 | v1.11.510 (na końcu serii v1.29.380, niżej) |
| App Installer | 1.26.509.0 | 1.26.509.0 |
| `TypeOfAdminApprovalMode` / `EnableLUA` / `ConsentPromptBehaviorAdmin` | 1 / 1 / 5 | 1 / 1 / 5 |
| `InstalledUICulture`, `Get-WinSystemLocale` | pl-PL, pl-PL | pl-PL, pl-PL |
| Wpisy Uninstall (HKLM 64 + 32 + HKU dso) | 30 | 30 |
| Wolne miejsce C: | 40,3 GB | 39,7 GB → 21,5 GB po całej liście |

## Kody wyjścia (int ze znakiem, Home = Pro)

| Sytuacja | Polecenie | Kod | Tekst w stdout |
|---|---|---|---|
| Świeży system, bez sieci (źródło nigdy niepobrane) | `install` | `0x8A15000F` | `0x8a15000f : Data required by the source is missing` |
| j.w. | `search` | `0x8A15000F` | j.w. |
| j.w. | `list --id … --exact --source winget` | `0x8A15000F` | j.w. |
| j.w. | `list --id … --exact` (bez `--source`) | `0x8A15004B` | `Failed when opening source(s)…` |
| Z siecią | `show` istniejącego pakietu | 0 | `Found <nazwa> [<id>]`, `Installer Type: …` |
| j.w. | `show RustDesk.RustDesk` | `0x8A150014` | `No package found matching input criteria.` |
| j.w. | `search` bez wyników | `0x8A150014` | j.w. |
| j.w. | `install … --locale pl-PL` (brak instalatora pl-PL) | `0x8A150010` | `No applicable installer found; see logs for more details.` |
| j.w. | `install … --no-upgrade` na zainstalowanym (7-Zip, Firefox, LibreOffice, VC++ x64, Teams) | `0x8A150061` | `A package version is already installed. Installation cancelled.` |
| Bez sieci, źródło już pobrane | `install` (WinMerge, nigdy nieinstalowany) | **`0x80072EE7`** | `InternetOpenUrl() failed.` / `0x80072ee7 : unknown error` |
| j.w. | `search vlc` | **0** | wyniki z pamięci źródła (nie widać, że jest offline) |
| j.w. | `show 7zip.7zip` | 0 | pełny opis z pamięci |
| j.w. | `list --id 7zip.7zip --exact` (z `--source winget` i bez) | 0 | tabela z wierszem |
| j.w. | `list --id Discord.Discord --exact` (per user) | 0 | tabela z wierszem |

Wnioski:
- `--no-upgrade` na zainstalowanym daje **`0x8A150061`** (PACKAGE_ALREADY_INSTALLED), a nie `0x8A15002B` jak bez flagi w spike'u.
  Mapowanie spec 5.3 („Już zainstalowane”) pasuje.
- `0x80072EE7` (WININET_E_NAME_NOT_RESOLVED) to brak sieci po wcześniejszym pobraniu źródła. Spec 5.3 i 5.1 znają tylko
  `0x8A15000F`, `0x8A150107` i `0x8A150008`.
- `list --id <id> --exact` jako odczyt zwrotny (pozycje z wyszukiwania, spec 5.4) działa offline, gdy źródło jest pobrane.
  Na świeżym systemie bez sieci daje `0x8A15000F` (z `--source winget`) albo `0x8A15004B` (bez). Po udanej instalacji źródło
  zawsze już jest, więc odczyt przez `list` działa. Wariant z `--source winget` (forma programu) drukuje tabelę bez kolumny
  `Source`.

## Pozycje listy (spec pkt 4) – dowód

`show` z sieci (Pro, Home identycznie). Instalacja: Pro, faza `install-all` (dla Firefox, LibreOffice i 7-Zip faza `network`,
bo w `install-all` były już zainstalowane: `0x8A150061`). Kolumna „Próby”: `--locale pl-PL` → bez `--locale`. „Wpis” to nowe
widoczne wpisy Uninstall (bez `SystemComponent=1` i `ParentKeyName`) i nowe pakiety AppX. „list” = kody `list --id --exact` z
`--source winget` / bez. „log” = czy plik `--log` powstał.

| `wingetId` | show | typ (show) | wersja | Próby (Pro) | Wpis po instalacji (Pro) | list | log | Home |
|---|---|---|---|---|---|---|---|---|
| `Google.Chrome` | 0 | wix | 156.0.8078.48 | 0x8A150010 1s → 0 96s | HKLM: `Google Chrome` / `Google LLC` | 0/0 | tak | 0x8A150010 1s → 0 87s |
| `Mozilla.Firefox` | 0 | nullsoft | 157.0.1 | **0 26s, wersja 129.0.2** | HKLM: `Mozilla Firefox (x64 pl)` / `Mozilla`; HKLM: `Mozilla Maintenance Service` / `Mozilla` | 0/0 | nie | 0 23s (też 129.0.2) |
| `Brave.Brave` | 0 | exe | 155.1.97.56 | 0x8A150010 10s → 0 68s | **HKU**: `Brave` / `Autorzy Brave` (wydawca po polsku) | 0/0 | nie | |
| `Opera.Opera` | 0 | exe | 137.0.6036.39 | 0x8A150010 10s → 0 35s | **HKU**: `Opera Stable 137.0.6036.39` / `Opera Software` (po kilku min. sama się zaktualizowała do .63: stary wpis zniknął, nowy doszedł) | 0/0 | nie | |
| `Discord.Discord` | 0 | exe | 1.0.9265 | 0x8A150010 9s → 0 11s | **brak wpisu w odczycie tuż po kodzie 0** (Home i Pro); spike: **HKU** `Discord` / `Discord Inc.` odczytany później; `list` = 0 | 0/0 | nie | 0x8A150010 52s → 0 14s |
| `Telegram.TelegramDesktop` | 0 | inno | 7.3.3 | 0x8A150010 57s → 0 19s | **HKU**: `Telegram Desktop` / `Telegram FZ-LLC` | 0/0 | tak | |
| `OpenWhisperSystems.Signal` | 0 | nullsoft | 8.32.0 | 0x8A150010 83s → 0 42s | **HKU**: `Signal 8.32.0` / `Signal Messenger, LLC` | 0/0 | nie | |
| `Zoom.Zoom` | 0 | msi | 7.2.48556 | 0x8A150010 8s → 0 21s | HKLM: `Zoom Workplace (64-bit)` / `Zoom` | 0/0 | tak | |
| `Microsoft.Teams` | 0 | msix | 26198.304.4946.9672 | `0x8A150061` 0s (już jest) | – (AppX `MSTeams` jest na świeżym 26H2, research 4a) | 0/0 | nie | |
| `VideoLAN.VLC` | 0 | nullsoft | 3.0.24 | 0x8A150010 1s → 0 92s (v1.29: **MSI**) | HKLM: `VLC media player` / `VideoLAN` | 0/0 | tak | 0x8A150010 2s → 0 230s (EXE) |
| `CodecGuide.K-LiteCodecPack.Standard` | 0 | inno | 20.1.0 | 0x8A150010 28s → 0 103s | HKLM32: `K-Lite Codec Pack 20.1.0 Standard` / `KLCP` | 0/0 | tak | |
| `PeterPawlowski.foobar2000` | 0 | nullsoft | 2.26.1 | 0x8A150010 14s → 0 19s | HKLM: `foobar2000 v2.26.1 (x64)` / `Peter Pawlowski` | 0/0 | nie | |
| `TheDocumentFoundation.LibreOffice` | 0 | msi | 26.8.1.1 | 0x8A150010 6s → 0 118s | HKLM: `LibreOffice 26.8.1.1` / `The Document Foundation` + zależność VC++ x64 (HKLM32 + 2 wpisy `SystemComponent`) | 0/0 | tak | 0x8A150010 3s → 0 104s |
| `Adobe.Acrobat.Reader.64-bit` | 0 | exe | 26.002.22031 | 0x8A150010 32s → 0 113s | HKLM: `Adobe Acrobat (64-bit)` / `Adobe` + AppX `AdobeAcrobatReaderCoreApp` (konto i obraz) | 0/0 | tak | |
| `SumatraPDF.SumatraPDF` | 0 | exe | 3.6.1 | 0x8A150010 5s → 0 7s | **HKU**: `SumatraPDF` / `Krzysztof Kowalczyk` | 0/0 | nie | 0x8A150010 2s → 0 34s (HKU) |
| `7zip.7zip` | 0 | exe | 26.05 | 0x8A150010 3s → 0 7s | HKLM: `7-Zip 26.05 (x64)` / `Igor Pavlov` | 0/0 | nie | 0x8A150010 5s → 0 6s |
| `Notepad++.Notepad++` | 0 | nullsoft | 8.9.8.1 | 0x8A150010 2s → 0 8s (v1.29: **MSI**) | HKLM: `Notepad++ (x64)` / `Notepad++ team (MSI installer)` | 0/0 | tak | 0x8A150010 6s → 0 26s (EXE): HKLM `Notepad++ (64-bit x64)` / `Notepad++ Team` + AppX `NotepadPlusPlus` |
| `voidtools.Everything` | 0 | wix | 1.4.1.1032 | 0x8A150010 6s → 0 7s | HKLM: `Everything 1.4.1.1032 (x64)` / `voidtools` | 0/0 | tak | |
| `Microsoft.PowerToys` | 0 | burn | 0.101.2362.0 | 0x8A150010 34s → 0 57s | **HKU**: `PowerToys (Preview) x64` / `Microsoft Corporation` (+ HKLM `PowerToys (Preview)` z `SystemComponent=1`, 5 pakietów AppX PowerToys) | 0/0 | tak | |
| `RARLab.WinRAR` | 0 | exe | 7.23.0 | **0 5s z `--locale pl-PL`** | HKLM: `WinRAR 7.23 (64-bitowy)` / `win.rar GmbH` + AppX `WinRAR.ShellExtension` | 0/0 | nie | |
| `ShareX.ShareX` | 0 | inno | 21.0.0 | 0x8A150010 9s → 0 26s | HKLM: `ShareX` / `ShareX Team` | 0/0 | tak | |
| `CPUID.CPU-Z` | 0 | inno | 3.01 | 0x8A150010 12s → 0 5s | HKLM: `CPUID CPU-Z 3.01` / `CPUID, Inc.` | 0/0 | tak | |
| `CPUID.HWMonitor` | 0 | inno | 1.68 | 0x8A150010 9s → 0 5s | HKLM: `CPUID HWMonitor 1.68` / `CPUID, Inc.` | 0/0 | tak | |
| `TechPowerUp.GPU-Z` | 0 | exe | 2.71.0 | 0x8A150010 5s → 0 23s | HKLM32: `TechPowerUp GPU-Z` / `TechPowerUp` | 0/0 | nie | |
| `AnyDesk.AnyDesk` | 0 | exe | 9.8.2 | 0x8A150010 0s → 0 12s | HKLM32: `AnyDesk` / `AnyDesk Software GmbH` (wersja `ad 9.8.2`) | 0/0 | nie | 0x8A150010 0s → 0 65s |
| `TeamViewer.TeamViewer` | 0 | wix (zip) | 15.82.6 | 0x8A150010 15s → 0 21s | HKLM: `TeamViewer` / `TeamViewer` | 0/0 | tak | |
| `Valve.Steam` | 0 | nullsoft | 2.10.91.91 | 0x8A150010 0s → 0 5s | HKLM32: `Steam` / `Valve Corporation` | 0/0 | nie | 0x8A150010 1s → 0 8s |
| `EpicGames.EpicGamesLauncher` | 0 | wix | 1.3.210.0 | 0x8A150010 5s → 0 54s | HKLM: `Epic Games Launcher` / `Epic Games, Inc.` + `Microsoft GameInput` / `Microsoft Corporation` | 0/0 | tak | |
| `GOG.Galaxy` | 0 | inno | 2.1.10.56 | 0x8A150010 9s → 0 42s | HKLM: `GOG GALAXY` / `GOG.com` | 0/0 | tak | |
| `Microsoft.VCRedist.2015+.x64` | 0 | burn | 14.51.36247.0 | `0x8A150061` (zależność LibreOffice, wyżej; spike: 0 7s) | HKLM32: `Microsoft Visual C++ v14 Redistributable (x64) - 14.51.36247` / `Microsoft Corporation` (+ HKLM `… X64 Additional/Minimum Runtime`, `SystemComponent=1`) | 0/0 | nie | `0x8A150061` |
| `Microsoft.VCRedist.2015+.x86` | 0 | burn | 14.51.36247.0 | 0x8A150010 8s → 0 9s | HKLM32: `Microsoft Visual C++ v14 Redistributable (x86) - 14.51.36247` / `Microsoft Corporation` (+ 2 × `SystemComponent=1`) | 0/0 | tak | |
| `Microsoft.DotNet.DesktopRuntime.8` | 0 | burn | 8.0.32 | 0x8A150010 6s → 0 13s | HKLM32: `Microsoft Windows Desktop Runtime - 8.0.32 (x64)` / `Microsoft Corporation` (+ 4 × HKLM `SystemComponent=1`) | 0/0 | tak | |
| `Microsoft.DotNet.DesktopRuntime.9` | 0 | burn | 9.0.21 | 0x8A150010 5s → 0 11s | HKLM32: `Microsoft Windows Desktop Runtime - 9.0.21 (x64)` / `Microsoft Corporation` (+ 4 × HKLM `SystemComponent=1`) | 0/0 | tak | |
| `Spotify.Spotify` (odrzucony) | 0 | exe | 1.3.6.1921 | – (spike: `0x8A150056`) | | | | |
| `RustDesk.RustDesk` (odrzucony) | `0x8A150014` | | | – | | | | |
| `Microsoft.DirectX` (odrzucony) | 0 | **msix** | 9.29.1974.0 | – | | | | |

Uwagi do tabeli:
- Przy .NET 8 w tym samym oknie pojawił się wpis `Microsoft Windows Application Compatibility Fix Database` (HKLM, bez wydawcy).
  To aktualizacja systemu w tle, nie instalator .NET. Podobne zmiany przy Chrome, Firefox i Brave (`changed=1`) to aktualizatory
  innych programów.
- Zależności winget: LibreOffice doinstalował `Microsoft.VCRedist.2015+.x64` (linie `This package requires the following
  dependencies:` i `(1/1) Found …`). Epic doinstalował `Microsoft GameInput` (zrobił to własny instalator, bez linii zależności).
- Gałąź **HKU** (per user, tylko konto `dso`): Brave, Opera, Discord, Telegram, Signal, SumatraPDF, PowerToys. **HKLM32**
  (WOW6432Node): K-Lite, GPU-Z, AnyDesk, Steam, VC++ x64/x86, .NET Desktop Runtime 8/9. Reszta: HKLM 64.

## Najdłuższe instalacje (limit 30 min)

Najdłuższa pojedyncza próba to VLC na Home (EXE): 230,1 s. Na Pro: LibreOffice 117,8 s (dodatkowy przebieg Home: 163,8 s),
Adobe 113 s, K-Lite 103 s. Cała lista na Pro (33 pozycje) zajęła 30 min. Żadna próba nie przekroczyła limitu (`TimedOut = false`
wszędzie, brak plików `.late.json`). W fazie `final` nie było procesów instalatorów w tle. **Limit 30 min zostaje.**

## `--locale pl-PL`

- Instalator dla `pl-PL` miały tylko 2 pakiety: Firefox i WinRAR. Z 31 prób, które doszły do wyboru instalatora (bez Teams
  i VC++ x64, oba `0x8A150061`), 29 dało `0x8A150010`, a ponowna próba bez `--locale` się udaje.
- Firefox z `--locale pl-PL`: `Found Mozilla Firefox [Mozilla.Firefox] Version 129.0.2`, pobrany
  `…/releases/129.0.2/win64/pl/Firefox Setup 129.0.2.exe`, choć `show` podaje aktualną 157.0.1 (en-US). Winget wybiera
  najnowszą wersję, **która ma instalator w danym języku**, a nie najnowszą wersję pakietu. Powtórzone trzy razy (Home, Pro,
  Home dodatkowy). Potem `--no-upgrade` trzyma tę starą wersję (`0x8A150061` w `install-all`).
- Koszt: na Pro 392 s nieudanych prób `--locale`. Pojedyncza próba trwała do 83 s (Signal), 57 s (Telegram) i 52 s (Discord
  na Home), czyli nie zawsze kończy się od razu.
- LibreOffice: `0x8A150010`, więc spec 5.1 dostaje odpowiedź, że ponowienie działa. Wersja domyślna jest wielojęzyczna (MSI).

## Wyszukiwanie (spec 7)

- Nagłówek `Name  Id  Version  [Match]  [Source]` po angielsku mimo pl-PL. Linia kresek bez przerw, szerokość kolumn = najdłuższa
  wartość + spacja. Kolumna `Match` pojawia się, gdy wynik pasuje po tagu (`Tag: putty`).
- **Obcinanie „…” (U+2026):** pojawia się, gdy wiersz przekroczyłby 119 znaków. Przy przekierowanym wyjściu winget formatuje do
  120 kolumn. `redistributable`: 17 wierszy, 16 obciętych nazw (`Microsoft Visual C++ 2010 x64 Redistribu…`) i 1 obcięty Id
  (`Microsoft.DotNet.Framework.Redistributab…`). `microsoft` (`--count 50`): 6 nazw i 2 Id (`nkdAgility.AzureDevOpsMigrationTools.P…`,
  `Microsoft.Azure.ArtifactSigningClientT…`). Obcięty Id stoi w kolumnie Id i nie przesuwa kolumn. Długie, ale nieobcięte Id
  (`CoreyHayward.InputTyperforCommandPalette`) wypełniają kolumnę równo do początku następnej.
- Zapytania z zadania: `Microsoft Visual C++ Redistributable` i `runtime desktop` dają `0x8A150014` (fraza nie występuje
  w nazwie, Id ani tagu), a `codec pack` daje 4 wiersze bez obcięć.
- Spinner przed tabelą: `\r   - \r` + 120 spacji + `\r`, potem nagłówek w tej samej „linii” do `\r\n`. Parser dzieli na linie
  także po samym `\r` (`ConsoleLineSplitter`), wtedy nagłówek jest osobną linią.
- Offline z pobranym źródłem `search` zwraca 0 i wyniki z pamięci. Program nie odróżni tego od wyszukiwania online.

## Wyjście instalacji i postęp

- winget **v1.11.510**: spinner, linie paska `█▒` + `X MB / Y MB` rozdzielone samym `\r` (VLC: 4,4 KB wyjścia, LibreOffice: 282 × `\r`).
- winget **v1.29.380** (Pro po aktualizacji): przekierowane wyjście instalacji **bez spinnera i bez paska** (VLC MSI: 382 B,
  zero samych `\r`). Zostają linie `Found …`, `Downloading <url>`, `Successfully verified installer hash`,
  `Starting package install...`, `Successfully installed`. Etap da się więc pokazać zawsze, a procent tylko w starszym winget.
- `--log <plik>` to **log instalatora** („if supported”), a nie log winget. To, czy plik powstaje, zależy od pozycji, a nie
  od samego typu instalatora (Adobe ma instalator `exe`, a log powstał). Plik powstał dla: Chrome, Zoom, VLC MSI, K-Lite,
  LibreOffice, Adobe, Notepad++ MSI, Everything, PowerToys, ShareX, CPU-Z, HWMonitor, TeamViewer, Epic, GOG, Telegram, VC++ x86,
  .NET. Nie powstał dla: Firefox, Brave, Opera, Discord, Signal, foobar2000, SumatraPDF, 7-Zip, WinRAR, GPU-Z, AnyDesk, Steam,
  ani dla prób zakończonych `0x8A150010`.

## Aktualizacja App Installera w trakcie serii (Pro)

Przy pozycji Telegram (ok. 00:50) diff AppX pokazał nowe `Microsoft.DesktopAppInstaller` 1.29.379.0 (konto) i 2026.917.151.0
(obraz), a do tego `Microsoft.WindowsAppRuntime.1.8` i `VCLibs`. Na końcu `winget --version` (Limited) = **v1.29.380**. Sklep
zaktualizował App Installer, gdy VM była online. Skutki:
- dalsze instalacje szły nowym winget, który wybrał MSI zamiast EXE (VLC, Notepad++), więc wpisy Uninstall różnią się od Home;
- nowy winget nie rysuje paska postępu;
- kody (`0x8A150010`, `0x8A150061`, `0x80072EE7` w `final`) się nie zmieniły.
Na Home (krótszy czas online) aktualizacji nie było widać.

## Administrator Protection (Pro)

`TypeOfAdminApprovalMode` 1 → 2 (API .NET, HKLM 64), restart. Potem z zadania Harmonogramu jako `dso`:

| RunLevel | SID procesu | Konto | Integralność | `LOCALAPPDATA` | alias winget | `winget --version` |
|---|---|---|---|---|---|---|
| Highest | `…-1002` (≠ dso `…-1000`) | `DSO-26H2-PRO\ADMIN_dso` | wysoka (S-1-16-12288) | `C:\Users\ADMIN_dso\AppData\Local` | **nie istnieje** | start nieudany: „Nie można odnaleźć określonego pliku” |
| Limited | `…-1000` (= dso) | `DSO-26H2-PRO\dso` | średnia (S-1-16-8192) | `C:\Users\dso\AppData\Local` | jest | v1.29.380 |

- Ukryte konto `ADMIN_dso` **nie** pokazuje się w `Get-LocalUser`, ale ma profil w `ProfileList` (`C:\Users\ADMIN_dso`).
- Rozpoznanie: tak. `TypeOfAdminApprovalMode = 2` w `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System` plus
  SID procesu ≠ SID zalogowanego (i nazwa `ADMIN_<konto>`). Obecna reguła „inne konto” odmawia sama, a wartość 2 pozwala
  dopisać przyczynę („Administrator Protection”).
- Wartość przywrócona do 1 i restart, potem revert. Faza `final` przeszła już jako `dso` (SID `…-1000`).

## Kandydat spoza listy (T11, Home)

`search --query putty` zwraca `PuTTY  PuTTY.PuTTY  0.85.0.0` (pierwszy wiersz, bez `Match`).
`install --id PuTTY.PuTTY` (Home i Pro): `0x8A150010` z `--locale`, potem 0 w 5 s. Wpis HKLM: `PuTTY release 0.85 (64-bit)` /
`Simon Tatham`, a `list` = 0/0. **Kandydat: `winget:PuTTY.PuTTY`** (MSI, 4 MB, per machine).

## Próbki (`tests/DSOptimizer.Tests.Unit/Install/Samples/`)

Surowy stdout winget bajt w bajt (`.gitattributes`: `binary`), bez danych hosta. Kod wyjścia w nawiasie.

| Plik | Źródło | Kod |
|---|---|---|
| `e4c-version-home.bin` | `--version` (v1.11.510) | 0 |
| `e4c-version-1-29-pro.bin` | `--version` po aktualizacji (v1.29.380) | 0 |
| `e4c-offline-fresh-install-home.bin` | `install 7zip.7zip`, świeży system bez sieci | `0x8A15000F` |
| `e4c-offline-fresh-search-home.bin` | `search vlc`, j.w. | `0x8A15000F` |
| `e4c-offline-fresh-list-source-home.bin` | `list --id 7zip.7zip --exact --source winget`, j.w. | `0x8A15000F` |
| `e4c-offline-fresh-list-plain-home.bin` | `list --id 7zip.7zip --exact`, j.w. | `0x8A15004B` |
| `e4c-show-vlc-home.bin` | `show VideoLAN.VLC` | 0 |
| `e4c-show-notfound-rustdesk-home.bin` | `show RustDesk.RustDesk` | `0x8A150014` |
| `e4c-search-codec-pack-home.bin` | `search "codec pack"` (4 wiersze, bez `Match`) | 0 |
| `e4c-search-putty-match-home.bin` | `search putty` (kolumna `Match`) | 0 |
| `e4c-search-noresults-home.bin` | `search "Microsoft Visual C++ Redistributable"` | `0x8A150014` |
| `e4c-search-truncated-redistributable-home.bin` | `search redistributable` (16 nazw i 1 Id z „…”) | 0 |
| `e4c-search-truncated-microsoft-home.bin` | `search microsoft` (50 wierszy i linia `<additional entries truncated due to result limit>`, 6 nazw i 2 Id z „…”) | 0 |
| `e4c-install-locale-noapplicable-home.bin` | `install Discord.Discord --locale pl-PL` | `0x8A150010` |
| `e4c-install-locale-firefox-pl-home.bin` | `install Mozilla.Firefox --locale pl-PL` (wersja 129.0.2) | 0 |
| `e4c-install-success-vlc-home.bin` | `install VideoLAN.VLC` (v1.11, spinner i pasek) | 0 |
| `e4c-install-success-libreoffice-home.bin` | `install TheDocumentFoundation.LibreOffice` (zależność VC++, dwa pobrania) | 0 |
| `e4c-install-success-msi-vlc-pro.bin` | `install VideoLAN.VLC` (v1.29, MSI, bez paska) | 0 |
| `e4c-install-already-noupgrade-home.bin` | `install 7zip.7zip --no-upgrade` drugi raz | `0x8A150061` |
| `e4c-install-already-teams-pro.bin` | `install Microsoft.Teams` (wstępnie zainstalowany) | `0x8A150061` |
| `e4c-install-offline-cached-home.bin` | `install WinMerge.WinMerge` offline, źródło pobrane | `0x80072EE7` |
| `e4c-search-offline-cached-home.bin` | `search vlc` offline, źródło pobrane | 0 |
| `e4c-list-installed-source-home.bin` | `list --id 7zip.7zip --exact --source winget` (offline) | 0 |
| `e4c-list-installed-plain-home.bin` | `list --id 7zip.7zip --exact` (offline) | 0 |
| `e4c-list-peruser-discord-home.bin` | `list --id Discord.Discord --exact --source winget` (per user, offline) | 0 |

## Decyzja

Pomiar → Home → Pro → wnioski dla kolejnych Tasków. Punkty 1–3 zmieniają spec i idą do akceptacji autora.
Do tego czasu implementacja trzyma się ich jako zalecenia.

1. **Bez `--locale` (zmiana spec 5.1, decyzja 6).** Polecenie instalacji bez `--locale`. `WingetArguments.Install(id, null, log)`
   już to obsługuje. Uzasadnienie: Firefox 129.0.2 zamiast 157.0.1 to realne ryzyko bezpieczeństwa, a 29 z 31 prób z `--locale`
   i tak wraca do wersji domyślnej, kosztem do 83 s na pozycję. Wynik „zainstalowano w wersji domyślnej (brak wersji <język>)” znika.
   Alternatywa (gorsza): `--locale` tylko wtedy, gdy wersja z `Found …` = wersja z `show`. Jeśli Dawid chce zostawić `--locale`,
   T5 musi przynajmniej porównać te wersje.
2. **`0x80072EE7` = brak sieci (spec 5.1 i 5.3).** Dopisać do `WingetExitCodes.IsNetwork` / `NoNetwork` (T3) i do warunku
   „pierwsza pozycja serii bez sieci kończy serię” (T5). Przegląd T3 może rozważyć całą rodzinę WinINet `0x80072EE2`–`0x80072EFF`.
   Zmierzony jest tylko `…EE7`.
3. **Odczyt zwrotny z oknem czasu (spec 5.3, T5).** Po kodzie 0 brak dopasowania przy pierwszym odczycie nie oznacza jeszcze
   „Nie potwierdzono”. Discord (Squirrel) zapisuje wpis HKU po wyjściu winget. Zalecenie: jak weryfikacja w `AppRemovalStep`,
   świeży odczyt co 1 s do 60 s, a „Nie potwierdzono” dopiero po oknie.
4. **Reguły `match` niezależne od wersji, gałęzi i typu instalatora** (tabela w `docs/apps/install-README.md`). Opera zmienia wersję
   w `DisplayName` sama po kilku minutach. Notepad++ ma inną nazwę i wydawcę w EXE i w MSI. Brave ma wydawcę po polsku
   („Autorzy Brave”), więc reguła tylko po `displayName`.
5. **Teams i DirectX wypadają z listy.** Teams jest na świeżym 26H2 (AppX `MSTeams`, `0x8A150061`), a instalacji MSIX przez winget
   nie potwierdzono. Na liście dostałby tylko status „Zainstalowana”, a strona „Aplikacje” (4a) sugeruje jego usunięcie.
   DirectX ma dziś instalator MSIX (nie ten ze spec) i nie był instalowany. RustDesk: `0x8A150014`. Spotify: `0x8A150056`
   (spike). Lista do T9: **32 pozycje** (33 zmierzone minus Teams; DirectX, RustDesk i Spotify nie należały do 33
   instalowanych). Wcześniejsza liczba „31” odejmowała DirectX drugi raz (poprawka w T9).
6. **Postęp (T3/T5/T6):** etap z linii `Downloading` / `Starting package install` zawsze, procent tylko gdy jest pasek (v1.11). Brak
   paska w v1.29 to stan normalny, a nie błąd parsera. `FoundVersion` musi brać linię z właściwym `[wingetId]`, bo zależności
   dają linie `(1/1) Found …`.
7. **`--log`** to log instalatora i nie zawsze powstaje. Wynik pozycji podaje ścieżkę tylko, gdy plik istnieje. Pełne wyjście winget
   i tak trafia do logu DSOptimizera (spec 5.2).
8. **Administrator Protection:** odmowa „inne konto” działa bez zmian. Gdy `TypeOfAdminApprovalMode = 2`, wyjaśnienie może dodać
   przyczynę (spec 2, „jeśli pomiar pokaże, że da się ją rozpoznać”: da się).
9. **Limit 30 min zostaje** (maksimum 230 s).
10. **Ostrzeżenia `warning` z pomiaru:** instalacja tylko dla bieżącego konta (HKU: Brave, Opera, Discord, Telegram, Signal,
    SumatraPDF, PowerToys), dodatkowe składniki (LibreOffice → VC++ x64, Epic → Microsoft GameInput, Adobe → pakiet AppX)
    i WinRAR płatny po okresie próbnym (spec). Tekst ostrzeżeń ustala T9.

## Do potwierdzenia na sprzęcie / później

- Nie zmierzono: instalacji MSIX przez winget (Teams, DirectX), `0x8A150056` na v1.29, zachowania przy zerwaniu sieci w środku
  pobierania, kodów innych błędów WinINet.
- Aktualizacja App Installera ze Sklepu w trakcie serii: na komputerze klienta może się zdarzyć tak samo. Pozycja trwająca w chwili
  aktualizacji przeszła bez błędu (Telegram), ale tego nie wymuszałem.
