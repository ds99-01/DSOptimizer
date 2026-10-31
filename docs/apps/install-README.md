# Instalacja aplikacji – lista `apps.json`

Plik `src/DSOptimizer.Catalog/catalog/apps.json` to lista aplikacji strony „Instalacja” (Etap 4c). Ładuje go
`AppsInstallCatalog.Load` (Core). Jest osadzony jako osobny zasób `BuiltInCatalog.AppsInstallSource`
(`DSOptimizer.AppsInstall.apps.json`) i, tak jak `apps-bloat.json`, nie należy do `BuiltInCatalog.Sources` (katalog tweaków).
Program sam niczego nie pobiera. Instaluje winget, wyłącznie ze źródła `winget` (`--source winget`), z weryfikacją hasha
instalatora z manifestu. Wyjątek na tej liście: jedna pozycja pobierana przez program: RustDesk z GitHuba, reguły w `RustDeskSource`
(jedno repozytorium, jeden wydawca podpisu, reguły w kodzie,
nie w tym pliku; dowód w sekcji „RustDesk z GitHuba (spike 2026-10-24)”). Drugi wyjątek jest poza tą listą: `winutil.ps1`
z `ChrisTitusTech/winutil`, reguły w `WinUtilSource`.

## Format

```json
{
  "entries": [
    {
      "id": "install.7zip",
      "wingetId": "7zip.7zip",
      "title": "7-Zip",
      "summary": "Program do pakowania i rozpakowywania archiwów (7z, zip, rar i inne).",
      "category": "tools",
      "match": { "displayName": "7-Zip*", "publisher": "Igor Pavlov" },
      "warning": "opcjonalnie",
      "source": "https://github.com/microsoft/winget-pkgs/tree/master/manifests/7/7zip/7zip"
    }
  ]
}
```

- `id`: stabilny identyfikator w programie, wzorzec `^install\.[a-z0-9-]+$`, unikalny bez rozróżniania wielkości liter.
- `wingetId`: dokładny identyfikator pakietu w źródle `winget`, instalowany zawsze z `--exact`. Wzorzec
  `WingetArguments.IsValidWingetId` (`^[A-Za-z0-9][A-Za-z0-9.+_-]{0,127}$`), unikalny bez rozróżniania wielkości liter.
- `github`: repozytorium GitHuba, z którego wydania program sam pobiera instalator – zamiast `wingetId` (wpis ma dokładnie jedno
  z nich). Dozwolone wyłącznie dla pozycji z listy w kodzie (`GitHubSources`): dziś tylko `install.rustdesk` → `rustdesk/rustdesk`
  (porównanie dokładne); każda inna para = błąd pliku. Adres pobrania, plik, skrót i podpis sprawdza `RustDeskSource`, nie JSON.
- `title`: nazwa w programie, najwyżej 60 znaków.
- `summary`: jedno zdanie po polsku (co to jest), najwyżej 160 znaków.
- `category`: jedna z wartości `browsers` (Przeglądarki), `messengers` (Komunikatory), `multimedia` (Multimedia),
  `documents` (Dokumenty), `tools` (Narzędzia), `remote-help` (Zdalna pomoc), `games` (Gry), `runtimes` (Środowiska
  uruchomieniowe). Kolejność = kolejność w UI (`InstallCategory`), nazwy wyświetlane z `InstallText.CategoryName`.
  Wpisy w pliku idą w tej samej kolejności kategorii.
- `match`: reguła „czy już zainstalowane”. Format i kod dopasowania są te same co w `apps-bloat.json`: wzorce z `*`, bez regex,
  bez rozróżniania wielkości liter. Reguła ma `packageName` (tylko AppX) **albo** `displayName` i/lub `publisher` (tylko
  Win32; gdy podane są oba, muszą pasować oba). `packageName` razem z `displayName` = błąd. Jedna reguła na wpis. Dopasowanie
  działa na **surowym** odczycie listy zainstalowanych (przed klasyfikacją strony „Aplikacje”), więc wpisy z `SystemComponent`
  i `ParentKeyName` też się liczą. Reguła pochodzi z prawdziwego wpisu Uninstall po instalacji na VM (tabela niżej). Gdy wpisów
  jest kilka (pakiet + składniki MSI), bierzemy ten widoczny dla użytkownika.
- `warning`: opcjonalne ostrzeżenie przy pozycji i w potwierdzeniu serii.
- `source`: bezwzględny adres `https` (strona producenta albo manifest w `microsoft/winget-pkgs`; pozycja `github` – strona wydań
  repozytorium, `https://github.com/rustdesk/rustdesk/releases`).

Pola zaznaczenia ani presetów nie ma (decyzja autora 2026-10-15: nic nie zaznacza się samo). Test
`AppsInstallCatalogTests.BuiltIn_NoEntryPreselected` pilnuje, że wbudowany plik nie ma innych pól niż powyższe.

Błąd w pliku = `InstallCatalogException(entryId, field, message)` z komunikatem `apps: <id>, pole <pole>: <treść>`. Ten sam
wyjątek dostaje duplikat `id` albo `wingetId` i nieznana kategoria.

## Zasady reguł `match` (z pomiaru)

- Bez wersji w regule. Opera sama zmienia wersję w `DisplayName` kilka minut po instalacji (`Opera Stable 137.0.6036.39` →
  `.63`), a numery wersji mają też 7-Zip, LibreOffice, K-Lite, foobar2000, Everything, CPU-Z, HWMonitor, Signal, VC++ i .NET.
- Bez założeń o typie instalatora. Winget po aktualizacji App Installera (v1.11.510 → v1.29.380 w trakcie serii na Pro) wybrał
  MSI zamiast EXE dla VLC i Notepad++. VLC ma w obu wariantach ten sam wpis (`VLC media player` / `VideoLAN`). Notepad++ ma
  inną nazwę i wydawcę: EXE (Home, v1.11) `Notepad++ (64-bit x64)` / `Notepad++ Team`, MSI (Pro, v1.29) `Notepad++ (x64)` /
  `Notepad++ team (MSI installer)`. Format dopuszcza jedną regułę na wpis, więc reguła obejmuje oba warianty wzorcami:
  `displayName: "Notepad++*"`, `publisher: "Notepad++*"` (bez zmiany formatu).
- Bez wydawcy, gdy jest tłumaczony. Brave zapisuje `Publisher` = `Autorzy Brave` na polskim systemie, więc reguła ma sam
  `displayName: "Brave"` (dokładnie).
