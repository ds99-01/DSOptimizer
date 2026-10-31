# disk.ntfs-last-access – Wyłącz zapisywanie czasu ostatniego dostępu do plików

## Werdykt 26H2
Portować: wartość rejestru istnieje na świeżym 26H2 i zmienia się dokładnie tak, jak opisuje `fsutil` (zaobserwowane na VM); skutek na plikach nie był mierzony. Detekcja rozpoznaje też tryb 3 (po masce dolnych 2 bitów).

## Co zmienia
- `HKLM\SYSTEM\CurrentControlSet\Control\FileSystem`, wartość `NtfsDisableLastAccessUpdate`, **DWord**: `0x80000002` (2147483650) -> `0x80000001` (2147483649). `scope=machine`. Pozycja jest skryptem (`script`, nie `registry[]`), bo detekcja wymaga maski bitów, a silnik porównuje w `registry[]` tylko całe wartości, więc API C# samo nie wystarcza (patrz „Detekcja i undo”). Skrypt czyta i zapisuje wartość wyłącznie przez .NET (`Microsoft.Win32.RegistryKey.OpenBaseKey(LocalMachine, Registry64)`) w funkcjach pomocniczych `Get-/Set-/Remove-DsoLastAccessValue`, bez `reg.exe`, `fsutil` i `HKLM:`.
- `0x80000001` to dokładnie to, co zapisuje `fsutil behavior set disablelastaccess 1` (odczyt VM). Ten wybór jest zgodny z założeniem, że tweak zapisuje to samo co narzędzie oficjalne.
- Nie używamy `fsutil`, bo VM nie rozstrzygnęła, czy `fsutil` robi coś poza zapisem tej wartości. Narzędzie pisze „takes effect immediately (no reboot required)”, więc prawdopodobnie zmienia także ustawienie działającego systemu, czego zapis rejestru nie robi (to wnioskowanie, nie wynik testu). Dlatego `requiresReboot = true`: dokumentacja MS mówi „You must restart your computer for this parameter to take effect”, a zapis samego rejestru da skutek po restarcie.

## Źródła
- https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/fsutil-behavior – MS, dostęp 2026-10-17 (updated_at 2026-02-16): `disablelastaccess {1|0}`, parametr aktualizuje `NtfsDisableLastAccessUpdate`, „You must restart your computer for this parameter to take effect”; narzędzia (Backup, Remote Storage) mogą być dotknięte. Dokumentacja opisuje tylko 0 i 1.
- https://learn.microsoft.com/en-us/archive/blogs/embedded/preventing-last-access-chatter – MS-archiwum (2006), dostęp 2026-10-17.
- https://winaero.com/disable-ntfs-last-access-time-updates-in-windows-10 – hint, dostęp 2026-10-17: tryby 0-3 i `0x8000000N` (poza dokumentacją MS).
- Odczyt VM 26H2 Home i Pro, 2026-10-18 (`docs/zrodla/2026-10-17-partia-c1-zrodla.md`, „Wyniki VM”, sekcja 4).

## Wartości domyślne na świeżym 26H2
Odczyt z VM DSO-26H2-Home i DSO-26H2-Pro (build 26300.9457), 2026-10-18 (Home = Pro):
- `NtfsDisableLastAccessUpdate` = **DWORD `0x80000002`** (odczyt jako Int32: -2147483646); `fsutil behavior query disablelastaccess`: `DisableLastAccess = 2  (System Managed, Last Access Time Updates ENABLED)`.
- `fsutil behavior set disablelastaccess 1` -> `0x80000001`, `set 0` -> `0x80000000`, `set 2` -> `0x80000002` (przywrócenie stanu startowego). Flaga `0x80000000` była ustawiona w każdym zaobserwowanym stanie; tryb to dolne 2 bity. **Tryb 3 nie był testowany.**

