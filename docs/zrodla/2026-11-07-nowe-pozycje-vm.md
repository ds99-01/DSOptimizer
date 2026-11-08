# Nowe pozycje prywatności: obserwacje na VM 26H2 (2026-11-07)

Dokument źródłowy dla nowych pozycji z grup reklam, telemetrii i funkcji AI w kategorii „Prywatność i AI”. Każda pozycja
wchodzi tylko wtedy, gdy na VM 26H2 widać skutek w Ustawieniach, w aplikacji albo w przeglądarce. Sam zapis wartości nie wystarcza.
Dokument zawiera wyłącznie to, co zaobserwowano. Czego nie sprawdzono, jest oznaczone jako „nieodczytane”.

## Warunki

Stan: 2026-11-07, build 26300.9457.

| VM | Edycja (`EditionID`) | Język interfejsu | Build | Do czego |
|---|---|---|---|---|
| `DSO-EN-Home` | Core (Home) | en-US | 26300.9457 | obserwacje skutku |
| `DSO-EN-Pro` | Professional (Pro) | en-US | 26300.9457 | obserwacje skutku |
| `DSO-PL-Pro` | Professional (Pro) | pl-PL | 26300.9457 | polskie brzmienia (UI Automation, bez zapisu wartości) |
| `DSO-PL-Home` | Core (Home) | pl-PL | 26300.9457 | polskie brzmienie paska zadań |

- Każdy przebieg startował z punktu kontrolnego `czysta-26H2` i kończył się powrotem do niego. Windows nieaktywowany
  (`LicenseStatus` = 5), konto lokalne `dso` bez konta Microsoft, region USA (`GeoId` 244). Treść MSN w Edge była po polsku
  (lokalizacja po adresie IP), mimo en-US.
- Sieć (`Default Switch`) podłączona w grupach reklam, Edge i aplikacji. Telemetria, pasek zadań i przebiegi z restartem VM szły bez sieci.
- Zapis wartości: API rejestru .NET przez PowerShell Direct (wbudowany Administrator). Wartości użytkownika trafiały do
  `HKU\<SID dso>`, czyli tam, gdzie zapisuje je program.
- Obserwacja: skrypt w sesji `dso` przez UI Automation i zrzuty ekranu. Ustawienia w świeżym oknie na każdą stronę. Opcje
  Eksploratora plików przez komunikaty okna i MSAA, bo kontrolki tego okna są w UI Automation tylko panelami. Edge, Notatnik i
  Paint uruchamiane od nowa w każdej fazie i zamykane po odczycie.
- Fazy: przed → zapis → po → cofnięcie → cofnięte. Cofnięcie przywraca stan sprzed zapisu (usuwa wartość albo klucz, który powstał
  przy zapisie). Dla pozycji z przełącznikiem w Ustawieniach skrypt najpierw przełączał go w obie strony i zapisywał różnicę rejestru
  (mechanizm przełącznika). Faza „przed” jest odczytem sprzed tego klikania, a wartość `original` w kartach pochodzi z odczytu
  sprzed klikania. Cofnięcie przywraca stan z chwili tuż przed zapisem pozycji, czyli już po klikaniu. Dlatego przy pozycjach
  z przełącznikiem faza „cofnięte” może mieć wartość zapisaną przez przełącznik zamiast braku wartości (karty 3 i 10).
  Przy pozycjach Eksploratora i paska zadań dochodzi faza „po restarcie Eksploratora”. Grupa z restartem VM:
  zapis → restart → obserwacja → cofnięcie → restart → obserwacja.
- Edge startował z flagą `--no-first-run` (przełącznik Chromium, nie polityka). Bez niej ekran powitalny Edge zasłaniał każdą stronę
  i nie był widoczny w UI Automation. W punkcie kontrolnym jest Edge 152.0.4191.66. Z siecią Edge aktualizował się w trakcie
  przebiegu do 157.0.4341.42, więc przy każdej fazie Edge podana jest wersja.
- Skrypt eksploracji: `tests/vm/explore/` w tym repozytorium.

## Wyniki

Stan: 2026-11-07, build 26300.9457, Home = `DSO-EN-Home`, Pro = `DSO-EN-Pro`.

| # | Pozycja | Home | Pro | Werdykt | `suggested` | `editions` | `risk` |
|---|---|---|---|---|---|---|---|
| 1 | `privacy.explorer-sync-provider-notifications` | skutek | skutek | Portować: pole w Opcjach Eksploratora plików przechodzi na Off | tak | brak | safe |
| 2 | `privacy.lockscreen-spotlight` | skutek | skutek | Portować: ekran blokady przechodzi z Windows spotlight na Picture (tylko `SubscribedContent-338387Enabled`) | nie | brak | caution |
| 3 | `privacy.start-account-notifications` | skutek | skutek | Portować: przełącznik w Personalizacja > Start przechodzi na Off | tak | brak | safe |
| 4 | `privacy.toast-suggested` | skutek | skutek | Portować: nadawca „Suggested” na liście powiadomień = Off | tak | brak | safe |
| 5 | `privacy.toast-backup-reminder` | skutek | skutek | Portować: nadawca „Backup settings” na liście powiadomień = Off | nie | brak | safe |
| 6 | `privacy.settings-365-ads` | brak | brak | nie wchodzi: brak obserwowanego skutku na Home i Pro | – | – | – |
| 7 | `privacy.edge-shopping-recommendations` | skutek | skutek | Portować: pole zakupów w ustawieniach Edge Off i zablokowane | nie | brak | safe |
| 8 | `privacy.edge-newtab-content` | skutek | skutek | Portować: kanał MSN i domyślne kafelki znikają z nowej karty | nie | brak | safe |
| 9 | `privacy.edge-prompts-default-browser` | nieobserwowalne | nieobserwowalne | nie wchodzi: nieobserwowalne na VM (brak monitu o przeglądarkę domyślną) | – | – | – |
| 10 | `privacy.start-track-progs` | skutek | skutek | Portować: przełącznik „Improve Start and search results” przechodzi na Off | tak | brak | caution |
| 11 | `privacy.online-speech` | skutek (dowód odwrotny) | skutek (dowód odwrotny) | Portować: wartość steruje przełącznikiem rozpoznawania mowy online | nie | brak | caution |
| 12 | `privacy.activity-history` | nieobserwowalne | nieobserwowalne | nie wchodzi: nieobserwowalne na VM (brak strony Historii aktywności) | – | – | – |
| 13 | `privacy.inking-typing-improve` | skutek | skutek | Portować: przełącznik słownika pisma odręcznego i pisania przechodzi na Off (4 wpisy) | nie | brak | caution |
| 14 | `privacy.edge-diagnostic-data` | skutek | skutek | Portować: dane diagnostyczne i Feedback w ustawieniach Edge Off i zablokowane | nie | brak | caution |
| 15 | `privacy.edge-copilot` | skutek | skutek | Portować: przycisk „Chat” (Copilot) znika z paska Edge | nie | brak | caution |
| 16 | `privacy.paint-ai` | nieobserwowalne | nieobserwowalne | nie wchodzi: nieobserwowalne na VM (funkcje AI Painta wymagają aktualizacji aplikacji) | – | – | – |
| 17 | `privacy.notepad-ai` | skutek | skutek | Portować: przycisk „Writing tools” znika z Notatnika | nie | brak | caution |
| 18 | `privacy.copilot-taskbar-button` | brak | brak | nie wchodzi: brak obserwowanego skutku na Home i Pro | – | – | – |
| – | `privacy.ai-service-fabric` | – | – | nie wchodzi: zysk pomijalny | – | – | – |

