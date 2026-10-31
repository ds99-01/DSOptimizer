# net.ipv6-disable – Wyłącz IPv6

## Werdykt 26H2
Portować jako `experimental`, z ostrzeżeniami dosłownie z MS: wartość 0xFF jest udokumentowana („IPv6 disabled”), ale MS wprost nie zaleca wyłączania IPv6 i poleca Prefer IPv4 (`net.ipv6-prefer-ipv4`). Opis z v3.2 („odciąża DNS”, „przyspiesza rozwiązywanie nazw”) i ostrzeżenia o Intune/DirectAccess nie mają źródła MS, więc nie wchodzą do katalogu. Alias v3.2: `net_ipv6`.

## Co zmienia
- `HKLM\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters`, wartość `DisabledComponents`, **DWord**: brak -> `0xFF` (255). `scope=machine`, zapis przez API rejestru w C#, bez skryptu.
- Wartość `0xFFFFFFFF` nie jest używana: MS pisze, że opóźnia start systemu o pięć sekund, a poprawna wartość to 0xFF.
- Zmiana działa po restarcie (MS), więc `requiresReboot=true`.
- Ta sama wartość rejestru jest zapisywana przez `net.ipv6-prefer-ipv4` (0x20); pozycje są oznaczone jako wzajemnie konfliktowe (pole `conflicts`, wymuszane przez silnik przy Apply).

## Źródła
- https://learn.microsoft.com/en-us/troubleshoot/windows-server/networking/configure-ipv6-in-windows – MS, dostęp 2026-10-18 (pobrana dwukrotnie): 255 / 0xFF / `1111 1111` = IPv6 disabled (max); „We don't recommend that you disable IPv6 or IPv6 components or unbind IPv6 from interfaces. If you do, some Windows components might not function”; „You cannot completely disable IPv6… you will still be able to run ping `::1`”; pole wyboru IPv6 karty nie zmienia stanu (oczekiwane zachowanie); 0xffffffff -> opóźnienie startu o pięć sekund; „Values other than 0 or 32 causes the Routing and Remote Access service to fail”; wymagany restart.
- Odczyt VM 26H2 Home i Pro, 2026-10-18 (`docs/zrodla/2026-10-18-partia-c3-zrodla.md`, „Wyniki VM”, sekcja 2).

## Wartości domyślne na świeżym 26H2
Odczyt z VM DSO-26H2-Home i DSO-26H2-Pro (build 26300.9457), 2026-10-18 (Home = Pro):
- Klucz `Tcpip6\Parameters` istnieje, a **`DisabledComponents` nie istnieje** (brak, nie DWORD 0). Stąd `original` = `<RemoveEntry>`.
- `Get-NetAdapterBinding -ComponentID ms_tcpip6`: 1 wiązanie (Ethernet), `Enabled` = true. Opis wiązania jest po polsku, więc nie służy do detekcji.

## Detekcja i undo
- Detekcja: tylko rejestr, przez równość: dokładnie DWORD 0xFF -> `Applied`; 0xFFFFFFFF, 0x20 i każda inna wartość lub brak -> `NotApplied`. Nie ma sprawdzenia realnego stanu stosu (MS nie podaje do tego udokumentowanego odczytu; pole wyboru karty się nie zmienia), więc `Applied` znaczy „wartość ustawiona wg MS”.
- Undo: przywraca wartość ze snapshotu z journala; jeśli wartości nie było, usuwa ją (nie zapisuje 0). Bez wpisu w journalu fallback `original` = `<RemoveEntry>`; jeśli przed zmianą działało `net.ipv6-prefer-ipv4` bez wpisu w journalu, to ustawienie zostanie utracone.
- Konflikt z `net.ipv6-prefer-ipv4` (ta sama wartość rejestru): silnik wymusza pole `conflicts`. Apply tej pozycji jest odrzucany (`Refused`, bez żadnego zapisu i bez zmiany journala), dopóki `net.ipv6-prefer-ipv4` ma aktywny wpis w journalu; trzeba ją najpierw cofnąć, po czym Apply się powiedzie (testy silnika na fake'ach). Gdy journala nie da się odczytać, Apply kończy się `Failed` bez zapisu (nie przepuszcza konfliktu). Cofnięcie tej pozycji bez własnego wpisu w journalu (undo z wartości domyślnych) też jest odrzucane (`Refused`), dopóki `net.ipv6-prefer-ipv4` jest aktywna, żeby nie nadpisać jej wartości. Ograniczenie: konflikt dotyczy tylko wpisów journala, wartość `DisabledComponents` ustawiona ręcznie poza narzędziem nie jest wykrywana jako konflikt (apply ją nadpisze, a undo przywróci ze snapshotu).
- Bez `CheckAfterApply` z polityką prefiksów: MS nie definiuje, jak `Get-NetPrefixPolicy` wygląda przy 0xFF.

## Ryzyka i ostrzeżenia
- `risk=experimental`.
- Ostrzeżenia w katalogu: „Microsoft nie zaleca wyłączania IPv6: część funkcji Windows może przestać działać, np. usługa Routing and Remote Access.” oraz „Wymaga restartu komputera.”
- Poza uwagami w programie (w szczegółach technicznych albo tylko tutaj): zalecane Prefer IPv4 zamiast wyłączania; usługa Routing and Remote Access przestaje działać przy wartościach innych niż 0 i 32; `::1` nadal odpowiada, a pole wyboru IPv6 karty się nie zmienia; ta sama wartość co `net.ipv6-prefer-ipv4`, więc Apply jest odrzucany, dopóki ta pozycja jest aktywna (wartość ustawiona poza narzędziem nie jest wykrywana jako konflikt).
- Bez obietnic wydajności.

## Test VM
Przebieg VM z 2026-10-18 (Home 19:58, Pro 20:10; build 26300.9457), DSO-26H2-Home (Core) i DSO-26H2-Pro (Professional), oba bez błędów (278 zaliczonych / 0 błędów / 17 pominiętych na każdej VM). Raporty lokalne: `tests/vm/out/<data>/<VM>/<RunLabel>/report.json` (katalog w `.gitignore`).

Przypadek `net-ipv6-disable` (`Flow=Reboot`, przebieg `reboot-net-ipv6-disable`, osobny przebieg od czystego checkpointu; Pester 14/14 na Home i Pro), **Passed**, Home i Pro identycznie:
- Sprawdzono: `detect-before` = `NotApplied` (`DisabledComponents` nie istnieje), `apply` = `Applied` (`requiresReboot` = true; snapshot po apply: DWORD 255 = 0xFF), `detect-after-apply` = `Applied`, **prawdziwy restart** (późniejszy `LastBootUpTime`, `MarkerGone` = true), `detect-after-reboot` = `Applied`, `undo` = `Undone` (`requiresReboot` = true), po drugim restarcie `detect-after-undo` = `NotApplied`. Stan końcowy = stan przed: wartość nieobecna, klucz `Tcpip6\Parameters` zostaje (`KeyPresentAfterUndo`), `SnapshotDiff` pusty.
- Bez `CheckAfterApply`: MS nie definiuje odczytu stanu stosu przy 0xFF (pole wyboru karty się nie zmienia), więc sprawdzana jest tylko wartość rejestru i jej trwałość po restarcie.
- **Nie sprawdzono:** skutku dla składników Windows i ruchu (VM bez sieci), czasu startu, zachowania usług zależnych od IPv6.