- Język w nazwie. Firefox zapisuje `Mozilla Firefox (x64 pl)` (pomiar z `--locale pl-PL`) albo `Mozilla Firefox (x64 en-US)`
  (spike, instalator domyślny). Reguła `Mozilla Firefox (*` obejmuje oba i nie łapie `Mozilla Maintenance Service`.
- Architektura w nazwie. Środowiska uruchomieniowe mają `(x64)`/`(x86)` w regule, więc środowisko x86 nie oznacza pozycji x64
  jako zainstalowanej (`Microsoft Windows Desktop Runtime - 8.* (x64)`, `Microsoft Visual C++ *Redistributable (x64) - 14.*`).
  .NET 8 i 9 zostawiają ten sam widoczny wpis w HKLM32 i wpis `SystemComponent=1` w HKLM 64; oba pasują do reguły.
- Gałąź (HKLM 64/32, HKU) nie wchodzi do reguły. Czytnik listy z 4a czyta wszystkie trzy.
- Reguły są tylko Win32. Pakiety AppX doinstalowane obok (NotepadPlusPlus, WinRAR.ShellExtension, AdobeAcrobatReaderCoreApp,
  pakiety PowerToys) nie dają statusu.
- Żadna reguła nie pasuje do wpisów świeżego 26H2 (Microsoft Edge, Edge Update, Edge WebView2 Runtime) ani do składników
  doinstalowanych obok (Mozilla Maintenance Service, Microsoft GameInput, `Microsoft Visual C++ 2022 X64 Minimum Runtime`,
  `Microsoft .NET Runtime`). Reguła Foxit PDF Reader (`Foxit PDF Reader` / `Foxit Software Inc.`, dokładnie) nie łapie płatnego
  edytora tego samego wydawcy (`Foxit PDF Editor`, winget `Foxit.PhantomPDF`; test negatywny na nazwie syntetycznej). Pilnuje tego test `AppsInstallCatalogTests.BuiltIn_MatchFromMeasuredUninstallEntry` na wpisach
  z pomiaru.

## Lista – pozycje z dowodem (35)

Dowód: pomiar VM 2026-10-24 (`docs/zrodla/2026-10-24-etap4c-pomiary.md`, surowe wyniki poza gitem). `show` = 0 na Home i Pro dla każdej pozycji (w nawiasie typ instalatora
z `show`). „Pro”/„Home” = kod i czas próby bez `--locale` (polecenie programu); `0x8A150061` = już zainstalowane
w fazie `network` albo jako zależność. „Zakres” = gałąź zmierzonego wpisu Uninstall (`winget show` v1.11 nie drukuje zakresu
instalatora): HKU = tylko bieżące konto, HKLM/HKLM32 = cały komputer. „Wpis” = widoczny wpis Uninstall (`DisplayName` /
`Publisher`) po instalacji. `source` = katalog manifestu w `microsoft/winget-pkgs` (istnienie sprawdzone przez GitHub API
2026-10-24 dla każdej pozycji). Foxit PDF Reader doszedł później tego samego dnia (prośba autora): pomiar winget (tylko Pro) i przebieg CLI, surowe wyniki poza gitem.
RustDesk (`install.rustdesk`) nie ma pakietu winget: w kolumnie `wingetId` stoi repozytorium `github`, `show` nie dotyczy, a dowód
to spike VM Home (sekcja „RustDesk z GitHuba (spike 2026-10-24)”); kolumny Pro, Home i „przebieg CLI” pochodzą z przebiegu CLI
z siecią (plan RustDesk, Task 7 – akapit „RustDesk: przebieg CLI z siecią” niżej; czas `msiexec` z logu MSI `/l*v`).