Home i Pro dały wszędzie ten sam wynik, więc żadna pozycja nie ma `editions`.

## Źródła wspólne

Data dostępu do repozytoriów: 2026-11-06. Data dostępu do stron Microsoft Learn: 2026-11-07.

- Win11Debloat: https://github.com/Raphire/Win11Debloat (gałąź `master`). Pliki `Regfiles/*.reg` są w UTF-16; numery linii po konwersji do UTF-8, bez pustych linii.
- WinUtil: https://github.com/ChrisTitusTech/winutil, plik `config/tweaks.json` (gałąź `main`).
- RemoveWindowsAI: https://github.com/zoicware/RemoveWindowsAI, plik `RemoveWindowsAi.ps1` (gałąź `main`).
- privacy.sexy: https://github.com/undergroundwires/privacy.sexy, plik `src/application/collections/windows.yaml` (gałąź `master`).
- Indeks polityk Edge: https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies (ms.date 2026-11-04). Każda strona
  polityki otwarta z indeksu. Wszystkie użyte polityki: ścieżka `SOFTWARE\Policies\Microsoft\Edge`, `REG_DWORD`, żadna nie jest
  oznaczona jako przestarzała, żadna strona nie ogranicza polityki do komputerów w domenie albo w MDM.

## Karty pozycji, które wchodzą

Zapis ścieżek: `HKCU\…` = wartość zalogowanego użytkownika (w przypadkach VM `HKU\{UserSid}\…`). `<RemoveEntry>` = na czystej VM
wartości nie ma, cofnięcie ją usuwa. Stan klucza na czystej VM: `KeyPresentAfterUndo` = klucz istnieje i zostaje po cofnięciu,
`KeyAbsentAfterUndo` = klucza nie ma, cofnięcie usuwa go razem z wartością.

### 1. `privacy.explorer-sync-provider-notifications`

Stan: 2026-11-07, build 26300.9457.

| Pole | Wartość |
|---|---|
| `scope` / `group` | `user` / `ads` |
| `suggested` | tak |
| `risk` | `safe`: zmienia jedno pole Opcji Eksploratora plików, utraty funkcji nie zaobserwowano |
| `editions` | brak |
| Restart | brak: pole Off już przed restartem Eksploratora |
| Stan klucza na czystej VM | `KeyPresentAfterUndo`: `HKU\{UserSid}\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced` |

| Ścieżka (JSON) | Nazwa | Typ | Wartość | `original` |
|---|---|---|---|---|
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced` | `ShowSyncProviderNotifications` | DWord | 0 | `<RemoveEntry>` |

- Brzmienia: okno „File Explorer Options” / „Opcje Eksploratora plików”; pole „Show sync provider notifications” /
  „Pokaż powiadomienia dostawcy synchronizacji” (zakładka Widok).
- Obserwacja (Home 20:14–20:15, Pro 20:14–20:15): Opcje Eksploratora plików, zakładka Widok. Przed: pole zaznaczone, wartości brak.
  Po zapisie: pole odznaczone, tak samo po restarcie Eksploratora. Cofnięte: pole zaznaczone. Home i Pro tak samo.
- Nieodczytane: banerów OneDrive w samym Eksploratorze na VM nie było (OneDrive nieużywany), więc skutek dotyczy pola ustawień.
- Źródła: Win11Debloat `Regfiles/Disable_Windows_Suggestions.reg` linie 28–30 (sekcja „Sync provider ads”);
  privacy.sexy `windows.yaml:30489`. Brak dokumentacji MS, dowód: test na VM.

### 2. `privacy.lockscreen-spotlight`

Stan: 2026-11-07, build 26300.9457.

| Pole | Wartość |
|---|---|
| `scope` / `group` | `user` / `ads` |
| `suggested` | nie |
| `risk` | `caution`: ekran blokady traci Windows spotlight (lista przechodzi na Picture) |
| `editions` | brak |
| Restart | brak: skutek w świeżym oknie Ustawień |
| Stan klucza na czystej VM | `KeyPresentAfterUndo`: `HKU\{UserSid}\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager` |

| Ścieżka (JSON) | Nazwa | Typ | Wartość | `original` |
|---|---|---|---|---|
| `HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager` | `SubscribedContent-338387Enabled` | DWord | 0 | `<RemoveEntry>` |

- Druga wartość ze źródeł, `RotatingLockScreenOverlayEnabled`=0, nie wchodzi: zapisana sama nie zmieniła niczego na Home ani na Pro
  (lista przed, po i po cofnięciu = Windows spotlight; sonda Home i Pro 21:29–21:30). Na czystej VM ma wartość 1.
- Brzmienia: „Personalize your lock screen” = „Windows spotlight” / „Picture”; PL „Personalizuj ekran blokady” = „W centrum uwagi
  Windows” (pozycji „Obraz” po polsku nie odczytano, bo polska VM nie miała zapisu). Pole „Get fun facts, tips, tricks, and more on
  your lock screen” pojawia się po zapisie; brzmienie PL nieodczytane.
- Obserwacja (sonda z samą `SubscribedContent-338387Enabled`; Home 21:30–21:31, Pro 21:30–21:31): `ms-settings:lockscreen`. Przed: lista =
  Windows spotlight, pola „fun facts” brak. Po zapisie: lista = Picture, pojawiają się „Choose a photo”, „Browse photos” i pole
  „Get fun facts…” = Off. Cofnięte: Windows spotlight, pola „fun facts” brak. Home i Pro tak samo. Pierwszy przebieg (obie wartości
  naraz, 20:15–20:16) dał ten sam wynik.
- Windows nieaktywowany: wszystkie pola strony były wyszarzone, więc to odczyt stanu, nie kliknięcie.
- Nieodczytane: zmiana samego obrazu ekranu blokady. Podgląd na stronie był identyczny przed i po (domyślny obraz Windows), a ekranu
  blokady nie wywołano, bo zablokowałby sesję, w której działa obserwacja. Na aktywowanym Windows strona może wyglądać inaczej.
- Związek z `privacy.spotlight`: polityka tej pozycji (`DisableWindowsSpotlightFeatures`=1) nie zmieniła tej listy na Home ani na Pro,
  także po restarcie VM. Ta pozycja nie pokrywa więc skutku `privacy.spotlight` i odwrotnie.
- Źródła: Win11Debloat `Config/Features.json:487-492` (DisableLockscreenTips), `Regfiles/Disable_Lockscreen_Tips.reg` linie 2–5.
  Brak dokumentacji MS, dowód: test na VM.

### 3. `privacy.start-account-notifications`

Stan: 2026-11-07, build 26300.9457.

| Pole | Wartość |
|---|---|
| `scope` / `group` | `user` / `ads` |
| `suggested` | tak |
| `risk` | `safe`: wyłącza powiadomienia o koncie w Start, utraty funkcji nie zaobserwowano |
| `editions` | brak |
| Restart | brak: przełącznik Off już przed restartem Eksploratora |
| Stan klucza na czystej VM | `KeyPresentAfterUndo`: `HKU\{UserSid}\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced` |

| Ścieżka (JSON) | Nazwa | Typ | Wartość | `original` |
|---|---|---|---|---|
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced` | `Start_AccountNotifications` | DWord | 0 | `<RemoveEntry>` |

- Mechanizm przełącznika (`SystemSettings_Start_AccountNotifications_ToggleSwitch`) z różnicy rejestru: Off zapisuje
  `Start_AccountNotifications`=0, On zapisuje 1. Na czystej VM wartości nie ma, przełącznik jest On (faza „przed”).
