# disk.reserved-storage – Wyłącz rezerwę miejsca na aktualizacje

## Werdykt 26H2
Portować z ostrzeżeniem: mechanizm (cmdlety DISM) jest udokumentowany przez MS i działa w Windows PowerShell 5.1 na Home i Pro (VM), a stan da się odczytać enumem bez parsowania tekstu.

## Co zmienia
- Apply: `Set-WindowsReservedStorageState -State Disabled` (moduł `Dism`), potem weryfikacja `(Get-WindowsReservedStorageState).ReservedStorageState`; jeśli stan nie jest `Disabled`, skrypt kończy się błędem.
- Undo: `Set-WindowsReservedStorageState -State <Enabled|Disabled>` ze snapshotu zapisanego przez `capture` (stan przed zmianą); brak lub nieznany snapshot -> błąd bez zmiany.
- Bez `-Online`: cmdlet nie ma tego parametru (przykład w dokumentacji MS go używa, ale VM potwierdziła jego brak).
- Rejestr `ReserveManager` nie jest zapisywany ani czytany (`ShippedWithReserves` jest stałe i nie odzwierciedla stanu; hint, nie MS).

## Źródła
- https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/dism-storage-reserve?view=windows-11 – MS, dostęp 2026-10-17 (updated_at 2021-09-06): `DISM /Online /Set-ReservedStorageState /State:Enabled|Disabled`, błąd „This operation is not supported when reserved storage is in use. Please wait for any servicing operations to complete and then try again later.”
- https://learn.microsoft.com/en-us/powershell/module/dism/set-windowsreservedstoragestate?view=windowsserver2025-ps i .../get-windowsreservedstoragestate?view=windowsserver2025-ps – MS, dostęp 2026-10-17.
- https://support.microsoft.com/help/4496396/windows-10-how-reserved-storage-works – MS, dostęp 2026-10-17 (nie wspomina `ReserveManager` ani rozmiaru).
- Odczyt VM 26H2 Home i Pro, 2026-10-18 (`docs/zrodla/2026-10-17-partia-c1-zrodla.md`, „Wyniki VM”, sekcja 6).
- Odczyt hosta deweloperskiego (Windows 11 Pro 26200, nie VM), 2026-10-18, tylko do odczytu: skrypty `detect` i `capture` zwróciły `Applied` / `Disabled` (rezerwa wyłączona na tym hoście).

## Wartości domyślne na świeżym 26H2
Odczyt z VM DSO-26H2-Home i DSO-26H2-Pro (build 26300.9457), 2026-10-18:
- Cmdlety `Get-/Set-WindowsReservedStorageState` dostępne w PS 5.1 na Home i Pro (moduł `Dism` 3.0), `Set-` ma parametr `State`, nie ma `-Online`.
- `Get-` zwraca `Microsoft.Dism.Commands.ReservedStorageStateObject`: `ReservedStorageState` (enum Disabled/Enabled) = **Enabled**, a także `LogLevel` (osobny enum; nie mylić z stanem).
- Po cyklu Disabled -> Enabled w `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\ReserveManager` zostaje nowa wartość `MiscPolicyInfo` (brak -> 1); `PassedPolicy` wraca do 1, pozostałe wartości bez zmian.

