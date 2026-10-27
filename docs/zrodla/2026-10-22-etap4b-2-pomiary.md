# Etap 4b partia 2 – pomiary VM: długie naprawy (2026-10-22)

Pomiar na VM Hyper-V `DSO-26H2-Home` (EditionID `Core`) i `DSO-26H2-Pro` (`Professional`), Windows 11 26H2, kompilacja
26300.9457, interfejs pl-PL, strona OEM 852. Każdy przebieg zaczynał się od checkpointu `czysta-26H2` i kończył revertem.
Skrypt: `tests/vm/explore/Invoke-E4B2Exploration.ps1` (Pester na mockach: `tests/vm/tests/E4B2Exploration.Tests.ps1`).
Surowe wyniki (JSON + bajty wyjścia + segmenty logów) poza gitem: zapis pomiaru w archiwum autora.

**Zakres (decyzja Dawida R8, 2026-10-22):** bez uszkadzania plików systemowych i bez zmiany właściciela/ACL. SFC i DISM
mierzone tylko na zdrowej VM. Werdykty „naprawiono”, „nie udało się naprawić”, „naprawa czeka na restart” i kody braku
źródła DISM pochodzą z dokumentacji MS i są oznaczone **niezmierzony**. Windows.old = zwykły folder z plikami testowymi
utworzony kontem Administratora (właściciel `BUILTIN\Administratorzy`, ACL dziedziczone), tylko mechanizm A (cleanmgr).

Konteksty: **K1** = PowerShell Direct jako wbudowany Administrator (sesja 0); **K2** = zadanie Harmonogramu konta `dso`
(Interactive + Highest, sesja 1), czyli proces jak DSOptimizer po UAC. Narzędzia startowały z pełnej ścieżki System32 przez
`Process` z `CreateNoWindow` (to samo co `CREATE_NO_WINDOW` w `System32ToolRunner`), stdout i stderr potokiem, zapis bajtowo.

Przebiegi:
- **P0** – próba na Home (baseline + windowsold, `exploration/proba-home-baseline-windowsold/`).
- **F1** – Home i Pro, wszystkie fazy, **równolegle** na jednym hoście (`exploration/DSO-26H2-Home|Pro/`). Czasy mogą być
  lekko zawyżone przez wspólny dysk hosta.
- **N2** – Home i Pro równolegle, baseline + network + final (`exploration/network2/`): usuwanie pamięci DO na prawdziwych
  plikach pobranych przez Windows Update i drugi pomiar DISM z siecią.

Wszystkie przebiegi bez błędu (`error` puste, `cleanup.Ok = true`, VM `Off`, karta bez przełącznika).

## Tabela decyzji

