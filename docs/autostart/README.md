# Autostart – baza znanych pozycji `autostart-known.json`

Plik `src/DSOptimizer.Catalog/catalog/autostart-known.json` mówi stronie „Autostart”, których pozycji nie wolno wyłączać (kłódka)
i które znane programy można wyłączyć (sugestia). Ładuje go `AutostartKnownCatalog.Load` (Core), osadzony jest jako
`BuiltInCatalog.AutostartKnownSource`. Nie należy do `BuiltInCatalog.Sources` (to tylko tablice tweaków); test
`Sources_MatchCatalogFilesOnDisk` pomija go z nazwy, a `AutostartKnownSource_IsEmbeddedAndMatchesFileOnDisk` pilnuje, że plik na dysku
i zasób są tym samym.

Baza niczego nie wykonuje i niczego nie zaznacza. Kłódka zabiera pole wyboru, a sugestię zaznacza dopiero przycisk „Zaznacz sugerowane”.

## Format

```json
{
  "entries": [
    { "id", "match": { "file", "folders", "publishers", "valueName?", "packageFamily?" }, "verdict", "summary", "source", "confirmed" }
  ]
}
```

- `id` – unikalny w pliku, bez rozróżniania wielkości liter.
- `match.file` – sama nazwa pliku wykonywalnego, bez folderu; wzorzec z `*`, ale nie z samych gwiazdek.
- `match.folders` – niepusta lista wzorców folderu pliku. Każdy zaczyna się od jednej ze zmiennych `%SystemRoot%`, `%ProgramFiles%`,
  `%ProgramFiles(x86)%`, `%ProgramData%`, `%LOCALAPPDATA%`, `%APPDATA%`, zaraz po niej ma `\` i kończy się `\` albo `*`.
  Wpis `protected` może używać tylko `%SystemRoot%`, `%ProgramFiles%` i `%ProgramFiles(x86)%` – tam zapisuje tylko administrator.
- `match.publishers` – niepusta lista wzorców wydawcy (`CompanyName` z wersji pliku, dla Sklepu wydawca pakietu); wymagana w każdym
  wpisie, nie z samych gwiazdek.
- `match.valueName` – opcjonalnie: nazwa wartości w kluczu Run (albo nazwa pliku w folderze Autostart, TaskId zadania Sklepu) musi być równa.
- `match.packageFamily` – opcjonalnie: rodzina pakietu Sklepu musi być równa.
- `verdict` – `protected` (kłódka, pozycja tylko do odczytu) albo `can-disable` (sugestia wyłączenia).
- `summary` – ≤ 160 znaków, po polsku, po ludzku: co to jest i co się stanie po wyłączeniu. Bez kluczy rejestru, znaku `=`,
  identyfikatorów, dat i obietnic przyspieszenia. Angielski w `src/DSOptimizer.Core/Localization/Translations/catalog-autostart-known.json`.
- `source` – niepusta lista: bezwzględne adresy `https://` albo dokumenty z folderu `docs/zrodla` repozytorium.
- `confirmed` – `true`, gdy nazwy pliku, folderu, wydawcy i wartości są potwierdzone odczytem z systemu; `false`, gdy pochodzą tylko
  ze źródeł opisowych.

