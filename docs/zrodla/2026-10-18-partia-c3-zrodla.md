# Partia C3 – źródła i mechanizmy (research 2026-10-18)

Zakres: 6 pozycji katalogu kategorii `net` (net.throttling-index, net.ipv6-prefer-ipv4, net.ipv6-disable, net.smb-guest-access, net.smb-harden, net.network-private). Spec: specyfikacja w archiwum autora, sekcje 2, 3, 5 (partia 3) i 11. Format i legenda jak w `2026-10-18-partia-c2-zrodla.md`.
Zasada: nic nie jest wymyślone. Co nie ma potwierdzenia w źródle MS, jest oznaczone „niepotwierdzone w źródłach MS” i trafia na listę kontroli VM.
Wszystkie URL: data dostępu 2026-10-18. Rodzaj źródła: **MS** = learn.microsoft.com / support.microsoft.com (strona bieżąca); **MS-archiwum** = stara dokumentacja MS (może być nieaktualna); **MS-blog** = techcommunity.microsoft.com; **MS-Q&A** = odpowiedź społeczności na learn.microsoft.com/answers lub wątek techcommunity „discussions” (to NIE jest dokumentacja, tylko hint); **hint** = prasa, fora, narzędzia społeczności.
Uwaga o metodzie: pobrana strona zwracała surowy markdown (nie streszczenie), więc cytaty i wartości liczbowe (0x20, 0xFF, zakresy MMCSS) pochodzą z treści strony; kluczowe strony pobrano dwukrotnie (MMCSS, IPv6) z identycznym wynikiem. Wyjątek: wpis techcommunity 4154300 („Accessing a third-party NAS with SMB in Windows 11 24H2 may fail”) zwrócił tylko tytuł, jego treść NIE została przeczytana (patrz pozycja 4). Wartości oznaczone **[SPRAWDZIĆ RĘCZNIE]** trzeba porównać z oryginałem w przeglądarce przed wpisaniem do katalogu.
Nie wykonywano żadnych poleceń zmieniających system ani odczytów rejestru/CIM/usług na hoście. Wartości domyślne „z VM” nie są zmierzone.

## 0. Co silnik umie – rzeczy nowe dla sieci (sprawdzone w kodzie, nie ze źródeł MS)

Podstawy (`registry[]`, `services[]`, agregacja stanu) – patrz sekcja 0 w C2. Nowe lub istotne dla partii 3:
- **DWORD 0xFFFFFFFF**: `RegValueJson` przyjmuje DWORD jako liczbę całkowitą do `uint.MaxValue`, a zapis idzie jako `long` – wartość 4294967295 jest do wyrażenia w katalogu. Porównanie w detekcji jest równością typu i danych (`RegValue.Equals`), więc **maski bitowe nie są wspierane**: `DisabledComponents` jest „Applied” tylko przy dokładnie 0x20 (albo 0xFF), nie przy innej kombinacji bitów zawierającej bit 0x20.
- **Wiele usług**: `services[]` to lista; każda usługa to osobna operacja (`ServiceStartupOperation`), agregator daje `Partial` przy mieszance. Detekcja usługi = tylko typ startu (stan Running nie bierze udziału). Brak usługi = NotApplicable.
- **`script.detect` obok `registry[]`**: `ScriptOperation.ParticipatesInDetection` = `script.detect != null`; ostatnia linia stdout `Applied` / `NotApplied` / `NotSupported` (w kodzie widziałem trzy; pełna lista wartości – patrz `ScriptOperation.cs`, nie czytałem do końca) jest składana w agregacji razem z wpisami rejestru. Czyli tweak może mieć `registry[]` (apply/undo przez API, journal) plus `script.detect` (realny stan przez cmdlet/CIM) – oba muszą się zgadzać, żeby wyszło `Applied`.
- **`kind=action`**: `TweakEngine.Execute` uruchamia WYŁĄCZNIE `script.apply` (przez `-File`, kod 0 = `Executed`, inny = `Failed` z treścią stderr). Akcja **nie ma** detekcji, journala, snapshotu ani undo; `registry[]` i `services[]` w akcji są ignorowane przez Execute. Dla `net.smb-harden` i `net.network-private` oznacza to: cała logika w jednym skrypcie PS 5.1, a „brak sieci” nie ma osobnego wyniku – tylko kod wyjścia 0 (zielone) albo ≠0 (Failed z komunikatem). To decyzja do podjęcia (patrz pozycja 6).
- Bezpieczniki harnessu skryptów (zasady repo) NIE obejmują `Set-SmbClientConfiguration`, `Set-NetConnectionProfile`, `Set-ItemProperty` na `HKLM:` – skrypty muszą wołać je przez funkcje pomocnicze mockowane w testach (wzorzec `disk.ntfs-last-access`).

---

## 1. net.throttling-index (alias v3.2 `net_throttle`)

**Co robiła v3.2** (poprzednia, niepubliczna wersja 3.2): tworzyła klucz, jeśli brak, i ustawiała `HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile` → `NetworkThrottlingIndex` = 0xFFFFFFFF i `SystemResponsiveness` = 0 (oba DWord). Detekcja: tylko `NetworkThrottlingIndex` == 0xFFFFFFFF. Opis v3.2: „Windows domyślnie limituje 80% przepustowości dla multimediów… odblokowuje pełną moc karty sieciowej”.

**Mechanizm wg MS**
- Klucz `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile` to klucz ustawień MMCSS (MS, Win32 „Multimedia Class Scheduler Service”, ms.date 2025-07-14).
- **SystemResponsiveness** (REG_DWORD): „determines the percentage of CPU resources that should be guaranteed to low-priority tasks”; przykład MS: wartość 20 = 20% CPU zarezerwowane dla zadań niskiego priorytetu. „Values that are not evenly divisible by 10 are rounded down to the nearest multiple of 10. **Values below 10 and above 100 are clamped to 20.** A value of 100 disables MMCSS (driver returns `STATUS_SERVER_DISABLED`).” Wniosek wprost z tekstu MS: **0 jest poniżej 10, więc wg MS jest traktowane jak 20** – wartość z v3.2 nie daje „0%”, a udokumentowany efekt to ustawienie domyślnego (20), czyli prawdopodobnie brak zmiany. Strona nie podaje wprost „domyślnie 20” (tylko przykład z 20) – **domyślna wartość niepotwierdzona w źródłach MS** (pomiar VM). Nie jest to parametr sieciowy: dotyczy CPU, nie przepustowości.
- **NetworkThrottlingIndex: brak w dokumentacji MS.** Strona MMCSS go nie wymienia (sprawdzone dwukrotnie). Wyszukiwanie w domenach MS dało wyłącznie odpowiedzi społeczności (MS-Q&A). Odpowiedź anonimowa z 2022-09-01 (rola autora niepodana, „Anonymous”): „The Dword section should be set to 10 for the default value” oraz `NetworkThrottlingIndex=ffffffff` jako wyłączenie; nie podaje zakresu, restartu ani czy 10 jest hex czy dziesiętnie. Streszczenie wyszukiwarki twierdziło o „zakresie 1-70 i restarcie”, ale **tego nie potwierdziło pobranie strony** – nie używać. Cała semantyka tej wartości (domyślna 10, 0xFFFFFFFF = wyłączone, „10 pakietów na ms”) jest więc **hintem, nie dowodem MS**.
- Stwierdzenie v3.2 „limit 80% przepustowości” nie ma źródła MS; MS mówi o CPU dla zadań niskiego priorytetu, nie o 80% łącza.
- Restart: dla `NetworkThrottlingIndex` niepotwierdzone w źródłach MS (Q&A nie podaje). MMCSS to usługa – kiedy czyta wartości (przy starcie usługi/wątku?) – niepotwierdzone.
- Działanie na Windows 10/11: strona MMCSS dotyczy Vista+ („Windows Server 2003 and Windows XP: MMCSS is not available”); czy `NetworkThrottlingIndex` jest nadal honorowany przez stos sieciowy 26H2 – **niepotwierdzone w źródłach MS**.

**Proponowana detekcja (zgodna z silnikiem):** `registry[]` – tylko `NetworkThrottlingIndex` DWord 0xFFFFFFFF (`original` po pomiarze VM). **Bez** wpisu `SystemResponsiveness` (wg MS 0 = 20, więc „Applied” po zapisie 0 mówiłoby nieprawdę o efekcie; poza tym zapis wartości, której MS nie honoruje, to właśnie fałszywy tweak). Bez parsowania tekstu.

**Undo:** snapshot z journala; jeśli wartości nie było – `<RemoveEntry>` (MS-Q&A: brak wartości zachowuje się jak domyślna – hint).

**Restart:** niepotwierdzone w źródłach MS; do pomiaru w teście `Reboot` na VM nie da się sprawdzić efektu sieciowego (brak sieci), tylko trwałość wartości.

**Ryzyka i ostrzeżenia:** experimental. Brak dokumentacji MS dla głównej wartości, brak mierzalnego skutku bez sieci. Opis v3.2 („80% przepustowości”, „GNS3, gry online”) nie może wejść. Q&A ostrzega, że zmiana może pogorszyć jakość multimediów (hint).

**Źródła**
- https://learn.microsoft.com/en-us/windows/win32/procthread/multimedia-class-scheduler-service – MS, 2026-10-18 (pobrana dwukrotnie; `SystemResponsiveness`: zaokrąglanie do 10, <10 i >100 → 20, 100 wyłącza MMCSS; `NetworkThrottlingIndex` NIE wymieniony; updated_at 2025-07-16)
- https://learn.microsoft.com/en-my/answers/questions/4324930/network-throttling-mechanism – MS-Q&A/hint, 2026-10-18 (odpowiedź anonimowa 2022-09-01: domyślnie 10, ffffffff)
- Brak źródła MS dla `NetworkThrottlingIndex` (zakres, domyślna, restart, działanie na 11/26H2).

