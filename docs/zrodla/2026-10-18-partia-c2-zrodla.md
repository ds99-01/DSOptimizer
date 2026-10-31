# Partia C2 - źródła i mechanizmy (research 2026-10-18)

Zakres: 4 pozycje katalogu (perf.sysmain, perf.paging-executive, perf.spectre-mitigations, perf.vbs-hvci). Spec: specyfikacja w archiwum autora, sekcja 5, partia 2.
Zasada: nic nie jest wymyślone. Co nie ma potwierdzenia w źródle MS, jest oznaczone "niepotwierdzone w źródłach MS" i trafia na listę kontroli VM.
Wszystkie URL: data dostępu 2026-10-18. Rodzaj źródła: **MS** = learn.microsoft.com / support.microsoft.com (strona bieżąca); **MS-archiwum** = stara dokumentacja MS lub archiwalny blog MS (może być nieaktualna); **MS-blog** = techcommunity.microsoft.com; **MS-Q&A** = odpowiedź społeczności na learn.microsoft.com/answers (to NIE jest dokumentacja, tylko hint); **hint** = prasa, fora, narzędzia społeczności.
Uwaga o metodzie: strony były pobierane przez narzędzie, które streszcza treść małym modelem. Tam, gdzie wartości liczbowe są kluczowe (Spectre), są one zgodne w dwóch niezależnych pobraniach (strona serwerowa i klienckiej KB), ale przed wpisaniem do katalogu warto je jeszcze raz porównać ręcznie z oryginałem (patrz Otwarte pytania, pozycja 3).
Nie wykonywano żadnych poleceń zmieniających system ani odczytów rejestru/CIM na hoście. Wartości domyślne "z VM" nie są jeszcze zmierzone.

## 0. Co silnik robi z `services[]` i `registry[]` (sprawdzone w kodzie, nie ze źródeł MS)

Z `src/DSOptimizer.Core/Operations/ServiceStartupOperation.cs` i `src/DSOptimizer.Windows/WindowsServiceManager.cs`:
- Pola wpisu usługi: `name`, `startType` (Boot/System/Automatic/AutomaticDelayed/Manual/Disabled), `original` (opcjonalne), `stop` (domyślnie false).
- **Detekcja usługi = tylko typ startu** (`query.StartType == entry.StartType` → Match). Stan "działa/zatrzymana" NIE bierze udziału w detekcji. Brak usługi → NotApplicable.
- **Apply:** `SetStartType`, potem `StopService` tylko gdy `stop=true` i usługa działa. Zatrzymanie przy działających usługach zależnych (kod Win32 1051) kończy się błędem "nie zatrzymuję jej, żeby zmiana pozostała odwracalna" (typ start zostaje już zmieniony przed próbą stopu - do sprawdzenia w planie, czy apply jest atomowy).
- **Capture:** snapshot = istnienie, typ startu, `WasRunning`. Usługi o typie Boot/System (sterowniki) są odrzucane.
- **Restore:** przywraca typ startu, a gdy `WasRunning` i usługa stoi, startuje ją.
- Rejestr: `registry[]` = ścieżka/nazwa/typ/wartość + `original` (fallback) + `detectOnly`; wartość `"<RemoveEntry>"` = "ma nie istnieć". Wpis `detectOnly` nie jest zapisywany ani przywracany.
- Skrypt: `script.detect/apply/undo/capture` (PS 5.1). Walidator: tweak musi mieć detekcję (rejestr, usługa lub `script.detect`); `script.apply` wymaga `script.undo`; zakaz `HKCU:` w skryptach.
- Agregacja stanu (`StateAggregator`): dowolne Unknown → Unknown; wszystkie Match → Applied; żaden → NotApplied; mieszane → Partial. Z tego wynika, że wpisy rejestru + `script.detect` w jednym tweaku muszą się zgadzać, żeby dać `Applied`. Jak dokładnie `script.detect` łączy się z `registry[]` w jednym tweaku (czy uczestniczy w agregacji obok wpisów) - do potwierdzenia w planie na podstawie spec sekcja 4 (nie sprawdzano tutaj).

---

## 1. perf.sysmain (alias v3.2 `io_sysmain`)

**Co robiła v3.2** (poprzednia, niepubliczna wersja 3.2): `Stop-Service -Name 'SysMain' -Force -ErrorAction SilentlyContinue; Set-Service -Name 'SysMain' -StartupType Disabled`; detekcja: `HKLM\SYSTEM\CurrentControlSet\Services\SysMain` wartość `Start` == 4. Opis v3.2 obiecywał poprawę na NVMe i mniejsze zużycie CPU - żadnej z tych obietnic nie potwierdza źródło MS (patrz niżej).

**Mechanizm wg MS**
- Nazwa usługi `SysMain`, wyświetlana nazwa "Superfetch", opis "Maintains and improves system performance over time." Źródło MS (Windows Server 2016 z Desktop Experience, tabela usług): Installation "Always installed", **Startup type: Manual**, Recommendation: **"No guidance"**, Comments: None. To jest dokumentacja serwerowa; wartość dla klienta Windows 11 nie jest podana przez MS.
- **Domyślny typ startu na kliencie 26H2: niepotwierdzony w źródłach MS** (strona serwerowa mówi Manual). Zmierzyć na VM Home i Pro (`Win32_Service`/API: StartMode, DelayedAutoStart).
- **Skutki wyłączenia:** MS nie dokumentuje (strona usług daje "No guidance"; brak strony MS opisującej wpływ na wydajność lub prefetch). **Brak źródła MS** dla twierdzeń o wpływie na SSD/NVMe, CPU czy RAM.
- Sposób wyłączenia podany przez społeczność na Microsoft Q&A (MS-Q&A/hint, odpowiedź "Carlo T.", 2021-09-01, autor sam siebie nazywa "community member", nie pracownik MS): `services.msc` → Typ uruchomienia Disabled, albo `Stop-Service -Force -Name SysMain; Set-Service -Name SysMain -StartupType Disabled`. To potwierdza tylko, że zmiana typu startu przez SCM jest zwykłym sposobem; nie jest to dokumentacja MS.
- Ten sam wątek: autor pytania zgłasza, że zmiana wartości `Start` w rejestrze "wracała do 3 co kilka dni" (hint, niepotwierdzone). **Niepotwierdzone w źródłach MS**, czy Windows (aktualizacje, serwisowanie) resetuje typ startu. Silnik używa API usług (ChangeServiceConfig), nie edycji rejestru, więc ta uwaga dotyczy trwałości efektu, nie mechanizmu.
- **Usługi zależne:** źródło MS nie podaje. Do sprawdzenia na VM (`Get-Service SysMain -DependentServices`, albo API `EnumDependentServices`). Silnik i tak odmówi stopu, gdy zależne działają.

**Proponowana detekcja (zgodna z silnikiem):** `services[]`: `{ name: "SysMain", startType: "Disabled", stop: true }`. Applied = typ startu Disabled. Bez parsowania tekstu. Uwaga: detekcja nie widzi, czy usługa nadal działa (np. zatrzymanie nieudane); to jest ograniczenie silnika, nie błąd katalogu. Sukces apply = typ startu Disabled, a faktyczny stop sprawdza się w teście VM osobnym `Check` (usługa `Stopped`).
- `original`: wpisać dopiero po pomiarze na VM (Automatic lub AutomaticDelayed - nie zgadywać). Bez `original` fallback undo bez journala nie istnieje; undo ze snapshotu działa bez tego.