| # | Pomiar | Home | Pro | DECYZJA (wartości do Tasków 3–8) |
|---|---|---|---|---|
| 1 | DISM `/RestoreHealth` bez sieci, zdrowa VM | kod 0, 74,5 s | kod 0, 72,1 s | Kod 0 + linia „The restore operation completed successfully.”. Format wyjścia – sekcja 1. |
| 1s | DISM `/ScanHealth` | kod 0, 79,4 s, „No component store corruption detected.” | kod 0, 73,3 s, to samo | Tylko do porównania; handler go nie woła. |
| 1n | DISM `/RestoreHealth` z siecią (źródło WU) | kod 0, 608,4 s (F1) i 579,6 s (N2); WU w tym czasie pobierał i instalował aktualizacje | kod 0, 594,5 s (F1) i 613,6 s (N2) | Czas zależy od aktywności WU: **1–1,5 min bez WU, ok. 10 min przy aktywnym WU**. |
| 1z | Zdrowy czy naprawiony | `CBS.log`: blok „Checking System Update Readiness.” / „Summary:” z `Total Detected Corruption:\t0`, `Total Repaired Corruption:\t0`, `Operation result: 0x0` | to samo | **Da się odróżnić z `CBS.log`** (segment od offsetu sprzed startu): `Total Detected Corruption` = 0 → `Healthy`; > 0 i `Total Repaired Corruption` = `Total Detected Corruption` → `Repaired`; > 0 i mniej naprawionych → `Failed`/`SourceMissing` według kodu. Samo wyjście konsoli i `dism.log` nie rozróżniają (te same zdania). Przypadek „naprawiony” **niezmierzony** (format z dokumentacji MS, KB 947821). Bez segmentu CBS → `HealthyOrRepaired`. |
| 1b | Brak źródła (`/Source:C:\brak /LimitAccess`) | zdrowy magazyn: **kod 0**, 61,2 s, „The restore operation completed successfully.” | kod 0, 57,8 s | Zdrowy magazyn nie sięga po źródło, więc kodu braku źródła nie da się zmierzyć. Kody z dokumentacji MS (**niezmierzony**, sekcja 1): `0x800F081F` CBS_E_SOURCE_MISSING, `0x800F0906` CBS_E_DOWNLOAD_FAILURE, `0x800F0907` CBS_E_GROUPPOLICY_DISALLOWED → `SourceMissing`. `0x800F0950`: brak dokumentacji MS – nie mapować na `SourceMissing` (→ `Failed`). |
| 2 | SFC `/scannow`, zdrowa VM | kod 0, 52,4 s, „Windows Resource Protection did not find any integrity violations.” | kod 0, 53,9 s | Kod 0 + segment `[SR]` bez znaczników naprawy = `NoViolations`. Kodowanie i format – sekcja 2. |
| 2r | SFC `[SR]` w `CBS.log` | 498 linii `[SR]` w segmencie 46 KB: szablony `Verifying # components`, `Beginning Verify and Repair transaction`, `Verify complete`, na końcu `Repairing 0 components` + `Repair complete` | 525 linii, te same szablony | **NoViolations:** są linie `Verify complete`, nie ma `Repairing corrupted file`, `Repaired file`, `Cannot repair member file`, a `Repairing # components` ma tylko `0`. Znaczniki pozostałych werdyktów z KB 928228 (**niezmierzony**): `Repairing corrupted file … from store` / `Repaired file … by copying from backup` → `Repaired`; `Cannot repair member file` → `NotAllRepaired`. „Naprawa czeka na restart”: brak znacznika `[SR]` w dokumentacji MS – rozpoznawać po kluczu `…\Component Based Servicing\RebootPending` po SFC (sekcja 2), inaczej `Unknown`. |
| 2k | Prośba o restart | DISM i SFC na zdrowej VM: brak (`RebootPending`, `PendingFileRenameOperations`, `WindowsUpdate\Auto Update\RebootRequired` – bez zmian) | to samo | `RequiresReboot = false` przy `Healthy`/`NoViolations`. Kod 3010 DISM – **niezmierzony** (zostaje `RebootRequired` z planu). |
| 3 | Lista procesów „operacja serwisowania trwa” | bezczynna VM po zalogowaniu: `TiWorker` i `TrustedInstaller` żyją przez pierwsze ok. 75–80 s (16/36 próbek), potem znikają; po aktywności WU żyją cały czas (24/24) | to samo (16/36, 23/24) | **`IServicingActivity`: `dism`, `sfc`, `cleanmgr`** (nazwy procesów bez `.exe`, kolejność sprawdzania). Na bezczynnej VM żadna z nich ani razu (baseline 4 × 36 próbek, final 4 × 24). **Bez `TiWorker` i `TrustedInstaller`** – fałszywe alarmy po starcie systemu i przy każdej instalacji aktualizacji. **Bez `DismHost`** – w N2 na Pro pojawił się na 5 s bez `dism`/`cleanmgr`, gdy WU instalował aktualizacje (fałszywy alarm); towarzyszy zawsze `dism` albo `cleanmgr`, więc niczego nie dodaje. |
| 3c | Drugi DISM / SFC w trakcie pierwszego | `dism /CheckHealth` start w 30. s `RestoreHealth`: **czeka** do końca pierwszego, potem kod 0 (koniec po 74 s, czyli razem z pierwszym). `sfc /verifyfile` w 45. s: też czeka, kod 0. Bez komunikatu o konflikcie | to samo | System szereguje operacje serwisowania – drugi proces nie dostaje błędu, tylko czeka. Strażnik (`Refused` „Poprzednia operacja serwisowania jeszcze trwa…”) jest więc decyzją programu, nie wymaganiem narzędzi: bez niego druga naprawa wisiałaby ukryta. |
| 4a | Pliki tymczasowe | bezpośrednie usuwanie (model `DeleteOldFiles`, próg 24 h): stare usunięte, świeży i otwarty bez współdzielenia zostają, junction usunięty jako link, plik-strażnik w celu nietknięty, pusty stary podfolder usunięty | identycznie | **Bezpośrednie usuwanie w C#** (`DeleteOldFiles`). cleanmgr „Temporary Files” usunął tylko plik sprzed 10 dni (nie ten sprzed 2 dni) i tylko w `%SystemRoot%\Temp` i `%TEMP%` konta `dso` (nie w `%TEMP%` Administratora), a z K1 **nie zakończył się w 20 min** (zabity). |
| 4b | Optymalizacja dostarczania | `Delete-DeliveryOptimizationCache -Force` jest (funkcja modułu `DeliveryOptimization` 1.0.3.0). Bez sieci pamięć pusta (0,1 s). N2 z pamięcią 5,16 GB / 4 zadania `Caching`: 0,3 s, zadań 4 → 3, folder DO −28,6 MB (16 → 14 plików) | N2: 5,43 GB / 7 zadań: 0,3 s, 7 → 6, folder −28,6 MB (25 → 23 pliki) | **Cmdlet DO** (`DiskCleanupScripts.DeliveryOptimization`, `-Force`). Skutek realny, ale na VM mały: pliki zadań, które WU właśnie pobiera, zostają. Zwolnione miejsce liczyć z folderu albo z całości, nie z tego kroku osobno (WU pisał na dysk w tym samym czasie). Duży zysk po zakończonych pobraniach – **do potwierdzenia na sprzęcie**. |
| 4c | `SoftwareDistribution\Download` | F1 offline: 60 MB plików testowych, żaden w użyciu, usunięte. N2: 255 MB / 8 plików, żaden w użyciu, usunięte. F1 z siecią: 1,21 GB / 16 754 pliki; 2 pliki w użyciu (`.msu`/`.cab` instalowanej aktualizacji) także po zatrzymaniu `wuauserv` i `bits`; `File.Delete` mimo to usunął wszystkie | 1,32 GB / 16 738 plików; 4 w użyciu, po zatrzymaniu `wuauserv` 1 (ten sam `.msu`), po `bits` dalej 1 | **Zatrzymać `wuauserv` i `bits`** (logika `WindowsUpdateReset`, przywrócenie stanu sprzed: `bits` startuje 4,3 s). Zatrzymanie nie zwalnia pliku trzymanego przez instalator (`TiWorker`), a `File.Delete` go usuwa (otwarty z `FILE_SHARE_DELETE`). Dlatego **„w użyciu” = nie da się otworzyć z `FileShare.None`** – sprawdzać przed usunięciem, nie po błędzie `Delete` (wejście do Tasku 4). Folder może znikać w trakcie (WU sprząta sam): `DirectoryNotFound` = pominięcie, nie błąd. |
| 4d | `StartComponentCleanup` (bez `/ResetBase`) | kod 0, 12,1 s, +449 MB („Cache and Temporary Data” 481 MB → 0) | kod 0, 11,7 s, +367 MB (404 MB → 0) | **DISM `/StartComponentCleanup`** przez `LongTools`. Postęp skokowy: 5,0% → 65,0% → 70,0% → 100,0%. `/AnalyzeComponentStore` zbędny w handlerze (wolne miejsce przed/po wystarcza). |
| 4e | Miniatury konta (`thumbcache_*.db`) przy działającym `explorer.exe` konta `dso` | 15 plików (2 × 1 MB + drobne), żaden zablokowany, wszystkie usunięte | to samo | **Bezpośrednie usuwanie**, pliki zablokowane pomijane i liczone (na VM nie wystąpiły). |
| 4f | cleanmgr `/sagerun` z PS Direct (K1) | „Temporary Files”: proces żyje przez cały limit 1200 s (z potomnym `DismHost` i `TiWorker`), zabity; zwolnił netto ok. 18 MB | to samo (18 MB) | **cleanmgr nie jest mechanizmem czyszczenia** (wolniejszy, usuwa mniej, z sesji 0 się nie kończy). |
| 5 | Windows.old (mechanizm A) | K1: folder 200 MB znika **w ≤ 5 s**, miejsce wraca, ale cleanmgr **nie kończy się w 1200 s** (zabity). K2 (`dso`): folder znika w ≤ 5 s, cleanmgr kończy się sam po **641 s**, wynik zadania 0x0 | K1 jak Home. K2: znika w ≤ 5 s, koniec po **665 s**, wynik 0x0 | **Mechanizm A działa: cleanmgr rozpoznaje przygotowany folder i go usuwa.** Handler uruchamia cleanmgr w sesji użytkownika (jak GUI), czeka na koniec procesu (`DismHost` pracuje ok. 11 min po usunięciu folderu), odczyt zwrotny = brak folderu. Przypadek VM w Tasku 8: **`RunAs='dso'`** (z PS Direct cleanmgr nie kończy się) i `CliTimeoutSeconds` ≥ 2700. Uwaga: przygotowany folder ≠ prawdziwy powrót do poprzedniej wersji – **do potwierdzenia na sprzęcie**. |
| 5f | `StateFlags<N>` | Na świeżej VM brak jakiejkolwiek wartości `StateFlags*` pod `VolumeCaches` (31 programów obsługi, w tym `Previous Installations` i `Temporary Files`). Własna `StateFlags7351` ustawiona tylko pod jednym kluczem i usunięta po każdym przebiegu (także po zabiciu) | to samo | **`WindowsOldHandler.SageRunId = 7351`** (cleanmgr przyjmuje 0–9999 według dokumentacji `cleanmgr`, KB 253597 podaje 0–65535; 7351 mieści się w obu). Wartość DWORD `2`. |
| 6 | Czasy i limity | sekcja 6 | sekcja 6 | `system-files`: **2700 s na narzędzie**; `disk-cleanup`: **1200 s** (StartComponentCleanup); `windows-old`: **2700 s**. |

