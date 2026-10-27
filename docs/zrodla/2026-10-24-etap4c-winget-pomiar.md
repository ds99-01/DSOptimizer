# Etap 4c – pomiar wstępny winget na VM (2026-10-24, spike)

VM `DSO-26H2-Pro` (26300.9457, pl-PL), od `czysta-26H2`, revert po pomiarze, karta odłączona, VM `Off`.
Winget uruchamiany z zadania Harmonogramu jako `dso` (Interactive + Highest = jak DSOptimizer po UAC), alias
`%LOCALAPPDATA%\Microsoft\WindowsApps\winget.exe`, stdout/stderr przekierowane do potoków (jak w programie).
Najpierw przebieg bez sieci (`off`), potem `Default Switch` (`net`). Surowe bajty: zapis pomiaru w archiwum autora.
Skrypt jednorazowy (poza repo); pełny pomiar Home + cała lista = Task 1 planu.

## Środowisko
- App Installer 1.26.509.0, `winget --version` = `v1.11.510`.
- `TypeOfAdminApprovalMode` = 1 → **Administrator Protection wyłączona** na świeżej 26H2 z ISO (2 = włączona).
- Wyjście winget **po angielsku** mimo pl-PL (brak lokalizacji w tej wersji albo wybór języka wyświetlania – nie ustalono).

## Kody wyjścia (`ExitCode` jako int ze znakiem)
| Polecenie | Bez sieci | Z siecią |
|---|---|---|
| `--version`, `--info`, `install --help` | 0 | 0 |
| `source update --name winget` | **0** (3,7 s) | 0 (1,5 s) |
| `search --query vlc --source winget --count 50` | `0x8A15000F` SOURCE_DATA_MISSING | 0 |
| `search` bez wyników („przeglądarka”) | `0x8A15000F` | `0x8A150014` NO_APPLICATIONS_FOUND |
| `show --id VideoLAN.VLC --exact` | `0x8A15000F` | 0 |
| `list --id 7zip.7zip --exact` (nie zainstalowany) | `0x8A15004B` | `0x8A150014` |
| `list --id 7zip.7zip --exact` (zainstalowany) | – | 0, tabela z wierszem |
| `install 7zip.7zip` | `0x8A15000F` | 0 (7,5 s) |
| `install 7zip.7zip` ponownie | – | `0x8A15002B` UPDATE_NOT_APPLICABLE („Found an existing package already installed. Trying to upgrade…”) |
| `install Dso.Nonexistent.Package` | `0x8A15000F` | `0x8A150014` |
| `install … --no-progress` | – | `0x8A150002` (argument nieznany w 1.11) |
| `install Discord.Discord` | – | 0 (12,5 s), wpis w `HKU\<SID dso>` |
| `install VideoLAN.VLC` | – | 0 (48,1 s), HKLM |
| `install Microsoft.VCRedist.2015+.x64` | – | 0 (7,1 s), pakiet w HKLM WOW6432Node + 2 składniki MSI |
| `install Spotify.Spotify` | – | **`0x8A150056`** „The installer cannot be run from an administrator context.” (1,1 s) |
| `install Mozilla.Firefox` | – | 0 (20,7 s), **`Mozilla Firefox (x64 en-US)`** – wersja angielska |

Wnioski:
- `source update` nie jest testem sieci (kod 0 offline). Brak sieci na świeżym systemie (źródło nigdy niepobrane) =
  `0x8A15000F` przy pierwszym `search`/`install`/`show`. Po pierwszym pobraniu źródła offline może dawać inne kody –
  niezmierzone.
- `install` zainstalowanego pakietu domyślnie próbuje **aktualizacji**; flaga `--no-upgrade` jest w 1.11 (pomoc).
- Instalatory z `ElevationRequirement: elevationProhibited` (Spotify) nie działają z procesu podniesionego – kod
  `0x8A150056`.
- Locale instalatora: winget wybrał `en-US` dla Firefoxa na pl-PL. Flaga `--locale` jest w 1.11.

## Wpisy Uninstall po instalacji
| Pakiet | Gałąź | `DisplayName` | `Publisher` |
|---|---|---|---|
| 7zip.7zip | HKLM | `7-Zip 26.05 (x64)` | Igor Pavlov |
| Mozilla.Firefox | HKLM | `Mozilla Firefox (x64 en-US)` | Mozilla |
| VideoLAN.VLC | HKLM | `VLC media player` | VideoLAN |
| Microsoft.VCRedist.2015+.x64 | HKLM WOW6432Node | `Microsoft Visual C++ v14 Redistributable (x64) - 14.51.36247` | Microsoft Corporation |
| (składniki MSI) | HKLM | `Microsoft Visual C++ 2022 X64 Additional/Minimum Runtime - 14.51.36247` | Microsoft Corporation |
| Discord.Discord | HKU\<SID dso> | `Discord` | Discord Inc. |

## Format wyjścia
- UTF-8, linie `\r\n`. Spinner: `\r   - \r   \ \r   | \r   / …`, czyszczenie linii: `\r` + 120 spacji + `\r`.
- Pasek pobierania: `\r  ██████▒▒▒▒  14.0 MB / 44.1 MB` (znaki U+2588 i U+2592, rozmiary `KB`/`MB`, kropka dziesiętna),
  kilka aktualizacji w jednej linii rozdzielonych `\r`, zakończonych `\r\n`. Procent da się policzyć z rozmiarów.
- Linie tekstu: `Found VLC media player [VideoLAN.VLC] Version 3.0.24`, `Downloading <url>`,
  `Successfully verified installer hash`, `Starting package install...`, `Successfully installed`;
  błąd: `An unexpected error occurred while executing the command:` + `0x8a15000f : Data required by the source is missing`.
- `search` (z `--source winget`, przekierowane): bez spinnera; nagłówek `Name  Id  Version  Match`, linia samych `-`,
  wiersze z kolumnami wyrównanymi spacjami (pozycje z nagłówka), kolumna `Match` bywa pusta. Bez obcinania przy 10 wynikach
  (najdłuższa nazwa 26 znaków) – obcinanie „…” przy długich nazwach niezmierzone.
- `list --id … --exact`: spinner przed tabelą `Name  Id  Version  Source`.