**Undo:** snapshot ze strony silnika: przywrócenie typu startu z journala i start usługi, jeśli działała (`WasRunning`). Nie ustawiać Manual "na oko". Gdy usługi brak (NotFound) - NotApplicable, nic do cofnięcia.

**Restart:** niepotrzebny z punktu widzenia mechanizmu (zmiana typu startu + stop działa od razu). MS nie podaje wprost - niepotwierdzone w źródłach MS; w spec pozycja bez `requiresReboot`.

**Ryzyka i ostrzeżenia:** experimental. MS nie ma opinii o skutkach ("No guidance"); korzyść nie jest zmierzona ani udokumentowana; opis w katalogu nie może obiecywać przyspieszenia. Zależne usługi mogą zablokować stop (wtedy tytuł pozostaje w stanie częściowym - plan musi to obsłużyć).

**Źródła**
- https://learn.microsoft.com/en-us/windows-server/security/windows-services/security-guidelines-for-disabling-system-services-in-windows-server - MS (Windows Server 2016), 2026-10-18 (odczytana tabela "Superfetch": SysMain, Manual, "No guidance"; updated_at 2026-02-16)
- https://learn.microsoft.com/en-us/answers/questions/3827250/how-to-disable-sysmain - MS-Q&A/hint, 2026-10-18 (odpowiedź społeczności z 2021-09-01)
- Wyszukiwanie w domenach MS (learn/support/techcommunity) nie zwróciło strony MS o SysMain na kliencie 2026-10-18. **Brak źródła MS** dla skutków wyłączenia i domyślnego typu startu klienta.

**Otwarte pytania (VM)**
1. Domyślny typ startu SysMain na świeżym 26H2 Home i Pro (Automatic / delayed / Manual) i czy jest uruchomiona.
2. Usługi zależne (`DependentServices`) i usługi, od których zależy SysMain.
3. Czy `Set-StartType Disabled` + stop przez API kończy się sukcesem na Home i Pro i usługa pozostaje `Stopped`; czy po restarcie nadal Disabled.
4. Czy po undo usługa wraca do typu i stanu sprzed apply (Running).
5. (poza VM, długoterminowo) czy Windows Update resetuje typ startu - VM tego nie sprawdzi w jednym przebiegu.

**Rekomendacja: portować z ograniczeniem.** Mechanizm (typ startu usługi) jest prosty i w pełni odwracalny w silniku, ale MS nie ma żadnej tezy o korzyści ("No guidance"). Wchodzi jako experimental, z opisem bez obietnic wydajności; człowiek decyduje, czy w ogóle chce pozycję bez udokumentowanego efektu.

---

## 2. perf.paging-executive (alias v3.2 `cpu_paging`)

**Co robiła v3.2** (ok. linii 306-314): `Set-ItemProperty ... 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management' -Name DisablePagingExecutive -Value 1 -Type DWord`; detekcja: ta sama wartość == 1. Opis v3.2: "Drastycznie przyspiesza syscalle", ostrzeżenie "tylko 32GB+ RAM".

**Mechanizm wg MS**
- `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management`, wartość `DisablePagingExecutive`, **REG_DWORD**, zakres 0 | 1, **domyślnie 0**. 0 = sterowniki i kod systemowy mogą być stronicowane na dysk; 1 = muszą pozostać w pamięci fizycznej. **Aby zmiana zadziałała, trzeba zrestartować system.** Źródło MS-archiwum: TechNet Windows Server 2003 "DisablePagingExecutive: Core Services" (ms.date 2009-10-08), dotyczy wyłącznie Windows Server 2003 / R2.
- Wskazówka w tym samym źródle (MS-archiwum): ustawienie 1 przydaje się przy debugowaniu sterowników, a także "improves performance on machines with a lot of memory, because it prevents disk reads to get code or data".
- Źródło MS-archiwum (blog MS, SYSK 318, 2007-03-30): ten sam opis, plus "on machines with a lot of memory, this setting can improve performance, but this would depend on the installed applications and their resource utilization."
- **Dla współczesnych Windows (10/11/26H2): brak źródła MS.** Nie znaleziono bieżącej strony learn.microsoft.com opisującej tę wartość. Nie wiadomo, czy współczesne jądro nadal ją honoruje, ani jaki jest efekt przy włączonym VBS/HVCI. MS nie nazywa wartości ani placebo, ani szkodliwą; po prostu nie wypowiada się o 11/26H2.
- Klucz "wartość domyślna 0" nie mówi, czy wartość istnieje na świeżej instalacji, czy jej brakuje (obie sytuacje = 0 według dokumentu 2003).

**Proponowana detekcja (zgodna z silnikiem):** `registry[]`: `{ path: "HKLM\\SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Memory Management", name: "DisablePagingExecutive", type: "DWord", value: 1, original: 0 lub "<RemoveEntry>" }`. Applied = wartość DWORD == 1. `original` ustalić po pomiarze (czy wartość istnieje na świeżym systemie). Bez parsowania tekstu. 64-bitowy rejestr zapewnia silnik.

**Undo:** snapshot z journala (poprzednia wartość albo jej brak); `original` to tylko fallback. Jeśli wartości nie było - `<RemoveEntry>`, bo MS (2003) traktuje brak jak domyślne 0, ale tego dla 26H2 nie potwierdza.

**Restart:** tak (MS-archiwum: "you must restart the system"). `requiresReboot: true`. Efekt przed restartem nie istnieje, więc po apply detekcja rejestrowa da Applied, a "realny stan" jądra nie jest dostępny przez żadne udokumentowane API - to jest ograniczenie tej pozycji.

**Ryzyka i ostrzeżenia:** experimental. Wartość trzyma w RAM-ie kod jądra i sterowników, co zużywa pamięć fizyczną; MS (2003) mówi o zysku tylko "na maszynach z dużą ilością pamięci" i "zależy od aplikacji". Ostrzeżenie o RAM jest zasadne, ale próg "32 GB" z v3.2 nie ma źródła MS - nie powtarzać go jako faktu. Opis v3.2 ("drastycznie przyspiesza syscalle") nie ma źródła, nie przenosić.

**Źródła**
- https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-server-2003/cc757875(v=ws.10) - MS-archiwum (Server 2003), 2026-10-18 (odczytana w całości)
- https://learn.microsoft.com/en-us/archive/blogs/irenak/sysk-318-registry-key-that-controls-device-driver-and-system-code-pagination-to-disk - MS-archiwum (blog MS, 2007), 2026-10-18 (odczytany w całości)
- Brak źródła MS dla Windows 10/11/26H2.

**Otwarte pytania (VM)**
1. Czy wartość istnieje na świeżym 26H2 Home i Pro i jaka jest jej wartość / typ (zakładamy DWORD 0 lub brak).
2. Czy po zapisie 1 i restarcie wartość pozostaje 1 (jądro lub inne komponenty jej nie nadpisują).
3. Czy istnieje jakikolwiek mierzalny skutek - VM tego raczej nie wykaże (brak udokumentowanego wskaźnika, np. brak licznika "paged kernel code" w źródłach MS). Do ewentualnego pomiaru: liczniki pamięci (nazwy liczników niepotwierdzone w źródłach MS).
4. Zachowanie przy włączonym VBS/HVCI - nieudokumentowane.

