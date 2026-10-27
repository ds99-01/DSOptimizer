# net.network-private – Zmień aktywną sieć z publicznej na prywatną

## Werdykt 26H2
Portować z ograniczeniem, jako `kind=action`, `risk=caution` (pozycja nowa; v3.2 robiła to jako pierwszy krok `lan_godmode` dla **wszystkich** profili). Decyzje: zmieniane są wyłącznie **aktywne profile `Public`** (nigdy `DomainAuthenticated`, `Private` ani profile bez łączności), a brak takiego profilu to kod wyjścia ≠ 0 z komunikatem („Failed”), a nie zielone `Executed` bez efektu.

Akcja nie ma detekcji, journala ani undo. Jedyny dowód skutku to **odczyt zwrotny** kategorii po zmianie (kod wyjścia 0 tylko wtedy, gdy każdy zmieniony profil jest teraz `Private`).

## Co zmienia
- Skrypt (`script.apply`, `-File`, PowerShell 5.1): przez funkcję pomocniczą `Get-DsoNetConnectionProfile` czyta `Get-NetConnectionProfile`, wybiera profile z `NetworkCategory` = `Public` oraz `IPv4Connectivity` lub `IPv6Connectivity` ≠ `Disconnected`, i dla każdego woła przez `Set-DsoNetConnectionProfileCategory` cmdlet `Set-NetConnectionProfile -InputObject <profil> -NetworkCategory Private`. Potem czyta profile ponownie i sprawdza kategorię każdego zmienionego (dopasowanie po `InterfaceIndex` i `Name`).
- Porównania idą po **nazwie enuma** (`[string]$p.NetworkCategory -eq 'Public'`), nie po liczbie ani po sformatowanym tekście zależnym od języka. Nazwy enumów nie są tłumaczone; profil z surową liczbą zamiast nazwy nie zostałby uznany za `Public` (skrypt odmawia zamiast zgadywać numerację, której MS nie opublikowało dla klasy `MSFT_NetConnectionProfile`).
- Wyjście (stdout skryptu; silnik przy `Executed` przekazuje je w wyniku, a CLI w polu `message` raportu): dla każdego zmienionego profilu nazwa, alias interfejsu, indeks i poprzednia kategoria, plus gotowa wskazówka ręcznego cofnięcia (`-InterfaceIndex <n> -NetworkCategory Public`).
- Brak aktywnego profilu `Public` (także pusty wynik `Get-NetConnectionProfile`): `throw` z komunikatem „brak aktywnego połączenia w profilu publicznym – nic nie zmieniono”, kod wyjścia 1, nic nie zmienione.
- Nie dotyka: `DomainAuthenticated` (MS: „You cannot set the DomainAuthenticated type by using this cmdlet”), profili `Private`, profili bez łączności, zapory, usług, rejestru.

## Źródła
- https://learn.microsoft.com/en-us/powershell/module/netconnection/set-netconnectionprofile – MS Learn, dostęp 2026-10-18: `-NetworkCategory` (`Public`, `Private`, `DomainAuthenticated`), `-InputObject`, filtry; „Private”: „Your PC is discoverable and can be used for printer and file sharing if you set it up”, „Public”: „Your PC is hidden from other devices on the network and can't be used for printer and file sharing”; „You cannot set the DomainAuthenticated type by using this cmdlet”.
- https://learn.microsoft.com/en-us/powershell/module/netconnection/get-netconnectionprofile?view=windowsserver2025-ps – MS Learn, dostęp 2026-10-18: odczyt profili, `NetworkCategory`, `IPv4Connectivity`, `IPv6Connectivity`, `InterfaceIndex`.
- https://learn.microsoft.com/en-us/windows/security/operating-system-security/network-security/windows-firewall/ – MS Learn (updated_at 2026-10-08), dostęp 2026-10-18: profile zapory (domain/private/public), reguły per profil, Private można ustawić ręcznie, Public to domyślny profil sieci niezidentyfikowanych; `Get-NetConnectionProfile`/`Set-NetConnectionProfile` do zmiany kategorii. Sformułowanie „networks at home or work, where you know and trust the people and devices on the network” dla Private.
- Hint (tylko ze streszczenia wyszukiwarki, nie z pełnej strony): wątki MS Q&A o błędzie „not found” przy filtrach `Set-NetConnectionProfile` oraz o „Provider load failure” przy odczycie `MSFT_NetConnectionProfile` przez .NET WMI; stąd odczyt cmdletem w PowerShell, nie klasą CIM z C#.
- Strony referencyjnej klasy `MSFT_NetConnectionProfile` (przestrzeń `root\StandardCimv2`, wartości liczbowe `NetworkCategory`) MS Learn nie ma; dlatego skrypt nie zależy od liczb.
- Odczyt VM 26H2 Home i Pro, 2026-10-18 (sekcja „Wyniki VM” w `docs/zrodla/2026-10-18-partia-c3-zrodla.md`).