## 1. DISM – szczegóły

**Wyjście konsoli (potok, `CREATE_NO_WINDOW`), Home i Pro identycznie:**
- Kodowanie: strona OEM (852), **bez BOM**. Na VM tekst w całości ASCII (0 bajtów ≥ 0x80) – zdania DISM są **po angielsku**
  mimo pl-PL (sesja PS Direct Administratora). Na komputerze z polskim DISM zdania mogą być zlokalizowane – parser opiera się
  na kodzie wyjścia, procentach i `CBS.log`, nie na zdaniach.
- Pierwsze bajty: `0D 0A` (wyjście zaczyna się od pustej linii `\r\n`), potem nagłówek „Deployment Image Servicing and
  Management tool”, „Version: 10.0.26100.8972”, „Image Version: 10.0.26300.9457”.
- Postęp: **każda aktualizacja to osobna linia** `\r[====      45.0%      ] \r\n` – znak `\r` na początku, `\r\n` na końcu,
  procent z **kropką** i jedną cyfrą po przecinku, wtopiony w pasek z `=`. RestoreHealth: ok. 90 linii postępu, ScanHealth ok. 120.
  W liczeniu: `\r\n` 96, samotne `\r` 88, samotne `\n` 0 (RestoreHealth Home).
- Wyjście płynie na bieżąco: długość pliku rośnie co próbkę 5 s (112 → 560 → 1456 → … bajtów), więc procent w GUI jest żywy.
- Linie końcowe: RestoreHealth – „The restore operation completed successfully.” + „The operation completed successfully.”;
  ScanHealth – „No component store corruption detected.” + „The operation completed successfully.”. Stderr pusty.