**Otwarte pytania (VM)**
1. Czy `NetworkThrottlingIndex` i `SystemResponsiveness` istnieją na świeżym 26H2 Home i Pro, jakiego typu, z jaką wartością (oczekiwane z hintu: 10 i 20; do zmierzenia).
2. Czy po zapisie 0xFFFFFFFF wartość trwa po restarcie (`Reboot`); czy Windows jej nie nadpisuje.
3. Skutek sieciowy – VM bez sieci tego nie sprawdzi (status `Skipped`: „brak sieci”).
4. (ręcznie, poza VM) czy istnieje strona MS opisująca `NetworkThrottlingIndex`; jeśli nie, pozycja pozostaje bez źródła MS.

**Rekomendacja: nie portować** (domyślnie). Główna wartość nie ma źródła MS, druga (`SystemResponsiveness`=0) jest według MS clamped do 20, a opis v3.2 jest sprzeczny z MS. Gdyby autor chciał pozycję mimo to: portować z ograniczeniem – tylko `NetworkThrottlingIndex`=0xFFFFFFFF, experimental, ukryta domyślnie, opis „ustawia wartość z nieoficjalnych poradników; MS tego nie dokumentuje; skutek niezmierzony”, test VM tylko części rejestrowej.

---

## 2. net.ipv6-prefer-ipv4

**Co robiła v3.2:** nie było odpowiednika (tylko `net_ipv6`, pełne wyłączenie). Pozycja nowa w katalogu.

**Mechanizm wg MS** (MS Learn „Configure IPv6 for advanced users”, ms.date 2026-02-12, updated_at 2026-02-19; pobrana dwukrotnie, zgodnie)
- `HKLM\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters`, wartość **`DisabledComponents`**, **REG_DWORD**, „Min Value: 0x00 (default value)”, „Max Value: 0xFF (IPv6 disabled)”.
- **Prefer IPv4 over IPv6: dziesiętnie 32, hex 0x20, binarnie `xx1x xxxx`** – „Recommended instead of disabling IPv6”. Tabela bitów (od najniższego): Tunnel, Tunnel6to4, TunnelIsatap, Tunnel Teredo, Native (też PPP), PreferIpv4, TunnelCp, TunnelIpTls; 0x20 = tylko bit PreferIpv4.
- „We recommend using **Prefer IPv4 over IPv6** in prefix policies instead of disabling IPV6.” (MS wprost preferuje tę wartość nad wyłączaniem).
- **Restart: tak** – „You must restart your computer for these changes to take effect.”
- Uwaga MS: „Values other than 0 or 32 causes the Routing and Remote Access service to fail after this change takes effect.” (dotyczy usługi RRAS; na kliencie raczej bez znaczenia, ale warto w ostrzeżeniu).
- Weryfikacja wg MS: `ping` i `netsh interface ipv6 show prefixpolicies` (tekst zależny od języka i sieci – NIE do detekcji; na VM bez sieci `ping` nie ma sensu).
- Domyślnie wg tej strony: „By default, Windows favors IPv6 global unicast addresses over IPv4 addresses.” Czy wartość `DisabledComponents` istnieje na świeżym systemie – **niepotwierdzone w źródłach MS** (min/domyślna 0x00 sugeruje „0 lub brak”).

**Proponowana detekcja (zgodna z silnikiem):** `registry[]` `{ HKLM\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters, DisabledComponents, DWord, 0x20, original: 0 lub <RemoveEntry> po pomiarze }`. Applied = dokładnie 0x20. Ograniczenie silnika (równość): stan z innymi ustawionymi bitami, np. 0x21, jest `NotApplied`, mimo że bit „prefer IPv4” jest ustawiony; apply nadpisze całą wartość, snapshot ją zachowa. Realny efekt (kolejność adresów) tą drogą nie jest wykrywany – opis: „ustawia klucz wg MS”. Nie parsować `netsh`.

**Undo:** snapshot z journala (poprzednia wartość albo jej brak). MS opisuje „Prefer IPv6 over IPv4” jako bit `xx0x xxxx`, czyli powrót do 0x00.

**Restart:** tak (MS). `requiresReboot: true`.

**Ryzyka i ostrzeżenia:** caution. Zmiana kolejności preferencji adresów (nie wyłącza IPv6). Konflikt z `net.ipv6-disable`: oba piszą tę samą wartość (0x20 vs 0xFF) – w katalogu `conflicts` obustronnie. Ostrzeżenie RRAS z MS. Wpływ na aplikacje/DNS niezmierzony.

**Źródła**
- https://learn.microsoft.com/en-us/troubleshoot/windows-server/networking/configure-ipv6-in-windows – MS, 2026-10-18 (tabela wartości, typ, min/max, restart, rekomendacja; pobrana dwukrotnie)

**Otwarte pytania (VM)**
1. Czy `DisabledComponents` istnieje na świeżym 26H2 Home/Pro (typ, wartość) → ustala `original` (0 vs `<RemoveEntry>`).
2. Po zapisie 0x20 i restarcie: wartość trwa; czy `Get-NetIPv6Protocol`/`Get-NetPrefixPolicy` (CIM, bez tekstu) pokazuje zmianę priorytetu prefiksu `::ffff:0:0/96` – opcjonalnie jako dodatkowy `Check` w teście (prefiksy lokalne, niezależne od sieci; wynik tylko informacyjny, nie opiera się na MS).
3. Undo po restarcie: wartość i polityka prefiksów wracają do stanu sprzed.

**Rekomendacja: portować.** Wartość 0x20 jest wprost udokumentowana przez MS, ma typ, restart i rekomendację „zamiast wyłączania”. Prosta detekcja rejestrowa, pełne undo ze snapshotu. Ograniczenia opisać (równość wartości, brak wykrywania efektu).

---

## 3. net.ipv6-disable (alias v3.2 `net_ipv6`)

**Co robiła v3.2** (ok. linii 329-336): `New-ItemProperty ... Tcpip6\Parameters -Name DisabledComponents -Value 255 -PropertyType DWord -Force`; detekcja `DisabledComponents` == 255. Opis: „Odciąża DNS i zmniejsza overhead… Przyspiesza rozwiązywanie nazw”; ostrzeżenie: „ZAKAZ W KORPO: Intune i DirectAccess wymagają IPv6!”.

**Mechanizm wg MS** (ta sama strona co pozycja 2)
- **Disable IPv6: dziesiętnie 255, hex 0xFF, binarnie 1111 1111** = maksimum (`Max Value: 0xFF (IPv6 disabled)`). v3.2 używała poprawnej wartości 255.
- **0xFF vs 0xFFFFFFFF:** „system startup will be delayed for five seconds if IPv6 is disabled by incorrectly, setting the **DisabledComponents** registry setting to a value of 0xffffffff. The correct value should be 0xff.”
- Windows 7 SP1 / Server 2008 R2 SP1: opóźnienie startu opisane w KB3014406 (dotyczy starych systemów; dla 26H2 niepotwierdzone, link tylko z tej strony).
- **Odradzanie (MS wprost):** „Internet Protocol version 6 (IPv6) is a mandatory part of Windows Vista and Windows Server 2008 and newer versions. **We don't recommend that you disable IPv6 or IPv6 components or unbind IPv6 from interfaces. If you do, some Windows components might not function.**” Oraz: „You cannot completely disable IPv6 as IPv6 is used internally on the system for many TCPIP tasks. For example, you will still be able to run ping `::1` after configuring this setting.”
- Checkbox „Internet Protocol Version 6 (TCP/IPv6)” w właściwościach karty **nie** zmienia stanu po ustawieniu rejestru – „expected behavior”. Czyli GUI nie jest wskaźnikiem.
- Przykłady problemów (MS, sekcja Reference): kontrolery domeny (LDAP over UDP 389), Exchange 2010, klastry Failover (poprzez linki). Dla klienta Windows 11 konkretnych skutków strona nie wymienia. Ostrzeżenia v3.2 o Intune i DirectAccess **nie mają potwierdzenia w tej stronie MS** – zastąpić zdaniami z MS wyżej.
- **Restart: tak** (to samo zdanie co w pozycji 2). Uwaga o RRAS (wartości inne niż 0 lub 32) dotyczy też 0xFF.
- Alternatywa wg MS dla wyłączenia na karcie: `Disable-NetAdapterBinding -Name "<karta>" -ComponentID ms_tcpip6` (strona pisze `ms_tcpip[6]`; niejasny zapis – nie przenosić) – to osobny mechanizm, MS go też nie zaleca bez uzasadnienia („unsupported Windows configuration”). Poza zakresem.

**Proponowana detekcja (zgodna z silnikiem):** `registry[]` `{ Tcpip6\Parameters, DisabledComponents, DWord, 0xFF }`. Applied = dokładnie 0xFF. Wartość 0xFFFFFFFF (błąd z MS) dałaby `NotApplied` – prawda. Bez parsowania tekstu.

**Undo:** snapshot; jeśli wartości nie było – `<RemoveEntry>` (min. wartość MS 0x00 = domyślna; równoważność „brak” = 0 niepotwierdzona wprost – VM).

**Restart:** tak. `requiresReboot: true`.

**Ryzyka i ostrzeżenia:** experimental (jak w spec). Ostrzeżenia wyłącznie z MS: „MS nie zaleca wyłączania IPv6; niektóre składniki Windows mogą przestać działać; IPv6 nie da się wyłączyć całkowicie (loopback ::1 zostaje); GUI karty nadal pokazuje zaznaczony IPv6”. Opis v3.2 („przyspiesza DNS”) bez źródła MS – nie przenosić. Konflikt z `net.ipv6-prefer-ipv4` (ta sama wartość).

**Źródła**
- https://learn.microsoft.com/en-us/troubleshoot/windows-server/networking/configure-ipv6-in-windows – MS, 2026-10-18 (pobrana dwukrotnie; 0xFF, 0xffffffff → 5 s opóźnienia, „don't recommend”, restart)
- https://support.microsoft.com/help/3014406 – MS, wskazana przez stronę powyżej (opóźnienie startu Windows 7 SP1 / 2008 R2 SP1); nie pobierana osobno.