Wzorce porównuje się bez rozróżniania wielkości liter, `*` obejmuje dowolny ciąg (także z `\`). Błąd w pliku daje
`AutostartCatalogException` z id wpisu i nazwą pola.

## Zasady dopasowania

- Ocenę dostaje tylko pozycja z ustalonym plikiem (pełna, rozwinięta ścieżka) i z wydawcą. Plik, folder i wydawca muszą pasować
  jednocześnie, a podane `valueName` i `packageFamily` – być równe.
- Ten sam plik w innym folderze nie dostaje oceny: `SecurityHealthSystray.exe` w `%TEMP%` albo `steam.exe` wprost w `%APPDATA%`
  nie ma ani kłódki, ani sugestii. Tak samo plik o znanej nazwie z innym wydawcą.
- Zmienne maszyny we wzorcach folderów (`%SystemRoot%`, `%ProgramFiles%`, `%ProgramFiles(x86)%`, `%ProgramData%`) są brane z procesu,
  nigdy z ustawień konta; `%LOCALAPPDATA%` i `%APPDATA%` – z konta zalogowanego użytkownika. Konto nie może przenieść folderu
  administratora we własne miejsce.
- Ścieżka z `/` albo z segmentem `.` lub `..` nie pasuje nigdy.
- Najpierw sprawdzane są wpisy `protected`, potem `can-disable`: ochrona wygrywa z każdą sugestią.
- Pozycja bez dopasowania jest bez oceny – można ją przełączać, ale program jej nie sugeruje.

## Progi dowodu

Progi są celowo różne:

- `can-disable` wymaga `confirmed: true`, dokumentu z odczytem na maszynie testowej (nazwa pliku, folder, wydawca i nazwa wartości,
  `docs/zrodla/2026-11-02-autostart-programy.md`) oraz co najmniej jednego adresu `https://` o skutku wyłączenia. Pilnuje tego loader.
- `protected` może mieć `confirmed: false`. Zbędna kłódka kosztuje mniej niż sugestia wyłączenia antywirusa albo sterownika
  touchpada, więc wystarczą źródła opisowe. Wpisy z `confirmed: false` nie zostały jeszcze potwierdzone na prawdziwym sprzęcie.
- Gdy źródła nie podają folderu (`asus-hotkeys`, `kaspersky`, `avast`, `bitdefender`, `logitech-g-hub`), folder to
  `%ProgramFiles%\*` albo `%ProgramFiles(x86)%\*`, a wydawca jest wymagany: plik o tej nazwie z `%TEMP%` albo `AppData` nie dostaje kłódki.
- `security-health` jest jedynym potwierdzonym wpisem `protected`: wartość `SecurityHealth` w `HKLM\…\Run` jest na czystym systemie
  (`docs/zrodla/2026-11-02-autostart-pomiar.md`).

## Brak pliku = sugerowane

Pozycja, której plik na pewno nie istnieje (ścieżka rozwinięta w całości, system odpowiada „nie istnieje”), trafia do grupy „bez pliku”
i jest sugerowana do wyłączenia bez wpisu w bazie – nic jej nie uruchomi. Gdy pliku nie da się ustalić (nieznana zmienna, polecenie bez
cudzysłowu bez pasującego pliku), pozycja nie jest ani „bez pliku”, ani sugerowana. Kłódka wygrywa także tu.

## Wpisy

| id | ocena | plik | folder | wydawca | potwierdzone | źródła |
|---|---|---|---|---|---|---|
| `steam` | `can-disable` | `steam.exe` | `%ProgramFiles(x86)%\Steam\` | `Valve Corporation` | tak | `docs/zrodla/2026-11-02-autostart-programy.md`, [pcrisk](https://blog.pcrisk.com/windows/13714-stop-steam-from-opening-on-startup), [Help Desk Geek](https://helpdeskgeek.com/how-to-stop-steam-from-opening-on-startup/) |
| `epic-games-launcher` | `can-disable` | `EpicGamesLauncher.exe` | `%ProgramFiles%\Epic Games\Launcher\Portal\Binaries\Win64\` | `Epic Games, Inc.` | tak | `docs/zrodla/2026-11-02-autostart-programy.md`, [Epic Games](https://www.epicgames.com/help/epic-games-store-c-202300000001639/launcher-support-c-202300000001735/how-to-stop-the-epic-games-launcher-from-running-on-pc-startup-a202300000017760) |
| `battle-net` | `can-disable` | `Battle.net.exe` | `%ProgramFiles(x86)%\Battle.net\` | `Blizzard Entertainment` | tak | `docs/zrodla/2026-11-02-autostart-programy.md`, [Blizzard](https://eu.battle.net/support/en/article/358265) |
| `spotify` | `can-disable` | `Spotify.exe` | `%APPDATA%\Spotify\` | `Spotify Ltd` | tak | `docs/zrodla/2026-11-02-autostart-programy.md`, [pcrisk](https://www.pcrisk.com/blog/windows/13713-stop-spotify-from-opening-on-startup), [Technipages](https://www.technipages.com/spotify-disable-automatic-startup) |
| `discord` | `can-disable` | `Discord.exe` | `%LOCALAPPDATA%\Discord\` | `Discord Inc.` | tak | `docs/zrodla/2026-11-02-autostart-programy.md`, [pcrisk](https://blog.pcrisk.com/windows/13704-stop-discord-from-opening-on-startup) |
| `ea-app` | `can-disable` | `EALauncher.exe` | `%ProgramFiles%\Electronic Arts\EA Desktop\EA Desktop\` | `Electronic Arts` | tak | `docs/zrodla/2026-11-02-autostart-programy.md`, [forum EA](https://forums.ea.com/discussions/ea-app-general-discussion-en/how-do-i-prevent-the-ea-app-from-starting-with-windows/7546567) |
| `security-health` | `protected` | `SecurityHealthSystray.exe` | `%SystemRoot%\System32\` | `Microsoft Corporation` | tak | `docs/zrodla/2026-11-02-autostart-pomiar.md`, [Microsoft Q&A](https://learn.microsoft.com/en-us/answers/a/6762277), [Winhelponline](https://www.winhelponline.com/blog/windows-security-icon-tray-taskmgr-startup/) |
| `realtek-audio-service` | `protected` | `RtkAudUService64.exe` | `%SystemRoot%\System32\`, `%SystemRoot%\System32\DriverStore\FileRepository\*\` | `Realtek Semiconductor Corp.` | nie | [Gridinsoft](https://blog.gridinsoft.com/rtkauduservice64-exe/), [forum ROG](https://rog-forum.asus.com/t5/promotions-general-discussions/unreadable-title/td-p/741740/page/69) |
| `realtek-hd-audio-manager` | `protected` | `RAVCpl64.exe` | `%ProgramFiles%\Realtek\Audio\HDA\` | `Realtek Semiconductor Corp.` | nie | [Gridinsoft](https://blog.gridinsoft.com/ravcpl64-exe/), [Seven Forums](https://www.sevenforums.com/threads/can-i-safely-remove-syntpenh-exe-ravcpl64-exe-s-from-startup.146658/) |
| `elan-touchpad` | `protected` | `ETDCtrl*.exe` | `%ProgramFiles%\Elantech\*`, `%SystemRoot%\System32\` | `ELAN Microelectronics Corp.` | nie | [Gridinsoft](https://blog.gridinsoft.com/etdctrl-exe/), [Appuals](https://appuals.com/fix-etd-control-center-not-responding/) |
| `synaptics-touchpad` | `protected` | `SynTPEnh.exe` | `%ProgramFiles%\Synaptics\SynTP\` | `Synaptics Incorporated` | nie | [Gridinsoft](https://blog.gridinsoft.com/syntpenh-exe/), [Seven Forums](https://www.sevenforums.com/threads/can-i-safely-remove-syntpenh-exe-ravcpl64-exe-s-from-startup.146658/), [Windows Report](https://windowsreport.com/synaptics-touchpad-disabled-windows-startup/) |
| `asus-hotkeys` | `protected` | `HControl.exe` | `%ProgramFiles%\*`, `%ProgramFiles(x86)%\*` | `ASUSTeK Computer Inc.` | nie | [file.net](https://www.file.net/process/hcontrol.exe.html), [forum ROG](https://rog-forum.asus.com/t5/rog-gaming-notebooks/atk-issue-solved/td-p/360155) |
| `kaspersky` | `protected` | `avp.exe` | `%ProgramFiles%\*`, `%ProgramFiles(x86)%\*` | `AO Kaspersky Lab` | nie | [Wilders Security](https://www.wilderssecurity.com/threads/kis-v7-startup-issue.185155/) |
| `avast` | `protected` | `AvastUI.exe` | `%ProgramFiles%\*`, `%ProgramFiles(x86)%\*` | `AVAST Software*`, `Gen Digital*` | nie | [forum Spybot](https://forums.spybot.info/showthread.php?p=418242) |
| `bitdefender` | `protected` | `bdagent.exe` | `%ProgramFiles%\*`, `%ProgramFiles(x86)%\*` | `Bitdefender SRL` | nie | [Bitdefender](https://community.bitdefender.com/en/discussion/99506/bdagent-and-bdagentapp-startup-apps-both-using-the-same-exe), [Bitdefender](https://community.bitdefender.com/en/discussion/comment/337641) |
| `logitech-g-hub` | `protected` | `lghub.exe` | `%ProgramFiles%\*`, `%ProgramFiles(x86)%\*` | `Logitech Europe S.A.` | nie | [Logitech](https://support.logi.com/hc/en-us/articles/9631120673943-G-HUB-or-Logi-Options-is-stuck-on-Loading-screen-with-macOS-Ventura) |

Uwagi do źródeł:

- Wpisy `can-disable`: ścieżki, wydawcy i nazwy wartości pochodzą z odczytu po instalacji programów z winget; skutek wyłączenia
  (program nie startuje sam, działa po ręcznym otwarciu) – z adresów w tabeli. Wartość Run instalatora maszynowego Discorda
  (`C:\ProgramData\SquirrelMachineInstalls\Discord.exe --checkInstall`) nie pasuje do wpisu `discord` i zostaje bez oceny.
- Wpis `ea-app`: wyłączenie autostartu nie zatrzymuje usługi `EABackgroundService`, którą program instaluje obok.
- Źródła wpisów `protected` to głównie serwisy techniczne i fora; dokładne nazwy wartości Run nie są w nich potwierdzone, dlatego wpisy
  nie mają `valueName`. Źródło `logitech-g-hub` opisuje macOS; dla Windows skutek nie jest potwierdzony.

## Celowo bez oceny

Te pozycje nie mają wpisu – program nie daje im ani kłódki, ani sugestii, decyzja zostaje przy użytkowniku:

- **Microsoft Teams** – startuje dwiema drogami (wartość Run i zadanie startowe Sklepu), a konto służbowe może mieć zasady firmy.
- **OneDrive** – synchronizuje też foldery Pulpit, Dokumenty i Obrazy; po wyłączeniu pliki mogą przestać trafiać do chmury, a użytkownik
  może sądzić, że ma kopię.
- **Dropbox** – synchronizacja stoi do ręcznego uruchomienia, a wpis bywa odtwarzany po aktualizacji.
- **Google Drive** – synchronizacja i dostęp do Dysku w Eksploratorze nie działają do ręcznego uruchomienia.
- **Java Update Scheduler** (`jusched.exe`) – wyłączenie ukrywa powiadomienia o poprawkach bezpieczeństwa Javy.
- **iTunesHelper** – Apple odradza wyłączanie ([Apple](https://support.apple.com/en-us/102018)); iTunes przestaje sam reagować na
  podłączony iPhone albo iPod.
- **Sterowniki grafiki** (NVIDIA, AMD, Intel) – nazwy wartości nie są potwierdzone, a od wpisu zależą dodatki (ikona, nagrywanie,
  profile, skróty klawiszowe), nie sam sterownik.
- **Bluetooth** (stary stos z ikoną w zasobniku) – od wpisu zależy tylko ikona, połączenia obsługuje usługa stosu.
- **Microsoft Edge – przyspieszanie uruchamiania** – Edge sam odtwarza wpis; trwale wyłącza się je w ustawieniach Edge
  ([Microsoft](https://support.microsoft.com/en-us/edge/get-help-with-startup-boost)).

## Jak dodać wpis

1. Zainstaluj program na czystej maszynie testowej i odczytaj wpis autostartu: miejsce, nazwę wartości, polecenie, `CompanyName` pliku.
   Odczyt zapisz w dokumencie źródłowym repozytorium (tak jak `docs/zrodla/2026-11-02-autostart-programy.md`).
2. Znajdź źródło `https://` o skutku wyłączenia (dokumentacja producenta albo serwis techniczny). Bez źródła wpis nie wchodzi.
3. Dopisz wpis do `autostart-known.json`: `can-disable` tylko z `confirmed: true`, odczytem i adresem `https://`; `protected` tylko
   z folderami `%SystemRoot%`, `%ProgramFiles%`, `%ProgramFiles(x86)%` i z wydawcą.
4. Dopisz angielski `summary` do `catalog-autostart-known.json`, wiersz do tabeli wyżej i do `docs/en/autostart/README.md`.
5. Uruchom testy (`AutostartKnownBuiltInTests`, `CatalogCopyRulesTests`) i raport `tools/i18n/Get-MissingTranslations.ps1`.