| `id` | `wingetId` | kategoria | `show` | zakres | Pro | Home | przebieg CLI | wpis | `match` | werdykt | `source` |
|---|---|---|---|---|---|---|---|---|---|---|---|
| `install.chrome` | `Google.Chrome` | browsers | 0 (wix) | komputer (HKLM) | 0 (96 s) | 0 (87 s) | Pro: `Installed` (86 s) | `Google Chrome` / `Google LLC` | `displayName: "Google Chrome"`, `publisher: "Google LLC"` | wchodzi | https://github.com/microsoft/winget-pkgs/tree/master/manifests/g/Google/Chrome |
| `install.firefox` | `Mozilla.Firefox` | browsers | 0 (nullsoft) | komputer (HKLM) | 0 (26 s, faza `network`, z `--locale` → 129.0.2) | 0 (23 s, j.w.) | Pro: `Installed` (24 s); Home: `Installed` (24 s) | `Mozilla Firefox (x64 pl)` / `Mozilla`; spike bez `--locale`: `Mozilla Firefox (x64 en-US)` 157.0.1 | `displayName: "Mozilla Firefox (*"`, `publisher: "Mozilla"` | wchodzi (instaluje też `Mozilla Maintenance Service`) | https://github.com/microsoft/winget-pkgs/tree/master/manifests/m/Mozilla/Firefox |
| `install.brave` | `Brave.Brave` | browsers | 0 (exe) | konto (HKU) | 0 (68 s) | – | Pro: `Installed` (44 s) | `Brave` / `Autorzy Brave` | `displayName: "Brave"` | wchodzi | https://github.com/microsoft/winget-pkgs/tree/master/manifests/b/Brave/Brave |
| `install.opera` | `Opera.Opera` | browsers | 0 (exe) | konto (HKU) | 0 (35 s) | – | Pro: `Installed` (46 s) | `Opera Stable 137.0.6036.39` / `Opera Software` (po kilku min. `.63`) | `displayName: "Opera Stable*"`, `publisher: "Opera Software"` | wchodzi | https://github.com/microsoft/winget-pkgs/tree/master/manifests/o/Opera/Opera |
| `install.discord` | `Discord.Discord` | messengers | 0 (exe) | konto (HKU) | 0 (11 s) | 0 (14 s) | Pro: `Installed` (33 s); Home: `Installed` (25 s) | `Discord` / `Discord Inc.` (spike); w odczycie tuż po kodzie 0 wpisu jeszcze nie ma, `list --id --exact` kilka sekund później = 0 z nazwą `Discord` (Home i Pro) | `displayName: "Discord"`, `publisher: "Discord Inc."` | wchodzi (wpis z opóźnieniem, odczyt zwrotny do 60 s) | https://github.com/microsoft/winget-pkgs/tree/master/manifests/d/Discord/Discord |
| `install.telegram` | `Telegram.TelegramDesktop` | messengers | 0 (inno) | konto (HKU) | 0 (19 s) | – | Pro: `Installed` (17 s) | `Telegram Desktop` / `Telegram FZ-LLC` | `displayName: "Telegram Desktop*"`, `publisher: "Telegram FZ-LLC"` | wchodzi | https://github.com/microsoft/winget-pkgs/tree/master/manifests/t/Telegram/TelegramDesktop |
| `install.signal` | `OpenWhisperSystems.Signal` | messengers | 0 (nullsoft) | konto (HKU) | 0 (42 s) | – | Pro: `Installed` (36 s) | `Signal 8.32.0` / `Signal Messenger, LLC` | `displayName: "Signal*"`, `publisher: "Signal Messenger, LLC"` | wchodzi | https://github.com/microsoft/winget-pkgs/tree/master/manifests/o/OpenWhisperSystems/Signal |
| `install.zoom` | `Zoom.Zoom` | messengers | 0 (msi) | komputer (HKLM) | 0 (21 s) | – | Pro: `Installed` (30 s) | `Zoom Workplace (64-bit)` / `Zoom` | `displayName: "Zoom Workplace*"`, `publisher: "Zoom"` | wchodzi | https://github.com/microsoft/winget-pkgs/tree/master/manifests/z/Zoom/Zoom |
| `install.vlc` | `VideoLAN.VLC` | multimedia | 0 (nullsoft) | komputer (HKLM) | 0 (92 s, v1.29: MSI) | 0 (230 s, EXE) | Pro: `Installed` (25 s); Home: `Installed` (34 s) | `VLC media player` / `VideoLAN` (EXE i MSI) | `displayName: "VLC media player"`, `publisher: "VideoLAN"` | wchodzi | https://github.com/microsoft/winget-pkgs/tree/master/manifests/v/VideoLAN/VLC |
| `install.k-lite` | `CodecGuide.K-LiteCodecPack.Standard` | multimedia | 0 (inno) | komputer (HKLM32) | 0 (103 s) | – | Pro: `Installed` (27 s) | `K-Lite Codec Pack 20.1.0 Standard` / `KLCP` | `displayName: "K-Lite Codec Pack * Standard"`, `publisher: "KLCP"` | wchodzi | https://github.com/microsoft/winget-pkgs/tree/master/manifests/c/CodecGuide/K-LiteCodecPack/Standard |
| `install.foobar2000` | `PeterPawlowski.foobar2000` | multimedia | 0 (nullsoft) | komputer (HKLM) | 0 (19 s) | – | Pro: `Installed` (7 s) | `foobar2000 v2.26.1 (x64)` / `Peter Pawlowski` | `displayName: "foobar2000*"`, `publisher: "Peter Pawlowski"` | wchodzi | https://github.com/microsoft/winget-pkgs/tree/master/manifests/p/PeterPawlowski/foobar2000 |
| `install.foxit-reader` | `Foxit.FoxitReader` | documents | 0 (exe – instalator wielojęzyczny `…_L10N_Setup_x64.exe`, w środku MSI) | komputer (HKLM) | 0 (87 s) | – | Pro: `Installed` (80 s) | `Foxit PDF Reader` / `Foxit Software Inc.` 2026.2.1.39815 (`MsiExec.exe /I{0E43C5B8-B377-11F1-8E85-BB2B83F9BAD7}`) | `displayName: "Foxit PDF Reader"`, `publisher: "Foxit Software Inc."` (nie łapie `Foxit PDF Editor`) | wchodzi (instaluje usługę `FoxitReaderUpdateService`, start automatyczny) | https://github.com/microsoft/winget-pkgs/tree/master/manifests/f/Foxit/FoxitReader |
| `install.libreoffice` | `TheDocumentFoundation.LibreOffice` | documents | 0 (msi) | komputer (HKLM) | 0 (118 s, faza `network`) | 0 (104 s, faza `network`) | Pro: `Installed` (73 s) | `LibreOffice 26.8.1.1` / `The Document Foundation` | `displayName: "LibreOffice*"`, `publisher: "The Document Foundation"` | wchodzi (doinstalowuje VC++ x64 jako zależność winget) | https://github.com/microsoft/winget-pkgs/tree/master/manifests/t/TheDocumentFoundation/LibreOffice |
| `install.acrobat-reader` | `Adobe.Acrobat.Reader.64-bit` | documents | 0 (exe) | komputer (HKLM) | 0 (113 s) | – | Pro: `Installed` (168 s) | `Adobe Acrobat (64-bit)` / `Adobe` | `displayName: "Adobe Acrobat*"`, `publisher: "Adobe"` | wchodzi (dodaje AppX `AdobeAcrobatReaderCoreApp` dla konta i do obrazu) | https://github.com/microsoft/winget-pkgs/tree/master/manifests/a/Adobe/Acrobat/Reader/64-bit |
| `install.sumatrapdf` | `SumatraPDF.SumatraPDF` | documents | 0 (exe) | konto (HKU) | 0 (7 s) | 0 (34 s) | Pro: `Installed` (9 s); Home: `Installed` (8 s) | `SumatraPDF` / `Krzysztof Kowalczyk` | `displayName: "SumatraPDF"`, `publisher: "Krzysztof Kowalczyk"` | wchodzi | https://github.com/microsoft/winget-pkgs/tree/master/manifests/s/SumatraPDF/SumatraPDF |
| `install.7zip` | `7zip.7zip` | tools | 0 (exe) | komputer (HKLM) | 0 (7 s, faza `network`) | 0 (6 s, faza `network`) | Pro: `Installed` (7 s); Home: `Installed` (7 s) | `7-Zip 26.05 (x64)` / `Igor Pavlov` | `displayName: "7-Zip*"`, `publisher: "Igor Pavlov"` | wchodzi | https://github.com/microsoft/winget-pkgs/tree/master/manifests/7/7zip/7zip |
| `install.notepadplusplus` | `Notepad++.Notepad++` | tools | 0 (nullsoft) | komputer (HKLM) | 0 (8 s, v1.29: MSI) | 0 (26 s, EXE) | Pro: `Installed` (24 s) | Pro MSI: `Notepad++ (x64)` / `Notepad++ team (MSI installer)`; Home EXE: `Notepad++ (64-bit x64)` / `Notepad++ Team` (+ AppX `NotepadPlusPlus`) | `displayName: "Notepad++*"`, `publisher: "Notepad++*"` | wchodzi (jedna reguła na oba warianty) | https://github.com/microsoft/winget-pkgs/tree/master/manifests/n/Notepad++/Notepad++ |
| `install.everything` | `voidtools.Everything` | tools | 0 (wix) | komputer (HKLM) | 0 (7 s) | – | Pro: `Installed` (14 s) | `Everything 1.4.1.1032 (x64)` / `voidtools` | `displayName: "Everything*"`, `publisher: "voidtools"` | wchodzi | https://github.com/microsoft/winget-pkgs/tree/master/manifests/v/voidtools/Everything |
| `install.powertoys` | `Microsoft.PowerToys` | tools | 0 (burn) | konto (HKU) | 0 (57 s) | – | Pro: `Installed` (43 s) | `PowerToys (Preview) x64` / `Microsoft Corporation` (+ HKLM `PowerToys (Preview)` z `SystemComponent=1`, 5 pakietów AppX) | `displayName: "PowerToys*"`, `publisher: "Microsoft Corporation"` | wchodzi | https://github.com/microsoft/winget-pkgs/tree/master/manifests/m/Microsoft/PowerToys |
| `install.winrar` | `RARLab.WinRAR` | tools | 0 (exe) | komputer (HKLM) | 0 (5 s, instalator `pl` z `--locale`) | – | Pro: `Installed` (12 s) | `WinRAR 7.23 (64-bitowy)` / `win.rar GmbH` (+ AppX `WinRAR.ShellExtension`) | `displayName: "WinRAR*"`, `publisher: "win.rar GmbH"` | wchodzi (program płatny po okresie próbnym) | https://github.com/microsoft/winget-pkgs/tree/master/manifests/r/RARLab/WinRAR |
| `install.sharex` | `ShareX.ShareX` | tools | 0 (inno) | komputer (HKLM) | 0 (26 s) | – | Pro: `Installed` (41 s) | `ShareX` / `ShareX Team` | `displayName: "ShareX"`, `publisher: "ShareX Team"` | wchodzi | https://github.com/microsoft/winget-pkgs/tree/master/manifests/s/ShareX/ShareX |
| `install.cpu-z` | `CPUID.CPU-Z` | tools | 0 (inno) | komputer (HKLM) | 0 (5 s) | – | Pro: `Installed` (7 s) | `CPUID CPU-Z 3.01` / `CPUID, Inc.` | `displayName: "CPUID CPU-Z*"`, `publisher: "CPUID, Inc."` | wchodzi | https://github.com/microsoft/winget-pkgs/tree/master/manifests/c/CPUID/CPU-Z |
| `install.hwmonitor` | `CPUID.HWMonitor` | tools | 0 (inno) | komputer (HKLM) | 0 (5 s) | – | Pro: `Failed` `0x80004004` (winget „Cancelling operation”, program zainstalowany – `--list-install` = `true`; po poprawce = `NotConfirmed`); Home (ponowienie): `Installed` (8 s) | `CPUID HWMonitor 1.68` / `CPUID, Inc.` | `displayName: "CPUID HWMonitor*"`, `publisher: "CPUID, Inc."` | wchodzi | https://github.com/microsoft/winget-pkgs/tree/master/manifests/c/CPUID/HWMonitor |
| `install.gpu-z` | `TechPowerUp.GPU-Z` | tools | 0 (exe) | komputer (HKLM32) | 0 (23 s) | – | Pro: `Installed` (19 s) | `TechPowerUp GPU-Z` / `TechPowerUp` | `displayName: "TechPowerUp GPU-Z"`, `publisher: "TechPowerUp"` | wchodzi | https://github.com/microsoft/winget-pkgs/tree/master/manifests/t/TechPowerUp/GPU-Z |
| `install.hwinfo` | `REALiX.HWiNFO` | tools | 0 (inno – wpis `…_is1`; licencja „Proprietary”, wersja darmowa) | komputer (HKLM) | 0 (10 s; instalator sam uruchamia HWiNFO64 po instalacji) | – | Pro: przebieg CLI – do uzupełnienia | `HWiNFO® 64` (znak U+00AE) / `Martin Malik, REALiX s.r.o.` | `displayName: "HWiNFO* 64"`, `publisher: "Martin Malik, REALiX s.r.o."` | wchodzi (dodane 2026-10-26 na prośbę autora) | https://github.com/microsoft/winget-pkgs/tree/master/manifests/r/REALiX/HWiNFO |
| `install.rustdesk` | `github`: `rustdesk/rustdesk` | remote-help | – (brak w winget; GitHub API `releases/latest`: `1.5.0`, MSI) | komputer (HKLM) | 0 (`msiexec` 3,7 s, przebieg CLI) | 0 (`msiexec` 3,5 s, przebieg CLI; spike 7,5 s) | Pro (z 7-Zip): `Installed` (7,6 s), drugi raz `AlreadyInstalled`; Home: `Installed` (7,2 s), drugi raz `AlreadyInstalled` | `RustDesk` / `Purslane Tech Pte. Ltd.` 1.5.0.29846474 (HKLM 64, MSI `{9D5F8A39-0D7B-4BC4-851D-DA8501D03DE7}`, `MsiExec.exe /X{…}`) | `displayName: "RustDesk"`, `publisher: "Purslane Tech*"` (nie łapie `RustDesk Client`) | wchodzi (spike Home: `msiexec` kod 0 w 7,5 s; instaluje usługę `RustDesk`, 2 reguły zapory, skróty pulpit/Start/Autostart wszystkich kont) | https://github.com/rustdesk/rustdesk/releases |
| `install.anydesk` | `AnyDesk.AnyDesk` | remote-help | 0 (exe) | komputer (HKLM32) | 0 (12 s) | 0 (65 s) | Pro: `Installed` (13 s) | `AnyDesk` / `AnyDesk Software GmbH` | `displayName: "AnyDesk"`, `publisher: "AnyDesk Software GmbH"` | wchodzi | https://github.com/microsoft/winget-pkgs/tree/master/manifests/a/AnyDesk/AnyDesk |
| `install.teamviewer` | `TeamViewer.TeamViewer` | remote-help | 0 (wix w zip) | komputer (HKLM) | 0 (21 s) | – | Pro: `Installed` (23 s); Home: `Installed` (18 s) | `TeamViewer` / `TeamViewer` | `displayName: "TeamViewer"`, `publisher: "TeamViewer"` | wchodzi | https://github.com/microsoft/winget-pkgs/tree/master/manifests/t/TeamViewer/TeamViewer |
| `install.steam` | `Valve.Steam` | games | 0 (nullsoft) | komputer (HKLM32) | 0 (5 s) | 0 (8 s) | Pro: `Installed` (7 s) | `Steam` / `Valve Corporation` | `displayName: "Steam"`, `publisher: "Valve Corporation"` | wchodzi | https://github.com/microsoft/winget-pkgs/tree/master/manifests/v/Valve/Steam |
| `install.epic-games` | `EpicGames.EpicGamesLauncher` | games | 0 (wix) | komputer (HKLM) | 0 (54 s) | – | Pro: `Installed` (39 s) | `Epic Games Launcher` / `Epic Games, Inc.` (+ `Microsoft GameInput`) | `displayName: "Epic Games Launcher"`, `publisher: "Epic Games, Inc."` | wchodzi | https://github.com/microsoft/winget-pkgs/tree/master/manifests/e/EpicGames/EpicGamesLauncher |
| `install.gog-galaxy` | `GOG.Galaxy` | games | 0 (inno) | komputer (HKLM) | 0 (42 s) | – | Pro: `Installed` (42 s); Home: `Installed` (51 s) | `GOG GALAXY` / `GOG.com` | `displayName: "GOG GALAXY"`, `publisher: "GOG.com"` | wchodzi | https://github.com/microsoft/winget-pkgs/tree/master/manifests/g/GOG/Galaxy |
| `install.vcredist-x64` | `Microsoft.VCRedist.2015+.x64` | runtimes | 0 (burn) | komputer (HKLM32) | `0x8A150061` (zależność LibreOffice z fazy `network`); spike: 0 (7 s) | `0x8A150061` (j.w.) | Pro: `Installed` (11 s); Home (ponowienie): `Installed` (11 s) | `Microsoft Visual C++ v14 Redistributable (x64) - 14.51.36247` / `Microsoft Corporation` (+ 2 × HKLM `SystemComponent=1`) | `displayName: "Microsoft Visual C++ *Redistributable (x64) - 14.*"`, `publisher: "Microsoft Corporation"` (łapie też nazwę `Microsoft Visual C++ 2015-2022 Redistributable (x64) - 14.51.36231` z T11 Home) | wchodzi (instalacja: spike i zależność winget) | https://github.com/microsoft/winget-pkgs/tree/master/manifests/m/Microsoft/VCRedist/2015+/x64 |
| `install.vcredist-x86` | `Microsoft.VCRedist.2015+.x86` | runtimes | 0 (burn) | komputer (HKLM32) | 0 (9 s) | – | Pro: `Installed` (8 s) | `Microsoft Visual C++ v14 Redistributable (x86) - 14.51.36247` / `Microsoft Corporation` (+ 2 × `SystemComponent=1`) | `displayName: "Microsoft Visual C++ *Redistributable (x86) - 14.*"`, `publisher: "Microsoft Corporation"` (łapie też `… 2015-2022 Redistributable (x86) - 14.51.36231` z T11 Home) | wchodzi | https://github.com/microsoft/winget-pkgs/tree/master/manifests/m/Microsoft/VCRedist/2015+/x86 |
| `install.dotnet-desktop-8` | `Microsoft.DotNet.DesktopRuntime.8` | runtimes | 0 (burn) | komputer (HKLM32) | 0 (13 s) | – | Pro: `Installed` (12 s); Home: `Installed` (17 s) | `Microsoft Windows Desktop Runtime - 8.0.32 (x64)` / `Microsoft Corporation` (+ 4 × HKLM `SystemComponent=1`) | `displayName: "Microsoft Windows Desktop Runtime - 8.* (x64)"`, `publisher: "Microsoft Corporation"` | wchodzi | https://github.com/microsoft/winget-pkgs/tree/master/manifests/m/Microsoft/DotNet/DesktopRuntime/8 |
| `install.dotnet-desktop-9` | `Microsoft.DotNet.DesktopRuntime.9` | runtimes | 0 (burn) | komputer (HKLM32) | 0 (11 s) | – | Pro: `Installed` (13 s) | `Microsoft Windows Desktop Runtime - 9.0.21 (x64)` / `Microsoft Corporation` (+ 4 × HKLM `SystemComponent=1`) | `displayName: "Microsoft Windows Desktop Runtime - 9.* (x64)"`, `publisher: "Microsoft Corporation"` | wchodzi | https://github.com/microsoft/winget-pkgs/tree/master/manifests/m/Microsoft/DotNet/DesktopRuntime/9 |

