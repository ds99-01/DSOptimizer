# net.ipv6-prefer-ipv4 – Preferuj IPv4 przed IPv6

## Werdykt 26H2
Portować jako `caution`: wartość 0x20 jest wprost udokumentowana przez MS jako „Prefer IPv4 over IPv6” i polecana zamiast wyłączania IPv6. Skutek sieciowy (kolejność adresów w ruchu) nie jest mierzony, bo VM nie mają sieci. Nowa pozycja, v3.2 miała tylko pełne wyłączenie (`net.ipv6-disable`).

## Co zmienia
- `HKLM\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters`, wartość `DisabledComponents`, **DWord**: brak -> `0x20` (32). `scope=machine`, zapis przez API rejestru w C#, bez skryptu.
- Wg MS wartość to maska bitów; 0x20 = tylko bit PreferIpv4. Zmiana działa po restarcie (MS: „You must restart your computer for these changes to take effect”), więc `requiresReboot=true`.
- Ta sama wartość rejestru jest zapisywana przez `net.ipv6-disable` (0xFF). Pozycje są oznaczone w katalogu jako wzajemnie konfliktowe (pole `conflicts`, wymuszane przez silnik przy Apply).

## Źródła
- https://learn.microsoft.com/en-us/troubleshoot/windows-server/networking/configure-ipv6-in-windows – MS, dostęp 2026-10-18 (pobrana dwukrotnie): `DisabledComponents` REG_DWORD, min 0x00 (domyślna), max 0xFF; „Prefer IPv4 over IPv6” = 32 / 0x20 / `xx1x xxxx`, „Recommended instead of disabling IPv6”; wymagany restart; „Values other than 0 or 32 causes the Routing and Remote Access service to fail” (0x20 jest dozwoloną wartością).
- Odczyt VM 26H2 Home i Pro, 2026-10-18 (`docs/zrodla/2026-10-18-partia-c3-zrodla.md`, „Wyniki VM”, sekcja 2).

## Wartości domyślne na świeżym 26H2
Odczyt z VM DSO-26H2-Home i DSO-26H2-Pro (build 26300.9457), 2026-10-18 (Home = Pro):
- Klucz `Tcpip6\Parameters` istnieje (podklucze `Interfaces`, `Winsock`), a **`DisabledComponents` nie istnieje** (brak, nie DWORD 0). Stąd `original` = `<RemoveEntry>`.
- `Get-NetPrefixPolicy` działa bez sieci (9 wpisów): `::ffff:0:0/96` ma precedencję **35**, `::/0` ma **40**, więc domyślnie IPv6 wygrywa z IPv4.

