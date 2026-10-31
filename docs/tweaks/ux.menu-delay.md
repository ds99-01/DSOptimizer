# ux.menu-delay – Szybsze otwieranie podmenu

## Werdykt 26H2
Portować: wartość `MenuShowDelay` istnieje na świeżym 26H2 jako `REG_SZ "400"` (VM), więc tweak ma realny, odczytywalny stan; skuteczność w Eksploratorze na 26H2 nie była mierzona.

## Co zmienia
- `HKCU\Control Panel\Desktop`, wartość `MenuShowDelay`, typ **String** (REG_SZ, tekst z liczbą milisekund), `400` -> `10`.
- Zakres `scope=user`: katalog zapisuje do hive'u zalogowanego użytkownika interaktywnego, nie do HKCU konta Administratora.

Wartość docelowa `"10"` pochodzi z v3.2 (`ux_menu`). Dokumentacja MS (archiwum TechNet) opisuje wartość tylko jako „milisekundy w zapisie dziesiętnym”, domyślnie 400, **bez podania dolnej i górnej granicy zakresu**. Źródło MS nie potwierdza więc, że `10` jest „właściwą” wartością; to świadomy wybór (praktycznie natychmiastowe podmenu, a niezerowe, bo wartość 0 bywa opisywana jako przyczyna przypadkowych otwarć podmenu – hint). WinUtil (hint) używa 200. Zmiana wartości to zmiana jednego pola w `ux.json`.

## Źródła
- https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-2000-server/cc978617(v=technet.10) – MS-archiwum, dostęp 2026-10-17: typ REG_SZ, zakres „milisekundy dziesiętnie”, domyślna 400, opis wartości.
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfow – MS, dostęp 2026-10-17 (tylko streszczenie z wyszukiwania): `SPI_GETMENUSHOWDELAY`/`SPI_SETMENUSHOWDELAY`, wartość w ms. Katalog z tego API nie korzysta.
- https://raw.githubusercontent.com/ChristitusTech/winutil/main/config/tweaks.json – hint (WinUtil: 200, String, original 400), dostęp 2026-10-17.
- Odczyt VM 26H2 Home i Pro, 2026-10-18 (`docs/zrodla/2026-10-17-partia-c1-zrodla.md`, „Wyniki VM”).
- Research: `docs/zrodla/2026-10-17-partia-c1-zrodla.md`, sekcja 1.

## Wartości domyślne na świeżym 26H2
Odczyt z VM DSO-26H2-Home i DSO-26H2-Pro (build 26300.9457), 2026-10-18, konto `dso`:
- `HKU\<SID>\Control Panel\Desktop\MenuShowDelay` = **REG_SZ `"400"`** (Home i Pro identycznie).

Nie rozstrzygnięte (nie testowano): zachowanie systemu po usunięciu wartości.

## Detekcja i undo
- Detekcja: wartość `MenuShowDelay` (String) równa `"10"` = `Applied`; `"400"` albo brak wartości = `NotApplied`.
- Undo: przywraca wartość ze snapshotu zapisanego przy apply (typ String). Gdy snapshotu brak, fallback `original` = `"400"` (potwierdzone na VM).

## Ryzyka i ostrzeżenia
- `risk=safe`.
- Ostrzeżenie w katalogu: „Zmiana może wymagać ponownego zalogowania, zanim zadziała w już uruchomionych programach.” Ani MS, ani VM tego nie rozstrzygają (VM tylko odczytała wartość), więc ostrzeżenie zostaje.
- Część aplikacji Win32 może ignorować wartość (hint).

## Test VM
Przebieg VM z 2026-10-18 (build 26300.9457), DSO-26H2-Home (Core) i DSO-26H2-Pro (Professional), oba **Passed**. Raporty lokalne: `tests/vm/out/20261018-080435/<VM>/<RunLabel>/report.json` (katalog w `.gitignore`); w przebiegu `main` Pester zaliczył 140, 0 błędów, 10 pominiętych (Home i Pro identycznie).

Przypadek `ux-menu-delay` (`Flow=Full`, przebieg `main`), Home i Pro:
- Sprawdzono: `detect-before` = `NotApplied` (Setup ustawił `MenuShowDelay` = `"250"`, inaczej niż `original` `"400"`), `apply` kod 0, `detect-after-apply` = `Applied`, `undo` kod 0, `detect-after-undo` = `NotApplied`. `SnapshotBefore` zawiera `HKU\<SID>\Control Panel\Desktop\MenuShowDelay` String `"250"`, a `SnapshotDiff` (przed vs. po undo) jest pusty, więc undo przywróciło wartość ze snapshotu, nie z fallbacku `"400"`. Snapshot po apply = `"10"`. `NotWrittenToDiff` pusty: apply nie ruszył `HKCU` konta Administrator.
- Check: brak (pozycja rejestrowa). Restartu nie było (nie wymagany w katalogu).
- **Nie sprawdzono:** czy menu faktycznie otwierają się szybciej, czy zmiana wymaga ponownego zalogowania i jak zachowują się aplikacje Win32 (VM sprawdza tylko zapis, detekcję i undo wartości).