Zasada dowodu (spec 2): każda pozycja winget ma `show` = 0, a każda przeszła pełną instalację z odczytem wpisu Uninstall na Pro
(Discord – wpis z opóźnieniem, niżej). VC++ x64 w głównym pomiarze dał tylko `0x8A150061`, bo winget doinstalował go wcześniej
jako zależność LibreOffice; własne `install` z odczytem zwrotnym przeszło w spike'u Pro (0, 7 s) i w serii CLI Tasku 11
(Pro `Installed`, 11 s; Home – ponowienie – `Installed`, 11 s). Na Home przeszło po jednej pozycji z każdej kategorii: Chrome
i Firefox, Discord, VLC, SumatraPDF i LibreOffice, Notepad++ i 7-Zip, AnyDesk, Steam (VC++ x64 tylko jako zależność
LibreOffice), a do tego PuTTY spoza listy. Żadna pozycja nie zostaje na liście tylko z reguły „ta sama rodzina instalatora”.

Discord: zaraz po kodzie 0 wpisu Uninstall jeszcze nie było (Home i Pro), ale `winget list --id Discord.Discord --exact`
uruchomiony kilka sekund później zwrócił 0 z wierszem `Discord`, a spike odczytał wpis HKU `Discord` / `Discord Inc.`. Odczyt
zwrotny programu czeka do 60 s, więc pozycja zostaje.