**Rekomendacja: portować z ograniczeniem.** Wartość jest udokumentowana przez MS (archiwum 2003/2007: typ, domyślna, restart, cel), ale tylko dla dawnych systemów; dla 26H2 nie ma źródła ani zmierzalnego skutku. Wchodzi jako experimental z opisem ograniczonym do tego, co mówi MS ("kod i dane w pamięci", "zależy od aplikacji"), bez obietnic wydajności. Uczciwa alternatywa: nie portować, jeśli człowiek uzna, że brak skutku na 26H2 to "fałszywy tweak".

---

## 3. perf.spectre-mitigations (alias v3.2 `cpu_spectre`)

**Co robiła v3.2** (ok. linii 298-305): `Memory Management` → `FeatureSettingsOverride = 3` (DWord) i `FeatureSettingsOverrideMask = 3` (DWord); detekcja: `FeatureSettingsOverride` == 3 (maska nie była sprawdzana). Bez rozróżnienia Intel/AMD. Opis v3.2: "przywraca pełną prędkość predykcji gałęzi", ostrzeżenie o hypervisorach.

**Mechanizm wg MS**
- Klucz: `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management`, wartości `FeatureSettingsOverride` i `FeatureSettingsOverrideMask`, oba **REG_DWORD**. **Wymagany restart** ("Restart the device for the changes to take effect").
- **Wartości wyłączające mitygacje (MS, KB4073119 klient i strona serwerowa "Windows Server guidance…"; oba pobrania zgodne):**

| Scenariusz wg strony | FeatureSettingsOverride | FeatureSettingsOverrideMask |
|---|---|---|
| tylko Spectre wariant 2 (CVE-2017-5715) | 1 | 3 |
| Spectre V2 + Meltdown (CVE-2017-5754) + MMIO (CVE-2022-21123/21125/21127/21166) | 3 | 3 |
| Spectre V2 + Meltdown + SSBD (CVE-2018-3639) | 3 | 3 |
| TSX TAA + MDS + Spectre + Meltdown + MMIO + SSBD + L1TF | 3 | 3 |

- **Wariant z v3.2 (3/3) jest dokładnie wariantem opisanym przez MS** jako wyłączenie Spectre V2 + Meltdown (+ MMIO / SSBD / MDS / L1TF / TAA wg tytułu bloku). Maska 3 jest podawana przez MS jako poprawna zarówno dla włączania, jak i wyłączania.
- **Warianty Intel/AMD: dla wyłączania MS ich nie rozdziela** (jedna para 3/3 lub 1/3). Warianty zależne od dostawcy dotyczą wyłącznie **włączania** dodatkowych mitygacji: m.in. AMD/ARM user-to-kernel dla V2 = 64/3; Intel TAA/MDS/SSBD/L1TF z HT = 72/3, bez HT = 8264 (0x2048)/3; AMD CVE-2022-23825 = 16777280/3; AMD CVE-2023-20569 = 67108928/3; Intel CVE-2022-0001 (BHI) = 0x00800000, łączenie przez dodawanie. Dla CVE-2022-23825, CVE-2023-20569 i CVE-2022-0001 MS nie podaje wartości wyłączających. Wniosek: "warianty Intel/AMD" ze spec 5 nie mają odpowiednika w opisie wyłączania; do katalogu może wejść co najwyżej jeden wariant (3/3), ewentualnie drugi (1/3, tylko V2).
- **Klient czy serwer:** strona KB4073119 ("Windows client guidance…") podaje wartości wyłączające dla klienta; lista "Applies to" tej strony obejmuje Windows 10 1607, 1809, 21H2, 22H2 oraz **Windows 11 21H2, 22H2, 23H2** (Home/Pro/Enterprise/Education/IoT) i serwery. Ostatni wpis w change-logu: **2024-04-16**. **Windows 11 24H2, 25H2 i 26H2 nie są wymienione** na tej stronie. Strona serwerowa (Server 2012-2022 i Azure Local, change-log do 2024-04-16) zawiera zdanie: "we provide registry key settings for users who want to disable the mitigations when applicable for Windows clients".
- **Domyślne wartości mitygacji na kliencie wg tabeli MS:** CVE-2017-5753 (Spectre V1) włączona i bez opcji wyłączenia; Meltdown, MDS, TAA domyślnie włączone; Spectre V2 włączona (user-to-kernel domyślnie wyłączone na AMD/ARM); SSBD domyślnie wyłączona na Intel/AMD; MMIO włączone od 1809; CVE-2022-0001 domyślnie wyłączona. MS wprost: "defaults vary by CVE". Domyślne wartości samych kluczy rejestru na 26H2 (czy istnieją) - **niepotwierdzone w źródłach MS**.
- **Weryfikacja realnego stanu wg MS:** moduł PowerShell `SpeculationControl` (`Install-Module SpeculationControl`, `Get-SpeculationControlSettings`), opisany w KB4074629. Wymaga pobrania z PowerShell Gallery (sieć), więc na VM bez sieci i na obcych PC bez internetu nie jest dostępny; w detekcji katalogu nie używać. Czy istnieje udokumentowane API w C# do odczytu stanu mitygacji - **brak źródła MS** (nie przeszukiwano; nie zgadywać).

**Odradzanie i wpływ na bezpieczeństwo (MS):** MS nie pisze "nie wyłączaj" wprost, ale: (a) "If you want to obtain all available protections… you must make registry key changes to enable these mitigations" (domyślne ustawienia nie włączają wszystkiego); (b) Hyper-V hosts, Remote Desktop Services hosts i systemy uruchamiające niezaufany kod są "at increased risk"; (c) ostrzeżenie o błędnej edycji rejestru; (d) kolejność: najpierw Windows Update, potem mikrokod. Wpływ na wydajność: MS pisze, że włączanie wyłączonych mitygacji "may affect performance"; nie podaje, ile zyskuje się wyłączeniem na konkretnym CPU. Czy na procesorach z mitygacją sprzętową (np. nowsze CPU) wyłączenie zmienia cokolwiek - **brak źródła MS**. Dla maszyn obcych (pendrive u klienta) wyłączenie zabezpieczeń na cudzym komputerze bez zmierzonej korzyści jest sprzeczne z celem narzędzia ("bezpieczne, każda zmiana wykrywalna i odwracalna" w zasadach repo).

**Proponowana detekcja (zgodna z silnikiem):** `registry[]`: dwa wpisy DWord `FeatureSettingsOverride`=3 i `FeatureSettingsOverrideMask`=3, oba `original: "<RemoveEntry>"`. Applied = oba == 3. Każda inna kombinacja (np. 72/3 albo 3 bez maski) = NotApplied/Partial (silnik agreguje wpisy); apply nadpisuje obie wartości, snapshot przywraca poprzednie. To poprawia v3.2 (która sprawdzała tylko Override). Bez parsowania tekstu. Rzeczywisty stan mitygacji (np. czy CPU w ogóle jest podatny) tą drogą nie jest wykrywany - opis musi to jasno mówić ("ustawia klucze rejestru wg MS, nie mierzy mitygacji").

**Undo:** snapshot z journala; gdy wartości nie było - `<RemoveEntry>` (zgodnie ze spec). Uwaga: MS opisuje jawne "przywróć domyślne" jako `FeatureSettingsOverride`=0 z `Mask`=3 (domyślny V2+Meltdown), a nie usunięcie wartości. Czy usunięcie wartości daje ten sam stan co 0/3, MS nie mówi - **niepotwierdzone w źródłach MS**, do sprawdzenia na VM (zachowanie po restarcie: jeśli system sam zapisuje wartości, `RemoveEntry` mogłoby nie wrócić do tego samego stanu). Na świeżym 26H2 wartości najpewniej nie ma, ale to musi pokazać VM.