- Faza „cofnięte” przywróciła wartość 1 zapisaną wcześniej przez klikanie przełącznika, a nie brak wartości. Stan „brak wartości = On”
  widać w fazie „przed”.
- Brzmienia: „Show account-related notifications” / „Pokaż powiadomienia związane z kontem”.
- Obserwacja (Home 20:17–20:19, Pro 20:17–20:19): `ms-settings:personalization-start`. Przed: On. Po zapisie: Off, tak samo po restarcie
  Eksploratora. Cofnięte: On. Home i Pro tak samo.
- Nieodczytane: plakietki konta w samym menu Start (konto lokalne; zrzuty Startu bez oceny różnicy).
- Źródła: Win11Debloat `Regfiles/Disable_Windows_Suggestions.reg` linie 40–42. Brak dokumentacji MS, dowód: test na VM.

### 4. `privacy.toast-suggested`

Stan: 2026-11-07, build 26300.9457.

| Pole | Wartość |
|---|---|
| `scope` / `group` | `user` / `ads` |
| `suggested` | tak |
| `risk` | `safe`: wyłącza jedną kategorię powiadomień, utraty funkcji nie zaobserwowano |
| `editions` | brak |
| Restart | brak: skutek w świeżym oknie Ustawień |
| Stan klucza na czystej VM | `KeyAbsentAfterUndo`: `HKU\{UserSid}\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\Windows.SystemToast.Suggested`; `KeyPresentAfterUndo`: `HKU\{UserSid}\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings` (Setup go zapewnia: na świeżej VM klucza zaraz po zalogowaniu nie było, Windows utworzył go po kilku minutach, w eksploracji ok. 5,5 min po starcie, razem z pierwszym powiadomieniem systemowym; w różnicy rejestru wiersz `Notifications\Settings` jako nowy klucz) |

| Ścieżka (JSON) | Nazwa | Typ | Wartość | `original` |
|---|---|---|---|---|
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\Windows.SystemToast.Suggested` | `Enabled` | DWord | 0 | `<RemoveEntry>` |

- Brzmienia: nadawca „Suggested” na liście powiadomień (`Windows.SystemToast.Suggested2_ToggleSwitch`); brzmienie PL nieodczytane
  (nadawca pojawia się dopiero po zapisie, a polska VM nie miała zapisu).
- Obserwacja (Home 20:19–20:20, Pro 20:19–20:20): `ms-settings:notifications`, lista nadawców. Przed: nadawcy „Suggested” brak. Po zapisie:
  „Suggested” jest, przełącznik Off. Cofnięte: nadawcy brak. Home i Pro tak samo.
- Skutek pośredni: samego powiadomienia „Suggested” na VM nie było w żadnej fazie, więc nie porównano „jest/nie ma powiadomienia”.
- Źródła: Win11Debloat `Regfiles/Disable_Windows_Suggestions.reg` linie 34–36. Brak dokumentacji MS, dowód: test na VM.

### 5. `privacy.toast-backup-reminder`

Stan: 2026-11-07, build 26300.9457.

| Pole | Wartość |
|---|---|
| `scope` / `group` | `user` / `ads` |
| `suggested` | nie |
| `risk` | `safe`: wyłącza jedną kategorię powiadomień, utraty funkcji nie zaobserwowano |
| `editions` | brak |
| Restart | brak: skutek w świeżym oknie Ustawień |
| Stan klucza na czystej VM | `KeyAbsentAfterUndo`: `HKU\{UserSid}\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\Windows.SystemToast.BackupReminder`; `KeyPresentAfterUndo`: `HKU\{UserSid}\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings` (Setup go zapewnia: na świeżej VM klucza zaraz po zalogowaniu nie było, Windows utworzył go po kilku minutach, w eksploracji ok. 5,5 min po starcie, razem z pierwszym powiadomieniem systemowym; w różnicy rejestru wiersz `Notifications\Settings` jako nowy klucz) |

| Ścieżka (JSON) | Nazwa | Typ | Wartość | `original` |
|---|---|---|---|---|
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\Windows.SystemToast.BackupReminder` | `Enabled` | DWord | 0 | `<RemoveEntry>` |

- Brzmienia: nadawca „Backup settings” (`Windows.SystemToast.BackupReminder1_ToggleSwitch`); brzmienie PL nieodczytane (jak w 4).
- Obserwacja (Home 20:20–20:21, Pro 20:20–20:21): jak w 4. Przed: nadawcy „Backup settings” brak. Po zapisie: jest, Off. Cofnięte: brak.
- Na Pro pojawiło się w innej fazie powiadomienie OneDrive „Turn On Windows Backup”. To inny nadawca (OneDrive), nie ta kategoria.
- Źródła: Win11Debloat `Regfiles/Disable_Windows_Suggestions.reg` linie 43–45. Brak dokumentacji MS, dowód: test na VM.

### 7. `privacy.edge-shopping-recommendations`

Stan: 2026-11-07, build 26300.9457.

| Pole | Wartość |
|---|---|
| `scope` / `group` | `machine` / `ads` |
| `suggested` | nie |
| `risk` | `safe`: wyłącza funkcje zakupowe Edge, utraty innej funkcji nie zaobserwowano |
| `editions` | brak |
| Restart | brak systemowego. Edge był uruchamiany od nowa w każdej fazie; strona MS `SpotlightExperiencesAndRecommendationsEnabled` mówi „Requires browser restart” |
| Stan klucza na czystej VM | `KeyAbsentAfterUndo`: `HKLM\SOFTWARE\Policies\Microsoft\Edge`; `KeyPresentAfterUndo`: `HKLM\SOFTWARE\Policies\Microsoft` |

| Ścieżka (JSON) | Nazwa | Typ | Wartość | `original` |
|---|---|---|---|---|
| `HKLM\SOFTWARE\Policies\Microsoft\Edge` | `EdgeShoppingAssistantEnabled` | DWord | 0 | `<RemoveEntry>` |
| `HKLM\SOFTWARE\Policies\Microsoft\Edge` | `ShowRecommendationsEnabled` | DWord | 0 | `<RemoveEntry>` |
| `HKLM\SOFTWARE\Policies\Microsoft\Edge` | `SpotlightExperiencesAndRecommendationsEnabled` | DWord | 0 | `<RemoveEntry>` |

| Polityka | Strona MS (ms.date) | `edge://policy` Home | `edge://policy` Pro | Osobna obserwacja |
|---|---|---|---|---|
| `EdgeShoppingAssistantEnabled` | https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/edgeshoppingassistantenabled (2026-05-20) | false, Platform, Device, Mandatory, OK | jak Home | tak: pole zakupów |
| `ShowRecommendationsEnabled` | https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/showrecommendationsenabled (2026-05-20) | false, Platform, Device, Mandatory, OK | jak Home | nie |
| `SpotlightExperiencesAndRecommendationsEnabled` | https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/spotlightexperiencesandrecommendationsenabled (2026-05-20) | false, Platform, Device, Mandatory, OK | jak Home | nie |

- Polityki zapisane razem. Skutek pokazało tylko pole zakupów, czyli `EdgeShoppingAssistantEnabled`. Dla pozostałych dwóch na VM nie
  było monitów, banerów ani osobnego pola, więc ich skutku nie obserwowano.