**Otwarte pytania (VM)**
1. Jak w pozycji 2: istnienie i domyślna wartość `DisabledComponents`.
2. Po 0xFF i restarcie: wartość trwa; start systemu bez opóźnienia (czas rozruchu – informacyjnie); `Get-NetAdapterBinding -ComponentID ms_tcpip6` (CIM, bez tekstu) – czy binding nadal Enabled (spodziewane wg MS: tak, checkbox nie reaguje).
3. Undo + restart: stan jak przed.
4. Wpływ na komponenty Windows (np. Store, Aktualizacje, Hyper-V Default Switch) – VM bez sieci tego nie wykaże; ewentualnie `Skipped`.

**Rekomendacja: portować z ograniczeniem.** Wartość 0xFF i restart są udokumentowane, a MS wprost odradza – dlatego experimental, ukryta domyślnie, ostrzeżenia dosłownie z MS, bez obietnic wydajności. Alternatywa: nie portować, skoro MS rekomenduje `net.ipv6-prefer-ipv4`; decyzja autora.

---

## 4. net.smb-guest-access (alias v3.2 `lan_godmode`)

**Co robiła v3.2** (ok. linii 362-370; skrypt jednolinijkowy): (1) `Set-NetConnectionProfile -NetworkCategory Private` (wszystkie profile), (2) dla `fdPHost, FDResPub, LanmanWorkstation, LanmanServer, SSDPSRV`: `Set-Service -StartupType Automatic` + `Start-Service`, (3) `HKLM:\SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters`: `AllowInsecureGuestAuth`=1 i `RequireSecuritySignature`=0 (DWord), (4) NetBIOS `SetTcpipNetbios(1)` na kartach z `IPEnabled`. Detekcja: tylko `AllowInsecureGuestAuth`==1. Opis: „Odblokowuje widoczność LAN w Windows 11 24H2… Insecure Guest Auth, rozbraja podpisywanie SMB. Komputery widoczne w sieci.” Uwaga: skrypt ruszał **tylko klienta** (`LanmanWorkstation`), nie serwera (`LanmanServer\Parameters`), mimo że opis mówi ogólnie o „podpisywaniu SMB”.

**Mechanizm wg MS**

*Dostęp gościa (klient SMB)*
- MS Learn „Enable insecure guest logons in SMB2 and SMB3” (ms.date 2025-08-13, updated_at 2026-02-16): od Windows 10 1709 klient SMB2/3 domyślnie nie pozwala na dostęp gościa ani fallbacku do konta Guest. Wg strony: „Windows 10 Home and Pro editions still allow the use of guest authentication by default” (zdanie dotyczy Windows 10); „In Windows 11 Pro Insider Preview build 25267, and all subsequent builds, guest credentials can't be used… by default”. Strona **nie mówi**, jaki jest stan domyślny Windows 11 **Home**. Strona „SMB security hardening” (MS, 2024-10-25): „By default, Windows 11 Pro no longer permits SMB client guest connections or guest fallback… This brings Windows 11 Pro in line with… Windows 10 and Windows 11 Enterprise, Education, and Pro for Workstation editions.” – czyli **Home nie jest tam wymieniony**; z tych dwóch stron wynika, że Home może nadal dopuszczać gościa, ale **bezpośrednio tego nie piszą** (niepotwierdzone w źródłach MS, patrz pomiar VM).
- **Jak włączyć wg MS:** Group Policy `Computer Configuration\Administrative Templates\Network\Lanman Workstation` → „Enable insecure guest logons” = Enabled, albo PowerShell `Set-SmbClientConfiguration -EnableInsecureGuestLogons $true -Force` (elevated). Strona cmdletu (MS, `SmbShare`): „-EnableInsecureGuestLogons: Indicates whether SMB client will allow insecure guest logons to an SMB server.” (Boolean). Cmdlet nie ma w opisie ograniczenia do edycji.
- **Klucz rejestru:** MS Learn **nie podaje** nazwy ani ścieżki klucza dla `AllowInsecureGuestAuth` ani informacji, który klucz zapisuje cmdlet (ani relacji polityka ↔ `Services\LanmanWorkstation\Parameters`). Wartość `AllowInsecureGuestAuth` = 1 w `HKLM\SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters` pochodzi **tylko z MS-Q&A/hint** (odpowiedź społeczności z 2024-05-27, „Rosy”, anonimowa; wątek techcommunity „Work Around” – autor „Copper Contributor”, nie pracownik MS). Dodatkowy hint z wyszukiwania: polityka zapisuje tę samą wartość pod `Software\Policies\...` – **nazwa wartości i ścieżka polityki niepotwierdzone w źródłach MS** (strona ADMX_LanmanWorkstation Policy CSP, którą pobrałem, wymienia `Software\Policies\Microsoft\Windows\LanmanWorkstation` jako klucz polityk Lanman Workstation, ale polityki „insecure guest logons” nie znalazłem w fragmencie, który się załadował). Tabela edycji na tej stronie dla innych polityk Lanman Workstation: Pro, Enterprise, Education, IoT Enterprise – **bez Home**.
- **Ostrzeżenia MS:** „enabling guest logons can pose a security threat”: spoofed server bez monitu o poświadczenia, ransomware, ataki AitM; „We recommend that you don't enable insecure guest logons.” Plus: „Guest logons don't support standard security features such as SMB signing and SMB encryption even if the SMB client is set to allow guest logons” oraz „Both SMB signing, and SMB encryption policies must be disabled in Group Policy in order to use guest logons.”
- Audyt (24H2+): zdarzenia 31017 (odrzucone), 31018 (administrator włączył), 31022 (dozwolone) w `Microsoft-Windows-SmbClient/Security`; cmdlet `-AuditInsecureGuestLogon` (24H2+). Do ewentualnej weryfikacji ręcznej, nie do detekcji (tekst).
- Siła, z jaką MS odradza: strona „Access Denied… SMB file share” (MS, ms.date 2026-07-09): „Do **not** re-enable insecure guest logons as a fix except as a temporary, risk-accepted workaround”. Rozwiązanie preferowane: poświadczenia.

*Podpisywanie SMB (klient)*
- MS „Control SMB signing behavior” (ms.date 2025-08-13, updated_at 2025-09-15): „**Windows 11, version 24H2 Enterprise, Pro, and Education require both outbound and inbound SMB signing.** Windows Server 2025 requires outbound SMB signing only. **Windows 11, version 24H2 Home edition doesn't require outbound or inbound SMB signing.**” Natomiast strona „SMB security hardening” mówi ogólnie: „Starting with Windows 11 24H2 and Windows Server 2025, all outbound and inbound SMB connections are now required to be signed by default” (bez rozróżnienia edycji), a strona funkcji „SMB features” (updated_at 2025-11-27) wymienia „SMB signing required by default” dla klienta Windows 11 24H2 bez edycji. **Dwa źródła MS są niezgodne co do Home** – do rozstrzygnięcia na VM (patrz otwarte pytania).
- „Requiring SMB signing also disables guest access to shares. In these cases, you must disable SMB signing manually to restore access for guest accounts.” (to samo źródło) oraz „Packets from guest users are rejected, which means that the feature… (guest fallback) is disabled.” (SMB security hardening). Czyli **na Pro/Enterprise/Education 24H2 samo włączenie gościa nie wystarcza – podpisywanie też musi być wyłączone.**
- Jak wyłączyć wg MS: Group Policy `Computer Configuration\Windows Settings\Security Settings\Local Policies\Security Options` → „Microsoft network client: Digitally sign communications (always)” = Disabled; PowerShell `Set-SmbClientConfiguration -RequireSecuritySignature $false` (klient, połączenia wychodzące) i `Set-SmbServerConfiguration -RequireSecuritySignature $false` (serwer, przychodzące). Weryfikacja wg MS: `Get-SmbClientConfiguration | FL RequireSecuritySignature` → True/False (wartość logiczna, niezależna od języka). „Caution: We don't recommend disabling SMB signing as a workaround for third-party servers. We also don't recommend trying to sign with guest accounts.”
- Klucz rejestru klienta: strona „Microsoft network client: Digitally sign communications (always)” (MS-archiwum, Windows 10, updated_at 2026-04-27) wymienia **`RequireSecuritySignature`** jako odpowiednik polityki i ostrzega: „Also don't use the **EnableSecuritySignature** registry settings” (dotyczą tylko SMB1) – ale w pobranej treści **nie podaje ścieżki klucza**. Ścieżka `...\Services\LanmanWorkstation\Parameters` pojawiła się w streszczeniu wyszukiwarki jako „z dokumentacji Windows Server”, ale nie potwierdziłem jej w pobranej treści – **[SPRAWDZIĆ RĘCZNIE]**. Tabela negocjacji z tej strony: podpisane, jeśli którakolwiek strona wymaga; domyślnie dla zwykłego ruchu „Not signed” (to opis sprzed 24H2). Domyślna wartość polityki: Disabled (stan archiwalny, sprzed 24H2).
- Restart: dla polityki podpisywania MS pisze: „None. Changes… become effective without a device restart”. Dla `AllowInsecureGuestAuth` restart niepotwierdzony w źródłach MS (hint z Q&A: restart po zmianie rejestru) – cmdlet `-Force` w przykładzie MS nie wspomina restartu; nowe połączenia SMB najpewniej wystarczą, ale to do VM/sieci.