**Restart:** tak (MS). `requiresReboot: true`. Wartości są odczytywane przy starcie, więc po apply bez restartu efektu brak.

**Ryzyka i ostrzeżenia:** experimental; ostrzeżenie o utracie ochrony (Spectre V2/Meltdown i pokrewne), o hostach wielodostępnych / RDS / Hyper-V oraz o uruchamianiu niezaufanego kodu (przeglądarka, skrypty). Ostrzeżenie "ZAKAZ w korpo: Proxmox/ESXi" z v3.2 nie ma źródła MS; zastąpić zdaniami z MS powyżej.

**Źródła**
- https://support.microsoft.com/help/4073119 (Windows client guidance for IT Pros…, KB4073119) - MS, 2026-10-18 (wartości wyłączające i włączające, domyślne wg CVE, Applies-to, restart; change-log do 2024-04-16)
- https://support.microsoft.com/en-us/topic/windows-server-guidance-to-protect-against-speculative-execution-side-channel-vulnerabilities-2f965763-00e2-8f98-b632-0d96f30c8c8e - MS, 2026-10-18 (sekcje "To disable…": 3/3 i 1/3; zdanie o klientach; ostrzeżenia dla Hyper-V/RDS/niezaufanego kodu; restart; Applies-to serwery)
- KB4074629 (wyjście `Get-SpeculationControlSettings`) - wskazane przez stronę MS, nie pobierane osobno.
- Wyszukiwanie ogólne (PowerShell Gallery `Set-WindowsSpeculativeMitigation`, Kaspersky, Windows Latest) zwróciło tylko pozycje spoza MS - nie użyte jako dowód.

**Otwarte pytania (VM)**
1. Czy na świeżym 26H2 (Home i Pro) wartości `FeatureSettingsOverride` / `Mask` istnieją, jakie mają wartości i typ.
2. Po zapisie 3/3 i restarcie: czy wartości zostają; czy Windows ich nie nadpisuje.
3. Czy undo przez `RemoveEntry` jest równoważne jawnemu 0/3 (porównać `Get-SpeculationControlSettings` przed/po - wymaga modułu; bez sieci VM tego nie zrobi, więc ta część to Skipped z powodem).
4. Rzeczywisty skutek (czy mitygacje faktycznie się wyłączają na CPU hosta Hyper-V VM) - VM tego nie rozstrzygnie wiarygodnie, bo procesor jest wirtualizowany i część mitygacji zależy od hosta. Wynik pomiaru VM nie jest dowodem dla żelaza.
5. Ponowne, ręczne porównanie wartości 3/3 i 1/3 z oryginałem strony KB4073119 (pobranie przez narzędzie streszczające).

**Rekomendacja: nie portować** (decyzja człowieka). Uzasadnienie: (1) wartości 3/3 są udokumentowane przez MS, ale strona klienta nie obejmuje 24H2-26H2; (2) wyłączenie obniża ochronę bez zmierzonej i udokumentowanej korzyści na współczesnym CPU; (3) narzędzie działa na cudzych komputerach, gdzie obniżanie zabezpieczeń jest ryzykiem dla właściciela; (4) detekcja rejestrowa nie widzi realnego stanu mitygacji; (5) warianty Intel/AMD ze spec nie istnieją dla wyłączania. Jeśli człowiek zdecyduje o wejściu: portować tylko wariant 3/3 (jak v3.2) jako experimental, ukryty domyślnie, z ostrzeżeniami z MS, a w testach VM weryfikować wyłącznie część rejestrową.

---

## 4. perf.vbs-hvci (alias v3.2 `cpu_vbs`)

**Co robiła v3.2** (ok. linii 289-296): tworzyła (jeśli brak) klucz `HKLM:\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity`, ustawiała `Enabled`=0 (DWord), potem `HKLM:\SYSTEM\CurrentControlSet\Control\DeviceGuard` `EnableVirtualizationBasedSecurity`=0 (DWord). Detekcja: tylko `EnableVirtualizationBasedSecurity`==0. Opis v3.2: "VBS zjada 10-25% CPU" (bez źródła), "ZAKAZ w korpo: Pass-the-Hash w AD".

**Mechanizm wg MS (MS Learn "Enable memory integrity", ms.date 2026-08-14, updated_at 2026-10-08)**
- Nazwy: memory integrity = HVCI = hypervisor enforced code integrity; Device Guard "is no longer used except to locate memory integrity and VBS settings in Group Policy or the Windows registry".
- Rejestr (REG_DWORD, `reg add` z oficjalnej strony): `HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard` → `EnableVirtualizationBasedSecurity` (1 = włącz VBS), `RequirePlatformSecurityFeatures` (1 = Secure Boot, 3 = Secure Boot + DMA), `Locked` (0 = bez blokady UEFI, 1 = z blokadą UEFI), `Mandatory` (1 = tryb obowiązkowy: system nie wystartuje, gdy hypervisor / Secure Kernel nie załadują się). `...\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity` → `Enabled` (1 = włącz memory integrity), `Locked` (0/1 = bez/z blokadą UEFI), `WasEnabledBy` (usunięcie wartości wyszarza przełącznik w UI "This setting is managed by your administrator"; 2 = UI normalne).
- **Wyłączanie memory integrity w rejestrze: MS podaje dokładnie jedno polecenie**: `reg add "HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity" /v "Enabled" /t REG_DWORD /d 0 /f` (sekcja Troubleshooting, odzysk z WinRE; restart). To jest udokumentowana wartość "wyłączone" dla memory integrity.
- **`EnableVirtualizationBasedSecurity`=0:** strona memory integrity podaje tylko wartość 1 (włącz). Strona Credential Guard (poniżej) podaje wyłączenie przez politykę ("Turn On Virtualization Based Security: Disabled") i przez `LsaCfgFlags`=0, ale **nie podaje `reg add` z `EnableVirtualizationBasedSecurity`=0**. Wartość 0 tego klucza jako "wyłączone VBS" - **niepotwierdzone w źródłach MS**; v3.2 ją ustawiała. Rozstrzygnąć na VM.
- **Blokada UEFI:** "Once enabled with UEFI lock, you must have access to the UEFI BIOS menu to turn off Secure Boot if you want to turn off memory integrity." (GPO) oraz w WinRE: "If you turned on memory integrity with UEFI lock, you will need to disable Secure Boot to complete the Windows RE recovery steps." Wniosek wg MS: przy blokadzie UEFI samo `Enabled=0` w rejestrze nie wystarcza; potrzebna fizyczna obecność / zmiana Secure Boot. Czy da się odczytać "locked z poziomu UEFI" przez API: MS pokazuje tylko wartość rejestrową `Locked` (1 = z blokadą); czy ona zawsze odzwierciedla zmienną firmware - **niepotwierdzone w źródłach MS**. Zmiennych UEFI nie zgadywać.
- **Odczyt realnego stanu - `Win32_DeviceGuard`** (`Get-CimInstance -ClassName Win32_DeviceGuard -Namespace root\Microsoft\Windows\DeviceGuard`; wartości liczbowe, bez tekstu zależnego od języka):
  - `VirtualizationBasedSecurityStatus`: 0 = VBS nie włączone, 1 = włączone, ale nie działa, 2 = włączone i działa.
  - `SecurityServicesConfigured` (tablica): 1 = Credential Guard, 2 = memory integrity, 3 = System Guard Secure Launch, 4 = SMM Firmware Measurement, 5 = Kernel-mode Hardware-enforced Stack Protection, 6 = ta sama ochrona w trybie Audit, 7 = Hypervisor-Enforced Paging Translation. 0 = nic nie skonfigurowano.
  - `SecurityServicesRunning` (tablica): te same kody, ale "działa" (0 = nic nie działa).
  - Także `AvailableSecurityProperties`, `RequiredSecurityProperties`, `CodeIntegrityPolicyEnforcementStatus` (0 off / 1 audit / 2 enforced).
  - Pole jest tablicą, więc detekcja musi sprawdzać zawieranie wartości 2, nie równość.