- Dopiski stron MS: `EdgeShoppingAssistantEnabled` – „If you disable this policy, shopping features such as price comparison, coupons,
  rebates, and express checkout aren't automatically found for retail domains.”; `ShowRecommendationsEnabled` – „If you disable this
  setting, users won't receive any recommendations or notifications from Microsoft Edge.”; `SpotlightExperiencesAndRecommendationsEnabled`
  – „If you disable this setting, spotlight experiences and recommendations are turned off.”, „Requires browser restart”, tylko Windows.
- Brzmienia EN: „Save time and money with Shopping in Microsoft Edge”, „This setting is managed by your organization.”. Brzmienia PL nieodczytane.
- Obserwacja (`edge://settings/?search=shopping`; Home 20:56–20:59, Edge przed 152.0.4191.66, po i cofnięte 157.0.4341.42; Pro 20:56–20:59,
  Edge 152.0.4191.66 we wszystkich fazach). Przed: „Save time and money with Shopping in Microsoft Edge” = On, aktywne. Po: Off,
  nieaktywne, obok „This setting is managed by your organization.”. Cofnięte: On, aktywne. Home i Pro tak samo.
- Pierwszy przebieg (20:32–20:35, `edge://settings` i `edge://newtab`) nie pokazał różnic, bo strona główna ustawień w tej wersji nie ma
  tego pola. Polityki miały tam już status OK.
- Źródła: Win11Debloat `Config/Features.json:540-544`, `Regfiles/Disable_Edge_Ads_And_Suggestions.reg` linie 2–16;
  privacy.sexy `windows.yaml:10145-10277`; strony MS jak w tabeli.

### 8. `privacy.edge-newtab-content`

Stan: 2026-11-07, build 26300.9457.

| Pole | Wartość |
|---|---|
| `scope` / `group` | `machine` / `ads` |
| `suggested` | nie |
| `risk` | `safe`: znika treść MSN i domyślne kafelki, utraty funkcji nie zaobserwowano |
| `editions` | brak |
| Restart | brak systemowego (Edge uruchamiany od nowa w każdej fazie) |
| Stan klucza na czystej VM | `KeyAbsentAfterUndo`: `HKLM\SOFTWARE\Policies\Microsoft\Edge`; `KeyPresentAfterUndo`: `HKLM\SOFTWARE\Policies\Microsoft` |

| Ścieżka (JSON) | Nazwa | Typ | Wartość | `original` |
|---|---|---|---|---|
| `HKLM\SOFTWARE\Policies\Microsoft\Edge` | `NewTabPageContentEnabled` | DWord | 0 | `<RemoveEntry>` |
| `HKLM\SOFTWARE\Policies\Microsoft\Edge` | `NewTabPageHideDefaultTopSites` | DWord | 1 | `<RemoveEntry>` |

`NewTabPageHideDefaultTopSites` to wpis opcjonalny z osobną sondą. Wchodzi, bo osobna obserwacja pokazała skutek.

| Polityka | Strona MS (ms.date) | `edge://policy` Home | `edge://policy` Pro | Osobna obserwacja |
|---|---|---|---|---|
| `NewTabPageContentEnabled` | https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/newtabpagecontentenabled (2026-05-20) | false, Platform, Device, Mandatory, OK | jak Home | tak |
| `NewTabPageHideDefaultTopSites` | https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/newtabpagehidedefaulttopsites (2026-05-20) | true, Platform, Device, Mandatory, OK | jak Home (powtórka) | tak |

- Dopiski stron MS: `NewTabPageContentEnabled` – „If you disable this policy, Microsoft Edge doesn't display Microsoft content on the new
  tab page.”, polityka obejmuje „all profile types, namely unsigned local user profiles, profiles signed in using a Microsoft Account”;
  `NewTabPageHideDefaultTopSites` – „If you set this policy to true, the default top site tiles are hidden.”.
- Obserwacja `NewTabPageContentEnabled` (`edge://newtab`; Home 20:35–20:37, Edge 152.0.4191.66 przed i po, 157.0.4341.42 cofnięte;
  Pro 20:35–20:37, Edge 152.0.4191.66 przed, 157.0.4341.42 po i cofnięte). Przed: kanał MSN (m.in. „Odkrywaj”, „Ustawienia kanału
  informacyjnego”, artykuły, reklamy produktów). Po: kanału nie ma (elementy UI Automation 219 → 72 na Home, 218 → 71 na Pro).
  Cofnięte: kanał wraca. Home i Pro tak samo.
- Obserwacja `NewTabPageHideDefaultTopSites` (`edge://newtab`). Home (20:37–20:39, Edge 157.0.4341.42): przed kafelki Microsoft 365,
  Booking.com, Allegro, Rtv Euro Agd, Mediamarkt, LinkedIn; po tylko Microsoft 365. Faza „cofnięte” na Home nieodczytana: okno
  logowania OneDrive i powiadomienie zasłoniły Edge, a UI Automation nie zwróciło kafelków ani tabeli polityk. Pro (powtórka 21:12–21:14,
  Edge 152.0.4191.66): po tylko Microsoft 365, cofnięte Microsoft 365, Booking.com, Allegro, Rtv Euro Agd, Mediamarkt, LinkedIn.
  Pierwszy przebieg na Pro (20:37–20:39) pokazał przed sześć kafelków i po jeden, ale okno Ustawień zasłoniło wtedy `edge://policy`.
- Brzmienia: nazwy kafelków i treść MSN były po polsku na obu VM (lokalizacja po IP). Brzmienia ustawień nowej karty nieodczytane.
- Źródła: Win11Debloat `Config/Features.json:540-544`, `Regfiles/Disable_Edge_Ads_And_Suggestions.reg` linie 2–16; strony MS jak w tabeli.

### 10. `privacy.start-track-progs`

Stan: 2026-11-07, build 26300.9457.

| Pole | Wartość |
|---|---|
| `scope` / `group` | `user` / `telemetry` |
| `suggested` | tak |
| `risk` | `caution`: pole „Show most used apps” / „Pokaż najczęściej używane aplikacje” w Personalizacja > Start robi się nieaktywne (w fazie „po” `Enabled` = false na Home i Pro), więc listy najczęściej używanych aplikacji nie da się włączyć |
| `editions` | brak |
| Restart | brak: skutek w świeżym oknie Ustawień |
| Stan klucza na czystej VM | `KeyPresentAfterUndo`: `HKU\{UserSid}\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced` |

