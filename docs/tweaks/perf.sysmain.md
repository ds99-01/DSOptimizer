# perf.sysmain – Wyłącz usługę SysMain (Superfetch)

## Werdykt 26H2
Portować jako eksperymentalne: mechanizm (typ startu usługi plus zatrzymanie) jest zwykły i w pełni odwracalny, ale Microsoft nie podaje żadnego skutku wyłączenia ani korzyści („No guidance”). Opis i ostrzeżenia nie obiecują przyspieszenia; to, czy pozycja ma sens, decyduje użytkownik.

## Co zmienia
- Usługa `SysMain` (nazwa wyświetlana „Superfetch”): typ startu `Automatic` -> `Disabled`, a działająca usługa jest zatrzymywana (`services[]`: `{ name: "SysMain", startType: "Disabled", stop: true, original: "Automatic" }`). `scope=machine`, API usług w C# (`ChangeServiceConfig`), bez skryptu i bez zapisu w rejestrze przez tweak.
- Bez `requiresReboot`: zmiana typu startu i zatrzymanie działają od razu (na VM stop przeszedł bez restartu). Stanu po restarcie nie sprawdzano.
- Silnik zatrzymuje usługę bez wymuszania. Gdy zatrzymanie jest odrzucone (działają usługi zależne albo upłynął czas), apply kończy się błędem, a typ startu wraca do poprzedniego (`FailedRolledBack`). Na świeżej VM SysMain nie ma usług zależnych, więc ta ścieżka jest pokryta tylko testem jednostkowym na fake'ach. Przy upływie czasu usługa jest zwykle w stanie `StopPending`, więc przywrócenie typu startu się uda, ale ponowne uruchomienie usługi zwykle rzuci i wynik to raczej `FailedRollbackIncomplete` niż `FailedRolledBack` (uczciwy, nie sukces; nie sprawdzano na VM).

## Źródła
- https://learn.microsoft.com/en-us/windows-server/security/windows-services/security-guidelines-for-disabling-system-services-in-windows-server – MS (Windows Server 2016), dostęp 2026-10-18 (updated_at 2026-02-16): tabela „Superfetch”, usługa SysMain, opis „Maintains and improves system performance over time.”, typ startu Manual, rekomendacja „No guidance”. To dokumentacja serwerowa; dla klienta 26H2 MS nie podaje typu startu ani skutków.
- https://learn.microsoft.com/en-us/answers/questions/3827250/how-to-disable-sysmain – MS Q&A (odpowiedź społeczności z 2021-09-01, nie dokumentacja MS), dostęp 2026-10-18: potwierdza tylko, że zmiana typu startu przez menedżera usług to zwykły sposób. Ten sam wątek zgłasza, że edytowana w rejestrze wartość „wracała do 3”; niepotwierdzone przez MS.
- Wyszukiwanie w domenach MS (learn, support, techcommunity) nie zwróciło strony MS o SysMain na kliencie.
- Odczyt VM 26H2 Home i Pro, 2026-10-18 (`docs/zrodla/2026-10-18-partia-c2-zrodla.md`, „Wyniki VM”, sekcja 1).

## Wartości domyślne na świeżym 26H2
Odczyt z VM DSO-26H2-Home i DSO-26H2-Pro (build 26300.9457), 2026-10-18 (Home = Pro):
- `Win32_Service`: `StartMode` = **Auto**, `State` = **Running**, `DelayedAutoStart` = **false**; rejestr `Services\SysMain\Start` = **DWORD 2**, brak wartości `DelayedAutoStart`. Strona serwerowa MS (Manual) nie odpowiada klientowi 26H2.
- Usługi zależne: **brak**. SysMain zależy od `rpcss` i `fileinfo` (obie działają).
- Cykl wykonany cmdletami (nie silnikiem): `Set-Service -StartupType Disabled` -> Disabled (`Start` = 4), `Stop-Service` bez `-Force` -> Stopped, `Set-Service -StartupType Automatic` i uruchomienie -> Auto + Running, porównanie z baseline zgodne.
- Nie rozstrzygnięto: stan po restarcie, reset typu startu przez Windows Update.

## Detekcja i undo
- Detekcja: tylko typ startu z API usług; `Disabled` -> `Applied`, każdy inny -> `NotApplied`, brak usługi -> `NotApplicable`. Stan „działa/zatrzymana” nie bierze udziału w detekcji, więc `Applied` nie dowodzi, że usługa stoi (to sprawdza przypadek VM).
- Undo: snapshot z journala (istnienie, typ startu, czy działała). Przywraca typ startu i uruchamia usługę tylko wtedy, gdy działała przed apply; usługa, która stała (Manual), dalej stoi. Fallback `original` = `Automatic` (zmierzone na VM), bez opóźnionego startu.

## Ryzyka i ostrzeżenia
- `risk=experimental`.
- Ostrzeżenie w katalogu: „Microsoft nie podaje skutków wyłączenia tej usługi, a korzyść nie jest udokumentowana.”
- Poza uwagami w programie (w szczegółach technicznych albo tylko tutaj): w tabeli usług MS: „No guidance”; korzyść nie jest zmierzona; zależne usługi mogą zablokować zatrzymanie, wtedy zmiana jest wycofywana.
- Opis v3.2 obiecywał poprawę na NVMe i mniejsze zużycie CPU. Żadnego z tych twierdzeń nie potwierdza źródło MS, więc nie wchodzą do katalogu.

## Test VM
Przebieg VM z 2026-10-18 (13:51, build 26300.9457, HEAD `da78bd7`), DSO-26H2-Home (Core) i DSO-26H2-Pro (Professional), oba bez błędów (212 zaliczonych / 0 błędów / 10 pominiętych na każdej VM; pominięte to wyłącznie `disk-hibernation`). Raporty lokalne: `tests/vm/out/20261018-135146/<VM>/<RunLabel>/report.json` (katalog w `.gitignore`).

Przypadek `perf-sysmain` (`Flow=Full`, przebieg `main`), Home i Pro identycznie:
- Sprawdzono: `detect-before` = `NotApplied`, `apply` = `Applied` (`requiresReboot` = false), `detect-after-apply` = `Applied`, `undo` = `Undone` (`finalState` = `NotApplied`), `detect-after-undo` = `NotApplied`. `CheckAfterApply` Passed (usługa `Stopped`, `StartMode` `Disabled`), `CheckAfterUndo` Passed (`State` i `StartMode` jak przed apply: `Running`/`Auto`). Stan końcowy = stan przed (snapshot `Services\SysMain\Start`).
- **Nie sprawdzono:** skutku na wydajność, stanu po restarcie, trwałości ustawienia po Windows Update (aktualizacja może przywrócić usługę), ścieżki z działającymi usługami zależnymi (pokryta tylko testem jednostkowym na fake'ach).