- **Dodatkowy udokumentowany wskaźnik ulotny** (MS "Memory integrity enablement", updated_at 2026-06-12): `HKLM\System\CurrentControlSet\Control\CI\State` wartość `HVCIEnabled` - "volatile regkey reflects the state of memory integrity". Typ i znaczenie 0/1 nie są podane (niepotwierdzone w źródłach MS). Dobry kandydat na wpis `detectOnly` w `registry[]` (bez WMI), po potwierdzeniu na VM. Inne udokumentowane sposoby: msinfo32 ("Virtualization-based Security Services Running"), Windows Security → Device security → Core isolation → Memory integrity, narzędzie SkTool z Windows SDK (nie na obcym PC).
- **Memory integrity a Credential Guard:** CG wymaga VBS (`EnableVirtualizationBasedSecurity`=1 w kroku włączania CG), ale ma osobny przełącznik `HKLM\SYSTEM\CurrentControlSet\Control\Lsa\LsaCfgFlags` (0 = wyłączony, 1 = z blokadą UEFI, 2 = bez blokady; MS: "Deleting these registry settings may not disable Credential Guard. They must be set to a value of 0"). MS Learn "Configure Credential Guard" (updated_at 2026-10-08): od Windows 11 22H2 i Server 2025 CG jest **włączony domyślnie na urządzeniach spełniających wymagania**; jawna konfiguracja nadpisuje domyślne po restarcie; jeśli CG był jawnie wyłączony przed aktualizacją, pozostaje wyłączony. Wniosek: zerowanie `EnableVirtualizationBasedSecurity` (wariant v3.2) wyłączałoby też podstawę dla CG; wąski wariant (tylko `Scenarios\HVCI\Enabled`=0) tego nie robi. Skutek ubocznego wyłączenia VBS dla CG w rejestrze nie jest opisany w źródle - do pomiaru (`SecurityServicesRunning`).
- **Smart App Control:** strona MS (support.microsoft.com, "What is Smart App Control") **nie wspomina** VBS, memory integrity, HVCI ani Credential Guard jako zależności. Zależność SAC od VBS: **brak źródła MS**.
- **WSL2 / Hyper-V / Docker:** źródło MS (memory integrity) mówi tylko: "Memory integrity and nested virtualization can be enabled at the same time" oraz że HVCI działa w VM Hyper-V (Gen2). **Żadne pobrane źródło MS nie mówi, że memory integrity/VBS blokuje WSL2/Hyper-V/Docker ani że ich wyłączenie jest do nich potrzebne.** Ostrzeżenie "WSL2/Hyper-V/Docker" ze spec nie ma poparcia w źródłach MS i nie powinno wchodzić do katalogu jako fakt. Czy zmiana `EnableVirtualizationBasedSecurity` wpływa na uruchomienie hypervisora potrzebnego dla Hyper-V/WSL2 (osobny przełącznik: BCD `hypervisorlaunchtype`) - **brak źródła MS** w pobranych stronach; do sprawdzenia na VM z włączoną funkcją (jeśli VM to umożliwia) albo oznaczyć Skipped.
- **Stan domyślny na świeżym 26H2 - sprawdzenie twierdzenia "HVCI domyślnie od 10/2026":**
  - **MS (stan potwierdzony):** "Memory integrity is turned on by default on clean installs of Windows 11 … on compatible hardware… It's also turned on by default on all Secured-core PCs." Wymagania auto-enablement: Intel 8. generacji lub nowszy (od Windows 11 22H2), AMD Zen 2+, Qualcomm Snapdragon 8180+, RAM min. 8 GB (x64), SSD min. 64 GB, zgodne sterowniki, włączona wirtualizacja w BIOS. "Auto-enablement pertains only to clean installs, not upgrades of existing devices." (strona "Memory integrity enablement", updated_at 2026-06-12). Windows 11 22H2+ pokazuje w Windows Security ostrzeżenie, gdy memory integrity jest wyłączone.
  - **Rozszerzenie od października 2026:** istnieje wpis MS-blog (Windows IT Pro Blog, techcommunity.microsoft.com) "Expanding memory integrity protection across Windows devices" (ID 4551984) - tytuł i adres potwierdzone (cytuje go m.in. Windows Latest), ale **treść strony nie została pobrana** (narzędzie zwróciło sam tytuł). Szczegóły (aktualizacja z Patch Tuesday 2026-10-13, ocena gotowości sprzętu/sterowników, uszanowanie wcześniejszego wyłączenia przez GPO/Intune/rejestr, dotyczy także PC po aktualizacji) pochodzą z prasy (Windows Latest 2026-09-02, gHacks 2026-09-07, PCWorld i inne) i są **hint**. Strona MS Learn z 2026-06-12 nadal mówi "clean installs only" - czyli nie została jeszcze zaktualizowana pod to rozszerzenie.
  - **Wniosek:** twierdzenie "HVCI domyślnie od 10/2026" jest w części potwierdzone (MS-blog istnieje; stan "clean install + zgodny sprzęt" jest udokumentowany na MS Learn), ale dokładne reguły rozszerzenia (daty, kryteria) nie są potwierdzone w pobranym źródle MS. W ostrzeżeniach katalogu pisać: "Memory integrity jest domyślnie włączone na czystych instalacjach Windows 11 na zgodnym sprzęcie (MS Learn); od października 2026 MS zapowiada rozszerzenie na kolejne urządzenia".
- **VM a VBS (rozbieżność ze spec 11):** MS (strona OEM VBS, updated_at 2025-09-04): "VBS works on VMs that have either nested virtualization support or Guest VSM enabled. The latter is enabled by default for Generation 2 VMs on Hyper-V." VM Gen2 w testach (Secure Boot, vTPM) może więc uruchomić VBS bez zagnieżdżonej wirtualizacji. Spec zakłada przeciwnie ("może nie wystartować"). To trzeba zmierzyć, nie zakładać: odczyt `Win32_DeviceGuard` na świeżej VM przed czymkolwiek.