## Detekcja i undo
- Detekcja: tylko rejestr, przez równość: dokładnie DWORD 0x20 -> `Applied`, każda inna wartość lub brak -> `NotApplied`. Silnik nie ma masek bitowych, więc np. 0x21 (ustawiony też bit Tunnel) jest `NotApplied`; apply nadpisze całą wartość, a snapshot z journala zachowa poprzednią (undo ją odtworzy). Realny skutek (kolejność adresów) nie jest wykrywany przez katalog.
- Undo: przywraca wartość ze snapshotu z journala; jeśli wartości nie było, usuwa ją (nie zapisuje 0). Bez wpisu w journalu fallback `original` = `<RemoveEntry>` (stan świeżego 26H2 zmierzony na VM).
- Informacyjny `CheckAfterApply` w przypadku VM: `Get-NetPrefixPolicy` (CIM, bez tekstu zależnego od języka) po restarcie sprawdza, że precedencja `::ffff:0:0/96` jest większa niż `::/0` (porównanie liczbowe). Kryterium pochodzi ze strony MS „Configure IPv6 for advanced users” (zdanie: „The '::ffff:0:0/96' prefix should have a higher precedence than the '::/0' prefix”), a nie z niezależnego pomiaru ruchu. Prefix policy jest czytelna bez sieci (odczyt VM powyżej); wynik na VM po restarcie: sekcja „Test VM”.
- Konflikt z `net.ipv6-disable` (ta sama wartość rejestru): silnik wymusza pole `conflicts`. Apply tej pozycji jest odrzucany (`Refused`, bez żadnego zapisu i bez zmiany journala), dopóki `net.ipv6-disable` ma aktywny wpis w journalu; trzeba ją najpierw cofnąć, po czym Apply się powiedzie (testy silnika na fake'ach). Gdy journala nie da się odczytać, Apply kończy się `Failed` bez zapisu (nie przepuszcza konfliktu). Cofnięcie tej pozycji bez własnego wpisu w journalu (undo z wartości domyślnych) też jest odrzucane (`Refused`), dopóki `net.ipv6-disable` jest aktywna, żeby nie nadpisać jej wartości. Ograniczenie: konflikt dotyczy tylko wpisów journala, wartość `DisabledComponents` ustawiona ręcznie poza narzędziem nie jest wykrywana jako konflikt (apply ją nadpisze, a undo przywróci ze snapshotu).

## Ryzyka i ostrzeżenia
- `risk=caution`.
- Ostrzeżenie w katalogu: „Wymaga restartu komputera.”
- Poza uwagami w programie (w szczegółach technicznych albo tylko tutaj): zapisywana jest cała wartość (inne bity niż 0x20 są nadpisywane, rozpoznawane jest tylko dokładnie 0x20); ta sama wartość co `net.ipv6-disable`, więc Apply jest odrzucany, dopóki ta pozycja jest aktywna (wartość ustawiona poza narzędziem nie jest wykrywana jako konflikt); wpływ na ruch i aplikacje niezmierzony (VM bez sieci).
- Bez obietnic wydajności ani szybszego DNS.

## Test VM
Przebieg VM z 2026-10-18 (Home 19:58, Pro 20:10; build 26300.9457), DSO-26H2-Home (Core) i DSO-26H2-Pro (Professional), oba bez błędów (278 zaliczonych / 0 błędów / 17 pominiętych na każdej VM). Raporty lokalne: `tests/vm/out/<data>/<VM>/<RunLabel>/report.json` (katalog w `.gitignore`).

Przypadek `net-ipv6-prefer-ipv4` (`Flow=Reboot`, przebieg `reboot-net-ipv6-prefer-ipv4`, osobny przebieg od czystego checkpointu; Pester 15/15 na Home i Pro), **Passed**, Home i Pro identycznie:
- Sprawdzono: `detect-before` = `NotApplied` (wartości `DisabledComponents` brak), `apply` = `Applied` (`requiresReboot` = true; snapshot po apply: DWORD 32 = 0x20), `detect-after-apply` = `Applied`, **prawdziwy restart** (późniejszy `LastBootUpTime`, `MarkerGone` = true), `detect-after-reboot` = `Applied`, `undo` = `Undone` (`requiresReboot` = true), po drugim restarcie `detect-after-undo` = `NotApplied`. Stan końcowy = stan przed: wartość nieobecna, klucz `Tcpip6\Parameters` zostaje (`KeyPresentAfterUndo`), `SnapshotDiff` pusty.
- `CheckAfterApply` (po restarcie, `Get-NetPrefixPolicy`): **Passed**. Skrypt wymaga dokładnie jednego wpisu `::ffff:0:0/96`, dokładnie jednego `::/0` i `Precedence` pierwszego większej niż drugiego. Przed zmianą odczyt z VM dawał 35 i 40 (czyli odwrotnie), więc kryterium rozróżnia stan przed i po. Konkretne wartości po apply nie są zapisane w raporcie (tylko wynik Passed). Kryterium pochodzi ze zdania MS z sekcji „Detekcja i undo”; VM potwierdziła je jako zachowanie `Get-NetPrefixPolicy` po restarcie, nie jako skutek w ruchu.
- W przebiegu 20261018-195830 `CheckAfterUndo` nie było zdefiniowane: powrót tablicy prefix policy do 35 / 40 po undo i restarcie nie został wtedy sprawdzony (sprawdzono tylko brak wartości w rejestrze i `NotApplied`). Po końcowym review dodano `CheckAfterUndo` (precedencja `::ffff:0:0/96` mniejsza niż `::/0`). W przebiegu 20261018-204534 (`reboot-net-ipv6-prefer-ipv4`, Home i Pro) `CheckAfterUndo` = **Passed**, tak samo jak `CheckAfterApply`: po undo i drugim restarcie precedencja `::ffff:0:0/96` jest znów mniejsza niż `::/0`.
- **Nie sprawdzono:** faktycznej preferencji IPv4 w ruchu (VM bez sieci), zachowania na sprzęcie z działającym IPv6.
