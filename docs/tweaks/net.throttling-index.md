# net.throttling-index – NetworkThrottlingIndex / SystemResponsiveness (nie portowane)

## Werdykt 26H2
Nie portowane (decyzja autora z 2026-10-18): `NetworkThrottlingIndex` nie ma dokumentacji MS, a `SystemResponsiveness` = 0 z v3.2 według MS jest traktowane jak 20, więc nic nie zmienia. Alias `net_throttle` z v3.2 nie ma pozycji w katalogu.

## Co zmienia
Pozycji nie ma w katalogu, nic nie zmienia. Rozważany wpis (v3.2): `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile`, `NetworkThrottlingIndex` = 0xFFFFFFFF i `SystemResponsiveness` = 0 (oba DWord).

Powody odrzucenia:
- MS (MMCSS): `SystemResponsiveness` to odsetek CPU gwarantowany zadaniom o niskim priorytecie; wartości poniżej 10 i powyżej 100 są traktowane jak 20. Zero z v3.2 nie daje więc „0%”, a udokumentowanym efektem jest wartość domyślna.
- `NetworkThrottlingIndex` nie występuje w dokumentacji MS (strona MMCSS go nie wymienia). Semantyka (domyślnie 10, 0xFFFFFFFF = wyłączone) pochodzi tylko z anonimowej odpowiedzi społeczności, to nie jest dowód MS.
- Opis v3.2 („limit 80% przepustowości”) nie ma źródła MS: MS mówi o CPU dla zadań niskiego priorytetu, nie o przepustowości łącza.
- Skutek sieciowy nie jest mierzalny na VM bez sieci; zapis wartości, której MS nie honoruje albo nie dokumentuje, byłby fałszywym tweakiem.

## Źródła
- https://learn.microsoft.com/en-us/windows/win32/procthread/multimedia-class-scheduler-service – MS, dostęp 2026-10-18 (pobrana dwukrotnie): `SystemResponsiveness` (REG_DWORD, zaokrąglanie do wielokrotności 10, wartości poniżej 10 i powyżej 100 traktowane jak 20, 100 wyłącza MMCSS); `NetworkThrottlingIndex` nie jest wymieniony.
- https://learn.microsoft.com/en-my/answers/questions/4324930/network-throttling-mechanism – odpowiedź społeczności (to NIE jest dokumentacja MS), dostęp 2026-10-18: domyślnie 10, `ffffffff` jako wyłączenie.
- Research: `docs/zrodla/2026-10-18-partia-c3-zrodla.md`, sekcja 1.

## Wartości domyślne na świeżym 26H2
Odczyt z VM DSO-26H2-Home i DSO-26H2-Pro (build 26300.9457), 2026-10-18 (Home = Pro, tylko odczyt), klucz `SystemProfile`:
- `NetworkThrottlingIndex`: **DWORD 10** (0x0000000A).
- `SystemResponsiveness`: **DWORD 20** (0x00000014).
- Klucz zawiera tylko te dwie wartości i podklucz `Tasks`.
- Nie rozstrzygnięto: skutku zapisu 0xFFFFFFFF ani trwałości po restarcie (nie testowano), skutku sieciowego (VM bez sieci).

## Detekcja i undo
Nie dotyczy, pozycja nie jest portowana. Gdyby wróciła do rozważenia: tylko `NetworkThrottlingIndex` = 0xFFFFFFFF w `registry[]` (`original` = 10 według odczytu VM), `risk=experimental`, bez wpisu `SystemResponsiveness`.

## Ryzyka i ostrzeżenia
Powody w sekcji „Co zmienia”. Gdyby pozycja kiedyś weszła, musiałaby mówić, że wartość pochodzi z nieoficjalnych poradników, MS jej nie dokumentuje, a skutek jest niezmierzony.

## Test VM
Nie dotyczy (pozycja nie jest portowana, w katalogu nie ma przypadku VM). Wykonano tylko odczyt wartości domyślnych na VM Home i Pro 2026-10-18 (10 i 20, patrz „Wartości domyślne na świeżym 26H2”). Zapisu 0xFFFFFFFF, trwałości po restarcie ani skutku sieciowego nie sprawdzono.