- Procesy w trakcie: `dism`, `DismHost`, `TiWorker`, `TrustedInstaller` w każdej próbce.

**`CBS.log` (segment od offsetu sprzed startu, 790 KB):** linia `Corruption detection complete. numCorruptions = 0` i blok
podsumowania (wartości oddzielone tabulatorem):
```
Checking System Update Readiness.
Summary:
Operation: Detect and Repair
Operation result: 0x0
Last Successful Step: Remove staged packages completes.
Total Detected Corruption:	0
…
Total Repaired Corruption:	0
…
Exec: Processing complete, session(Corruption Repairing): … [HRESULT = 0x00000000 - S_OK]
```
ScanHealth ma `Operation: Detect only`. Przykład bloku z uszkodzeniem i `Operation result: 0x800f081f` jest w KB 947821
(**niezmierzony** na VM). `dism.log` z przebiegu (17 KB) zawiera wiersz polecenia i przebieg sesji, ale nie liczbę uszkodzeń.

**Kody braku źródła (dokumentacja MS, niezmierzony):**
- `0x800F081F` CBS_E_SOURCE_MISSING – „The source files could not be found. Use the Source option…” (KB 947821 tabela kodów;
  KB 2734782).
- `0x800F0906` CBS_E_DOWNLOAD_FAILURE – „The source files could not be downloaded.” (KB 947821, KB 2734782).
- `0x800F0907` CBS_E_GROUPPOLICY_DISALLOWED – polityka zabrania pobierania z WU (KB 2734782).
- `0x800F0950` – brak opisu w dokumentacji MS (tylko wątki Q&A przy .NET 3.5) → nie traktować jako brak źródła.

Źródła: https://learn.microsoft.com/en-us/troubleshoot/windows-server/installing-updates-features-roles/fix-windows-update-errors ,
https://learn.microsoft.com/en-us/troubleshoot/windows-client/application-management/dotnet-framework-35-installation-error ,
https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/configure-a-windows-repair-source .

## 2. SFC – szczegóły