Najdłuższa instalacja: VLC na Home, 230 s (EXE). Limit `InstallTimeouts.PerApp` = 30 min zostaje (zmiana byłaby potrzebna
dopiero przy instalacji dłuższej niż 7 min).

### Przebieg CLI z siecią (Task 11, 2026-10-24)

Kolumna „przebieg CLI” = wynik pozycji w serii `DSOptimizer.exe --install-apps … --out …` uruchomionej jako `dso` z zadania
Harmonogramu (`tests/vm/explore/Invoke-E4CExploration.ps1 -Phase cli-install`, sieć `Default Switch` tylko na czas fazy), czas
= od zdarzenia `Started` do zdarzenia wyniku w journalu (pobranie, instalacja i odczyt zwrotny). Surowe wyniki: poza gitem.

- **Pro, cała lista (32) w jednej serii:** 31 × `Installed` („Zainstalowano i sprawdzono.”), 1 × `Failed` (HWMonitor), kod CLI 1,
  17 min 13 s. `--list-install` po serii: 32/32 `installed = true`. Journal: 32 wpisy `app-install`, każdy `Started` → wynik.
  App Installer zaktualizował się w trakcie serii (winget v1.11.510 przed, v1.29.380 po); Notepad++ przyszedł jeszcze jako EXE.
