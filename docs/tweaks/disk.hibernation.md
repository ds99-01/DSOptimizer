# disk.hibernation – Wyłącz hibernację

## Werdykt 26H2
Portować jako `risk=experimental`: mechanizm (`powercfg /hibernate off|on`) jest udokumentowany przez MS, ale apply i undo nie były sprawdzone na żadnej maszynie z obsługą hibernacji (VM Hyper-V jej nie obsługują), więc pozycja zostaje eksperymentalna do weryfikacji na sprzęcie.

## Co zmienia
- Apply: `powercfg.exe /hibernate off`, tylko gdy detekcja mówi `NotApplied`. Na maszynie bez obsługi hibernacji (`NotSupported`) silnik odmawia apply, a skrypt dodatkowo sam kończy się błędem bez wywołania `powercfg` (nic nie jest zapisywane).
- Undo: `powercfg.exe /hibernate on`, tylko gdy snapshot z `capture` mówi `Enabled`. `/h on` jest pomijane (kod 0) tylko przy tym samym dowodzie co `capture` `Enabled` (plik hibernacji obecny i `HibernateEnabled` różne od 0); stan „apply w połowie” (`HibernateEnabled` = 0, plik jeszcze jest) nie jest „już włączone”, więc rollback wywołuje `/h on`. Po `/h on` skrypt sprawdza ten sam dowód (zakłada, że `HiberFilePresent` odświeża się zaraz po `/h on`; MS tego nie opisuje, na VM nie do sprawdzenia). Snapshot `Disabled` lub `Unsupported`: nic do przywrócenia (kod 0). Brak snapshotu albo nieznana wartość: błąd (nie wiadomo, czy włączać hibernację).
- Poza `powercfg` skrypty nic nie zapisują. Skutek uboczny samego `powercfg /h off` zaobserwowany na VM: tworzy `HKLM\SYSTEM\CurrentControlSet\Control\Power\HibernateEnabled` = DWORD 0.
- Nie zmienia typu pliku (`/hibernate /type`) ani rozmiaru (`/size`).

## Źródła
- https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/powercfg-command-line-options – MS, dostęp 2026-10-17: `/hibernate on|off`, `/type reduced|full`, `/size`.
- https://learn.microsoft.com/en-us/troubleshoot/windows-client/setup-upgrade-and-drivers/disable-and-re-enable-hibernation – MS, dostęp 2026-10-17: `powercfg.exe /hibernate off|on`; po wyłączeniu hibernacji hybrydowy stan uśpienia nie działa (ostrzeżenie o utracie danych przy zaniku zasilania).
- https://learn.microsoft.com/en-us/windows/win32/power/system-power-states – MS, dostęp 2026-10-17: rozmiary pliku hibernacji, Szybkie uruchamianie jako hibernacja po wylogowaniu.
- https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-system_power_capabilities – MS, dostęp 2026-10-18 (strona z updated_at 2024-02-22): `SystemS4` = „the operating system supports sleep state S4 (hibernation)”, `HiberFilePresent` = „the system hibernation file is present”.
- https://learn.microsoft.com/en-us/windows/win32/api/powerbase/nf-powerbase-callntpowerinformation – MS, dostęp 2026-10-18: `CallNtPowerInformation` (PowrProf.dll), poziom `SystemPowerCapabilities` (4) zwraca `SYSTEM_POWER_CAPABILITIES`; „represents the currently supported power capabilities. It may change as drivers are installed”.
- Odczyt VM 26H2 Home i Pro, 2026-10-18 (`docs/zrodla/2026-10-17-partia-c1-zrodla.md`, „Wyniki VM”, sekcja 3).
- Odczyt hosta deweloperskiego (Windows 11 Pro 26200, nie VM), 2026-10-18, tylko do odczytu: `SystemS4` = TRUE, `HiberFilePresent` = FALSE, `HibernateEnabled` = 0 (hibernacja wyłączona przez właściciela), skrypty `detect` i `capture` zwróciły `Applied` / `Disabled`.

## Wartości domyślne na świeżym 26H2
Odczyt z VM DSO-26H2-Home i DSO-26H2-Pro (build 26300.9457), 2026-10-18 (Home = Pro):
- `powercfg /a`: „The system firmware does not support hibernation” (także S1, S2, S3, S0 Low Power Idle); `C:\hiberfil.sys` nie istnieje w żadnym kroku.
- `Control\Power\HibernateEnabled`: **brak**; `HibernateEnabledDefault` = DWORD 1; `HiberFileSizePercent` = DWORD 0; `Control\Session Manager\Power\HiberbootEnabled` = DWORD 1; `Session Manager\Power\HibernateEnabled` – brak.
- `powercfg /h off` kończy się kodem 0 i **mimo braku obsługi** tworzy `HibernateEnabled` = 0; `powercfg /h on` kończy się kodem 1.

## Detekcja i undo
Bez parsowania tekstu `powercfg` (jest lokalizowany, a `powercfg /a` zwraca kod 0 także przy braku hibernacji). Skrypt `detect` czyta:
- `CallNtPowerInformation(SystemPowerCapabilities)` przez `Add-Type` (bajt 6 = `SystemS4`, bajt 8 = `HiberFilePresent`, układ z dokumentacji MS: pola `BOOLEAN` jedno za drugim),
- `HKLM\SYSTEM\CurrentControlSet\Control\Power\HibernateEnabled` (widok 64-bit).