## Detekcja i undo
- Detekcja (skrypt, maska dolnych 2 bitów wartości DWord): `(v & 3)` równe 1 lub 3 -> `Applied`; `(v & 3)` równe 0 lub 2, brak wartości albo typ inny niż DWord -> `NotApplied`. Flaga `0x80000000` jest stała w każdym zaobserwowanym stanie, o trybie decyduje reszta. Stany: `0x80000002` (zarządzane przez system, aktualizacje włączone; domyślny), `0x80000000` (zarządzane przez użytkownika, włączone), `0x80000001` (zarządzane przez użytkownika, wyłączone), `0x80000003` (zarządzane przez system, wyłączone wg hintu Winaero). Gołe `1` i `3` bez flagi też są `Applied` (MS dokumentuje 0 i 1). Tryb 3 nie był obserwowany na 26H2; jego rozpoznanie opiera się na hincie i masce, nie na odczycie VM.
- Dlaczego skrypt: silnik porównuje w `registry[]` całe wartości, a maski nie obsługuje, więc przy porównaniu z dokładnym `0x80000001` użytkownik, który ma już wyłączone aktualizacje w trybie 3 (albo gołe `1`), widziałby pozycję jako „do zastosowania”. API C# samo nie wystarcza; przejście na skrypt (zgodnie z zasadą „skrypty tylko gdy API nie wystarcza”) jest jedyną drogą do maski. Rejestr zgadza się z `query` dla zaobserwowanych 0/1/2.
- Apply: gdy stan to już `Applied` (także tryb 3), nic nie zapisuje; w przeciwnym razie zapisuje `0x80000001` (DWord, jak `fsutil behavior set disablelastaccess 1`) i sprawdza detekcją, że wynik to `Applied`.
- Snapshot (capture): `DWord:<wartość dziesiętna bez znaku>` (np. `DWord:2147483650`) albo `Absent`, gdy wartości nie ma; inny typ niż DWord kończy capture błędem, zanim cokolwiek zostanie zmienione.
- Undo: przywraca dokładnie snapshot: `DWord:<n>` -> zapis tej wartości, `Absent` -> usunięcie wartości; gdy stan już równa się snapshotowi, kończy się kodem 0 bez zapisu (idempotentnie), a po zapisie lub usunięciu sprawdza efekt. Nieprawidłowy snapshot to błąd bez zmian. Fallback `original` z katalogu nie istnieje (brak `registry[]`); snapshot z journala jest jedynym źródłem, a stan domyślny 26H2 to `0x80000002` (potwierdzony odczytem VM). Undo bez wpisu w journalu (apply wykonany poza narzędziem) kończy się więc `UndoIncomplete` (kod CLI 4) z komunikatem o braku poprawnego snapshotu i nic nie zmienia; wartość domyślna nie jest odtwarzana z katalogu.

## Ryzyka i ostrzeżenia
- `risk=safe` (wartość odwracalna, nie usuwa danych).
- Ostrzeżenia w katalogu: „Wymaga restartu komputera.” oraz „Programy korzystające z czasu ostatniego dostępu (kopie zapasowe, skrypty porządkujące) dostaną nieaktualne dane.”
- Poza uwagami w programie (w szczegółach technicznych albo tylko tutaj): MS: parametr może wpływać na Backup i Remote Storage; rozbieżność restartu między MS a `fsutil` (zdecydowano wg MS: wymagany restart).
- Wg hintu Winaero w trybie system managed znacznik czasu jest modyfikowany przy starcie systemu, a pojedyncze komentarze mówią, że `0x80000003` wracało do `0x80000002` po restarcie; niepotwierdzone, tryb 3 poza zakresem.
- Nie testowano na VM: skutek na `LastAccessTime` plików.
- Zysku wydajności nie mierzono (opis w programie od 2026-10-26 mówi tylko o mniejszej liczbie drobnych zapisów).

## Test VM
Przebieg VM z 2026-10-18 (13:51, build 26300.9457, HEAD `da78bd7`), DSO-26H2-Home (Core) i DSO-26H2-Pro (Professional), oba bez błędów (212 zaliczonych / 0 błędów / 10 pominiętych na każdej VM; pominięte to wyłącznie `disk-hibernation`). Raporty lokalne: `tests/vm/out/20261018-135146/<VM>/<RunLabel>/report.json` (katalog w `.gitignore`). Przebieg odświeżony po zmianie detekcji w partii C2 (Task 2: maska bitów zamiast porównania dokładnej wartości `0x80000001`); wcześniejszy przebieg C1 (2026-10-18 08:04, 13/13 w przebiegu Reboot) dotyczył poprzedniej detekcji.

Przypadek `disk-ntfs-last-access` (`Flow=Reboot`, przebieg `reboot-disk-ntfs-last-access`, osobny przebieg od czystego checkpointu; Pester 14/14 na Home i Pro), Home i Pro identycznie:
- Sprawdzono: `detect-before` = `NotApplied`, `apply` = `Applied` (`requiresReboot` = true), `detect-after-apply` = `Applied`, **prawdziwy restart** (potwierdzony), `detect-after-reboot` = `Applied`, `undo` = `Undone`, `detect-after-undo` = `NotApplied`. Restart nie cofnął ustawienia na VM.
- Przypadek `disk-ntfs-last-access-mode3` (`DetectOnly`, w przebiegu `main`): Setup zapisał `0x80000003` przez API .NET, detekcja = `Applied` (zgodnie z maską z Taska 2).
- Check: brak.
- **Nie sprawdzono:** skutku na `LastAccessTime` plików i zachowania na sprzęcie. Szczegółowe wartości snapshotu z odświeżonego przebiegu nie były tu analizowane (ostatnia analiza `SnapshotDiff` pochodzi z przebiegu C1: undo przywróciło wartość ze snapshotu, nie fallback).
