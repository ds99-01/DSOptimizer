# ux.game-dvr-policy – Zablokuj nagrywanie i transmisję gier na całym komputerze

## Werdykt 26H2
Portować z ostrzeżeniem: mechanizm jest udokumentowany przez MS (Policy CSP / ADMX), ale dokumentacja mówi o egzekwowaniu na Windows 10 i nie wymienia edycji Home, więc skuteczność na 26H2 jest niepotwierdzona.

## Co zmienia
- `HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR`, wartość `AllowGameDVR`, **DWord**, ustawiana na `0` (Not allowed); `scope=machine`.
- Na świeżym 26H2 nie istnieje ani wartość, ani klucz `GameDVR`. Apply tworzy klucz; undo usuwa wartość **i** pusty klucz `GameDVR` utworzony przez apply (mechanizm A1 dla `registry[]`), a klucz nadrzędny `...\Policies\Microsoft\Windows` zostaje.

**Zmiana semantyki względem v3.2:** `ux_gamedvr` w v3.2 było przełącznikiem użytkownika (klucze HKCU). Alias wskazuje teraz na tę pozycję, czyli politykę maszynową dla wszystkich użytkowników. Odpowiednik użytkownika (`GameDVR_Enabled`) nie został zportowany (patrz `ux.game-dvr.md`: brak źródła MS).

## Źródła
- https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-applicationmanagement – MS, dostęp 2026-10-17 (strona z updated_at 2026-02-28), sekcja AllowGameDVR: typ `int`, 0 = Not allowed, 1 = Allowed (domyślnie); mapowanie GP: `Software\Policies\Microsoft\Windows\GameDVR`, wartość `AllowGameDVR`, plik GameDVR.admx; edycje: Pro, Enterprise, Education, IoT Enterprise (Home nie wymieniony); uwaga MS „The policy is only enforced in Windows 10 for desktop”.
- Odczyt VM 26H2 Home i Pro, 2026-10-18 (stan domyślny: brak klucza i wartości).
- Research: `docs/zrodla/2026-10-17-partia-c1-zrodla.md`, sekcja 2.

## Wartości domyślne na świeżym 26H2
Odczyt z VM DSO-26H2-Home i DSO-26H2-Pro (build 26300.9457), 2026-10-18:
- `HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR\AllowGameDVR`: **brak** – nie istnieje nawet klucz `GameDVR` (Home i Pro). Brak wartości = „nie skonfigurowano” = dozwolone (CSP: wartość domyślna 1).

## Detekcja i undo
- Detekcja: `AllowGameDVR` = 0 -> `Applied`; brak wartości lub inna wartość -> `NotApplied`. Z braku klucza nie wnioskujemy „wyłączone”.
- Undo: usuwa wartość (`original` = `<RemoveEntry>`), nie ustawia 1; usuwa też pusty klucz `GameDVR` utworzony przez apply.

## Ryzyka i ostrzeżenia
- `risk=caution`: polityka maszynowa, dotyczy wszystkich użytkowników, wymaga admina.
- Ostrzeżenia w katalogu: „Microsoft opisuje tę politykę dla Windows 10 Pro i wyższych; na Windows 11 i edycji Home może nie działać.” oraz „W Ustawieniach może pojawić się komunikat, że niektórymi ustawieniami zarządza organizacja.”
- **Nie testowano**, czy polityka jest egzekwowana na 26H2 Home/Pro (VM tylko odczytała stan domyślny); test VM sprawdzi zapis, detekcję i czysty undo, nie zmianę zachowania systemu.
- Wyłącza nagrywanie i transmisję; nie usuwa Game Bar.
- Zysku w grach nie mierzono (opis w programie od 2026-10-26 o nim nie mówi; wcześniej: „Zysku w grach nie zmierzono”).

## Test VM
Przebieg VM z 2026-10-18 (build 26300.9457), DSO-26H2-Home (Core) i DSO-26H2-Pro (Professional), oba **Passed**. Raporty lokalne: `tests/vm/out/20261018-080435/<VM>/<RunLabel>/report.json` (katalog w `.gitignore`); w przebiegu `main` Pester zaliczył 140, 0 błędów, 10 pominiętych (Home i Pro identycznie).

Dwa przypadki `Flow=Full` (przebieg `main`), Home i Pro, w obu: `detect-before` = `NotApplied`, `apply` kod 0, `detect-after-apply` = `Applied`, `undo` kod 0, `detect-after-undo` = `NotApplied`, `SnapshotDiff` pusty.
- `ux-game-dvr-policy` (brak `AllowGameDVR` przed apply, `SnapshotBefore` `Exists=false`): oczekiwanie przypadku (Pester zielony) = po undo klucz `...\Policies\Microsoft\Windows\GameDVR` nie istnieje, a `...\Policies\Microsoft\Windows` zostaje; po apply `AllowGameDVR` = DWORD 0.
- `ux-game-dvr-policy-prev-value` (`AllowGameDVR` = DWORD 1 przed apply, `SnapshotBefore` `Exists=true`): undo przywraca 1 ze snapshotu (`SnapshotDiff` pusty), a nie usuwa wartości; klucze `GameDVR` i `Windows` zostają.
- **Nie sprawdzono:** czy polityka jest egzekwowana na 26H2 Home/Pro (Game Bar, nagrywanie, komunikat „zarządzane przez organizację”); VM sprawdza tylko zapis, detekcję i czysty undo.