**Wyjście konsoli (potok), Home i Pro identycznie:**
- Kodowanie: **UTF-16LE bez BOM**. Pierwsze bajty `0D 00 0D 00 0A 00` (`\r\r\n`), czyli pierwszy znak U+000D, a drugi bajt
  to `00` – wykrycie „drugi bajt = 0 → UTF-16LE” z Tasku 3 działa na tej próbce. Zdania po angielsku, 0 bajtów spoza ASCII.
- Końce linii zdań: **`\r\r\n`** (np. „Beginning system scan.  This process will take some time.\r\r\n”). Splitter nie może
  z tego robić pustej linii (albo puste linie trzeba pomijać).
- Postęp: `\rVerification N% complete.` – **samo `\r`, bez `\n`**, procent **całkowity** (bez separatora), każdy procent
  zwykle dwa razy. Ostatni „Verification 100% complete.\r\n”, potem `\r\r\n` i linia wyniku.
- **SFC wypisuje do potoku blokami po 4096 B**: przez pierwsze 20 s plik ma 0 B, w 25. s 4104 B, w 50. s 8200 B, reszta przy
  wyjściu. Postęp SFC w GUI będzie więc skokowy (paczki ok. 40 procent), a nie płynny – to cecha narzędzia, nie błąd splittera.
- Linia wyniku: „Windows Resource Protection did not find any integrity violations.”. Kod wyjścia 0. Stderr pusty.
- Procesy w trakcie: `sfc`, `TiWorker`, `TrustedInstaller` (bez `DismHost`).