Reguły (w tej kolejności):
1. `HibernateEnabled` = 0 i brak pliku hibernacji -> `Applied`.
2. `SystemS4` = TRUE, albo plik hibernacji istnieje, albo `HibernateEnabled` = 0 (ale plik jeszcze jest) -> `NotApplied`.
3. W pozostałych przypadkach (`SystemS4` = FALSE, brak pliku, `HibernateEnabled` różne od 0, w tym brak wartości) -> `NotSupported`. Tak powinna zachować się czysta VM (oczekiwane z `powercfg /a` i braku wartości `HibernateEnabled`; `SystemS4` nie było odczytane na VM bezpośrednio; przebieg VM 2026-10-18 (przypadek `DetectOnly`, Home i Pro) zwrócił `NotSupported`, co potwierdza regułę 3 pośrednio).

`capture` zapisuje stan przed zmianą tylko z dowodem: `Enabled` wymaga pliku hibernacji (`HiberFilePresent` = TRUE, pole udokumentowane przez MS) i `HibernateEnabled` różnego od 0; `Disabled` = jawne `HibernateEnabled` = 0 i brak pliku (`Applied`); `Unsupported` = `NotSupported`. Samo `SystemS4` = TRUE bez pliku hibernacji i bez jawnego `HibernateEnabled` = 0 (komputer z obsługą S4, na którym hibernacja jest domyślnie wyłączona) jest niejednoznaczne: `capture` kończy się błędem, więc silnik przerywa apply przed jakąkolwiek zmianą i undo nigdy nie włączy hibernacji, której nie było. Detekcja (`NotApplied`) dzieli te same dane wejściowe, ale to apply jest bramkowane przez `capture`. `original` nie dotyczy (tweak skryptowy).

**Niepewność (MS tego nie opisuje):** dokumentacja MS nie mówi, jak `SystemS4` zachowuje się po `powercfg /h off`, więc reguła 1 nie opiera się na `SystemS4`: stan „wyłączona” to jawne `HibernateEnabled` = 0 i brak pliku. Na jednej maszynie (host deweloperski, hibernacja wyłączona) `SystemS4` pozostaje TRUE, ale to jedna obserwacja, nie gwarancja. Wartość `HibernateEnabled` = 0 bez obsługi sprzętowej (jak na VM po `/h off`) jest raportowana jako `Applied` (hibernacja faktycznie jest wyłączona); dlatego apply najpierw wymaga stanu `NotApplied`. Testy jednostkowe sprawdzają całą macierz na mockach (`DiskCatalogTests`), nie na prawdziwym `powercfg`.

## Ryzyka i ostrzeżenia
- `risk=experimental` (decyzja projektowa): nie sprawdzone na sprzęcie z obsługą hibernacji.
- Ostrzeżenia w katalogu: „Na laptopach znika hibernacja i Szybkie uruchamianie, a rozładowanie baterii w uśpieniu grozi utratą niezapisanych danych.”
- Podstawa (dokumentacja MS, **nie wynik testu na VM**): Szybkie uruchamianie korzysta z pliku hibernacji (zdanie „powercfg /h off wyłącza Fast Startup” dosłownie nie występuje w źródłach MS); bez hibernacji nie działa hybrydowy stan uśpienia (ostrzeżenie MS o utracie danych przy zaniku zasilania).
- `HiberbootEnabled` = 1 na VM mimo „Fast Startup: Hibernation is not available”: ta wartość nie dowodzi, że Szybkie uruchamianie działa.
- Nie testowano na VM: apply i undo na maszynie z obsługą hibernacji, usunięcie `hiberfil.sys`, skutek dla czasu rozruchu, domyślny typ pliku (full/reduced) i to, czy `on` po `off` przywraca poprzedni typ i rozmiar. Undo włącza hibernację domyślnymi ustawieniami `powercfg /h on`, nie odtwarza `/type` ani `/size` sprzed zmiany.
- Restart: MS nie podaje wymogu; `requiresReboot` = false, niepotwierdzone.

## Test VM
Przebieg VM z 2026-10-18 (build 26300.9457), DSO-26H2-Home (Core) i DSO-26H2-Pro (Professional), oba **Passed**. Raporty lokalne: `tests/vm/out/20261018-080435/<VM>/<RunLabel>/report.json` (katalog w `.gitignore`); w przebiegu `main` Pester zaliczył 140, 0 błędów, 10 pominiętych (Home i Pro identycznie).

- `disk-hibernation-unsupported` (`Flow=DetectOnly`, przebieg `main`): **Passed** na Home i Pro. `detect-before` kod 0, wynik `Success`, stan **`NotSupported`**. To potwierdza przebiegiem regułę 3 detekcji dla czystej VM; `SystemS4` = FALSE jest tu wnioskiem pośrednim (skrypt zwrócił `NotSupported`), nie osobnym odczytem. Przypadek `DetectOnly` nie ma `Snapshot`, więc brak zmian w rejestrze nie był tu osobno sprawdzany.
- `disk-hibernation` (`Flow=Full`): **Skipped** na Home i Pro. Powód z `cases.psd1`: „VM Hyper-V bez obsługi S4 – apply/undo do weryfikacji na sprzęcie”. Pominięte to wszystkie testy tego przypadku (10 z 10 pominiętych w `main`), nigdy zaliczone.
- **Nie sprawdzono (do weryfikacji na sprzęcie z obsługą hibernacji):** apply `powercfg /h off` i zniknięcie `hiberfil.sys`, undo `/h on`, zachowanie `SystemS4` po wyłączeniu, skutek dla Szybkiego uruchamiania, przywrócenie typu i rozmiaru pliku. Pozycja zostaje `risk=experimental`.

## Reddit
Do uzupełnienia w części E.
