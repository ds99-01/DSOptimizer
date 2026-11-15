# DSOptimizer: tryb wiersza poleceń

[Wróć do README](../README.pl.md) · [English](en/cli.md) | **Polski**

## Budowanie ze źródeł

Potrzebny jest .NET SDK w wersji podanej w `global.json` (np. `winget install Microsoft.DotNet.SDK.10`).

```powershell
dotnet build DSOptimizer.sln -c Release
```

Gotowy program: `src\DSOptimizer.App\bin\Release\net481\DSOptimizer.exe`. Testy (nieobowiązkowo; część z nich
wymaga PowerShella uruchomionego jako administrator):

```powershell
dotnet test DSOptimizer.sln -c Release
```

## Tryb wiersza poleceń

`DSOptimizer.exe` uruchomiony z argumentami nie otwiera okna: wykonuje jedno polecenie, zapisuje raport JSON do pliku `--out` i kończy proces z kodem wyjścia. Bez argumentów otwiera okno. `--out` jest wymagany (wynik trafia do pliku, nie na stdout), a polecenie ma być dokładnie jedno.

```text
DSOptimizer.exe --detect <id> --out <plik.json>
DSOptimizer.exe --apply <id> [--restore-point required|ifavailable|skip] --out <plik.json>
DSOptimizer.exe --undo <id> --out <plik.json>
DSOptimizer.exe --execute <id> [--catalog <plik.json>] --out <plik.json>
DSOptimizer.exe --list-apps --out <plik.json>
DSOptimizer.exe --remove-app <id> [--restore-point required|ifavailable|skip] --out <plik.json>
DSOptimizer.exe --list-install [--apps-catalog <plik.json>] --out <plik.json>
DSOptimizer.exe --install-apps <id>[,<id>…] [--apps-catalog <plik.json>] --out <plik.json>
DSOptimizer.exe --search-apps <tekst> --out <plik.json>
DSOptimizer.exe --list-autostart --out <plik.json>
DSOptimizer.exe --set-autostart <id> disabled|enabled --out <plik.json>
DSOptimizer.exe --undo-autostart <id> --out <plik.json>
```

- `--detect`, `--apply` i `--undo` dotyczą pozycji `kind=tweak`; `--execute` dotyczy `kind=action` i `kind=panel` (`--apply` na akcji lub panelu daje kod 2).
- `--list-apps` zwraca widoczne wiersze strony „Aplikacje”, a `--remove-app` usuwa jedną aplikację po jej id (`appx:<PackageFamilyName>` albo `win32:<klucz>`); wiersz chroniony lub zablokowany jest odrzucany. `--restore-point` domyślnie ma wartość `required`: bez punktu przywracania nic nie jest usuwane.
- `--list-install` zwraca listę instalacji ze stanem winget, `--install-apps` instaluje podane pozycje (`install.<nazwa>` z listy albo `winget:<id>`) bez pytania, a `--search-apps` zwraca wyniki `winget search`. `--apps-catalog` podmienia wbudowaną listę instalacji.
- Instalacja i wyszukiwanie przez winget działają tylko na koncie zalogowanego użytkownika; na innym koncie kończą się odmową (kod 1), a winget nie jest uruchamiany.
- `--list-autostart` zwraca wiersze strony „Autostart” dla konta zalogowanego użytkownika w polu `autostartRows`: `id`, `source` (`RunUser`, `RunMachine`, `Run32`, `FolderUser`, `FolderCommon`, `StartupTask`), `name`, `publisher`, `scope` (`User`, `Machine`, `Store`), `state` (`Enabled`, `Disabled`, `Unknown`), `missingFile`, `verdict` (`Protected`, `CanDisable` albo `null`, gdy bez oceny), `canToggle`, `cpuMs` (czas procesora z pomiaru Windows w milisekundach) i `diskBytes` (odczyt z dysku w bajtach); obie liczby są `null`, gdy nie zmierzono.
- `--set-autostart` wyłącza albo włącza jedną pozycję po jej id z listy (stan bez rozróżniania wielkości liter), a `--undo-autostart` przywraca stan sprzed aktywnej zmiany tej pozycji z Historii zmian. Id spoza listy daje kod 1; wiersz z `canToggle=false` (chroniony albo tylko do odczytu) jest odrzucany (kod 1) z powodem w `message` i nic nie jest zapisywane; pozycja już w stanie docelowym daje `AlreadyApplied` (kod 0). Cofnięcie, które nie przywróciło wszystkiego, daje kod 4 i wymienia to w `notRestored`. `--catalog`, `--apps-catalog` i `--restore-point` przy tych poleceniach albo brak lub nieznany stan dają kod 2.
- Polecenia, które zmieniają system (`--apply`, `--undo`, `--execute`, `--remove-app`, `--install-apps`, `--set-autostart`, `--undo-autostart`), kończą się kodem 1 i niczego nie zmieniają, gdy działa inna kopia DSOptimizera (okno albo takie polecenie). Polecenia tylko do odczytu (`--detect`, `--list-apps`, `--list-install`, `--search-apps`, `--list-autostart`) działają także przy otwartym oknie, ale dopóki trwa jakiekolwiek polecenie, instalator i deinstalator odmawiają pracy.

### Kody wyjścia

| Kod | Znaczenie |
|---|---|
| 0 | Sukces (`--detect` zawsze zwraca 0, niezależnie od stanu) |
| 1 | Operacja niewykonana, stan bez zmian (`Failed`, `FailedRolledBack`, `Refused`, `NotSupported`, `Skipped`; seria przerwana, np. brak punktu przywracania przy `--restore-point required`, pole `aborted`); działa inna kopia DSOptimizera |
| 2 | Błędne argumenty albo polecenie niepasujące do rodzaju (`kind`) pozycji |
| 3 | Błąd katalogu (plik, JSON, walidacja, nieznane id) |
| 4 | Stan do ręcznej kontroli (`FailedRollbackIncomplete`, `UndoIncomplete`, `NotConfirmed`, `InstalledUnverified`) |
| 5 | Błąd wewnętrzny (wyjątek, nie da się zabezpieczyć korzenia `%ProgramData%\DSOptimizer`) |
| 6 | Nie da się zapisać pliku `--out` |

### Raport JSON

Najważniejsze pola: `exitCode` (to samo co kod procesu), `result` (nazwa kodu, np. `UsageError`), `state` (wykryty stan przed operacją), `outcome` (wynik operacji), `finalState` (stan po operacji), `warnings`, `restorePoint` (status punktu przywracania), `user` (SID użytkownika docelowego), `machine` (build, edycja, nazwa maszyny).

Plik `--out` to UTF-8 bez BOM. Windows PowerShell 5.1 czyta taki plik poprawnie (polskie znaki) tylko z jawnym kodowaniem: `Get-Content <plik> -Raw -Encoding UTF8`.

### Katalog zewnętrzny (`--catalog`)

`--catalog <plik.json>` **zastępuje** katalog wbudowany i wykonuje skrypty z tego pliku jako administrator. Używaj wyłącznie zaufanych plików. Użycie katalogu zewnętrznego jest odnotowane w `warnings` raportu, razem z hashem katalogu (z prefiksem `sha256:`). Hash to SHA-256 z nazwy pliku i zdekodowanego tekstu JSON, a nie z bajtów pliku, więc nie zgadza się z wynikiem `Get-FileHash`.