*Usługi odnajdywania*
- MS „Security guidelines for system services in Windows Server 2016” (ms.date 2024-08-22, updated_at 2026-02-16; **serwer**, nie klient): `fdPHost` – opis: „hosts the Function Discovery (FD) network discovery providers… for the Simple Services Discovery Protocol (SSDP) and Web Services - Discovery (WS-D)… Stopping or disabling the FDPHOST service disables network discovery for these protocols”; **Startup type: Manual**, Recommendation: „No guidance”. `FDResPub` – „Publishes this computer and resources… so that other computers can discover them”; Manual, „No guidance”. `SSDPSRV` – Manual, „OK to disable”. `upnphost` – Manual, „OK to disable”. `LanmanWorkstation` – Automatic, „No guidance”. `LanmanServer` – Automatic, „Do not disable”, „Needed for remote management, IPC$, SMB file sharing”. `lmhosts` (TCP/IP NetBIOS Helper) – Manual, „No guidance”.
- **Żadne źródło MS nie mówi, że te usługi trzeba przestawić na Automatic, żeby komputery były widoczne w LAN na Windows 11 24H2.** MS Support (strona „Share a printer as a network printer”, podsumowanie wyszukiwarki, strona nie pobrana w całości): opcje „Network discovery” i „File and printer sharing” są domyślnie włączone dla sieci **Private**; sieć musi być ustawiona jako Private; ścieżka: Settings → Network & internet → Advanced network settings → Advanced sharing settings. Lista czterech usług jako „do ustawienia na Automatic” pochodzi wyłącznie z MS-Q&A (hint).
- Typ startu usług na kliencie 26H2 (Manual/trigger) – **niepotwierdzone w źródłach MS**, do zmierzenia na VM (jak SysMain w C2: tam serwer mówił Manual, a klient miał Auto).

*NetBIOS*
- Spec: „NetBIOS nie wchodzi, jeśli research nie pokaże, że jest potrzebny do widoczności w LAN na 24H2+”. **Research NIE pokazuje potrzeby.** MS „SMB security hardening”: „The SMB NetBIOS ports are no longer included in the built-in firewall rules. These ports were only required for SMB1 usage, which is now deprecated and removed by default.” oraz „SMB firewall rule hardening” (SMB features): grupa „File and Printer Sharing (Restrictive)” „no longer permits inbound NetBIOS ports 137-139”. Odnajdywanie przez `fdPHost` działa po SSDP i WS-D (opis usługi wyżej), nie po NetBIOS. Dodatkowo: v3.2 w `lan_godmode` ustawiała `SetTcpipNetbios(1)` (EnableNetbios), a w `lan_panic` `SetTcpipNetbios(0)` (= „via DHCP”, a nie „disable”, które ma wartość 2 wg MS: 0 = EnableNetbiosViaDhcp, 1 = EnableNetbios, 2 = DisableNetbios; źródło: strona `Win32_NetworkAdapterConfiguration` / `SetTcpipNetbios`, MS, z wyników wyszukiwania; opis „twarde zabetonowanie” w `lan_panic` był więc nieprawdziwy). Wniosek: NetBIOS poza katalogiem partii 3.

*Edycje Home vs Pro (podsumowanie wg MS)*
- Podpisywanie: źródła MS niezgodne co do Home (jedno mówi, że Home nie wymaga; drugie, że wszystkie edycje wymagają).
- Gość: MS wymienia domyślne blokowanie dla Pro (11), Enterprise, Education, Pro for Workstations; Home – brak jednoznacznego zdania.
- Polityki Group Policy: `gpedit` nie ma w Home (wątek techcommunity, hint; autor niebędący pracownikiem MS). Cmdlety `Set-SmbClientConfiguration` są w module `SmbShare`; strona cmdletu nie ogranicza do edycji. Rejestr działa niezależnie od `gpedit`.