**Komunikaty pozostałych przypadków (dokumentacja MS, niezmierzony):** „Windows Resource Protection found corrupt files and
successfully repaired them.”, „…found corrupt files but was unable to fix some of them.”, „…could not perform the requested
operation.” (https://support.microsoft.com/en-us/topic/use-the-system-file-checker-tool-to-repair-missing-or-corrupted-system-files-79aa86cb-ca52-166a-92a3-966e85d4094e).
Kody wyjścia SFC dla tych przypadków nie są opisane w dokumentacji MS – **niezmierzony**; `SfcOutcome` opiera się na `[SR]`.

**`CBS.log`, segment `[SR]` z przebiegu (offset sprzed startu):** 46–49 KB, 498/525 linii `[SR]`, grupy po 100 komponentów:
`[SR] Verifying 100 components` → `[SR] Beginning Verify and Repair transaction` → `[SR] Verify complete` (165/174 grup),
na końcu `[SR] Repairing 0 components`, `[SR] Beginning Verify and Repair transaction`, `[SR] Repair complete`.
Znaczniki naprawy z KB 928228 (**niezmierzony**): `Repairing corrupted file … from store`, `Repaired file … by copying from
backup`, `Cannot repair member file …` (https://learn.microsoft.com/en-us/troubleshoot/windows-client/deployment/analyze-sfc-program-log-file-entries).
CBS.log nie rotował w żadnym przebiegu SFC (rotacja `Rotated=false`), ale po instalacji aktualizacji WU segment DISM z siecią
miał ponad 32 MB (obcięty do ostatnich 32 MB) – odczyt segmentu musi mieć limit (Task 4: 16 MB od końca wystarcza dla SFC).

**„Naprawa czeka na restart”:** dokumentacja MS nie podaje znacznika `[SR]` ani kodu – **niezmierzony**. Mierzalny sygnał:
klucz `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending` (odczyt działa; na zdrowej VM
po DISM/SFC brak, po instalacji aktualizacji przez WU w fazie sieci – jest). Propozycja dla Tasku 6: `RepairPendingReboot`
tylko gdy klucz pojawił się w trakcie SFC (nie było go przed), inaczej werdykt z `[SR]`.

## 3. Trwająca operacja serwisowania – szczegóły

| Moment | `dism` | `DismHost` | `sfc` | `cleanmgr` | `TiWorker` | `TrustedInstaller` |
|---|---|---|---|---|---|---|
| Bezczynna VM po zalogowaniu (180 s, co 5 s) | 0/36 | 0/36 | 0/36 | 0/36 | 16/36 (0–75 s) | 16/36 (0–75 s) |
| DISM RestoreHealth | 14/14 | 14/14 | 5/14 (verifyfile w tle) | 0 | 14/14 | 14/14 |
| SFC /scannow | 0 | 0 | 10/10 | 0 | 10/10 | 10/10 |
| cleanmgr `/sagerun` | 0 | 240/240 (potomny cleanmgr) | 0 | 240/240 | 240/240 | 240/240 |
| Po fazie sieci (WU instaluje, 120 s), F1 | 0/24 | 0/24 | 0/24 | 0/24 | 24/24 | 23–24/24 |
| Po fazie sieci, N2 | 0/24 | Home 0/24, **Pro 1/24** (65. s) | 0/24 | 0/24 | 24/24 | 24/24 |

Usługa `TrustedInstaller` na świeżej VM: `Running/Manual` w chwili logowania, `Stopped/Manual` po ok. 80 s. Po instalacji
aktualizacji przez WU typ startu zmienia się na `Auto`. Wniosek: lista `dism`, `sfc`, `cleanmgr`.

## 4. Czyszczenie – szczegóły

**Zestaw plików** (w `%SystemRoot%\Temp`, `%TEMP%` konta `dso`, `%TEMP%` Administratora): stary (−2 dni), bardzo stary
(−10 dni), świeży, stary otwarty z `FileShare.None`, stary w podfolderze, junction do folderu-strażnika (plik −10 dni).
Czasy `Creation`/`LastWrite`/`LastAccess` ustawione na wiek.

| Krok | Home | Pro |
|---|---|---|
| cleanmgr „Temporary Files” (K1) | tylko `veryold` znika w systemowym i `dso`; `admin` bez zmian; proces żyje 1200 s | to samo |
| Bezpośrednio, `%SystemRoot%\Temp` | usunięte 2 (old, subold), młode 1, w użyciu 1, link 1, folder 1, strażnik zostaje | to samo |
| Bezpośrednio, `%TEMP%` `dso` | usunięte 4, młode 5 (pliki sesji `dso`), w użyciu 1, link 1, foldery 2 | to samo |

**4b. Optymalizacja dostarczania:** F1 bez sieci – cmdlet działa, ale pamięć pusta (`Get-DeliveryOptimizationStatus` = 0
zadań, ostrzeżenie „No active Delivery Optimization download or upload jobs”; cmdlet pisze „Deleting…”/„Successfully
deleted Delivery Optimization cache” przez strumień hosta, nie przez wyjście). Pamięć DO na 26H2 leży w
`%SystemRoot%\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization` (po 10 minutach z siecią
F1: 10,6 GB / 27–34 pliki, 9 zadań `Caching`/`Downloading`); `%SystemRoot%\SoftwareDistribution\DeliveryOptimization` nie
istnieje. **N2 (z siecią, ok. 10 min po podłączeniu):** Home – 4 zadania `Caching`, folder 5161,2 MB / 16 plików; po
`Delete-DeliveryOptimizationCache -Force` (0,3 s, bez błędu) 3 zadania i 5132,6 MB / 14 plików. Pro – 7 zadań, 5426,9 MB / 25
plików → 6 zadań, 5398,3 MB / 23 pliki. Wolne miejsce C: w tym kroku +461,6 MB (Home) i +727,6 MB (Pro), ale WU w tym samym czasie
pobierał i instalował, więc tej liczby nie da się uczciwie przypisać krokowi DO (Review Focus 1). Cmdlet nie usuwa zawartości
zadań w toku – zgodne z obserwacją, mechanizm w dokumentacji MS nieopisany (strona cmdletu ma puste opisy).

**4c. SoftwareDistribution\Download:** na świeżej VM folder nie istnieje (baseline) – brak = krok pominięty, nie błąd.

**4d. StartComponentCleanup:** `Date of Last Cleanup` przed: 2026-10-17 (Home) / 2026-10-16 (Pro), po: data przebiegu;
„Number of Reclaimable Packages: 2” i „Component Store Cleanup Recommended: Yes” zostają po czyszczeniu (bez `/ResetBase`
tych pakietów się nie usuwa) – handler nie może oceniać sukcesu po tej linii.

## 5. Windows.old – szczegóły

- Przygotowanie: `C:\Windows.old` 209 715 325 B (15 plików w `Windows\System32`, `Windows\SysWOW64`, `Users\Public\Documents`,
  `Program Files\DsoTest`, `ProgramData\DsoTest`), właściciel `BUILTIN\Administratorzy`, bez zmiany ACL. Wolne miejsce: −200 MB.
- K1 (`cleanmgr /sagerun:7351`, flaga tylko pod `Previous Installations`): w pierwszej próbce (5 s) folderu już nie ma, miejsce
  wróciło (Home +208,8 MB). Proces cleanmgr (sesja 0) z potomnym `DismHost` żyje do limitu 1200 s – zabity (P0 i F1, Home i Pro).
- K2 (zadanie `dso`, Interactive + Highest): folder znika w ≤ 5 s, cleanmgr (sesja 1) z `DismHost` kończy się sam po 641 s
  (Home) i 665 s (Pro), `LastTaskResult` 0x0. Okno cleanmgr w sesji `dso` – niezmierzone (bez zrzutu).
- Flaga `StateFlags7351` po każdym przebiegu nieobecna pod wszystkimi 31 kluczami `VolumeCaches`.
- Co robi `DismHost` przez 11 minut po usunięciu folderu – niezmierzone; wolne miejsce w tym czasie zmienia się o mniej niż
  1 MB (Home 43 551 252 480 → 43 551 240 192 B), czyli niczego więcej nie zwalnia. Czy usunięcie flagi przed końcem procesu zmienia wynik – niezmierzone, dlatego
  decyzja: czekać na koniec procesu, flagę usuwać po nim (w `finally`).
- Dokumentacja: `cleanmgr` https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/cleanmgr ,
  `StateFlagsNNNN` (0 = niezaznaczone, 2 = zaznaczone) https://learn.microsoft.com/en-us/troubleshoot/windows-server/backup-and-storage/automating-disk-cleanup-tool .

## 6. Czasy i limity

Limit na jedno narzędzie (zadanie, pkt 6): max(4 × zmierzony, 45 min) dla DISM/SFC, max(4 × zmierzony, 20 min) dla czyszczenia
i cleanmgr. Liczone z najdłuższego pomiaru z obu edycji (`Get-E4B2TimeoutSeconds`).

| Operacja | Home | Pro | Najdłużej | Limit |
|---|---|---|---|---|
| DISM `/RestoreHealth` bez sieci | 74,5 s | 72,1 s | 74,5 s | 2700 s |
| DISM `/RestoreHealth` z siecią (WU aktywny) | 608,4 / 579,6 s | 594,5 / 613,6 s | 613,6 s | 2700 s (4 × 613,6 = 2455 < 2700) |
| DISM `/RestoreHealth /Source:C:\brak /LimitAccess` | 61,2 s | 57,8 s | 61,2 s | – (nie w handlerze) |
| DISM `/ScanHealth` | 79,4 s | 73,3 s | 79,4 s | – |
| SFC `/scannow` | 52,4 s | 53,9 s | 53,9 s | 2700 s |
| DISM `/StartComponentCleanup` | 12,1 s | 11,7 s | 12,1 s | 1200 s |
| DISM `/AnalyzeComponentStore` | 11,6 / 9,6 s | 10,6 / 11,0 s | 11,6 s | – |
| `Delete-DeliveryOptimizationCache -Force` | 0,1 s (pusta), 0,3 s (5,2 GB) | 0,1 s, 0,3 s (5,4 GB) | 0,3 s | 1200 s (skrypt) |
| Zatrzymanie `wuauserv` / `bits` | 0–1,5 s / 0,3 s | 1,3–1,5 s / 0,3 s | 1,5 s | – |
| Start `bits` po czyszczeniu | 4,3 s | 4,3 s | 4,3 s | – |
| cleanmgr Windows.old, K2 (do końca procesu) | 641,3 s | 665,5 s | 665,5 s | 2700 s (4 × 665,5 = 2662 → zaokrąglone w górę do 2700) |
| cleanmgr, K1 (sesja 0) | > 1200 s | > 1200 s | nie kończy się | nie używać z sesji 0 |

**`timeoutSeconds` w `repair.json`:** `repair.system-files` = 2700 (na każde z dwóch narzędzi), `repair.disk-cleanup` = 1200
(StartComponentCleanup; skrypt DO dostaje ten sam limit), `repair.windows-old` = 2700.

**„Zwykle N–M min” do ostrzeżeń:** `system-files` – na VM ok. 2–3 min (DISM 1–1,5 min + SFC ok. 1 min); przy pracującym
Windows Update DISM ok. 10 min; na wolnym dysku lub z realnym uszkodzeniem dłużej (niezmierzone). `disk-cleanup` – na VM
poniżej 1 min (bez cleanmgr). `windows-old` – folder znika w kilka sekund, a Oczyszczanie dysku kończy pracę po ok. 11 min.

**`CliTimeoutSeconds` przypadków VM:** `repair-system-files` offline ok. 130 s → domyślne 600 s wystarcza (zapas: 1200).
`repair-windows-old` → `RunAs='dso'` i `CliTimeoutSeconds` 2700 (z PS Direct cleanmgr się nie kończy).

## 7. R17 – narzędzie po wyjściu programu (Task 6, 2026-10-23)

Pytanie (Ruling R17): czy DISM i SFC uruchomione przez `ServicingToolRunner` (bez Job Objectu, potok stdout bez zamykania
po limicie – R16) kończą pracę, gdy proces, który je uruchomił, wychodzi po limicie czekania, a razem z nim znika koniec
odczytu potoku (narzędzie pisze dalej do zamkniętego potoku). Skrypt jednorazowy poza gitem:
zapis pomiaru w archiwum autora (+ surowe JSON). VM od `czysta-26H2`, bez sieci, na końcu revert, VM `Off`.

| Przebieg | Wywołujący | Home | Pro |
|---|---|---|---|
| A: DISM, CLI `--execute repair.system-files` z katalogiem testowym `timeoutSeconds: 20`, K1 (PS Direct) | CLI wraca `NotConfirmed`, kod 4, po 20 s (raport CLI) i kończy proces | `dism` żyje po wyjściu CLI, kończy się sam po 82,7 s od startu z **kodem 0**; `CBS.log`: `Operation result: 0x0`, `Total Detected Corruption: 0`, `Exec: Processing complete … S_OK`; `dism.log` bez błędów | kod 0 po 89,9 s, ten sam blok |
| B: to samo przez zadanie Harmonogramu `dso` (K2, Interactive + Highest) | zadanie kończy się z wynikiem 4 | kod 0 po 74,2 s, `Operation result: 0x0` | kod 0 po 73,4 s, `Operation result: 0x0` |
| C: SFC przez prawdziwy `ServicingToolRunner` (`DSOptimizer.Windows.dll` w `powershell.exe`, limit 15 s), K1 | `TimedOut = true`, 0 linii (SFC buforuje 4096 B), proces wychodzi | `sfc` żyje, kończy się po 62,3 s z **kodem 0**, segment `CBS.log`: 495 linii `[SR]`, na końcu `Repairing 0 components` / `Repair complete` | kod 0 po 62,1 s, 522 linie `[SR]`, `Repair complete` |
| D: to samo przez zadanie Harmonogramu `dso` (K2) | jw. | kod 0 po 64,5 s, `Repair complete` | kod 0 po 65 s, `Repair complete` |

**Wniosek:** zamknięty potok po wyjściu programu nie przerywa ani DISM, ani SFC (oba kończą się kodem 0 z pełnym wynikiem
w logach), także gdy program startował z zadania Harmonogramu (Job Object zadania nie zabija narzędzia po zakończeniu
akcji). Runner zostaje bez zmian (bez wyjścia do pliku, bez `CREATE_BREAKAWAY_FROM_JOB`). Niezmierzone: Job Object z
`KILL_ON_JOB_CLOSE` założony przez inny program uruchamiający DSOptimizer (np. narzędzie zdalne) i zamknięcie Harmonogramu
w trakcie przez „Zakończ” zadania.

## Próbki do testów (`tests/DSOptimizer.Tests.Unit/Repair/Samples/`)

Bajty wyjścia bez konwersji (`.bin`), z F1, Home i Pro (`-home`/`-pro`). `.gitattributes` oznacza katalog jako `binary`
(bez normalizacji końców linii).

| Plik | Zawartość |
|---|---|
| `dism-restorehealth-healthy-<ed>.bin` | stdout `/RestoreHealth` bez sieci (OEM 852, `\r…\r\n`) |
| `dism-scanhealth-healthy-<ed>.bin` | stdout `/ScanHealth` |
| `dism-restorehealth-nosource-healthy-<ed>.bin` | stdout `/RestoreHealth /Source:C:\brak /LimitAccess` na zdrowym magazynie (kod 0) |
| `dism-startcomponentcleanup-<ed>.bin`, `dism-analyzecomponentstore-before-<ed>.bin` | stdout czyszczenia magazynu i analizy |
| `dism-checkhealth-concurrent-<ed>.bin` | stdout drugiego DISM uruchomionego w trakcie pierwszego |
| `sfc-scannow-noviolations-<ed>.bin` | stdout `sfc /scannow` (UTF-16LE bez BOM, `\r` bez `\n`, `\r\r\n`) |
| `sfc-verifyfile-concurrent-<ed>.bin` | stdout `sfc /verifyfile` w trakcie DISM (krótka próbka UTF-16LE) |
| `sfc-scannow-noviolations-<ed>.cbs.log` | cały segment `CBS.log` z przebiegu SFC (od offsetu sprzed startu) |
| `dism-restorehealth-healthy-<ed>.cbs.log`, `dism-scanhealth-healthy-<ed>.cbs.log` | wycinek `CBS.log`: linia `Corruption detection complete` + blok od „Checking System Update Readiness.” do „Exec: Processing complete” |
| `dism-restorehealth-healthy-<ed>.dism.log` | cały segment `dism.log` z przebiegu `/RestoreHealth` |

Kody wyjścia próbek: wszystkie 0.