**Proponowana detekcja i apply (zgodne z silnikiem):**
- Wariant A (rekomendowany rdzeń, w pełni udokumentowany): `registry[]`: `{ HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity, Enabled, DWord, 0, original: <do ustalenia na VM> }`. Konfiguracja "Applied" = `Enabled`==0.
- Wariant B (v3.2): dodatkowo `...\DeviceGuard` `EnableVirtualizationBasedSecurity`=0. Wchodzi tylko jeśli VM potwierdzi, że wartość 0 działa i nie psuje startu; wpływa też na Credential Guard (patrz wyżej). Domyślnie: nie ruszać.
- Realny stan: `script.detect` (PS 5.1, `Get-CimInstance Win32_DeviceGuard`, jedna linia liczbowa na stdout, bez tekstu): "memory integrity działa" = `SecurityServicesRunning` zawiera 2. Dla UEFI lock: odczyt `Scenarios\HypervisorEnforcedCodeIntegrity\Locked` jako sygnał "zablokowane" (ostrzeżenie/odmowa apply). Sugestia: stany pośrednie (konfiguracja = wyłączone, ale usługa nadal działa do restartu) mapować na semantykę `requiresReboot` (PendingReboot) - szczegóły kontraktu skryptu detect są w spec sekcji 4 (nie przeczytano w tym kroku). Czy skrypt z samym `detect` może współistnieć z `registry[]` w jednym tweaku (agregacja wymaga zgodności wszystkich wpisów) - do potwierdzenia w planie.
- Pomocniczo `detectOnly`: `HKLM\System\CurrentControlSet\Control\CI\State\HVCIEnabled` (po poznaniu typu i znaczenia na VM).
- Bez parsowania tekstu: wszystkie sygnały to liczby (rejestr, CIM).

**Undo:** snapshot rejestru z journala (`Enabled`, `Locked`, `WasEnabledBy` jeśli zmieniane); fallback `original` z katalogu. Apply nie powinno dotykać `WasEnabledBy` (MS: ta wartość steruje auto-wyłączeniem po awarii bootu i UI "managed by administrator"). Przywrócenie `Enabled`=1 wymaga restartu; jeśli blokada UEFI była włączona, po wyłączeniu rejestrowo stan może być niespójny z firmware (Open question 3). Dla Wariantu B undo przywraca `EnableVirtualizationBasedSecurity` ze snapshotu (lub `<RemoveEntry>`).

**Restart:** tak (MS: GPO "restart or gpupdate", Credential Guard "Restart the device to apply"; WinRE: "restart your device"). `requiresReboot: true`.

**Ryzyka i ostrzeżenia:** experimental. Wyłączenie memory integrity obniża ochronę jądra (MS: VBS "provide stronger protections against malware trying to exploit the Windows kernel"). Domyślnie włączone na wielu nowych PC, a od 2026-10 MS rozszerza auto-enablement - wyłączenie na cudzym komputerze jest świadomym obniżeniem zabezpieczeń (Windows Security 22H2+ pokaże ostrzeżenie). Blokada UEFI: rejestr nie wystarczy. Opis v3.2 "10-25% CPU" nie ma źródła MS; MS mówi tylko, że starsze procesory (bez MBEC/GMET, emulacja "Restricted User Mode") mają "bigger impact on performance". Nie wprowadzać liczb.

**Źródła**
- https://learn.microsoft.com/en-us/windows/security/hardware-security/enable-virtualization-based-protection-of-code-integrity - MS, 2026-10-18 (odczytana w całości; ms.date 2026-08-14, updated_at 2026-10-08: klucze rejestru, UEFI lock, `Enabled`=0 w WinRE, `Win32_DeviceGuard`, kody)
- https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/oem-hvci-enablement - MS, 2026-10-18 (odczytana w całości; updated_at 2026-06-12: domyślne włączenie tylko na czystych instalacjach, wymagania sprzętowe, `WasEnabledBy`, `CI\State\HVCIEnabled`)
- https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/oem-vbs - MS, 2026-10-18 (odczytana; updated_at 2025-09-04: Guest VSM domyślnie w VM Gen2 Hyper-V, wymagania VBS)
- https://learn.microsoft.com/en-us/windows/security/identity-protection/credential-guard/configure - MS, 2026-10-18 (odczytana w całości; updated_at 2026-10-08: CG domyślnie od 22H2, `LsaCfgFlags`, wyłączanie, `SecurityServicesRunning`)
- https://techcommunity.microsoft.com/blog/windows-itpro-blog/expanding-memory-integrity-protection-across-windows-devices/4551984 - MS-blog, 2026-10-18 (tylko tytuł; **treść niepobrana**)
- https://support.microsoft.com/en-us/topic/what-is-smart-app-control-285ea03d-fa88-4d56-882e-6698afdb7003 - MS, 2026-10-18 (brak wzmianki o VBS/HVCI)
- hint: https://www.windowslatest.com/2026/09/02/microsoft-is-auto-enabling-memory-integrity-on-windows-11-pcs-from-october-why-you-shouldnt-turn-it-off/ i inne wyniki prasowe (gHacks 2026-09-07, PCWorld, thurrott.com, windowscentral.com) - tylko wskazówka co do zakresu i daty (2026-10-13)

**Otwarte pytania (VM)**
1. Świeża VM Gen2 Home/Pro: wartości `Win32_DeviceGuard` (`VirtualizationBasedSecurityStatus`, `SecurityServicesConfigured`, `SecurityServicesRunning`, `AvailableSecurityProperties`) przed zmianą. Czy VBS faktycznie startuje bez zagnieżdżonej wirtualizacji (Guest VSM)?
2. Czy klucze `...\DeviceGuard`, `...\Scenarios\HypervisorEnforcedCodeIntegrity` i ich wartości (`Enabled`, `Locked`, `WasEnabledBy`, `EnabledBootId`) istnieją na świeżym systemie i jakie mają typy i wartości.
3. Czy rejestrowe `Locked`==1 zawsze odpowiada blokadzie UEFI; zachowanie po `Enabled`=0 przy Locked=1 (VM bez możliwości wejścia do UEFI - raczej Skipped).
4. Czy `EnableVirtualizationBasedSecurity`=0 jest akceptowane i co robi (porównać `Win32_DeviceGuard` po restarcie); wpływ na Credential Guard (`SecurityServicesRunning` zawiera 1?).
5. Typ i znaczenie `CI\State\HVCIEnabled` (0/1) przed i po restarcie.
6. Po `Enabled`=0 i restarcie: `SecurityServicesRunning` nie zawiera 2? Po undo i restarcie - wraca do stanu sprzed? (jeśli VBS w VM nie startuje: test tylko części rejestrowej i detekcji, działanie na żelazie = `Skipped` z powodem, zgodnie ze spec 11).
7. Czy zmiana wpływa na uruchamianie hypervisora (funkcja Hyper-V/WSL2 w VM; wymaga zagnieżdżonej wirtualizacji, więc prawdopodobnie Skipped).
8. Czy auto-enablement (z blogu MS 2026-10) działa na świeżej instalacji VM 26H2 (log `setupact.log`: "SYSPRP HVCI: Enabling HVCI" / "OS does not meet HVCI auto-enablement requirements" - to są komunikaty z dokumentacji MS, tekst angielski, tylko do ręcznej analizy, nie do detekcji).
9. Treść wpisu MS-blog 4551984 - pobrać ręcznie (przeglądarka), bo narzędzie zwróciło tylko tytuł.

