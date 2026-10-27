# privacy.diagtrack – Wyłącz usługę telemetrii DiagTrack

## Werdykt 26H2
Portować jako `risk=caution`, `suggested=true` (decyzja R14): test na VM 26H2 (Home i Pro) pokazał, że usługa zostaje zatrzymana i nie startuje po restarcie, a wyszukiwanie Windows Update po restarcie z wyłączoną usługą działa. Brak dokumentacji MS dla wyłączania tej usługi, dowód: test na VM. Pozycja nie obiecuje, że Windows przestanie wysyłać dane diagnostyczne.

## Co zmienia
- Usługa `DiagTrack` (`services[]`): `startType=Disabled`, `stop=true`; `scope=machine`. Bez wpisów rejestru i bez skryptu (API usług w C#).
- Detekcja opiera się na **typie startu**, nie na tym, czy usługa działa. Undo przywraca typ startu ze snapshotu także wtedy, gdy usługa była w chwili capture w stanie `StartPending` (Review Focus 5; test jednostkowy na fake'ach).
- Pozycja nie zmienia polityki `AllowTelemetry` (osobna pozycja `privacy.diagnostic-data-required`).

## Źródła
- Strona MS o danych diagnostycznych nazywa komponent „Connected User Experiences and Telemetry”, a jako metody zarządzania podaje wyłącznie polityki (`System/AllowTelemetry`, `LimitDumpCollection`, `LimitDiagnosticLogCollection`) i GPO; zatrzymanie lub wyłączenie usługi `DiagTrack` nie jest tam opisane (**brak dokumentacji MS**).
- Test na VM 26H2 Home i Pro, 2026-10-21 (build 26300.9457, nieaktywowany Windows, konto lokalne): zmiana przez API usług, restart VM, wyszukiwanie przez Windows Update Agent.
- Research: `docs/zrodla/2026-10-20-etap4a-zrodla.md`, sekcje „2e. Usługa DiagTrack” i „Wyniki VM 26H2 (partia 1)”.

## Wartości domyślne na świeżym 26H2
Odczyt z VM DSO-26H2-Home i DSO-26H2-Pro, 2026-10-21: `DiagTrack` typ startu **Automatic**, stan **Running**; `original` w katalogu = Automatic.

## Detekcja i undo
- Detekcja: typ startu `Disabled` -> `Applied`; inny typ startu -> `NotApplied`.
- Undo przywraca snapshot sprzed zmiany z journala (typ startu i, gdy usługa działała, jej start); `original` = Automatic to tylko fallback.

## Ryzyka i ostrzeżenia
- `risk=caution` (R14), `suggested=true`.
- Ostrzeżenia w katalogu: „Microsoft nie opisuje wyłączania tej usługi, a korzysta z niej też Windows Update – przy kłopotach z aktualizacjami cofnij zmianę.” oraz „Nie gwarantuje, że Windows przestanie wysyłać dane diagnostyczne.”
- Poza uwagami w programie (w szczegółach technicznych albo tylko tutaj): na VM (Home i Pro) wyszukiwanie Windows Update po restarcie z wyłączoną usługą działało (WUA `Search`: ResultCode=2, 4 aktualizacje, bez błędu). To jedna obserwacja, nie gwarancja.
- Znane ograniczenie silnika (odroczone): usługę zatrzymuje tylko wtedy, gdy jest `Running`; usługa jeszcze startująca w chwili apply może działać do restartu. Typ startu to `Disabled` w obu przypadkach.
- Restart komputera nie jest wymagany (skutek od razu, trwały po restarcie).
- Zysku wydajności nie mierzono (wcześniej w programie: „Zysku wydajności nie zmierzono”; od 2026-10-26 opis mówi tylko, co się zmienia).

## Test VM
Przypadek `privacy-diagtrack` (`Flow=Full`) w `tests/vm/cases.psd1`: niepusty `Snapshot` usługi `DiagTrack` (stan przed = stan po undo), po apply `Disabled` + `Stopped`, po undo `Automatic` + `Running`. Przebieg na VM 2026-10-21 (`Invoke-VmTests.ps1`, Task 9 partii): **DSO-26H2-Home i DSO-26H2-Pro – zaliczony**, kroki detect `NotApplied` → apply `Applied` → detect `Applied` → undo `Undone` → detect `NotApplied`, stan po apply potwierdzony niezależnym odczytem, stan końcowy = stan przed (`SnapshotDiff` pusty). Po apply usługa `Disabled` + `Stopped` (`Start`=4), po undo typ startu ze snapshotu: `Auto` + `Running` (`Start`=2), jak przed apply. Eksploracja z 2026-10-21 przeszła na Home i Pro.

## Obserwacja skutku
- **Home** (`DSO-26H2-Home`, EditionID `Core`), 2026-10-21. Metoda: stan usługi (typ startu, uruchomienie) po zmianie i po restarcie VM; Windows Update Agent `Search("IsInstalled=0 and Type='Software'")` po restarcie. Wynik: usługa zatrzymana i nie startuje po restarcie; wyszukiwanie: ResultCode=2 (powodzenie), 4 aktualizacje, bez błędu. Po cofnięciu: Automatic i Running.
- **Pro** (`DSO-26H2-Pro`, EditionID `Professional`), 2026-10-21. Ta sama metoda i ten sam wynik.

## Reddit
Do uzupełnienia w części E.