| Ścieżka (JSON) | Nazwa | Typ | Wartość | `original` |
|---|---|---|---|---|
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced` | `Start_TrackProgs` | DWord | 0 | `<RemoveEntry>` |

- Mechanizm przełącznika (`SystemSettings_Privacy_StoreAppUsage_ToggleSwitch`) z różnicy rejestru: Off zapisuje `Start_TrackProgs`=0,
  On zapisuje 1. Na czystej VM wartości nie ma, przełącznik jest On (faza „przed”).
- Faza „cofnięte” przywróciła wartość 1 zapisaną wcześniej przez klikanie przełącznika, a nie brak wartości. Stan „brak wartości = On”
  widać w fazie „przed”.
- Uwaga: po zapisie pole „Show most used apps” / „Pokaż najczęściej używane aplikacje” jest nieaktywne (`Enabled` = false w fazie „po”
  na Home i Pro). Na czystej VM pole było Off, ale aktywne; po cofnięciu znów aktywne.
- Brzmienia: „Improve Start and search results” / „Popraw wyniki uruchamiania i wyszukiwania” (Prywatność > Ogólne);
  „Show most used apps” / „Pokaż najczęściej używane aplikacje” (Personalizacja > Start).
- Obserwacja (Home 20:24–20:26, Pro 20:24–20:26): `ms-settings:privacy-general` i `ms-settings:personalization-start`. Przed: „Improve Start
  and search results” = On; „Show most used apps” = Off, aktywne. Po zapisie: Off; „Show most used apps” Off i nieaktywne. Cofnięte: On;
  „Show most used apps” Off, aktywne. Home i Pro tak samo.
- Źródła: Win11Debloat `Regfiles/Disable_Telemetry.reg` linie 25–27; WinUtil `config/tweaks.json:543`; privacy.sexy `windows.yaml:7792`.
  Brak dokumentacji MS, dowód: test na VM.

### 11. `privacy.online-speech`

Stan: 2026-11-07, build 26300.9457.

| Pole | Wartość |
|---|---|
| `scope` / `group` | `user` / `telemetry` |
| `suggested` | nie |
| `risk` | `caution`: wyłącza rozpoznawanie mowy online; Windows pisze wprost, że aplikacje z tą technologią przestają działać |
| `editions` | brak |
| Restart | brak: skutek w świeżym oknie Ustawień |
| Stan klucza na czystej VM | `KeyAbsentAfterUndo`: `HKU\{UserSid}\Software\Microsoft\Speech_OneCore\Settings` (na czystej VM nie ma ani `Settings`, ani `Settings\OnlineSpeechPrivacy`); `KeyPresentAfterUndo`: `HKU\{UserSid}\Software\Microsoft\Speech_OneCore` |

| Ścieżka (JSON) | Nazwa | Typ | Wartość | `original` |
|---|---|---|---|---|
| `HKCU\Software\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy` | `HasAccepted` | DWord | 0 | `<RemoveEntry>` |

- Detekcja na świeżym Windows: przełącznik jest Off, ale wartości `HasAccepted` ani kluczy `Settings\OnlineSpeechPrivacy` nie ma, więc
  detekcja pozycji da `NotApplied`. Zastosowanie zapisze 0 i utworzy oba klucze; na ekranie nic się nie zmieni. `Applied` pokaże też,
  gdy użytkownik sam wyłączył przełącznik (Windows zapisuje wtedy `HasAccepted`=0).

- Dowód odwrotny. Na czystej VM przełącznik „Online speech recognition” jest już Off, a wartości i klucza nie ma. Zapis 0 nic więc nie
  zmienia na ekranie (przed = po = cofnięte = Off). Skutek wartości pokazała osobna sonda z wartością przeciwną:
  - mechanizm z różnicy rejestru: przełączenie Off → On tworzy klucze `Settings` i `OnlineSpeechPrivacy` i zapisuje `HasAccepted`=1;
    On → Off zapisuje `HasAccepted`=0;
  - zapis `HasAccepted`=1 (z wartościami ubocznymi z tej samej różnicy, potem cofniętymi) przełącza przełącznik na On i pokazuje sekcję
    „Start contributing my voice clips”; cofnięcie do `HasAccepted`=0 przełącza z powrotem na Off.
  Wniosek: `HasAccepted` steruje przełącznikiem w obie strony, a 0 = Off.
- Brzmienia: przełącznik ma w UI Automation nazwę równą opisowi: EN „If you turn off online speech recognition, you won’t be able to use apps
  that use Microsoft’s online speech recognition technology. …”; PL „Jeśli wyłączysz rozpoznawanie mowy online, nie będzie można korzystać
  z aplikacji używających technologii rozpoznawania mowy online firmy Microsoft. …”. Opis strony: EN „Use your voice for apps using
  Microsoft’s online speech recognition technology.”, PL „Używaj głosu w aplikacjach, korzystając z technologii rozpoznawania mowy online
  firmy Microsoft.”. Nagłówek EN „Online speech recognition”; nagłówka PL nie odczytano jako osobnego elementu.
- Obserwacja (`ms-settings:privacy-speech`): przebieg główny Home 20:26–20:27, Pro 20:26–20:28; sonda odwrotna Home 20:54–20:55, Pro 20:54–20:55.
  Home i Pro tak samo.
- Nieodczytane: czy dyktowanie (Win+H) przestaje działać. Okno „Windows Input Experience” otworzyło się tylko w fazie „przed”
  i nie zależało od wartości, która się nie zmieniła.
- Źródła: Win11Debloat `Regfiles/Disable_Telemetry.reg` linie 8–10; WinUtil `config/tweaks.json` (wpis `OnlineSpeechPrivacy`, okolice
  linii 480–490, numer przybliżony). Brak dokumentacji MS dla tej wartości, dowód: test na VM.

### 13. `privacy.inking-typing-improve`

Stan: 2026-11-07, build 26300.9457.

| Pole | Wartość |
|---|---|
| `scope` / `group` | `user` / `telemetry` |
| `suggested` | nie: wyłączenie może wyczyścić słownik niestandardowy, a powrotu słownika po cofnięciu nie sprawdzono |
| `risk` | `caution`: wyłączenie czyści słownik niestandardowy pisma odręcznego i pisania (opis Windows pod przełącznikiem, cytat niżej); czy cofnięcie przywraca zawartość słownika: nieodczytane |
| `editions` | brak |
| Restart | brak: skutek w świeżym oknie Ustawień |
| Stan klucza na czystej VM | `KeyPresentAfterUndo`: `HKU\{UserSid}\Software\Microsoft\InputPersonalization`, `HKU\{UserSid}\Software\Microsoft\InputPersonalization\TrainedDataStore`, `HKU\{UserSid}\Software\Microsoft\Personalization\Settings` |

| Ścieżka (JSON) | Nazwa | Typ | Wartość | `original` |
|---|---|---|---|---|
| `HKCU\Software\Microsoft\InputPersonalization` | `RestrictImplicitInkCollection` | DWord | 1 | 0 |
| `HKCU\Software\Microsoft\InputPersonalization` | `RestrictImplicitTextCollection` | DWord | 1 | 0 |
| `HKCU\Software\Microsoft\InputPersonalization\TrainedDataStore` | `HarvestContacts` | DWord | 0 | 1 |
| `HKCU\Software\Microsoft\Personalization\Settings` | `AcceptedPrivacyPolicy` | DWord | 0 | 1 |

- Mechanizm przełącznika „Custom inking and typing dictionary” (`SystemSettings_Privacy_PersonalizeInkType_Toggle_ToggleSwitch`) z różnicy
  rejestru: z wartości zapisywanych przez przełącznik pozycja bierze te cztery. Off zapisuje je z wartościami z kolumny „Wartość”, On
  z wartościami z kolumny `original`. Wartości `original` = odczyt z czystej VM przed jakimkolwiek kliknięciem, Home i Pro tak samo.
- Pozostałe zapisy przełącznika przy Off (pozycja ich nie bierze): `HKCU\Software\Microsoft\Input\Personalization` `UserDictKey` (binarna)
  i `UserDictName` (tekst, inny na każdej VM); `HKCU\Software\Microsoft\Windows\CurrentVersion\CPSS\Store\InkingAndTypingPersonalization`
  `Value` (0, przy On 1); wartości `Value` w `…\CPSS\Store\IME\Bopomofo\…` i `…\CPSS\Store\IME\Japanese\LearningLevel` (ustawienia IME).
- Faza „po” obejmowała zapis pięciu wartości: tych czterech i `HKCU\Software\Microsoft\Input\TIPC` `Enabled`=0. Piąty wpis ze źródeł,
  `TIPC\Enabled`, **nie wchodzi**. To, że wystarczą cztery, wynika z mechanizmu przełącznika: różnica rejestru przy przełączeniu na Off nie
  dotyka klucza `TIPC`. Pole, któremu `TIPC` może odpowiadać, na stronie Diagnostyka i opinie („Improve the language recognition and
  suggestion capabilities…”) było nieaktywne i Off we wszystkich fazach, bo opcjonalne dane diagnostyczne są wyłączone, więc skutku
  `TIPC\Enabled` nie widać. Na czystej VM klucz `TIPC` istnieje, wartości `Enabled` nie ma.
- Brzmienia: „Custom inking and typing dictionary” / „Niestandardowe pisanie odręczne i słownik pisania”. Opis pod przełącznikiem:
  EN „Get better suggestions by creating a custom dictionary using your typing and handwriting info. If you turn off this setting, your
  dictionary will be cleared and your suggestions won’t be personalized.”; PL „Uzyskaj lepsze sugestie, tworząc słownik niestandardowy przy
  użyciu informacji o pisaniu i piśmie ręcznym. Jeśli wyłączysz to ustawienie, słownik zostanie wyczyszczony, a Twoje sugestie nie będą
  spersonalizowane.”.
- Obserwacja (Home 20:29–20:30, Pro 20:29–20:31): `ms-settings:privacy-speechtyping` i `ms-settings:privacy-feedback`. Przed: On. Po zapisie: Off.
  Cofnięte: On. Home i Pro tak samo.
- Nieodczytane: czy słownik użytkownika faktycznie znika i czy cofnięcie go przywraca (na VM słownik był pusty).
- Źródła: Win11Debloat `Regfiles/Disable_Telemetry.reg` linie 11–21; RemoveWindowsAI `RemoveWindowsAi.ps1:1120-1122`; WinUtil
  `config/tweaks.json` (sekcja telemetrii, bez numeru linii). Brak dokumentacji MS, dowód: test na VM.

### 14. `privacy.edge-diagnostic-data`

Stan: 2026-11-07, build 26300.9457.

| Pole | Wartość |
|---|---|
| `scope` / `group` | `machine` / `telemetry` |
| `suggested` | nie |
| `risk` | `caution`: `DiagnosticData`=0 wyłącza także wymagane dane diagnostyczne Edge, a Microsoft tego nie zaleca („Off (Not recommended)”); wartość 0 zostaje |
| `editions` | brak |
| Restart | brak systemowego. Edge był uruchamiany od nowa w każdej fazie; strony MS `DiagnosticData` i `UserFeedbackAllowed` mówią „Requires browser restart” |
| Stan klucza na czystej VM | `KeyAbsentAfterUndo`: `HKLM\SOFTWARE\Policies\Microsoft\Edge`; `KeyPresentAfterUndo`: `HKLM\SOFTWARE\Policies\Microsoft` |

| Ścieżka (JSON) | Nazwa | Typ | Wartość | `original` |
|---|---|---|---|---|
| `HKLM\SOFTWARE\Policies\Microsoft\Edge` | `DiagnosticData` | DWord | 0 | `<RemoveEntry>` |
| `HKLM\SOFTWARE\Policies\Microsoft\Edge` | `PersonalizationReportingEnabled` | DWord | 0 | `<RemoveEntry>` |
| `HKLM\SOFTWARE\Policies\Microsoft\Edge` | `UserFeedbackAllowed` | DWord | 0 | `<RemoveEntry>` |

`UserFeedbackAllowed` to wpis opcjonalny z osobną sondą. Wchodzi, bo osobna obserwacja pokazała skutek.

| Polityka | Strona MS (ms.date) | `edge://policy` Home | `edge://policy` Pro | Osobna obserwacja |
|---|---|---|---|---|
| `DiagnosticData` | https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/diagnosticdata (2026-05-20) | 0, Platform, Device, Mandatory, OK | jak Home | tak |
| `PersonalizationReportingEnabled` | https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/personalizationreportingenabled (2026-05-20) | false, Platform, Device, Mandatory, OK | jak Home | nie |
| `UserFeedbackAllowed` | https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/userfeedbackallowed (2026-09-11) | false, Platform, Device, Mandatory, OK | jak Home | tak |