**Rekomendacja: portować z ograniczeniem.** Wąski wariant A (memory integrity: `Scenarios\HypervisorEnforcedCodeIntegrity\Enabled`=0) jest udokumentowany przez MS jako wartość "wyłączone" i ma udokumentowaną, niezależną od języka detekcję realnego stanu (`Win32_DeviceGuard`). Bez zerowania `EnableVirtualizationBasedSecurity` (wartość 0 niepotwierdzona, dotyka Credential Guard), z odmową lub ostrzeżeniem przy `Locked`==1, jako experimental z restartem. Ostrzeżenia z MS (kompatybilność sterowników, utrata ochrony jądra, UEFI lock); bez twierdzeń o WSL2/Hyper-V/Docker i bez liczb wydajności.

---

## Tabela zbiorcza

| Pozycja | Mechanizm udokumentowany przez MS | Detekcja (bez tekstu) | Undo | Restart | Rekomendacja | Wymaga sprawdzenia na VM |
|---|---|---|---|---|---|---|
| perf.sysmain | częściowo: tabela usług MS (Server 2016: SysMain, Manual, "No guidance"); skutki wyłączenia i domyślny typ klienta bez źródła MS | `services[]` startType=Disabled (silnik widzi tylko typ startu, nie stan) | snapshot typu startu + start, jeśli działała | nie (MS nie mówi wprost) | portować z ograniczeniem (opis bez obietnic wydajności) | domyślny typ startu, usługi zależne, stop przez API, powrót po undo |
| perf.paging-executive | częściowo: MS-archiwum 2003/2007 (REG_DWORD, domyślnie 0, restart); dla 11/26H2 brak źródła MS | `registry[]` DisablePagingExecutive == 1 | snapshot rejestru / `<RemoveEntry>` | tak (MS-archiwum) | portować z ograniczeniem (alternatywa: nie portować - skutek na 26H2 niewykazalny) | istnienie i domyślna wartość, trwałość po restarcie |
| perf.spectre-mitigations | tak: MS KB (klient i serwer): 3/3 (i 1/3), restart, ostrzeżenia; klient wymienia Win11 do 23H2; wyłączanie nie rozdziela Intel/AMD | `registry[]` Override==3 i Mask==3 (nie wykrywa realnych mitygacji) | snapshot / `<RemoveEntry>` (MS opisuje jawne 0/3) | tak (MS) | nie portować (decyzja człowieka); jeśli tak - tylko 3/3, experimental, ukryte | domyślne wartości, RemoveEntry vs 0/3; efekt na żelazie poza VM |
| perf.vbs-hvci | tak dla `Scenarios\HVCI\Enabled` (0 = wyłączone, WinRE), `Win32_DeviceGuard` (kody), UEFI lock, domyślne włączenie na czystych instalacjach; `EnableVirtualizationBasedSecurity`=0 i reguły października 2026 bez treści źródła | `script.detect` CIM `SecurityServicesRunning` zawiera 2 + `registry[]` Enabled; opcjonalnie `CI\State\HVCIEnabled` detectOnly | snapshot rejestru; restart | tak (MS) | portować z ograniczeniem (tylko HVCI Enabled=0, odmowa/ostrzeżenie przy UEFI lock) | czy VBS startuje w VM Gen2 (Guest VSM), `Locked`, typ `HVCIEnabled`, wpływ `EnableVirtualizationBasedSecurity`=0 |

## Wyniki VM (2026-10-18)

Źródło: `c2-exploration.json` z przebiegu `Invoke-C2Exploration.ps1` (kod wyjścia procesu 0; po przebiegu autor sprawdził `Get-VM`: obie VM Off, rodzic `czysta-26H2` – to obserwacja autora, nie dane z JSON), zapis pomiaru w archiwum autora. Odwołania w nawiasach kwadratowych to nazwy kroków w JSON (`probes.<sonda>[].Name`). Wszystkie dane pochodzą z odczytów na VM (kroki sysmain czytają stan po zmianie wykonanej cmdletami); to, czego odczyt nie rozstrzygnął, jest oznaczone „nie rozstrzygnięte”. Żaden krok nie zgłosił pola `Error`.

### Metadane

| | DSO-26H2-Home | DSO-26H2-Pro |
|---|---|---|
| CurrentBuild.UBR | 26300.9457 | 26300.9457 |
| EditionID | Core | Professional |
| DisplayVersion | 26H2 | 26H2 |
| Culture / UICulture | pl-PL / pl-PL | pl-PL / pl-PL |
| PowerShell | 5.1.26100.9444, proces 64-bit | to samo |
| Przebieg (`startedAt`–`finishedAt`) | 12:54:26,5–12:54:50,1 | 12:54:50,4–12:55:03,3 |

Wniosek ogólny: **Home i Pro dały identyczne wyniki w wartościach używanych przez pozycje C2**, z dwiema różnicami poza nimi. Różnice poza metadanymi, PID-ami i GUID-ami: `Memory Management\ModifiedWriteMaximum` (Home DWORD 4, Pro brak wartości) oraz `DeviceGuard\Scenarios\KeyGuard\Status` (Pro: dodatkowo `NumAttemptedRestarts`=1, `NumSuccessfulRestarts`=0, a `ExecSystemProcessesError` = -1073741823, czyli 0xC0000001; Home: 0 i brak tych dwóch wartości). Znaczenie wartości KeyGuard nie występuje w pobranych źródłach MS, więc ich nie interpretuję. `ModifiedWriteMaximum` też nie występuje w źródłach C2 – nie interpretuję. Żadna z tych wartości nie jest używana przez pozycje C2.

### 1. perf.sysmain [baseline … porownanie-z-baseline]

| Krok | `Win32_Service` StartMode / State / DelayedAutoStart | `Services\SysMain\Start` | `DelayedAutoStart` (rejestr) |
|---|---|---|---|
| baseline | **Auto / Running / false** | DWORD 2 | brak |
| set-disabled (`Set-Service -StartupType Disabled`) | Disabled / Running / false | DWORD 4 | brak |
| stop-service (`Stop-Service` bez `-Force`) | Disabled / **Stopped** / false | DWORD 4 | brak |
| przywroc-typ-startu (`Set-Service -StartupType Automatic`, `RestoreKind=Automatic`) | Auto / Stopped / false | DWORD 2 | brak |
| start-service-jesli-dzialala | Auto / Running / false | DWORD 2 | brak |
| po-przywroceniu (sam odczyt) | Auto / Running / false | DWORD 2 | brak |
| porownanie-z-baseline | `MatchesBaseline=true`, `Differences=[]` | | |

Zależności (Home = Pro, bez zmian we wszystkich krokach): `DependentServices` = **pusta lista**; `ServicesDependedOn` = `rpcss` (Running), `fileinfo` (Running).

Odpowiedzi na pytania (VM, Home i Pro):
1. Domyślny typ startu: **Automatic (bez opóźnienia)**, usługa **działa** [baseline]. Strona serwerowa MS (Manual) nie odpowiada klientowi 26H2.
2. Usługi zależne: **brak**; SysMain zależy od `rpcss` i `fileinfo` [baseline].
3. Wyłączenie i zatrzymanie oficjalnymi cmdletami: sukces, usługa zostaje `Stopped` [stop-service]; zatrzymanie bez `-Force` przeszło (zgodne z pustą listą zależnych). Stan po restarcie: **nie rozstrzygnięte** (sonda nie restartowała VM; sprawdzi przebieg w Tasku 6).
4. Powrót: typ Automatic i stan Running wracają, porównanie z baseline zgodne [porownanie-z-baseline]. Silnik robi to przez snapshot typu startu z journala; sonda użyła cmdletów, nie silnika.
5. Reset przez Windows Update: **nie rozstrzygnięte** (poza zasięgiem jednego przebiegu).