**Proponowany zakres (reguła: jeden tweak = jeden scope; rejestr i usługi przez API C#)**
Rekomendacja: **rozbić na dwa tweaki** (+ opcjonalnie trzeci), bo MS opisuje je jako osobne ustawienia, a podpisywanie jest potrzebne także bez gościa (NAS z poświadczeniami, który nie wspiera podpisywania):
- **A. `net.smb-guest-access`** – dostęp gościa po stronie klienta. `registry[]`: `LanmanWorkstation\Parameters` `AllowInsecureGuestAuth` DWord 1 (ścieżka/klucz z hintu; wymaga potwierdzenia na VM, że cmdlet MS zapisuje dokładnie ten klucz i że polityka go nie przesłania). Realny stan: `script.detect` (PS 5.1) `(Get-SmbClientConfiguration).EnableInsecureGuestLogons` → `Applied`/`NotApplied` (wartość logiczna, bez tekstu) – zgodny z rejestrem tylko, jeśli VM potwierdzi, że cmdlet i klucz opisują ten sam stan; w przeciwnym razie `script.detect` jest wiążący, rejestr jako `detectOnly`/apply. Ostrzeżenie: MS wprost odradza; na 24H2 Pro/Ent/Edu bez wyłączonego podpisywania połączenie z gościem i tak nie zadziała (patrz B).
- **B. `net.smb-signing-client-off`** (nowa pozycja, do zatwierdzenia przez autora) – klient SMB: `RequireSecuritySignature` DWord 0 w `LanmanWorkstation\Parameters` (nazwa wartości potwierdzona jako odpowiednik polityki w MS-archiwum; ścieżka – [SPRAWDZIĆ RĘCZNIE]); realny stan `Get-SmbClientConfiguration | RequireSecuritySignature` (boolean). Ostrzeżenie dosłownie z MS: „We don't recommend disabling SMB signing…”. Strona serwerowa (`LanmanServer`, inbound) – poza zakresem (v3.2 jej nie ruszała); decyzja autora, czy ma wejść do B jako drugi wpis.
- **C. Usługi odnajdywania** (`fdPHost`, `FDResPub`, `SSDPSRV`, `upnphost`): **nie wchodzą** do katalogu na tym etapie. Brak źródła MS, że ich typ startu blokuje widoczność w LAN; na świeżym 26H2 mogą być już Manual-z-wyzwalaczem i działać po włączeniu odnajdywania. Wrócą tylko, jeśli VM (po włączeniu Network discovery w Private) pokaże, że usługi nie startują. Jeśli wejdą: `services[]` z `startType: Automatic` (+ `stop: false`), `original` z pomiaru VM, każda osobno w agregacji (`Partial` przy mieszance).
- `LanmanWorkstation`/`LanmanServer` (v3.2 ustawiała Automatic): wg tabeli MS już Automatic domyślnie – wpis byłby no-opem, pominąć.
- NetBIOS: poza zakresem (uzasadnienie wyżej).

**Undo:** snapshot z journala dla wpisów rejestru (A i B); jeśli wartości nie było, `<RemoveEntry>` – ale **uwaga**: na Pro 24H2 brak wartości może oznaczać „podpisywanie wymagane z wbudowanego domyślnego”, a po `Set-SmbClientConfiguration` MS-owy cmdlet może zapisywać wartość jawnie. Czy `<RemoveEntry>` przywraca stan zgodny z domyślnym (a nie z „0”) – **niepotwierdzone w źródłach MS**, VM.

**Restart:** podpisywanie – nie (MS: polityka bez restartu). Gość – niepotwierdzone w źródłach MS (nie dodawać `requiresReboot` bez pomiaru).

**Ryzyka i ostrzeżenia:** caution (spec). Dosłownie z MS: spoofed server, ransomware, AitM; guest nie wspiera podpisywania/szyfrowania; zalecenie, by nie włączać; preferowane poświadczenia; podpisywanie – „don't recommend disabling”. Na cudzym PC w obcej sieci obniżenie tych ustawień jest realnym ryzykiem – ostrzeżenie w opisie, potwierdzenie w UI (spec). Skutek „komputery widoczne w sieci” z opisu v3.2 – nie obiecywać (MS: widoczność to profil Private + discovery, nie gość).

**Źródła**
- https://learn.microsoft.com/en-us/windows-server/storage/file-server/enable-insecure-guest-logons-smb2-and-smb3 – MS, 2026-10-18 (domyślne, GPO „Enable insecure guest logons”, `Set-SmbClientConfiguration -EnableInsecureGuestLogons $true -Force`, ostrzeżenia, audyt; Win10 Home/Pro vs Win11 Pro; podpisywanie wymagane na 24H2)
- https://learn.microsoft.com/en-us/windows-server/storage/file-server/smb-signing – MS, 2026-10-18 (wymagania per edycja, wyłączanie, `Get-SmbClientConfiguration`; „Requiring SMB signing also disables guest access”)
- https://learn.microsoft.com/en-us/windows-server/storage/file-server/smb-security-hardening – MS, 2026-10-18 (podpisywanie wymagane domyślnie, gość wyłączony dla Pro, porty NetBIOS tylko dla SMB1)
- https://learn.microsoft.com/en-us/windows-server/storage/file-server/smb-feature-descriptions – MS, 2026-10-18 (tabela: „SMB signing required by default” – klient Windows 11 24H2; „Guest access disabled”; firewall rule hardening 137-139)
- https://learn.microsoft.com/en-us/troubleshoot/windows-client/networking/access-denied-access-smb-file-share – MS, 2026-10-18 (ms.date 2026-07-09; zalecenie „do not re-enable insecure guest logons”)
- https://learn.microsoft.com/en-us/powershell/module/smbshare/set-smbclientconfiguration?view=windowsserver2025-ps – MS, 2026-10-18 (parametry `EnableInsecureGuestLogons`, `RequireSecuritySignature`, `Force`)
- https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-10/security/threat-protection/security-policy-settings/microsoft-network-client-digitally-sign-communications-always – MS-archiwum, 2026-10-18 (polityka podpisywania klienta, odpowiednik `RequireSecuritySignature`, brak restartu; ścieżka klucza nie w pobranej treści)
- https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-admx-lanmanworkstation – MS, 2026-10-18 (klucz polityk `Software\Policies\Microsoft\Windows\LanmanWorkstation`; tabela edycji bez Home dla pokazanych polityk; polityka „insecure guest logons” nie znaleziona w załadowanym fragmencie)
- https://learn.microsoft.com/en-us/windows-server/security/windows-services/security-guidelines-for-disabling-system-services-in-windows-server – MS (Server 2016), 2026-10-18 (fdPHost, FDResPub, SSDPSRV, upnphost, LanmanWorkstation/Server, lmhosts – typy startu i rekomendacje; dotyczy serwera)
- https://learn.microsoft.com/en-us/answers/questions/2189515/windows-11-24h2-and-insecure-guest-logins-settings – MS-Q&A/hint, 2026-10-18 (klucz `LanmanWorkstation\Parameters\AllowInsecureGuestAuth`; odpowiedź społeczności; najpomocniejsza odpowiedź 2024-10-09: `Set-SmbClientConfiguration -RequireSecuritySignature $false`)
- https://techcommunity.microsoft.com/discussions/windows11/accessing-a-third-party-nas-with-smb-in-windows-11-24h2-may-fail-work-around/4391526 – MS-Q&A/hint, 2026-10-18 (wątek społeczności, autor „Copper Contributor”: ścieżka klucza, `gpedit` na Home – uwaga)
- https://techcommunity.microsoft.com/blog/filecab/accessing-a-third-party-nas-with-smb-in-windows-11-24h2-may-fail/4154300 – MS-blog, 2026-10-18 (**tylko tytuł; treść niepobrana**; streszczenie wyszukiwarki twierdziło o domyślnym gościu na Home i o poleceniach PS – niezweryfikowane, **[SPRAWDZIĆ RĘCZNIE]**)
- https://support.microsoft.com/en-us/windows/hardware/printer/share-a-printer-as-a-network-printer – MS, 2026-10-18 (tylko ze streszczenia wyszukiwarki: Network discovery/File and printer sharing domyślnie włączone w sieci Private; **[SPRAWDZIĆ RĘCZNIE]**)
- hint: https://learn.microsoft.com/en-us/answers/questions/1462771/enabling-smb-guest-access-via-registry-breaks-the – MS-Q&A (nadpisanie klucza `Parameters` przez `New-Item -Force` zepsuło usługę Workstation; ostrzeżenie dla skryptów – nigdy nie `New-Item -Force` na tym kluczu).

**Otwarte pytania (VM)** (wszystkie da się zmierzyć bez sieci, odczytem; efekt realny w LAN – nie)
1. `Get-SmbClientConfiguration`: `EnableInsecureGuestLogons`, `RequireSecuritySignature` (+ `EnableSecuritySignature`, `AuditInsecureGuestLogon`) na świeżym 26H2 **Home i Pro** – rozstrzyga niezgodność MS co do Home (podpisywanie) i brak zdania o gościu na Home.
2. Czy w `HKLM\SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters` istnieją `AllowInsecureGuestAuth` i `RequireSecuritySignature` na świeżym systemie (typ, wartość); co zapisuje `Set-SmbClientConfiguration -EnableInsecureGuestLogons $true -Force` (klucz, typ) – test na VM bez sieci.
3. Czy polityka Group Policy (jeśli ustawiona w teście) tworzy `AllowInsecureGuestAuth` pod `Software\Policies\Microsoft\Windows\LanmanWorkstation` i czy `Get-SmbClientConfiguration` ją odzwierciedla (Pro; Home bez gpedit – pominięte).
4. Typ startu i stan `fdPHost`, `FDResPub`, `SSDPSRV`, `upnphost`, `lmhosts` (Win32_Service: StartMode, State, DelayedAutoStart, `TriggerInfo` jeśli dostępne przez API) na świeżym 26H2 Home i Pro, w profilu bez sieci.
5. Po apply A/B (przez silnik) i `Get-SmbClientConfiguration` – zgodność z rejestrem; po undo – stan jak przed (`RemoveEntry` vs jawne 0).
6. Czy zmiana działa bez restartu (czy `Get-SmbClientConfiguration` zmienia wartość od razu) – bez restartu.
7. Skutek w LAN (widoczność, połączenie z gościem) – **niemożliwy na VM bez sieci** → `Skipped: brak sieci`; w docs „nie testowane na VM: brak sieci/serwera SMB”.
8. Treść wpisu techcommunity 4154300 – przeczytać ręcznie (przeglądarka).

**Rekomendacja: portować z ograniczeniem** (jako rozbite A + B). Gość i podpisywanie mają udokumentowane przez MS mechanizmy (cmdlety, polityki, zależność „podpisywanie wyłącza gościa”), ale: (1) klucz rejestru `AllowInsecureGuestAuth` jest tylko hintem Q&A, (2) MS wprost odradza obie zmiany, (3) stan domyślny Home jest niejasny w źródłach MS, (4) usługi odnajdywania i NetBIOS nie mają dowodu MS, że są potrzebne – wypadają. Oba tweaki caution, z ostrzeżeniami dosłownie z MS i detekcją realnego stanu przez `Get-SmbClientConfiguration` (boolean) obok rejestru.

---

## 5. net.smb-harden (alias v3.2 `lan_panic`, kind=action)

**Co robiła v3.2** (ok. linii 372-380): `Set-NetConnectionProfile -NetworkCategory Public` (wszystkie profile); `fdPHost, FDResPub, SSDPSRV`: `Set-Service -StartupType Manual` + `Stop-Service`; `AllowInsecureGuestAuth`=0, `RequireSecuritySignature`=1 (`LanmanWorkstation\Parameters`); NetBIOS `SetTcpipNetbios(0)`. Opis: „Przywraca całkowite zabetonowanie… Wyłącza guest auth, wymusza podpisywanie SMB, zatrzymuje discovery. Przydatne przed wydaniem laptopa pracownikowi.”

**Mechanizm wg MS**
- Wartości, które MS podaje jako bezpieczne/domyślne na 24H2+:
  - Dostęp gościa klienta: **wyłączony** (`EnableInsecureGuestLogons` = `$false`; „Windows disables insecure guest logons by default. We recommend that you don't enable insecure guest logons”). Domyślnie wyłączony dla Windows 11 Pro/Enterprise/Education/Pro for Workstations (MS, patrz pozycja 4); dla Home – niepotwierdzone w źródłach MS.
  - Podpisywanie klienta: **wymagane** – `Set-SmbClientConfiguration -RequireSecuritySignature $true` (komenda dosłownie z MS „Control SMB signing behavior”, sekcja „Enable SMB signing”); serwer: `Set-SmbServerConfiguration -RequireSecuritySignature $true`. Domyślnie wymagane na Windows 11 24H2 **Pro/Enterprise/Education** (wg „Control SMB signing behavior”); **na Home 24H2 MS (ta sama strona) mówi: „doesn't require outbound or inbound SMB signing”**, ale strona „SMB security hardening” mówi o wszystkich edycjach – **niezgodność w źródłach MS**.
  - Zalecenie MS (polityka): „Best practice: Enable Microsoft network client: Digitally sign communications (always)” (MS-archiwum).
- **Konsekwencja dla Home:** ustawienie `RequireSecuritySignature=$true` na Home (jeśli MS-Home nie wymaga domyślnie) jest **zaostrzeniem względem domyślnego stanu**, nie „przywróceniem” – może zablokować połączenia z urządzeniami bez podpisywania (komunikaty z MS: `STATUS_INVALID_SIGNATURE`, „security policies block unauthenticated guest access”).
- Elementy v3.2 poza SMB (profil Public, usługi Manual + Stop, NetBIOS 0): MS nie podaje, że ma to być część „zabezpieczenia”; typ startu usług odnajdywania Manual to ich domyślna wartość wg tabeli **serwerowej** (klient niezmierzony); zatrzymanie usług odnajdywania pogarsza funkcję bez korzyści udokumentowanej; profil Public jest MS-owym domyślnym dla niezidentyfikowanych sieci. Zmiana wszystkich profili na Public to efekt uboczny – **nie przenosić**.
- Brak journala i undo: `TweakEngine.Execute` (kod) – akcja nie zapamiętuje stanu sprzed. Cofnięcie hardeningu = osobne działanie; dla stanu „co było przed” nie ma źródła.

**Proponowany skrypt (kind=action, `-File`, PS 5.1)** – tylko 2 ustawienia klienta SMB wg MS: `Set-SmbClientConfiguration -EnableInsecureGuestLogons $false -RequireSecuritySignature $true -Force` przez funkcję pomocniczą (mockowalną), po czym odczyt zwrotny `Get-SmbClientConfiguration` (boolean) i kod wyjścia ≠0, gdy nie zgadza się z oczekiwanym. Bez zmiany profilu, usług i NetBIOS. Opcjonalnie (decyzja autora) strona serwerowa `Set-SmbServerConfiguration -RequireSecuritySignature $true`. Wiersz stanu w UI: akcja nie ma detekcji (kind=action), więc jedyny dowód to kod wyjścia i komunikat.

**Undo:** brak z założenia (spec: „bez journala”). Opis musi to powiedzieć wprost; kto użył wcześniej `net.smb-guest-access`/`net.smb-signing-client-off`, dostanie rozjazd z journalem tych tweaków (ich `undo` przywróci snapshot sprzed ich apply, nadpisując hardening). To realne ryzyko spójności – ostrzeżenie + decyzja.

**Restart:** nie (MS dla polityki podpisywania: bez restartu).

**Ryzyka i ostrzeżenia:** caution zamiast `safe` w spec – wymuszenie podpisywania na Home (jeśli nie jest tam domyślne) i nadpisanie stanu tweaków z journala. Opis v3.2 („zabetonowanie usług sieciowych”) nieprawdziwy względem tego, co robi bezpieczna wersja – zmienić.

**Źródła**
- https://learn.microsoft.com/en-us/windows-server/storage/file-server/smb-signing – MS, 2026-10-18 (`Set-SmbClientConfiguration -RequireSecuritySignature $true`, `Set-SmbServerConfiguration …`, weryfikacja; wymagania per edycja)
- https://learn.microsoft.com/en-us/windows-server/storage/file-server/smb-security-hardening – MS, 2026-10-18
- https://learn.microsoft.com/en-us/windows-server/storage/file-server/enable-insecure-guest-logons-smb2-and-smb3 – MS, 2026-10-18 (gość wyłączony domyślnie, zalecenie, by nie włączać)
- https://learn.microsoft.com/en-us/powershell/module/smbshare/set-smbclientconfiguration?view=windowsserver2025-ps – MS, 2026-10-18
- https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-10/security/threat-protection/security-policy-settings/microsoft-network-client-digitally-sign-communications-always – MS-archiwum, 2026-10-18 (best practice: Enabled)

**Otwarte pytania (VM)**
1. Stan domyślny `EnableInsecureGuestLogons`/`RequireSecuritySignature` na świeżym 26H2 Home i Pro (jak w pozycji 4, pyt. 1) – rozstrzyga, czy hardening na Home to „przywrócenie” czy zaostrzenie.
2. Czy `Set-SmbClientConfiguration … -Force` działa na VM bez sieci i bez uruchomionych usług odnajdywania (kod wyjścia 0, odczyt zwrotny zgodny).
3. Czy po `Execute` na VM stan odczytany zgodny; test `Execute`: `Check` = odczyt `Get-SmbClientConfiguration` (boolean) → `$true`; `Snapshot` = klucze `LanmanWorkstation\Parameters` przed/po (bo akcja nie ma undo, `Cleanup` przywraca wartości z odczytu przed). Zgodnie z zasadami repo `Snapshot` nie zastępuje `Check`.
4. Efekt w LAN – `Skipped: brak sieci`.

**Rekomendacja: portować z ograniczeniem.** Tylko klient SMB (gość off, podpisywanie wymagane) – wartości i polecenia wprost z MS; bez profilu, usług i NetBIOS (nie są „hardeningiem” według MS). Ryzyko: caution (nie `safe`), brak undo jawnie w opisie, odczyt zwrotny jako dowód. Decyzja autora: czy akcja ma dotykać też serwera (`LanmanServer`).

---

## 6. net.network-private (kind=action, decyzja autora w specyfikacji)

**Co robiła v3.2:** pierwszy krok `lan_godmode`: `Set-NetConnectionProfile -NetworkCategory Private` (bez filtru – wszystkie profile połączeń).

**Mechanizm wg MS**
- `Set-NetConnectionProfile` (MS, moduł `NetConnection`): „changes the network category setting of a connection profile”. `-NetworkCategory`: `Public` – „Your PC is hidden from other devices on the network and can't be used for printer and file sharing”; `Private` – „Your PC is discoverable and can be used for printer and file sharing if you set it up”; `DomainAuthenticated` – sieć dołączona do domeny. „**You cannot set the DomainAuthenticated type by using this cmdlet**” – ustawia je system po uwierzytelnieniu w domenie. Filtry: `-Name`, `-InterfaceAlias`, `-InterfaceIndex`, `-IPv4Connectivity` (Disconnected, NoTraffic, Subnet, LocalNetwork, Internet), `-IPv6Connectivity`; wejście potokiem z `Get-NetConnectionProfile` (`-InputObject`, CimInstance). `-PassThru` zwraca obiekt.
- `Get-NetConnectionProfile`: „gets a connection profile associated with one or more physical network adapters”; wartości `NetworkCategory` jak wyżej (enum o nazwach angielskich, niezależnych od języka systemu, gdy czytane jako właściwość/enum, nie jako sformatowany tekst).
- Zapora: MS „Windows Firewall Overview” (updated_at 2026-10-08): trzy profile (domain/private/public); reguły przypisywane per profil („you can allow a specific application to communicate on a private network, but not on a public network”); „The *private network* profile… can be set manually on a network interface by an administrator”; domena „cannot be set manually”; Public „default profile for unidentified networks”; „Use the PowerShell cmdlet `Get-NetConnectionProfile` to retrieve the active network category (`NetworkCategory`). Use… `Set-NetConnectionProfile` to switch the category between private and public.” Czyli zmiana na Private przełącza na zestaw reguł profilu Private (zwykle luźniejszy – które konkretnie reguły są włączone, MS na tej stronie nie wymienia; **niepotwierdzone w źródłach MS**).
- Odnajdywanie: MS Support (podsumowanie wyszukiwarki, strona nie pobrana w całości): Network discovery i File and printer sharing są domyślnie włączone dla sieci Private – **[SPRAWDZIĆ RĘCZNIE]**.
- **Klasa CIM:** cmdlet MS ma `external help file: MSFT_NetConnectionProfile.cdxml` – czyli wrapper nad klasą `MSFT_NetConnectionProfile`. Strony referencyjnej klasy w MS Learn nie znalazłem (adres z previous-versions → 404; wyszukiwanie zwróciło tylko inne klasy `root\StandardCimv2`), więc **nazwa przestrzeni nazw `root\StandardCimv2` i wartości liczbowe `NetworkCategory` (0/1/2) są niepotwierdzone w źródłach MS dla tej klasy** (hint z innych klas MS w tej samej przestrzeni: `MSFT_NetRoute`, `MSFT_NetFirewallRule`). Wątek MS-Q&A (hint): odczyt `MSFT_NetConnectionProfile` przez C# WMI w `ROOT\StandardCimv2` rzucił „Provider load failure” na jednym Windows 10, a PowerShell działał. Czyli detekcja przez .NET `ManagementObjectSearcher` jest ryzykowna – bezpieczniej cmdlet w PS 5.1 z wynikiem jako enum (`[string]$p.NetworkCategory`).
- **Brak sieci (VM):** MS-Q&A (hint): przy braku zgodnego profilu `Set-NetConnectionProfile` z filtrami kończy się błędem „no matches found”; wirtualne adaptery bez profilu często nie pojawiają się w ogóle w `Get-NetConnectionProfile`. Dokumentacja MS cmdletu **nie opisuje** wyniku dla pustej listy – **niepotwierdzone w źródłach MS** → pomiar VM: `Get-NetConnectionProfile` na VM bez karty/bez sieci (liczba obiektów; błąd lub pusto), z kartą bez kabla, itp.
- Polityka: wątek MS-Q&A (hint): „Network List Manager Policies” (Group Policy) może blokować zmianę kategorii przez użytkownika; MS firewall overview wskazuje NetworkListManager Policy CSP dla urządzeń zarządzanych. Zachowanie `Set-NetConnectionProfile` pod tą polityką – niepotwierdzone w źródłach MS.

**Proponowana detekcja i skrypt (kind=action, `-File`, PS 5.1):** akcja nie ma detekcji/stanu w silniku. Skrypt: `Get-NetConnectionProfile` → wybór profili z `NetworkCategory -eq 'Public'` (porównanie z enumem, nie z tekstem) i `IPv4Connectivity`/`IPv6Connectivity` ≠ `Disconnected` → `Set-NetConnectionProfile -InputObject … -NetworkCategory Private` (przez funkcję pomocniczą mockowalną); `DomainAuthenticated` pomijany; odczyt zwrotny kategorii; wypisanie na stdout liczby zmienionych profili i ich nazw. **Wynik, gdy brak profili:** decyzja potrzebna – kod 0 oznaczałby „Executed” (zielone) mimo braku efektu (sprzeczne z anty-celem „zielony bez sprawdzenia”); proponuję kod wyjścia ≠0 z jasnym komunikatem „brak aktywnego połączenia sieciowego” (silnik pokaże `Failed` + stderr), a w teście VM jawny `Skip` z powodem, gdy VM nie ma sieci.

**Undo:** brak (akcja). Ręczne cofnięcie: `Set-NetConnectionProfile … -NetworkCategory Public` (komenda z MS); skrypt może wypisać poprzednią kategorię w wyjściu, ale nie zapisuje jej w journalu.

**Restart:** nie (MS nie podaje; kategoria jest zmieniana na żywo – niepotwierdzone wprost).

**Ryzyka i ostrzeżenia:** caution (spec). Zmiana na Private otwiera zestaw reguł zapory dla profilu Private i czyni PC „discoverable” (MS) – na cudzej/obcej sieci to obniżenie ochrony; ostrzeżenie dosłownie z MS: „Private: networks at home or work, where you know and trust the people and devices on the network”. Potwierdzenie w UI (spec sekcja 7). Nie zmieniać `DomainAuthenticated`. Wskazanie profilu: tylko aktywne połączenia, nie wszystkie wirtualne adaptery (VPN, Hyper-V) – v3.2 zmieniała wszystkie.

**Źródła**
- https://learn.microsoft.com/en-us/powershell/module/netconnection/set-netconnectionprofile – MS, 2026-10-18 (opis, `-NetworkCategory`, „cannot set DomainAuthenticated”, parametry, `-InputObject`)
- https://learn.microsoft.com/en-us/powershell/module/netconnection/get-netconnectionprofile?view=windowsserver2025-ps – MS, 2026-10-18
- https://learn.microsoft.com/en-us/windows/security/operating-system-security/network-security/windows-firewall/ – MS, 2026-10-18 (profile zapory, Private można ustawić ręcznie, domena nie, Get/Set-NetConnectionProfile; updated_at 2026-10-08)
- https://learn.microsoft.com/en-us/answers/questions/4308798/powershell-set-netconnectionprofile-ipv4connectivi – MS-Q&A/hint, 2026-10-18 (błąd „not found” przy filtrach; tylko ze streszczenia wyszukiwarki)
- https://learn.microsoft.com/en-us/answers/questions/4337471/c-wmi-how-to-fix-managementexception-provider-load – MS-Q&A/hint, 2026-10-18 (tylko streszczenie wyszukiwarki)
- Brak strony MS referencyjnej klasy `MSFT_NetConnectionProfile` (nie znaleziona 2026-10-18).

**Otwarte pytania (VM)**
1. `Get-NetConnectionProfile` na świeżej VM bez karty / z kartą bez sieci: liczba profili, kod wyjścia, czy cmdlet zgłasza błąd; jak wygląda `NetworkCategory` dla „Unidentified network” (jeśli pojawia się) – na VM bez sieci nie da się zmienić realnego profilu.
2. Czy `Set-NetConnectionProfile -NetworkCategory Private` przechodzi na VM, jeśli profil istnieje (np. po podłączeniu prywatnego przełącznika Hyper-V „Internal/Private” – to zmiana konfiguracji VM, poza zakresem researchu; ewentualnie `Skipped`).
3. Czy odczyt przez `[string]$p.NetworkCategory` jest `Public`/`Private`/`DomainAuthenticated` na polskim systemie (enum, nie tłumaczony) – VM `pl-PL`.
4. Przestrzeń nazw i wartości liczbowe `MSFT_NetConnectionProfile.NetworkCategory` (jeśli planujemy odczyt CIM zamiast cmdletu) – odczyt na VM (`Get-CimInstance -Namespace root\StandardCimv2 -ClassName MSFT_NetConnectionProfile`), bez zmian.
5. Efekt na zaporę (reguły włączone dla Private vs Public) – `Get-NetFirewallRule`/`Get-NetFirewallProfile` (CIM) przed i po – wymaga istniejącego profilu; na VM bez sieci `Skipped`.
6. Reakcja pod „Network List Manager Policies” – poza VM (ręcznie lub `Skipped`).

**Rekomendacja: portować z ograniczeniem.** Mechanizm (`Set-NetConnectionProfile`) i semantyka kategorii są udokumentowane przez MS; ale (1) akcja nie ma stanu ani undo, (2) wynik „brak sieci” nie ma pewnego kontraktu i wymaga decyzji (kod ≠0), (3) na VM bez sieci nie da się sprawdzić efektu – tylko ścieżkę „brak profili”, (4) skutek (otwarcie zapory/odnajdywania) trzeba w opisie nazwać wprost. Dotyczyć tylko aktywnych profili Public, nigdy DomainAuthenticated.

---

## Tabela zbiorcza

| Pozycja | Mechanizm udokumentowany przez MS | Detekcja (bez tekstu) | Undo | Restart | Rekomendacja | Wymaga sprawdzenia na VM |
|---|---|---|---|---|---|---|
| net.throttling-index | `SystemResponsiveness` tak (CPU; 0 → clamped do 20); `NetworkThrottlingIndex` brak w dokumentacji MS | `registry[]` `NetworkThrottlingIndex`=0xFFFFFFFF (bez `SystemResponsiveness`) | snapshot / `<RemoveEntry>` | niepotwierdzone | nie portować (alt.: tylko NTI, experimental) | domyślne wartości, trwałość po restarcie |
| net.ipv6-prefer-ipv4 | tak: 0x20, DWORD, restart, „recommended instead of disabling” | `registry[]` `DisabledComponents`==0x20 (równość) | snapshot / `<RemoveEntry>` lub 0 | tak (MS) | portować | istnienie/wartość domyślna, efekt na `Get-NetPrefixPolicy` |
| net.ipv6-disable | tak: 0xFF, DWORD, restart, „don't recommend”, 0xffffffff → 5 s | `registry[]` `DisabledComponents`==0xFF | snapshot / `<RemoveEntry>` | tak (MS) | portować z ograniczeniem (experimental) | domyślna wartość, trwałość, binding ms_tcpip6 |
| net.smb-guest-access (A) + `net.smb-signing-client-off` (B, nowa) | cmdlety i polityki: tak; klucz `AllowInsecureGuestAuth`: tylko hint; ścieżka `RequireSecuritySignature`: do potwierdzenia; Home niejasny | `registry[]` + `script.detect` (`Get-SmbClientConfiguration`, boolean) | snapshot | gość: niepotwierdzone; podpisywanie: nie | portować z ograniczeniem (rozbić; bez usług i NetBIOS) | stan domyślny Home/Pro, co zapisuje cmdlet, efekt w LAN niemożliwy na VM |
| net.smb-harden | tak: cmdlety i wartości MS (gość off, podpisywanie wymagane) | brak (akcja); odczyt zwrotny w skrypcie | brak (z założenia) | nie | portować z ograniczeniem (caution, tylko klient SMB) | stan domyślny Home, kod wyjścia bez sieci |
| net.network-private | tak: `Set-NetConnectionProfile`, Private/Public, domena nie | brak (akcja); enum z `Get-NetConnectionProfile` w skrypcie | brak | nie | portować z ograniczeniem | wynik bez sieci, enum na pl-PL, klasa CIM |

## Otwarte pytania dla eksploracji VM (zbiorczo)

Bez sieci na VM nie da się zmierzyć efektu sieciowego żadnej z 6 pozycji; mierzy się stan rejestru/CIM/cmdletów, trwałość po restarcie i ścieżki „brak sieci”. Wszystkie pomiary wyłącznie na `DSO-26H2-Home` i `DSO-26H2-Pro`, tylko odczyt przed zmianą.
1. **Rejestr (Multimedia\SystemProfile):** `NetworkThrottlingIndex` i `SystemResponsiveness` – istnienie, typ, wartość, Home vs Pro.
2. **Rejestr (Tcpip6\Parameters):** `DisabledComponents` – istnienie, typ, wartość; po 0x20 i 0xFF + restart trwałość; `Get-NetPrefixPolicy` i `Get-NetAdapterBinding -ComponentID ms_tcpip6` (CIM) przed/po jako informacja.
3. **SMB klient:** `Get-SmbClientConfiguration` (`EnableInsecureGuestLogons`, `RequireSecuritySignature`, `EnableSecuritySignature`) na Home i Pro – rozstrzyga niezgodność MS (podpisywanie na Home) i domyślny gość na Home.
4. **Rejestr LanmanWorkstation\Parameters:** `AllowInsecureGuestAuth`, `RequireSecuritySignature` – istnienie, typ; co zapisują `Set-SmbClientConfiguration -EnableInsecureGuestLogons $true/$false -Force` i `-RequireSecuritySignature` (uruchomione tylko na VM); zgodność z `Get-SmbClientConfiguration` po apply i po undo (`RemoveEntry` vs jawne 0/1).
5. **Usługi:** `fdPHost`, `FDResPub`, `SSDPSRV`, `upnphost`, `lmhosts`, `LanmanWorkstation`, `LanmanServer` – StartMode, State, DelayedAutoStart, zależności; czy są wyzwalane.
6. **Profil sieciowy:** `Get-NetConnectionProfile` na VM bez sieci – ile obiektów, błąd lub pusto; enum `NetworkCategory` na `pl-PL`; klasa `MSFT_NetConnectionProfile` w `root\StandardCimv2` (odczyt).
7. **Kod wyjścia akcji** `net.smb-harden` i `net.network-private` na VM (brak sieci): czy `Execute` daje uczciwy wynik; `Check` jako odczyt stanu.
8. **Bez VM, ręcznie (przeglądarka):** treść wpisu techcommunity 4154300; ścieżka klucza `RequireSecuritySignature` w dokumentacji MS (MS-archiwum / MS Learn); strona MS Support „Share a printer as a network printer” (odnajdywanie w Private); ewentualna strona MS o `NetworkThrottlingIndex`.
9. **Niemożliwe na VM bez sieci → `Skipped` z powodem:** skutek `NetworkThrottlingIndex`, widoczność LAN, połączenie z gościem/NAS, zmiana profilu rzeczywistej sieci, efekt zapory.

## Wartości do ręcznego sprawdzenia (przed wpisaniem do katalogu)

- `DisabledComponents`: 0x20 (prefer IPv4), 0xFF (disable), 0xffffffff → opóźnienie 5 s – zgodne w dwóch pobraniach; wystarczy jednorazowe porównanie z oryginałem strony „Configure IPv6 for advanced users”.
- `SystemResponsiveness`: „values below 10 and above 100 are clamped to 20”, „100 disables MMCSS” – zgodne w dwóch pobraniach; wystarczy jedno porównanie z oryginałem (to zdanie przesądza o odrzuceniu wartości 0).
- Ścieżka rejestru `RequireSecuritySignature` (klient) i wartość domyślna na 24H2 Home/Pro.
- Niezgodność MS: Home 24H2 „doesn't require” podpisywania (smb-signing) vs „all” (smb-security-hardening).
- Treść techcommunity 4154300 i strony Support o odnajdywaniu w sieci Private (obie tylko ze streszczenia wyszukiwarki lub tytuł).
- Domyślna wartość `NetworkThrottlingIndex` (10, hint Q&A).

## Wyniki VM (2026-10-18)

Źródło: `c3-exploration.json` z przebiegu `Invoke-C3Exploration.ps1` (kod wyjścia procesu 0; po przebiegu obie VM Off – to obserwacja autora, nie dane z JSON), zapis pomiaru w archiwum autora. Odwołania w nawiasach kwadratowych to nazwy kroków w JSON (`probes.<sonda>[].Name`). Dane pochodzą z odczytów na VM; zmiany SMB wykonał wyłącznie `Set-SmbClientConfiguration` w VM, a każdy krok niesie odczyt cmdletu i rejestru PO zmianie. Żaden krok nie zgłosił pola `Error`. To, czego odczyt nie rozstrzygnął, jest oznaczone „nie rozstrzygnięte”.

### Metadane

| | DSO-26H2-Home | DSO-26H2-Pro |
|---|---|---|
| CurrentBuild.UBR | 26300.9457 | 26300.9457 |
| EditionID | Core | Professional |
| ProductName (rejestr) | Windows 10 Home | Windows 10 Pro |
| DisplayVersion | 26H2 | 26H2 |
| Culture / UICulture | pl-PL / pl-PL | pl-PL / pl-PL |
| PowerShell | 5.1.26100.9444, proces 64-bit | to samo |

### 1. SystemProfile (MMCSS) [systemprofile-zrzut]

Home = Pro: `NetworkThrottlingIndex` **DWORD 10 (0x0000000A)**, `SystemResponsiveness` **DWORD 20 (0x00000014)**. Klucz zawiera tylko te dwie wartości i podklucz `Tasks`. Skutek zapisu 0xFFFFFFFF i trwałość po restarcie: nie rozstrzygnięte (przebieg odczytowy, bez zapisu). Skutek sieciowy: nie mierzony (VM bez sieci).

### 2. IPv6 [tcpip6-rejestr, get-netprefixpolicy, get-netadapterbinding-ms-tcpip6]

- `Tcpip6\Parameters` istnieje (podklucze `Interfaces`, `Winsock`), **`DisabledComponents` nie istnieje** (Exists=false) na Home i Pro. Czyli stan domyślny = brak wartości, nie DWORD 0.
- `Get-NetPrefixPolicy` działa bez sieci (Home = Pro, `PolicyStore` pusty ciąg), 9 wpisów (Prefix / Precedence / Label): `3ffe::/16` 1/12, `fec0::/10` 1/11, `::/96` 1/3, `fc00::/7` 3/13, `2001::/32` 5/5, `2002::/16` 30/2, **`::ffff:0:0/96` 35/4**, `::/0` 40/1, `::1/128` 50/0.
- `Get-NetAdapterBinding -ComponentID ms_tcpip6`: 1 wiązanie, `Name`=Ethernet, `Enabled`=true (Home i Pro). `DisplayName` jest po polsku („Protokół internetowy w wersji 6 (TCP/IPv6)”) – tekst zależny od języka, nie do detekcji.
- Skutek 0x20/0xFF po restarcie, zmiana priorytetu `::ffff:0:0/96`: nie rozstrzygnięte (bez zapisu w eksploracji; przebieg `Reboot` w Tasku 6).

### 3+4. SMB klient [baseline, set-guest, set-signing, przywroc-z-odczytu-startowego]

**Stan domyślny (baseline)** – rozstrzyga wartości zależne od edycji:

| | Home | Pro |
|---|---|---|
| `EnableInsecureGuestLogons` | **True** | **False** |
| `RequireSecuritySignature` | True | True |
| `EnableSecuritySignature` | True | True |
| `AuditInsecureGuestLogon` | False | False |
| `Parameters\AllowInsecureGuestAuth` w rejestrze | brak wartości | brak wartości |
| `Parameters\RequireSecuritySignature` w rejestrze | brak wartości | brak wartości |
| `Parameters\EnableSecuritySignature` | DWORD 1 | DWORD 1 |
| `Policies\Microsoft\Windows\LanmanWorkstation` | klucz nie istnieje | klucz nie istnieje |

Wnioski z danych: na świeżej 26H2 cmdlet pokazuje gościa niezabezpieczonego **włączonego na Home i wyłączonego na Pro**, mimo że wartość w rejestrze nie istnieje na żadnej edycji (domyślna zależy więc od edycji, nie od wartości w rejestrze). `RequireSecuritySignature` klienta czyta się jako **True na Home i Pro** (także bez wartości w rejestrze), więc dane nie potwierdzają zdania MS „Home nie wymaga podpisywania”; sprzeczność tekstów MS zostaje w dokumentach, a pomiar to True. Klucz Policies nie powstał w żadnym kroku (cmdlet go nie zapisuje).

**Co zapisał cmdlet** (zawsze `Parameters`, DWORD; Policies nietknięte):

| Ustawienie cmdletu | Wartość rejestru | Typ | Zapisana wartość |
|---|---|---|---|
| `-EnableInsecureGuestLogons $true` (Pro) | `Parameters\AllowInsecureGuestAuth` | DWord | 1 |
| `-EnableInsecureGuestLogons $false` (Home) | `Parameters\AllowInsecureGuestAuth` | DWord | 0 |
| `-RequireSecuritySignature $false` (Home i Pro) | `Parameters\RequireSecuritySignature` | DWord | 0 |
| `-RequireSecuritySignature $true` (przywrócenie) | `Parameters\RequireSecuritySignature` | DWord | 1 |

**Widoczność w cmdlecie od razu:** tak – odczyt `Get-SmbClientConfiguration` w tym samym kroku pokazał nową wartość (Home [set-guest] `EnableInsecureGuestLogons`=False, [set-signing] `RequireSecuritySignature`=False; Pro [set-guest] True, [set-signing] False). Zmiana jednego ustawienia nie ruszyła drugiego (w [set-guest] wartość `RequireSecuritySignature` w rejestrze nadal brak, cmdlet nadal True).

**Sekwencja Home** (start Guest=True, Sign=True; cel = wartość przeciwna): [baseline] obie wartości w rejestrze brak → [set-guest] `AllowInsecureGuestAuth`=0 → [set-signing] + `RequireSecuritySignature`=0 → [przywroc-z-odczytu-startowego] `AllowInsecureGuestAuth`=**1**, `RequireSecuritySignature`=**1**, cmdlet True/True, `MatchesInitial=true`, `Differences=[]`.

**Sekwencja Pro** (start Guest=False, Sign=True): [baseline] obie wartości brak → [set-guest] `AllowInsecureGuestAuth`=1 → [set-signing] + `RequireSecuritySignature`=0 → [przywroc-z-odczytu-startowego] `AllowInsecureGuestAuth`=**0**, `RequireSecuritySignature`=**1**, cmdlet False/True, `MatchesInitial=true`, `Differences=[]`.

**Po przywróceniu: jawna wartość, nie brak.** Odczyt przez cmdlet wraca do startowego, ale w rejestrze zostają jawne DWORD-y tam, gdzie na starcie nic nie było (Home: `AllowInsecureGuestAuth`=1 i `RequireSecuritySignature`=1; Pro: 0 i 1). Samo przywrócenie cmdletem NIE przywraca więc „braku wartości”. Wniosek z danych dla katalogu: undo ze snapshotu musi przywracać stan „wartości nie było” przez usunięcie wpisu (`<RemoveEntry>`), a `original` bez journala zależy od edycji (domyślny gość: Home True, Pro False), więc undo bez journala dla gościa nie może zgadywać jednej wartości. Czy po usunięciu wpisu cmdlet wraca do domyślnej wartości edycji: nie rozstrzygnięte (usunięcia wpisu nie testowano).

### 5. Usługi [win32-service]

Home = Pro, `DelayedAutoStart`=false wszędzie, wszystkie usługi znalezione:

| Usługa | StartMode | State |
|---|---|---|
| fdPHost | Manual | Stopped |
| FDResPub | Manual | Stopped |
| SSDPSRV | Manual | Stopped |
| upnphost | Manual | Stopped |
| lmhosts | Manual | Stopped |
| LanmanWorkstation | Auto | Running |
| LanmanServer | Auto | Running |

Zależności i wyzwalacze (`TriggerInfo`): nie rozstrzygnięte (sonda ich nie zbierała).

### 6. Profil sieciowy [get-netconnectionprofile, get-netadapter, cim-msft-netconnectionprofile]

Home = Pro:
- `Get-NetConnectionProfile`: Ok=true, **Count=0, bez błędu** (pusty wynik). `NetworkCategory` jako [string]/[int] oraz `IPv4Connectivity`: nie rozstrzygnięte (brak obiektów do odczytu).
- `Get-NetAdapter`: 1 karta, `Name`=Ethernet, `InterfaceDescription`=Microsoft Hyper-V Network Adapter, `Status`=**Disconnected** (ifIndex Home 6, Pro 5).
- CIM `root\StandardCimv2` `MSFT_NetConnectionProfile`: odczyt się powiódł (Ok=true), Count=0, bez błędu.

### Odpowiedzi na otwarte pytania

1. **MMCSS:** `NetworkThrottlingIndex` DWORD 10, `SystemResponsiveness` DWORD 20 na Home i Pro. Trwałość 0xFFFFFFFF po restarcie i skutek sieciowy: nie rozstrzygnięte.
2. **IPv6:** `DisabledComponents` nie istnieje na świeżym Home ani Pro (`original` = brak wartości, `<RemoveEntry>`). `Get-NetPrefixPolicy` czytelne bez sieci (9 wpisów; `::ffff:0:0/96` precedencja 35). Zmiana po 0x20/0xFF i restarcie: nie rozstrzygnięte.
3. **SMB klient, wartości domyślne:** gość Home True / Pro False; podpisywanie wymagane (True) na obu; `EnableSecuritySignature` True na obu.
4. **SMB klient, rejestr:** cmdlet zapisuje DWORD w `LanmanWorkstation\Parameters` (`AllowInsecureGuestAuth` dla gościa, `RequireSecuritySignature` dla podpisywania); Policies nie dotyka (klucza nie było i nie powstał). Zmiana widoczna w cmdlecie od razu. Po przywróceniu zostają jawne wartości (nie brak): undo musi używać `<RemoveEntry>` ze snapshotu.
5. **Usługi:** pięć usług Manual/Stopped, `LanmanWorkstation` i `LanmanServer` Auto/Running (Home = Pro). Zależności i wyzwalacze: nie rozstrzygnięte.
6. **Profil sieciowy bez sieci:** `Get-NetConnectionProfile` Count 0 bez błędu; karta Ethernet Disconnected; klasa CIM dostępna, pusta. `NetworkCategory` jako string/int: nie rozstrzygnięte.
7. **Kod wyjścia akcji `net.smb-harden`/`net.network-private` na VM:** nie rozstrzygnięte (poza tym przebiegiem; Task 6).
8. **Odczyty ręczne (strony MS) i punkt 9 (`Skipped`):** poza zakresem przebiegu VM.

### Rozstrzygnięte w Tasku 6 (przebieg VM 20261018-195830)

Pozycje oznaczone wyżej jako „nie rozstrzygnięte (Task 6)” rozstrzygnął przebieg VM z 2026-10-18; szczegóły w sekcjach „Test VM” docs pozycji (`docs/tweaks/net.*.md`): precedencja `Get-NetPrefixPolicy` po 0x20 i powrót po undo (przebieg `Reboot`), powrót odczytu cmdletu SMB po usunięciu wartości (rozstrzygający Pro dla gościa, Home i Pro dla podpisywania), kody wyjścia i komunikaty akcji. Nadal nie rozstrzygnięte: zachowanie `Get-SmbClientConfiguration` pod faktyczną polityką (Group Policy), zmiana prawdziwego profilu sieciowego, skutek w LAN.