- **HWMonitor na Pro:** winget po „Starting package install…” wypisał „Cancelling operation”, „Installation abandoned” i zakończył się
  `0x80004004` (E_ABORT), a instalator Inno w logu `--log` doszedł do „Installation process succeeded” i zapisał wpis Uninstall.
  Najbardziej prawdopodobna przyczyna: przerwanie winget przez aktualizację App Installera w tym samym czasie (niezmierzone wprost).
  Ponowienie na Home (VC++ x64 + HWMonitor, winget v1.11.510): oba `Installed`, kod 0. Pozycja zostaje.
  Po poprawce (fala po final review) ten sam przebieg dałby „Nie potwierdzono – winget zakończył się kodem 0x80004004, ale
  program jest widoczny na liście zainstalowanych; sprawdź log winget” (`NotConfirmed`, kod CLI 4 zamiast 1): po nieznanym kodzie
  pozycja listy dostaje jeden świeży odczyt, a widoczny program nigdy nie jest „Zainstalowano”, bo kod może też oznaczać
  instalację przerwaną w połowie. Wynik nie był mierzony ponownie na VM (wymaga powtórzenia samoaktualizacji App Installera).
- **Home (po jednej pozycji z kategorii, inne niż w pomiarze Tasku 2) + `winget:PuTTY.PuTTY`:** 9/9 `Installed`, kod CLI 0,
  3 min 34 s; PuTTY potwierdzony przez `winget list --id --exact` (`Installed`, nie `InstalledUnverified`). `--list-install`:
  8 pozycji listy z tej serii = `installed = true`. Winget bez aktualizacji (v1.11.510).
- **Discord:** wpis HKU pojawił się po wyjściu winget – „Zainstalowano i sprawdzono.” ok. 14 s (Home) i 20 s (Pro) po linii
  „Successfully installed”, czyli w oknie 60 s. Dłużej czekał tylko Adobe Acrobat Reader (ok. 30 s, Pro).
- **VC++ jako składnik innego instalatora:** na Home jeden z instalatorów serii (bez zależności winget w wyjściu; z logów nie da się rozstrzygnąć który) doinstalował
  `Microsoft Visual C++ 2015-2022 Redistributable (x64)` i `(x86)` – 14.51.36231 (starsza nazwa wpisu). Reguła z T11 go nie
  rozpoznawała (`installed = false`); ponowienie `install.vcredist-x64` na Home dało `Installed` (kod 0), czyli winget doinstalował
  wersję v14 obok starszej. Od fali poprawek po final review (M4) reguła ` - 14.*` łapie oba warianty nazwy, więc taka pozycja
  dostaje status „Zainstalowana” i nie startuje.

- **Foxit PDF Reader (2026-10-24, po zamknięciu Tasku 11, tylko Pro):** `--install-apps install.foxit-reader` jako `dso` z zadania
  Harmonogramu (jednorazowy skrypt na helperach `Invoke-E4CExploration.ps1`: faza `cli-install` z listą ograniczoną do tej pozycji):
  `Installed` („Zainstalowano i sprawdzono.”, wersja 2026.2.1.39815), kod CLI 0, 80 s od `Started` do wyniku (cały proces CLI 90 s),
  winget v1.11.510 bez aktualizacji. `--list-install`: `install.foxit-reader` = `installed = true`, pozostałe 32 = `false`. Journal: jeden
  wpis `app-install` (`Started` → `Installed`). Nowe po instalacji: jeden wpis Uninstall (HKLM 64, MSI, bez `QuietUninstallString`),
  usługa `FoxitReaderUpdateService` („Foxit PDF Reader Update Service”, `Auto`, `LocalSystem`, uruchomiona), żadnych nowych pakietów AppX
  (poza pobranym przez winget źródłem `Microsoft.Winget.Source`) ani wpisów `Run` (HKLM 64/32, HKU konta). Język: pakiet wielojęzyczny (`packagelang` = `ml`, foldery `Lang\<kultura>` w tym `pl-PL`),
  opis aplikacji w rejestrze (`Capabilities\ApplicationDescription`) po polsku; języka okna nie sprawdzano (program nie był uruchamiany).

### RustDesk: przebieg CLI z siecią (plan RustDesk, Task 7, 2026-10-24)

`tests/vm/explore/Invoke-E4CExploration.ps1 -Phase cli-install -CliIds … -CliRepeat` na czystych VM (`czysta-26H2`, winget v1.11.510
bez aktualizacji), `DSOptimizer.exe --install-apps … --out …` jako `dso` z zadania Harmonogramu (Interactive + Highest), sieć
`Default Switch` tylko na czas fazy. Czas pozycji = od zdarzenia `Started` do wyniku w journalu (UTC; zapytanie API, pobranie
26 345 472 B, SHA256, podpis, `msiexec`, odczyt zwrotny). Rozmiar MSI 1.5.0 nie występuje w artefaktach tego przebiegu (CLI go nie
zapisuje, a pobranie o innym rozmiarze niż `size` z API kończy się porażką): źródło to spike (plik `download.json` z surowych wyników, poza gitem), pole `Size` wpisu `rustdesk-1.5.0-x86_64.msi` (= `size` z API dla
tego samego wydania 1.5.0). „Cały proces” = `Seconds` z `raw/cli-install.result.json` (czas zadania Harmonogramu, ze
startem procesu), nie `startedAt` → `finishedAt` raportu CLI. Czasy `msiexec` z linii logu MSI – czas lokalny VM (UTC+2), stąd
12:16 obok 10:16 w journalu. Surowe wyniki poza gitem
(`e4c.json`, `raw/cli-install.json`, `raw/cli-install.result.json`, `raw/cli-install-again.json`, `raw/cli-install-again.result.json`,
`raw/cli-list.json`, `raw/journal.json`, log MSI).