Wniosek dla katalogu: `original` = `Automatic` (zmierzone na VM Home/Pro 26H2, `DelayedAutoStart=false`), `stop: true`; po undo silnik ma uruchomić usługę, jeśli działała przed apply.

### 2. perf.paging-executive [memory-management-zrzut] (tylko odczyt)

| Wartość (`HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management`) | Home | Pro |
|---|---|---|
| `DisablePagingExecutive` | **istnieje, DWORD 0** | to samo |

Odpowiedzi na pytania (VM):
1. Wartość istnieje na świeżym 26H2 Home i Pro: **DWORD 0**.
2. Trwałość 1 po restarcie: **nie rozstrzygnięte** (sonda tylko czytała; sprawdzi przebieg `Reboot` w Tasku 6).
3. Mierzalny skutek: **nie rozstrzygnięte** (brak udokumentowanego wskaźnika).
4. Zachowanie przy VBS/HVCI: **nie rozstrzygnięte** (na VM VBS nie jest włączone, patrz punkt 4).

Wniosek dla katalogu: `original` = DWORD 0 (wartość istnieje, więc snapshot zapisze 0, nie `<RemoveEntry>`).

### 3. perf.spectre-mitigations [memory-management-zrzut] (tylko odczyt, pozycja nieportowana)

| Wartość | Home | Pro |
|---|---|---|
| `FeatureSettingsOverride` | **brak** (klucz istnieje) | to samo |
| `FeatureSettingsOverrideMask` | **brak** | to samo |
| `FeatureSettings` (wartość pokrewna, nie z KB) | DWORD 0 | to samo |

Odpowiedź na pytanie 1: na świeżym 26H2 obu wartości **nie ma**. Pytania 2–4 nie badane: pozycja nie jest portowana (decyzja autora z 2026-10-18, plan w archiwum autora, sekcja „Decyzje autora”). Pytanie 5 (ręczne porównanie z KB4073119) to zadanie dokumentacyjne, nie VM – pozostaje otwarte. Znaczenie `FeatureSettings` nie występuje w źródłach punktu 3 – nie interpretuję.

### 4. perf.vbs-hvci [win32-deviceguard, rejestr-deviceguard, rejestr-ci-state, rejestr-lsacfgflags, bcdedit-enum-current] (tylko odczyt)

`Win32_DeviceGuard` (Home = Pro):

| Właściwość | Wartość |
|---|---|
| `VirtualizationBasedSecurityStatus` | **0** (wg MS: VBS nie włączone) |
| `SecurityServicesConfigured` | **[0]** (nic nie skonfigurowano) |
| `SecurityServicesRunning` | **[0]** (nic nie działa) |
| `AvailableSecurityProperties` | [1, 2, 3, 5, 7] |
| `RequiredSecurityProperties` / `SecurityFeaturesEnabled` | [0] / [0] |
| `CodeIntegrityPolicyEnforcementStatus` | 2 (wg MS: enforced) |
| `UsermodeCodeIntegrityPolicyEnforcementStatus` | 1 |
| `VirtualMachineIsolation` | false |

Znaczenie kodów `AvailableSecurityProperties` nie występuje w pobranych źródłach – nie interpretuję.

Rejestr (Home = Pro):

| Klucz / wartość | Stan |
|---|---|
| `Control\DeviceGuard` | istnieje: `CachedDrtmAuthIndex` DWORD 0, `RequireMicrosoftSignedBootChain` DWORD 1; **brak** `EnableVirtualizationBasedSecurity`, `RequirePlatformSecurityFeatures`, `Locked`, `Mandatory` |
| `Control\DeviceGuard\Scenarios` | istnieje, jedyny podklucz `KeyGuard` (z `Status`; na Home wszystkie wartości 0, na Pro różnice opisane we Wniosku ogólnym) |
| `Scenarios\HypervisorEnforcedCodeIntegrity` | **klucz nie istnieje** (więc też brak `Enabled`, `Locked`, `WasEnabledBy`, `EnabledBootId`) |
| `Control\CI\State` | **klucz nie istnieje** (brak `HVCIEnabled`) |
| `Control\Lsa\LsaCfgFlags` | **brak** (klucz `Lsa` istnieje) |
| `bcdedit /enum {current}` (informacyjnie) | brak wpisu `hypervisorlaunchtype`; `nx OptIn`, `isolatedcontext Yes` |

Odpowiedzi na pytania (VM, Home i Pro):
1. Na świeżej VM Gen2 **VBS nie jest włączone ani skonfigurowane** (status 0, configured/running = [0]). Czy VBS wystartowałby po włączeniu (Guest VSM wg strony MS) – **nie rozstrzygnięte**, przebieg tego nie włączał.
2. `Scenarios\HypervisorEnforcedCodeIntegrity` **nie istnieje**; w `DeviceGuard` brak wartości VBS.
3. `Locked`: brak wartości; zachowanie przy `Locked`=1 – **nie badane** (zaplanowane jako Skipped: brak dostępu do UEFI).
4. `EnableVirtualizationBasedSecurity`=0: **nie rozstrzygnięte** (nie zmieniano; wariant B pozostaje domyślnie nieużywany wg sekcji 4). Credential Guard nie działa w stanie bazowym (`SecurityServicesRunning`=[0]); wpływ zmiany nie badany.
5. `CI\State\HVCIEnabled`: klucz **nie istnieje** na świeżej VM, na której memory integrity nie działa (`SecurityServicesRunning`=[0]). Typ i znaczenie 0/1 przy włączonym – **nie rozstrzygnięte**. Wniosek: wpis `detectOnly` na tej wartości dawałby na świeżym systemie „brak klucza”, więc nie nadaje się na jedyny sygnał.
6. `Enabled`=0 przy działającym memory integrity i restart: **nie rozstrzygnięte** – na VM memory integrity w ogóle nie działa.
7. Wpływ na hypervisor: **nie badane** (zaplanowane jako Skipped).
8. Auto-enablement: w stanie checkpointu `czysta-26H2` memory integrity **nie działa** na obu VM. Czy powinno było się włączyć (wymagania sprzętowe, VM, czy obraz ma poprawkę z 2026-10-13) – **nie rozstrzygnięte**; `setupact.log` nie był czytany.
9. Treść MS-blog 4551984: poza VM, nadal niepobrana.

Wnioski dla Taska 5:
- `original` dla `Enabled`: **brak wartości i brak klucza** – snapshot z journala daje `<RemoveEntry>`, undo usuwa wartość, a pusty klucz utworzony przez apply obejmuje reguła A1 (spec w archiwum autora, A1: undo usuwa puste klucze utworzone przez Apply, od najgłębszego; tylko wpisy `registry[]`). Jeśli Task 5 przejdzie na `script`, A1 nie działa i usunięcie klucza musi zrobić skrypt.
- Na świeżej VM tweak jest już w stanie „memory integrity nie działa”. Scenariusz z planu (konfiguracja wyłączona, memory integrity działa do restartu → `PendingReboot`, po restarcie `Applied`) **nie odtworzy się na VM bez wcześniejszego włączenia memory integrity**. Decyzja w Tasku 5: przypadek z Setup włączającym memory integrity + restart (VBS może nie wystartować – do sprawdzenia) albo część rejestrowa + detekcja na VM, a działanie na żelazie `Skipped` z powodem (spec 11).
