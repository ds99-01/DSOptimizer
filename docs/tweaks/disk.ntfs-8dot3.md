# disk.ntfs-8dot3 – Wyłącz tworzenie krótkich nazw plików 8.3

## Werdykt 26H2
Portować z ostrzeżeniem: wartość polityki jest udokumentowana przez MS i na 26H2 wynosi 2 (per wolumin), ale tweak zmienia politykę globalną dla nowych woluminów, a **nie wolumin C:**, który na świeżym obrazie ma nazwy 8.3 już wyłączone.

## Co zmienia
- `HKLM\SYSTEM\CurrentControlSet\Control\FileSystem`, wartość `NtfsDisable8dot3NameCreation`, **DWord**: `2` -> `1` (wyłączone dla wszystkich woluminów). `scope=machine`, zapis przez API rejestru w C#, bez skryptu.
- Wartości wg MS (`fsutil 8dot3name`): 0 = włączone dla wszystkich, 1 = wyłączone dla wszystkich, 2 = ustawienie per wolumin, 3 = wyłączone poza woluminem systemowym.
- Wybór `registry[]` zamiast `fsutil 8dot3name set 1`: narzędzie według własnego wyjścia zmienia tylko stan rejestru („The registry state is now: 1 (Disable 8dot3 name creation on all volumes)”), a dokumentacja MS opisuje tę wartość jako to, co ustawia parametr. VM nie sprawdzała, czy `fsutil` robi coś jeszcze (np. po stronie działającego systemu).
- **Dotyczy tylko nowych nazw**: istniejące nazwy 8.3 zostają (usunięcie to osobne `fsutil 8dot3name strip`, poza tym tweakiem). Flaga per wolumin jest zapisana na dysku, nie w rejestrze (MS), i nie jest przez tweak zmieniana.

## Źródła
- https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/fsutil-8dot3name – MS, dostęp 2026-10-17 (updated_at 2026-02-16): wartości 0-3, klucz `NtfsDisable8dot3NameCreation`, ustawienie per wolumin wymaga wartości 2; ostrzeżenie o trwałym usuwaniu nazw i uszkodzeniu aplikacji.
- https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/fsutil-behavior – MS, dostęp 2026-10-17: `disable8dot3`; w odróżnieniu od `disablelastaccess` brak zdania o wymaganym restarcie.
- Odczyt VM 26H2 Home i Pro, 2026-10-18 (`docs/zrodla/2026-10-17-partia-c1-zrodla.md`, „Wyniki VM”, sekcja 5).

## Wartości domyślne na świeżym 26H2
Odczyt z VM DSO-26H2-Home i DSO-26H2-Pro (build 26300.9457), 2026-10-18 (Home = Pro):
- `NtfsDisable8dot3NameCreation` = **DWORD 2**; `fsutil behavior query disable8dot3`: „The registry state is: 2 (Per volume setting - the default)”.
- `fsutil 8dot3name query C:`: „The volume state is: 1 (8dot3 name creation is DISABLED)”, „Based on the above settings, 8dot3 name creation is DISABLED on "C:"”. **Rejestr = 2 nie opisuje C:**; C: ma nazwy 8.3 wyłączone już na świeżym obrazie.
- `fsutil 8dot3name set 1` -> rejestr 1, `set 2` -> rejestr 2 (stan startowy). Stanu woluminu po przywróceniu nie czytano ponownie.

## Detekcja i undo
- Detekcja: tylko globalna wartość rejestru; `NtfsDisable8dot3NameCreation` = 1 -> `Applied`, każda inna (0, 2, 3) lub brak -> `NotApplied`. Bez parsowania tekstu `fsutil` (wyjście jest tekstem; całe wyjście na VM było po angielsku mimo pl-PL, przyczyna nierozstrzygnięta). Stanu pojedynczego woluminu nie wykrywamy.
- Skutek dla użytkownika: `Applied` znaczy „polityka globalna = wyłącz”; na C: (już wyłączone) i dla istniejących nazw nic się nie zmienia, zmiana dotyczy nowo tworzonych nazw i woluminów, dla których dziś obowiązuje polityka globalna.
- Undo: przywraca wartość ze snapshotu; fallback `original` = 2 (potwierdzony odczytem VM). Ponowne włączenie nie odtwarza nazw plików utworzonych w czasie wyłączenia.

## Ryzyka i ostrzeżenia
- `risk=caution`.
- Ostrzeżenie w katalogu: „Stare instalatory (16-bitowe, część MSI) i programy, które używają krótkich nazw, mogą przestać działać.”
- Poza uwagami w programie (w szczegółach technicznych albo tylko tutaj): kopie zapasowe odwołujące się do nazw 8.3 też mogą przestać działać; zmiana dotyczy tylko nowych nazw, a na świeżym 26H2 dla C: nic nie zmienia.
- Wartość 1 dotyczy też woluminów zewnętrznych podłączonych później.
- `requiresReboot` = false: MS nie podaje wymogu restartu dla `disable8dot3` (wniosek z braku zdania w dokumentacji, niepotwierdzony na VM). Nie testowano na VM, czy zapis rejestru działa na nowe pliki bez restartu (nie tworzono plików, `dir /x`).
- Zysku wydajności nie mierzono (wcześniej w programie: „zysku wydajności nie zmierzono”; od 2026-10-26 opis mówi tylko, co się zmienia).

## Test VM
Przebieg VM z 2026-10-18 (build 26300.9457), DSO-26H2-Home (Core) i DSO-26H2-Pro (Professional), oba **Passed**. Raporty lokalne: `tests/vm/out/20261018-080435/<VM>/<RunLabel>/report.json` (katalog w `.gitignore`); w przebiegu `main` Pester zaliczył 140, 0 błędów, 10 pominiętych (Home i Pro identycznie).

Przypadek `disk-ntfs-8dot3` (`Flow=Full`, przebieg `main`), Home i Pro:
- Sprawdzono: `detect-before` = `NotApplied` (Setup ustawił 0, inaczej niż `original` 2), `apply` kod 0, `detect-after-apply` = `Applied` (rejestr = 1), `undo` kod 0, `detect-after-undo` = `NotApplied`. `SnapshotBefore` `NtfsDisable8dot3NameCreation` DWORD 0, `SnapshotDiff` pusty, więc undo przywróciło wartość ze snapshotu, nie fallback 2.
- Check: brak. Restartu nie było (`requiresReboot` = false).
- **Nie sprawdzono:** skutek na plikach (`dir /x`, nowe nazwy 8.3), stan woluminu C: (`fsutil 8dot3name query`), czy zapis działa na nowe pliki bez restartu; sprawdzana jest tylko globalna wartość rejestru.

## Reddit
Do uzupełnienia w części E.