- **Pro – seria mieszana `install.7zip,install.rustdesk`:** 7-Zip `Installed` (winget `0x00000000`, 26.05, 10,1 s), RustDesk
  `Installed` „Zainstalowano i sprawdzono.” (`source = github`, `msiCode` 0, `wingetCode` null, `version` 1.5.0, 7,6 s; `msiexec`
  12:16:16,8 → 12:16:20,5 czasu lokalnego VM (UTC+2) = 3,7 s), kod CLI 0, cały proces 27,7 s (`Seconds` z `raw/cli-install.result.json`;
  raport CLI `startedAt` → `finishedAt` = 25,3 s). `--list-install`: `install.rustdesk` `installed = true`,
  `source = github`, `gitHub = rustdesk/rustdesk`. Druga seria (`-CliRepeat`): obie pozycje `AlreadyInstalled` „Była już
  zainstalowana – nic nie uruchomiono.”, kod 0, 4,6 s (`Seconds` z `raw/cli-install-again.result.json`; bez nowego wpisu journala i nowego logu MSI; brak zapytania do GitHuba na tej ścieżce pilnuje test
  `AppInstallEngineGitHubTests.MatchedBeforeRun_AlreadyInstalled_NoHttp` – ruchu sieciowego VM nie mierzono).
- **Home – sam `install.rustdesk`:** `Installed` (`msiCode` 0, 1.5.0, 7,2 s; `msiexec` 12:13:53,8 → 12:13:57,3 czasu lokalnego VM
  = 3,5 s), kod CLI 0, proces 17 s (`Seconds` z `raw/cli-install.result.json`; raport CLI `startedAt` → `finishedAt` = 12,9 s);
  `--list-install` `installed = true`, `source = github`; druga seria `AlreadyInstalled`, kod 0, 3,6 s (`Seconds` z
  `raw/cli-install-again.result.json`).
- **Journal (obie VM):** jeden wpis `app-install` RustDesk – `Started` „github: rustdesk/rustdesk” → `Installed` „github:
  rustdesk/rustdesk, wersja 1.5.0, log: C:\ProgramData\DSOptimizer\logs\winget\rustdesk-msi-<czas>.log”, status `Active`
  (na Pro przed nim wpis 7-Zip). Drugi przebieg nie dopisał wpisu.
- **Po serii (tylko odczyt w VM):** `C:\ProgramData\DSOptimizer\downloads` istnieje i nie ma plików (MSI usunięty), log MSI
  `logs\winget\rustdesk-msi-*.log` (ok. 390 KB, „Product: RustDesk -- Installation completed successfully.”), usługa `RustDesk`
  („RustDesk Service”, `Automatic`, `Running`), 2 reguły zapory „RustDesk Service” (przychodząca i wychodząca, zezwalaj) – jak
  w spike'u. Nowy wpis Uninstall: HKLM 64 `{9D5F8A39-0D7B-4BC4-851D-DA8501D03DE7}` `RustDesk` / `Purslane Tech Pte. Ltd.`
  1.5.0.29846474 (na Pro dodatkowo `7-Zip 26.05 (x64)`); nic nie zniknęło ani się nie zmieniło.
- Na końcu obie VM: revert `czysta-26H2`, karta bez przełącznika, `Off` (`cleanup.Ok = true`, sprawdzone `Get-VM`/`Get-VMNetworkAdapter`).

### RustDesk z GitHuba (spike 2026-10-24)

RustDesk nie ma pakietu w źródle `winget` (`show` = `0x8A150014`, sekcja „Odrzucone”), więc pozycja `install.rustdesk` pobiera
oficjalny MSI z wydania GitHuba. Spike: VM
`DSO-26H2-Home` (26300), konto `dso` z zadania Harmonogramu (jak po UAC), sieć `Default Switch` tylko na czas spike'a, potem revert.
Surowe wyniki poza gitem (`releases.json`, `download.json`, `install.json`, `diff.json`,
`timeline.txt`, `msi-install.log`); research API i kodu także poza gitem.

- **Wersja:** `GET https://api.github.com/repos/rustdesk/rustdesk/releases/latest` = tag `1.5.0` (2026-09-30), `prerelease=false`,
  poprzednie `1.4.9`; `nightly` to osobne wydanie prerelease, które `latest` pomija.
- **Plik:** `rustdesk-1.5.0-x86_64.msi`, 26 345 472 B. Nazwa zawiera wersję, więc adres bierzemy z API.
- **Skrót:** pole `digest` z API (`sha256:624fe792…77580495`) = `Get-FileHash` pobranego pliku; tak samo dla 1.4.9 i dla wariantów EXE.
- **Pobranie:** `browser_download_url` na `github.com` → przekierowanie na `release-assets.githubusercontent.com`, 0,8 s.
- **Podpis (MSI i EXE, 1.5.0 i 1.4.9):** `Valid`; Subject `CN=PURSLANE, O=PURSLANE, S=North West, C=SG, …, SERIALNUMBER=53481265A`;
  Issuer `CN=Sectigo Public Code Signing CA EV R36` (Sectigo Limited); certyfikat ważny do 2028-02-22; znacznik czasu Sectigo
  (`Sectigo Public Time Stamping Signer R37`). Program przypina `O=PURSLANE`, `C=SG` i `SERIALNUMBER=53481265A`, nie odcisk.
- **Instalacja:** `%SystemRoot%\System32\msiexec.exe /i "<msi>" /qn /norestart /l*v "<log>"` jako `dso` = kod 0 w 7,5 s.
- **Wpis Uninstall:** HKLM 64 `{9D5F8A39-0D7B-4BC4-851D-DA8501D03DE7}`, `DisplayName` = `RustDesk`, `Publisher` =
  `Purslane Tech Pte. Ltd.`, `DisplayVersion` = `1.5.0.29846474`, `UninstallString` = `MsiExec.exe /X{9D5F8A39-0D7B-4BC4-851D-DA8501D03DE7}`,
  bez `QuietUninstallString` i `SystemComponent`. Drugi nowy wpis w tym samym diffie to `Microsoft OneDrive` w HKU konta (wydawca
  Microsoft Corporation) – nie należy do RustDesk.
- **Dodatki:** usługa `RustDesk` („RustDesk Service”, automatyczna, uruchomiona); 2 reguły zapory „RustDesk Service” (przychodząca
  i wychodząca, zezwalaj); skróty: pulpit publiczny (`C:\Users\Public\Desktop\RustDesk.lnk`), menu Start (`RustDesk\RustDesk.lnk`,
  `Uninstall RustDesk.lnk`) i Autostart wszystkich kont (`…\Start Menu\Programs\Startup\RustDesk Tray.lnk`); po 20 s procesy
  `RustDesk` jako SYSTEM (sesja 0 i 1) i jako `dso`. Stąd ostrzeżenie pozycji (spec 4, dosłownie w `apps.json`).
- **Reguła `match`:** `displayName: "RustDesk"` (dokładnie), `publisher: "Purslane Tech*"`; nie łapie podobnej nazwy tego samego
  wydawcy (`RustDesk Client`, test negatywny na nazwie syntetycznej).