- `DiagnosticData`=0 wyłącza także wymagane dane diagnostyczne Edge („'Off' turns off required and optional diagnostic data collection”),
  a Microsoft tego nie zaleca („Off (Not recommended)”). Wartość 0 zostaje: tak zapisują ją źródła i z nią sonda dała skutek. Wartości 1
  („RequiredData”) nie sondowano.
- `DiagnosticData` i `PersonalizationReportingEnabled` zapisane razem. Skutek pokazało pole danych diagnostycznych, czyli `DiagnosticData`.
  `PersonalizationReportingEnabled` nie miało osobnej obserwacji: wyszukiwanie „personalization” zwracało we wszystkich fazach tylko
  „Manage your history, privacy and personalization settings.”, a pole „Allow Microsoft to save your browsing activity… to personalize…”
  było Off już na czystej VM i nie zmieniło się.
- Dopiski stron MS: `DiagnosticData` – typ Integer, „Off (0) = Off (Not recommended)”, „'Off' turns off required and optional diagnostic
  data collection. This option isn't recommended.”, „Requires browser restart”; `PersonalizationReportingEnabled` – „If you disable this
  policy, users can't change or override the setting.”, „This setting isn't available for child accounts or enterprise accounts.”;
  `UserFeedbackAllowed` – „If you disable this policy, users can't invoke Edge Feedback.”, „Requires browser restart”.
- Brzmienia EN: „Required diagnostic data”, „Send optional diagnostic data to improve Microsoft products”, „Feedback”,
  „This setting is managed by your organization”. Brzmienia PL nieodczytane.
- Obserwacja `DiagnosticData` (`edge://settings/?search=diagnostic`; Home 20:59–21:02, Edge 157.0.4341.42; Pro 20:59–21:02, Edge 152.0.4191.66
  przed, 157.0.4341.42 po i cofnięte). Przed: tekst „Required diagnostic data” bez przełącznika, „Send optional diagnostic data…” Off
  i nieaktywne. Po: pojawia się przełącznik „Required diagnostic data” Off, nieaktywny, z „This setting is managed by your organization”;
  przy „Send optional diagnostic data…” też ten napis. Cofnięte: jak przed. Home i Pro tak samo.
- Obserwacja `UserFeedbackAllowed` (`edge://settings/?search=feedback`; Home 21:02–21:04, Pro 21:02–21:04, Edge 157.0.4341.42). Przed (Home):
  „Feedback” Off, aktywne; na Pro strona nie zdążyła się wczytać. Po: „Feedback” Off, nieaktywne, z „This setting is managed by your
  organization.”. Cofnięte: Off, aktywne. Pole jest Off już na czystej VM, więc skutek to zablokowanie pola.
- Pierwszy przebieg (20:40–20:44, `edge://settings/privacy`) nie pokazał pól, bo w Edge 157 ta strona jest tylko spisem podstron.
- Źródła: Win11Debloat `Regfiles/Disable_Telemetry.reg` linie 35–39 oraz `Regfiles/Disable_Edge_Ads_And_Suggestions.reg` linie 2–16
  (`UserFeedbackAllowed`); privacy.sexy `windows.yaml:9301-9330` i `10280-10317`; strony MS jak w tabeli.

### 15. `privacy.edge-copilot`

Stan: 2026-11-07, build 26300.9457.