## Wartości domyślne na świeżym 26H2
Odczyt z VM DSO-26H2-Home i DSO-26H2-Pro (build 26300.9457, 2026-10-18; obie tak samo): `Get-NetConnectionProfile` zwraca **0 obiektów, bez błędu**; `Get-NetAdapter` pokazuje jedną kartę Ethernet (Microsoft Hyper-V Network Adapter) w stanie `Disconnected`; klasa CIM `MSFT_NetConnectionProfile` w `root\StandardCimv2` jest dostępna i pusta. Wartość `NetworkCategory` (nazwa enuma albo liczba) i `IPv4Connectivity` **nie były obserwowane** (brak obiektów do odczytu).

## Detekcja i undo
Brak detekcji (`kind=action`). **Brak cofnięcia** (brak journala): akcja wypisuje poprzednią kategorię i indeks interfejsu każdego zmienionego profilu, a ręcznie kategorię przywrócisz oficjalnym cmdletem: `Set-NetConnectionProfile -InterfaceIndex <indeks> -NetworkCategory Public`. Gdy zmiana jednego z kilku profili się nie powiedzie albo odczyt zwrotny jej nie potwierdzi, komunikat błędu wymienia także profile już zmienione (z poprzednią kategorią), żeby było je czym cofnąć.

## Ryzyka i ostrzeżenia
- `risk=caution`. Dosłownie z MS: Private = „networks at home or work, where you know and trust the people and devices on the network”; w Private „Your PC is discoverable and can be used for printer and file sharing if you set it up”. Na obcej sieci (kawiarnia, hotel, cudze biuro) to obniżenie ochrony. Zapora Windows przełącza się na reguły profilu Private; MS nie wymienia na stronie przeglądowej, które reguły są wtedy włączone, więc narzędzie nie obiecuje konkretnych skutków.
- Polityka „Network List Manager Policies” (hint z MS Q&A) może blokować zmianę kategorii; zachowanie `Set-NetConnectionProfile` pod nią niesprawdzone. Odczyt zwrotny jej nie potwierdzi i akcja zakończy się `Failed`.
- Potwierdzenie w UI przed uruchomieniem (spec, sekcja 7).

## Test VM
Przebieg VM z 2026-10-18 (Home 19:58, Pro 20:10; build 26300.9457), DSO-26H2-Home (Core) i DSO-26H2-Pro (Professional), oba bez błędów (278 zaliczonych / 0 błędów / 17 pominiętych na każdej VM). Raporty lokalne: `tests/vm/out/<data>/<VM>/<RunLabel>/report.json` (katalog w `.gitignore`).

Dwa przypadki w `tests/vm/cases.psd1` (`Flow=Execute`, `Catalog=BuiltIn`):
- `net-network-private-no-network`: **Passed** na Home i Pro. VM bez sieci (0 profili): `execute` kod 1, wynik `OperationFailed` / `Failed`, a `message` w `report.json` ma nienaruszone polskie znaki. Zapis dosłowny z raportu: „Skrypt zakończył się kodem 1: brak aktywnego połączenia w profilu publicznym – nic nie zmieniono” (kody Unicode: `ł` U+0142, `ń` U+0144, `ą` U+0105, `ę` U+0119, myślnik `–` U+2013, czyli komunikat przeszedł z PowerShell przez CLI do JSON bez uszkodzenia kodowania). Asercja `MessageContains = 'profilu publicznym'` (fragment bez polskich znaków) przeszła. `Check` (`@(Get-NetConnectionProfile).Count -eq 0`): **Passed**, stan = baseline VM, nic się nie pojawiło ani nie zmieniło. To jedyna ścieżka sprawdzalna na VM.
- `net-network-private-change`: **Skipped** („brak sieci na VM”), realna zmiana profilu `Public` → `Private`. Raportowane jako pominięte, nigdy jako zaliczone.

**Nie testowane na VM: brak sieci.** Nie sprawdzono: zmiany prawdziwego profilu, odczytu `NetworkCategory`/`IPv4Connectivity` jako nazw enumów na prawdziwym profilu (na VM są 0 profili, więc rzutowanie `[string]NetworkCategory` na prawdziwym profilu pozostaje niezmierzone; założenie: `[string]` daje angielską nazwę; gdyby było inaczej, skrypt zakończy się komunikatem „brak aktywnego połączenia…” zamiast zmieniać cokolwiek), zachowania zapory po zmianie ani zachowania pod polityką. Skutek sieciowy (widoczność komputera, reguły zapory) jest poza zakresem testów VM.

## Reddit
Do uzupełnienia w części E.