## Detekcja i undo
- Detekcja: `(Get-WindowsReservedStorageState).ReservedStorageState`; `Disabled` -> `Applied`, `Enabled` -> `NotApplied`; brak cmdletu -> `NotSupported`; nieznana wartość -> błąd (kod 1, wynik `Unknown`, nigdy fałszywe `Applied`). Nie parsujemy wyjścia `DISM.exe` (tekst).
- Błąd „reserved storage is in use” / oczekująca aktualizacja: cmdlet rzuca błąd, skrypt kończy się kodem 1 z komunikatem MS, detekcja dalej mówi prawdę (`NotApplied`), apply kończy się niepowodzeniem, a silnik uruchamia rollback (`Restore` skryptu undo). Undo jest idempotentne: gdy `Get-` zwraca już stan ze snapshotu (tu: `Enabled`, bo nic się nie zmieniło), kończy się kodem 0 bez wywołania `Set-` (które też by padło na „in use”). Dzięki temu rollback się udaje i wynik silnika to `FailedRolledBack` (wpis journala `Aborted`), a nie `FailedRollbackIncomplete` z aktywnym wpisem (mechanizm w `TweakEngine.Apply.cs`: `RollBack`; wynik silnika nie ma osobnego testu, jest wnioskiem z kodu, test jednostkowy sprawdza skrypt undo). Pokryte testem jednostkowym na mocku cmdletu; **zachowanie przy prawdziwej oczekującej aktualizacji nie było odtwarzane** na VM.
- `capture` zapisuje `ReservedStorageState` (nie `LogLevel`). Undo przywraca ten stan, a gdy stan już się zgadza, nic nie robi.
- **Co zostaje po undo:** wartość `MiscPolicyInfo` w `ReserveManager`, której przed zmianą nie było. Dlatego w `Snapshot` przypadku VM jest tylko `ReserveManager\PassedPolicy`, a `MiscPolicyInfo` celowo zostaje poza nim (dałoby to fałszywą czerwień); stan weryfikuje dodatkowo Check czytający `PassedPolicy` z rejestru. Miejsce zwolnione po wyłączeniu może być widoczne dopiero po zakończeniu serwisowania, a po ponownym włączeniu rezerwa może pojawić się dopiero po kolejnej aktualizacji (hint).

## Ryzyka i ostrzeżenia
- `risk=caution`.
- Ostrzeżenie w katalogu: „Przy małej ilości wolnego miejsca aktualizacje mogą częściej kończyć się błędem.”
- Poza uwagami w programie (w szczegółach technicznych albo tylko tutaj): operacja niemożliwa w trakcie serwisowania; `MiscPolicyInfo` zostaje po cofnięciu.
- W środowiskach zarządzanych (WSUS/ConfigMgr) rezerwa działa wg opisu MS inaczej; nie testowano.
- Restart: nie wymagany (stdout cmdletów bez komunikatu o restarcie; MS nie mówi wprost, niepotwierdzone w źródłach MS).
- Nie testowano na VM: zachowanie przy oczekującej aktualizacji, rzeczywisty rozmiar rezerwy (w rejestrze `BaseHardReserveSize` = 5 GiB, `BaseSoftReserveSize` = 1,5 GiB to wartości bazowe, nie zmierzony rozmiar).

## Test VM
Przebieg VM z 2026-10-18 (build 26300.9457), DSO-26H2-Home (Core) i DSO-26H2-Pro (Professional), oba **Passed**. Raporty lokalne: `tests/vm/out/20261018-080435/<VM>/<RunLabel>/report.json` (katalog w `.gitignore`); w przebiegu `main` Pester zaliczył 140, 0 błędów, 10 pominiętych (Home i Pro identycznie).

Przypadek `disk-reserved-storage` (`Flow=Full`, przebieg `main`), Home i Pro:
- Sprawdzono: `detect-before` = `NotApplied` (cmdlet: `Enabled`), `apply` kod 0, `detect-after-apply` = `Applied`, `undo` kod 0, `detect-after-undo` = `NotApplied`. Niezależny od cmdletu **Check**: `CheckAfterApplyResult` = `Passed` (`ReserveManager\PassedPolicy` = 0), `CheckAfterUndoResult` = `Passed` (`PassedPolicy` = 1). `SnapshotDiff` pusty dla `PassedPolicy` (przed 1, po undo 1). `MiscPolicyInfo` nie jest w Snapshot ani w Check (zostaje po undo, zgodnie z opisem powyżej).
- **Nie sprawdzono:** zachowanie przy oczekującej aktualizacji (tylko test jednostkowy na mocku), rzeczywisty rozmiar rezerwy, środowiska zarządzane (WSUS/ConfigMgr).

## Reddit
Do uzupełnienia w części E.