| Pole | Wartość |
|---|---|
| `scope` / `group` | `machine` / `ai` |
| `suggested` | nie |
| `risk` | `caution`: z paska Edge znika przycisk „Chat” (Copilot) |
| `editions` | brak |
| Restart | brak systemowego (Edge uruchamiany od nowa w każdej fazie) |
| Stan klucza na czystej VM | `KeyAbsentAfterUndo`: `HKLM\SOFTWARE\Policies\Microsoft\Edge`; `KeyPresentAfterUndo`: `HKLM\SOFTWARE\Policies\Microsoft` |

| Ścieżka (JSON) | Nazwa | Typ | Wartość | `original` |
|---|---|---|---|---|
| `HKLM\SOFTWARE\Policies\Microsoft\Edge` | `CopilotPageContext` | DWord | 0 | `<RemoveEntry>` |
| `HKLM\SOFTWARE\Policies\Microsoft\Edge` | `EdgeEntraCopilotPageContext` | DWord | 0 | `<RemoveEntry>` |
| `HKLM\SOFTWARE\Policies\Microsoft\Edge` | `Microsoft365CopilotChatIconEnabled` | DWord | 0 | `<RemoveEntry>` |
| `HKLM\SOFTWARE\Policies\Microsoft\Edge` | `NewTabPageBingChatEnabled` | DWord | 0 | `<RemoveEntry>` |
| `HKLM\SOFTWARE\Policies\Microsoft\Edge` | `EdgeHistoryAISearchEnabled` | DWord | 0 | `<RemoveEntry>` |
| `HKLM\SOFTWARE\Policies\Microsoft\Edge` | `ComposeInlineEnabled` | DWord | 0 | `<RemoveEntry>` |

Wariant bez `HubsSidebarEnabled`: sonda z tymi sześcioma politykami dała ten sam skutek co sonda z siedmioma, więc pozycja nie chowa
paska bocznego Edge.

| Polityka | Strona MS (ms.date) | `edge://policy` Home | `edge://policy` Pro | Dopisek strony MS |
|---|---|---|---|---|
| `CopilotPageContext` | https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/copilotpagecontext (2026-05-20) | false, Platform, Device, Mandatory, OK | jak Home | „This policy applies only to Microsoft Entra ID profiles in Microsoft Edge. It doesn't apply to Microsoft account (MSA) profiles.” |
| `EdgeEntraCopilotPageContext` | https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/edgeentracopilotpagecontext (2026-08-18) | false, Platform, Device, Mandatory, OK | jak Home | „applies only to users who are signed in to Microsoft Edge with a Microsoft Entra account” |
| `Microsoft365CopilotChatIconEnabled` | https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/microsoft365copilotchaticonenabled (2026-05-20) | false, Platform, Device, Mandatory, OK | jak Home | „For users in an Entra ID Microsoft Edge profile …”; „If the policy is disabled: Copilot doesn't appear in the toolbar.” |
| `NewTabPageBingChatEnabled` | https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/newtabpagebingchatenabled (2026-05-20) | false, Platform, Device, Mandatory, OK | jak Home | tytuł strony mówi o nowej karcie Edge Enterprise |
| `EdgeHistoryAISearchEnabled` | https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/edgehistoryaisearchenabled (2026-05-20) | false, Platform, Device, Mandatory, OK | jak Home | „When disabled, users can only perform exact match (verbatim) searches in their history.” |
| `ComposeInlineEnabled` | https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/composeinlineenabled (2026-05-20) | false, Platform, Device, Mandatory, OK | jak Home | „This policy applies only to Microsoft Entra accounts and doesn't apply to Microsoft accounts.” |

- Polityki zapisane razem. Skutek pokazał przycisk „Chat” na pasku. Której z sześciu polityk to zasługa, nie rozdzielano. Strona
  `HubsSidebarEnabled` mówi: „As of Microsoft Edge version 141, the Microsoft365CopilotChatIconEnabled policy is the only means of controlling
  the display of Copilot in the toolbar.”. Pozostałe pięć polityk nie miało osobnej obserwacji. Elementów Bing Chat na nowej karcie nie
  rozpoznano ani przed, ani po. Profil Edge na VM był lokalny, bez logowania, a mimo dopisków „Entra ID” przycisk zniknął.
- Brzmienia: przycisk „Chat” / „Czat”, przycisk „Copilot menu” / „Menu Copilot”.
- Obserwacja (pasek narzędzi i `edge://newtab`; Home 20:44–20:48, Pro 20:44–20:48, Edge 157.0.4341.42 we wszystkich fazach):
  - siedem polityk z `HubsSidebarEnabled`: przed „Chat” (Home także „Copilot menu”); po ani „Chat”, ani „Copilot menu”; cofnięte „Chat”;
  - sześć polityk bez `HubsSidebarEnabled`: przed „Chat”; po brak; cofnięte „Chat” (Pro także „Copilot menu”).
  Home i Pro tak samo. Przycisk „Copilot menu” pojawiał się w Edge 157 także w fazach bez polityk, więc o skutku świadczy „Chat”.
- Źródła: Win11Debloat `Config/Features.json:770-774`, `Regfiles/Disable_Edge_AI_Features.reg` linie 2–11; RemoveWindowsAI
  `RemoveWindowsAi.ps1:958-977`; privacy.sexy `windows.yaml:9945-10088`; strony MS jak w tabeli. Strona `HubsSidebarEnabled`:
  https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/hubssidebarenabled (ms.date 2026-05-20, dostęp 2026-11-08).

### 17. `privacy.notepad-ai`

Stan: 2026-11-07, build 26300.9457.

| Pole | Wartość |
|---|---|
| `scope` / `group` | `machine` / `ai` |
| `suggested` | nie |
| `risk` | `caution`: z Notatnika znika przycisk „Writing tools” (narzędzia AI) |
| `editions` | brak |
| Restart | brak systemowego (Notatnik uruchamiany od nowa w każdej fazie) |
| Stan klucza na czystej VM | `KeyAbsentAfterUndo`: `HKLM\SOFTWARE\Policies\WindowsNotepad`; `KeyPresentAfterUndo`: `HKLM\SOFTWARE\Policies` |

| Ścieżka (JSON) | Nazwa | Typ | Wartość | `original` |
|---|---|---|---|---|
| `HKLM\SOFTWARE\Policies\WindowsNotepad` | `DisableAIFeatures` | DWord | 1 | `<RemoveEntry>` |

- Brzmienia: przycisk „Writing tools” / „Narzędzia do pisania”.
- Obserwacja (Notatnik 11.2605.34.0; Home 20:50–20:51, Pro 20:50–20:51): przed przycisk „Writing tools” jest; po zapisie go nie ma; cofnięte jest.
  Notatnik uruchomił się i żył po 10 s w każdej fazie. Home i Pro tak samo.
- Źródła: Microsoft Learn „Manage AI features in Notepad”, https://learn.microsoft.com/en-us/windows/client-management/manage-notepad
  (ms.date 2025-04-30, dostęp 2026-11-07): „To disable AI features in Notepad, set the `DisableAIFeatures` registry value to `1` under
  `HKLM:\SOFTWARE\Policies\WindowsNotepad`.”; wymagania „Windows 11, version 22H2 or later.”, „Notepad version 11.2503.16.0 or later.”.
  Typu danych strona nie podaje; DWord za Win11Debloat `Regfiles/Disable_Notepad_AI_Features.reg` linie 2–3 i WinUtil `config/tweaks.json:953-975`.

## Pozycje, które nie wchodzą

Stan: 2026-11-07, build 26300.9457.