### Ostrzeżenia (`warning`) z pomiaru

- **Tylko dla bieżącego konta** (wpis w HKU): Brave, Opera, Discord, Telegram, Signal, SumatraPDF, PowerToys. Tekst:
  „Instaluje się tylko dla bieżącego konta Windows.”
- **Dodatkowe składniki:** Firefox → usługa `Mozilla Maintenance Service`, LibreOffice → Microsoft Visual C++ v14
  Redistributable (x64) (zależność winget), Epic Games Launcher → Microsoft GameInput, Adobe Acrobat Reader → pakiet AppX
  `AdobeAcrobatReaderCoreApp` (konto i obraz systemu), PowerToys → 5 pakietów AppX, WinRAR → `WinRAR.ShellExtension`,
  Notepad++ (instalator EXE) → `NotepadPlusPlus`, Foxit PDF Reader → usługa `FoxitReaderUpdateService` (start automatyczny).
- **WinRAR:** „Program płatny po okresie próbnym.” (spec pkt 4).

### Ograniczenia reguł (świadome)

- Visual C++ x64/x86: reguła `Microsoft Visual C++ *Redistributable (x64) - 14.*` (analogicznie `(x86)`) łapie obie zmierzone
  nazwy rodziny 14.x: `… v14 Redistributable (x64) - 14.51.36247` (T2) i `… 2015-2022 Redistributable (x64) - 14.51.36231`
  (T11 Home), obie dają „Zainstalowana”. Nie łapie 2013 (` - 12.`), 2012 (` - 11.`), 2010 (`… 2010  x64 Redistributable - 10.`)
  ani składników `… X64 Minimum/Additional Runtime - 14.…` (testy negatywne na nazwach syntetycznych). Inne wydanie 14.x
  z jeszcze inną nazwą wpisu nie dostanie statusu; wtedy winget albo zwróci „Już zainstalowane”, albo – jak na Home w T11 –
  doinstaluje wersję v14 obok starszej (kod 0, `Installed`), więc nic się nie psuje.
- Adobe: `Adobe Acrobat*` / `Adobe` pasuje też do płatnego Acrobata. Od wersji 64-bit Reader i Acrobat to jeden instalator
  z tym samym wpisem (`Adobe Acrobat (64-bit)`), więc status „Zainstalowana” jest wtedy prawdziwy.
- Zoom (`Zoom`) i TeamViewer (`TeamViewer`): wydawca i nazwa dokładnie z pomiaru. Starsze wydania z innym wpisem nie dostaną
  statusu, a winget i tak zwróci „Już zainstalowane”.

## Odrzucone (z powodem)

| `wingetId` | Powód | Dowód |
|---|---|---|
| `Spotify.Spotify` | instalator nie działa z konta administratora: `0x8A150056` „The installer cannot be run from an administrator context.” | spike 2026-10-24 (Pro); `show` = 0 (pakiet jest w źródle) |
| `RustDesk.RustDesk` | brak w źródle `winget`; zamiast tego `install.rustdesk` z GitHuba | `show` = `0x8A150014` na Home i Pro; GitHub API: brak `manifests/r/RustDesk/RustDesk` (404, 2026-10-24) |
| `Microsoft.DirectX` | spec: brak wpisu Uninstall. Pomiar: instalator jest dziś pakietem **MSIX** (`UAPSignedBinary_Microsoft.DirectX.x64.appx`) i nie był instalowany, więc status i odczyt zwrotny są niepotwierdzone | `show` = 0, `Installer Type: msix` |
| `Microsoft.Teams` | jest już na świeżym 26H2 (AppX `MSTeams`, research 4a). Winget zwraca `0x8A150061`, a instalacji MSIX przez winget nie potwierdzono. Strona „Aplikacje” (4a) sugeruje Teams do usunięcia, więc pozycja na liście instalacji wprowadzałaby w błąd | `show` = 0, `install` = `0x8A150061` (Pro) |

Poza listą świadomie (spec pkt 4): sterowniki kart graficznych, Microsoft Edge, pakiety ze Sklepu (`msstore`).

## Wyszukiwanie: kandydat spoza listy

`winget:PuTTY.PuTTY` (`search --query putty`, pierwszy wiersz). Instalacja zmierzona na Home i Pro: kod 0, wpis HKLM
`PuTTY release 0.85 (64-bit)` / `Simon Tatham`, `list --id --exact` = 0. W przebiegu CLI na Home (Task 11)
`--install-apps winget:PuTTY.PuTTY` = `Installed` (odczyt zwrotny `winget list --id --exact`), wpis HKLM jak wyżej.

## Strona „Instalacja” (UI po poprawkach 2026-10-25)

- Lista kategorii: „Wszystkie” (wybierana przy każdym wejściu), „Zaznaczone (N)” (także 0), potem kategorie z tego pliku w kolejności `InstallCategory`. Grupy „Wyniki wyszukiwania” już nie ma.
- Jedno pole „Szukaj aplikacji, np. VLC” filtruje listę na żywo (`InstallSearch`: tytuł, `summary`, `wingetId`, repo GitHub; zapytanie i tekst składane bez spacji, „-”, „.”, „_”, więc „7zip” znajduje „7-Zip”). Wynik: sekcja „Z listy (N)” z nagłówkami kategorii. Winget rusza dopiero po Enterze albo „Szukaj też w winget” i dokłada sekcję „W winget (N)” (pakiety spoza listy, z notatką, że nie były sprawdzane na VM). Wynik winget z Id pozycji listy (także `RustDesk.RustDesk` → `install.rustdesk`) nie jest osobnym pakietem – notatka „jest na liście – wyżej”.
- Klik kategorii czyści pole. Zaznaczenie przetrwa zmianę kategorii, tekstu i nowe wyszukiwanie; „Zaznaczone” pokazuje pozycje listy i wyniki winget razem.
- Lista jest płaska: wiersze aplikacji przeplecione niefokusowalnymi liniami (nagłówki, notatki, komunikaty).
- Historia zmian pokazuje wpis instalacji z ikoną rodzaju, drugą linią „Instalacja aplikacji” i krótkim powodem porażki z `JournalEvent.Reason` („Nie zainstalowano – brak połączenia z internetem”). Kody `0x800704C7` (instalator przerwany, możliwy antywirus) i `0x800700E1`/`0x800700E2` (antywirus zablokował instalator) mają własne teksty; przy pozycji listy z widocznym programem obejmuje je ta sama reguła (`NotConfirmed`).
