# perf.spectre-mitigations – wyłączenie mitygacji Spectre/Meltdown (nie portowane)

## Werdykt 26H2
Nie portowane: strona kliencka MS (KB4073119) kończy się na Windows 11 23H2, wyłączenie mitygacji obniża ochronę na cudzym komputerze bez zmierzonej korzyści, a detekcja widzi tylko rejestr, nie rzeczywisty stan mitygacji. Decyzja Dawida z 2026-10-18. Alias `cpu_spectre` z v3.2 nie ma pozycji w katalogu.

## Co zmienia
Pozycji nie ma w katalogu, nic nie zmienia. Rozważany wpis (v3.2 i MS): `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management`, wartości `FeatureSettingsOverride` = 3 i `FeatureSettingsOverrideMask` = 3 (oba DWord), wymagany restart.

Powody odrzucenia:
- Strona kliencka MS „Windows client guidance…” (KB4073119) w „Applies to” obejmuje Windows 11 do 23H2, ostatni wpis w change-logu to 2024-04-16; 24H2, 25H2 i 26H2 nie są wymienione.
- Wyłączenie obniża ochronę (Spectre wariant 2, Meltdown i pokrewne). MS ostrzega, że hosty Hyper-V, hosty usług pulpitu zdalnego i systemy uruchamiające niezaufany kod są w zwiększonym ryzyku. Narzędzie działa na cudzych komputerach, więc obniżanie zabezpieczeń bez zmierzonej korzyści przeczy jego celowi.
- Brak zmierzonej ani udokumentowanej korzyści na współczesnym procesorze; MS nie podaje, ile daje wyłączenie. Wynik z VM nie byłby dowodem dla żelaza (procesor jest wirtualizowany).
- Detekcja rejestrowa nie widzi realnego stanu mitygacji (to robi moduł `SpeculationControl` z PowerShell Gallery, który wymaga sieci).
- Warianty Intel/AMD ze specu nie istnieją dla wyłączania: MS podaje jedną parę 3/3 (albo 1/3 tylko dla wariantu 2); warianty zależne od dostawcy dotyczą włączania dodatkowych mitygacji.
- Opis v3.2 („przywraca pełną prędkość predykcji gałęzi”) i ostrzeżenia o korporacyjnych hypervisorach nie mają źródła MS.

## Źródła
- https://support.microsoft.com/help/4073119 – MS (KB4073119, Windows client guidance for IT Pros), dostęp 2026-10-18: wartości wyłączające 3/3 i 1/3, restart, ostrzeżenia, Applies-to do Windows 11 23H2, change-log do 2024-04-16.
- https://support.microsoft.com/en-us/topic/windows-server-guidance-to-protect-against-speculative-execution-side-channel-vulnerabilities-2f965763-00e2-8f98-b632-0d96f30c8c8e – MS (wersja serwerowa), dostęp 2026-10-18: sekcje „To disable…”, ostrzeżenia dla Hyper-V, RDS i niezaufanego kodu.
- KB4074629 (wyjście `Get-SpeculationControlSettings`) – wskazane przez stronę MS, nie pobierane osobno.
- Research: `docs/zrodla/2026-10-18-partia-c2-zrodla.md`, sekcja 3. Wartości 3/3 i 1/3 pochodzą z pobrań narzędziem streszczającym; przed ewentualnym portem trzeba je ręcznie porównać z oryginałem KB4073119.

## Wartości domyślne na świeżym 26H2
Odczyt z VM DSO-26H2-Home i DSO-26H2-Pro (build 26300.9457), 2026-10-18 (Home = Pro, tylko odczyt):
- `FeatureSettingsOverride`: **brak** (klucz `Memory Management` istnieje).
- `FeatureSettingsOverrideMask`: **brak**.
- `FeatureSettings` (wartość pokrewna, nie z KB): DWORD 0; jej znaczenia nie ma w źródłach MS, więc nie jest interpretowana.

## Detekcja i undo
Nie dotyczy, pozycja nie jest portowana. Gdyby wróciła do rozważenia: dwa wpisy `registry[]` (oba == 3 -> `Applied`), `original` = `<RemoveEntry>` (na świeżym 26H2 obu wartości brak). Otwarte: czy usunięcie wartości daje ten sam stan co jawne 0/3 (MS opisuje „przywróć domyślne” jako 0/3) i czy system nie nadpisuje wartości po restarcie.

## Ryzyka i ostrzeżenia
Powody w sekcji „Co zmienia”. Gdyby pozycja kiedyś weszła, musiałaby mieć `risk=experimental`, ostrzeżenie o utracie ochrony i o hostach wielodostępnych oraz niezaufanym kodzie (zdania z MS), bez obietnic wydajności.

## Test VM
Nie dotyczy (pozycja nie jest portowana). Wykonano tylko odczyt wartości domyślnych, patrz wyżej.

## Reddit
Do uzupełnienia w części E.