| # | Pozycja i wpisy | Werdykt | Obserwacja |
|---|---|---|---|
| 6 | `privacy.settings-365-ads`: `HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent` `DisableConsumerAccountStateContent`=1 | nie wchodzi: brak obserwowanego skutku na Home i Pro | Strona główna Ustawień (`ms-settings:` w świeżym oknie, 20:21–20:22): karty „Back up your PC to Microsoft account” i „It’s all here with Microsoft account” są przed, po zapisie i po cofnięciu. Powtórka z restartem VM (21:32–21:35, bez sieci): karta „It’s all here with Microsoft account” jest we wszystkich fazach. Karty „Back up your PC to Microsoft account” w przebiegu z restartem nie było w żadnej fazie, więc dla niej wynik po restarcie jest nieodczytany. Microsoft (Policy CSP Experience) podaje tę politykę tylko dla Enterprise i Education. |
| 9 | `privacy.edge-prompts-default-browser`: `DefaultBrowserSettingEnabled`=0, `DefaultBrowserSettingsCampaignEnabled`=0 | nie wchodzi: nieobserwowalne na VM (brak monitu o przeglądarkę domyślną) | Monitu o przeglądarkę domyślną nie było w żadnej fazie. Edge był na VM przeglądarką domyślną („Microsoft Edge is your default browser”, przycisk „Make default” nieaktywny) i startował z `--no-first-run`, co samo może tłumić monity; skrypt nie rozstrzyga, czy polityki działają. Obie polityki miały w `edge://policy` status OK. Strona MS `DefaultBrowserSettingEnabled`: „This policy only works for PCs running Windows 7.” |
| 12 | `privacy.activity-history`: `HKLM\SOFTWARE\Policies\Microsoft\Windows\System` `EnableActivityFeed`=0, `PublishUserActivities`=0, `UploadUserActivities`=0 | nie wchodzi: nieobserwowalne na VM (brak strony Historii aktywności) | `ms-settings:privacy-activityhistory` otwiera stronę główną Ustawień, a na liście „Privacy & security” nie ma pozycji „Activity history”. Tak samo przed, po zapisie, po cofnięciu i w przebiegu z restartem VM. |
| 16 | `privacy.paint-ai`: `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Paint` `DisableCocreator`=1, `DisableGenerativeFill`=1, `DisableImageCreator`=1 | nie wchodzi: nieobserwowalne na VM (funkcje AI Painta wymagają aktualizacji aplikacji) | Paint 11.2601.440.0 pokazuje w każdej fazie „Download updates to use AI features – You need the latest updates to use new AI features.”. Przycisk „Copilot” jest przed, po i po cofnięciu. Jedyna różnica: przycisk „Sign in” znika po zapisie i wraca po cofnięciu, co nie dowodzi wyłączenia funkcji AI. Paint uruchomił się i żył po 10 s w każdej fazie. |
| 18 | `privacy.copilot-taskbar-button`: `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced` `ShowCopilotButton`=0 | nie wchodzi: brak obserwowanego skutku na Home i Pro | Osobnego przycisku Copilot na pasku zadań 26H2 nie ma w żadnej fazie. Home: przypięta aplikacja „Copilot” (PL „Copilot — przypięto”) zostaje po zapisie i po restarcie Eksploratora. Pro: aplikacji Copilot nie ma, przypięta „Microsoft 365 Copilot” bez zmian. |
| – | `privacy.ai-service-fabric`: `HKLM\SYSTEM\CurrentControlSet\Services\WSAIFabricSvc` `Start`=3 | nie wchodzi: zysk pomijalny | Bez sondy zmiany. Odczyt na tych samych VM (2026-11-07, tylko odczyt): usługa „Windows AI Components Host” działa (Auto, Running) i po 4 min bezczynności zajmuje 1,8 MB pamięci prywatnej (zestaw roboczy 9,4 MB) przy 0,0 s czasu CPU. |

Źródła wierszy, które nie wchodzą: Policy CSP Experience https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-experience
(ms.date 2026-10-14), Win11Debloat `Config/Features.json:744-748` i `Regfiles/Disable_Settings_365_Ads.reg` linie 2–3,
RemoveWindowsAI `RemoveWindowsAi.ps1:1125` (6); strony MS `defaultbrowsersettingenabled` i `defaultbrowsersettingscampaignenabled`
(ms.date 2026-05-20), Win11Debloat `Regfiles/Disable_Edge_Ads_And_Suggestions.reg` linia 13 (9); Policy CSP Privacy
https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-privacy, Win11Debloat `Regfiles/Disable_Telemetry.reg` linie 28–30,
WinUtil `config/tweaks.json:1-28` (12); Policy CSP WindowsAI https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-windowsai
(ms.date 2026-10-14), Win11Debloat `Regfiles/Disable_Paint_AI_Features.reg` linie 2–4, RemoveWindowsAI `RemoveWindowsAi.ps1:1134-1138` (16);
Win11Debloat `Regfiles/Disable_Copilot.reg` linie 2–4, RemoveWindowsAI `RemoveWindowsAi.ps1:945` (18); Win11Debloat
`Regfiles/Disable_AI_Service_Auto_Start.reg` linie 2–3, WinUtil `config/tweaks.json:953-1008` (usługa AI).

## Sondy istniejących pozycji

Stan: 2026-11-07, build 26300.9457.

| Sonda | Home | Pro | Wniosek |
|---|---|---|---|
| Przełącznik „Recommendations and offers in Settings” / „Rekomendacje i oferty w ustawieniach” (`SystemSettings_Privacy_EnableSuggestionsInSettings_ToggleSwitch`), różnica rejestru | Off zapisuje `SubscribedContent-338393Enabled`, `SubscribedContent-353694Enabled`, `SubscribedContent-353696Enabled` = 0, On = 1 | jak Home | Przełącznik nie zapisuje `SubscribedContent-353698Enabled`. `privacy.settings-suggestions` zostaje bez zmian. |
| `HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection` `DoNotShowFeedbackNotifications`=1, strona Diagnostyka i opinie | lista „Feedback frequency” znika, pojawia się „Some of these settings are managed by your organization.”; cofnięte: lista wraca („Automatically (Recommended)”) | jak Home | `privacy.feedback-notifications` zostaje bez zmian; wartości `Siuf\Rules` nie dokładałyby nic widocznego. |
| `HKCU\Software\Policies\Microsoft\Windows\CloudContent` `DisableWindowsSpotlightFeatures`=1 (`privacy.spotlight`), strona ekranu blokady | „Personalize your lock screen” = Windows spotlight przed, po i po cofnięciu, także z restartem VM | jak Home | Na tej liście `privacy.spotlight` nic nie zmienia. Tło pulpitu (skutek opisany w dokumencie `privacy.spotlight`) nie było w tym przebiegu sprawdzane. |

Polskie brzmienia z tabeli: „Częstotliwość opinii” = „Automatycznie (zalecane)”; komunikatu o zarządzaniu przez organizację po polsku nie odczytano.

## Czego te obserwacje nie sprawdzają

- Aktywowanego Windows (strony Personalizacji były wyszarzone), konta Microsoft, profilu Edge zalogowanego do konta Microsoft albo Entra ID,
  komputera w domenie albo w MDM.
- Rzeczywistych powiadomień (4, 5), banerów OneDrive w Eksploratorze (1), obrazu na ekranie blokady (2), dyktowania Win+H (11).
- Polskich brzmień pól ustawień Edge, nadawców powiadomień „Suggested” i „Backup settings”, pola „fun facts” i pozycji „Obraz” na ekranie blokady.
- Innych wersji Edge niż 152.0.4191.66 i 157.0.4341.42 oraz Notatnika i Painta w innych wersjach niż podane.
