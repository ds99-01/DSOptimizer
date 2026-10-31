# Partia C4 – źródła i mechanizmy (research 2026-10-18)

Zakres (spec Etapu 2, sekcja 5, partia 4): `sec.bitlocker-prevent-auto` (alias `sec_bitlocker_kill`), `sec.bitlocker-decrypt-c` (nowa akcja),
`sec.take-ownership-menu` (aliasy `sec_take_ownership`, `sec_take_ownership_remove`). Źródła pobrane 2026-10-18. Dowodem jest MS Learn,
a skutek w rejestrze lub stanie ustala odczyt na VM po zmianie oficjalnym narzędziem (spec 11). Wszystko poniżej oznaczone „hipoteza”
wymaga potwierdzenia na VM przed wpisaniem do katalogu.

## 0. Co robił v3.2 (kod dawnej wersji v3.2, Python)

- `sec_bitlocker_kill`: jeden skrypt robił dwie rzeczy: `Disable-BitLocker -MountPoint C: -ErrorAction SilentlyContinue` (błąd połknięty,
  komunikat „odszyfrowanie uruchomione” zawsze) oraz `PreventDeviceEncryption = 1` (DWORD) w `HKLM\SYSTEM\CurrentControlSet\Control\BitLocker`.
  Check czytał tylko wartość rejestru. Opis obiecywał „obcina wydajność NVMe o 45%” – bez źródła, w 4.0 nie wraca.
- `sec_take_ownership`: `reg.exe add` do `HKCR\*\shell\runas` i `HKCR\Directory\shell\runas` z etykietą, `HasLUAShield`, `NoWorkingDirectory`
  i komendą `cmd.exe /c takeown /f "%1" && icacls "%1" /grant administrators:F` (dla folderów `/r /d y` i `/t`).
  Błędy (mapa drogowa autora, poz. 21): nazwa grupy `administrators` nie istnieje na polskim Windows, `_remove` kasował cały klucz `runas` (także cudzy),
  rekurencja na folderze systemowym jednym klikiem, polskie znaki psute przez argv `reg.exe`.

## 1. sec.bitlocker-prevent-auto (alias `sec_bitlocker_kill`)

**Źródła**
- MS Learn, „BitLocker drive encryption in Windows 11 for OEMs” (windows-hardware, ms.date 2023-04-26, aktualizacja strony 2025-08-12),
  https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/oem-bitlocker
  - „BitLocker automatic device encryption starts during Out-of-box (OOBE) experience. However, protection is enabled (armed) only after users sign in
    with a Microsoft Account or an Azure Active Directory account. Until that, protection is suspended and data is not protected. BitLocker automatic
    device encryption is not enabled with local accounts”.
  - Od 24H2: „Auto-DE doesn't depend on Hardware Security Test Interface (HSTI) / Modern Standby anymore” i „Auto-DE is enabled even if untrusted
    Direct Memory Access (DMA) buses / interfaces are detected”. Warunki: TPM (1.2 lub 2.0), UEFI Secure Boot, Platform Secure Boot.
  - Sekcja „Disable BitLocker automatic device encryption”: „OEMs can choose to disable device encryption and instead implement their own encryption
    technology on a device. Disabling encryption outside of this scenario is prohibited as it violates the Windows 11 licensing requirements and the
    Windows 11 *secure by default* principle.” Mechanizm: Unattend `PreventDeviceEncryption` = True albo „update the
    HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\BitLocker registry key: Value: PreventDeviceEncryption equal to True (1).”
    Dalej: „We do not recommend setting this registry key on devices with Recall feature.”
- MS Learn, Unattend „PreventDeviceEncryption” (ms.date 2017-05-02): wartości `false` (domyślna, szyfruje automatycznie) / `true`; przebiegi
  `offlineServicing`, `specialize`, `auditSystem`, `oobeSystem`; „These settings only apply to Windows 8” (strona nieaktualizowana; strona OEM z 2025
  nadal wskazuje to ustawienie dla Windows 11).
  https://learn.microsoft.com/en-us/windows-hardware/customize/desktop/unattend/microsoft-windows-securestartup-filterdriver-preventdeviceencryption
- Wskazówka (nie dowód): wątek ElevenForum „How to prevent automatic drive encryption in Windows 11?” – ta sama wartość, także offline przez `reg load`.

**Wnioski**
- Mechanizm i typ są udokumentowane przez MS: DWORD `PreventDeviceEncryption` = 1, klucz `HKLM\SYSTEM\CurrentControlSet\Control\BitLocker`.
  Typ DWORD jest wnioskiem z „True (1)” → potwierdza odczyt VM (czy klucz istnieje na świeżym 26H2, czy wartość istnieje).
- **Skutek po OOBE nie jest opisany przez MS.** Auto-szyfrowanie zaczyna się w OOBE, więc tweak stosowany na działającym systemie
  nie odszyfrowuje dysku i nie wiadomo z dokumentacji, czy zatrzyma szyfrowanie już rozpoczęte. Co mówi MS: na komputerze z kontem lokalnym
  auto-szyfrowanie się nie włącza. **Rozbieżność z obserwacją VM (2026-10-18/19):** po starcie checkpointu na Home i Pro z kontem lokalnym
  auto-szyfrowanie urządzenia było w toku (zob. „Wyniki VM”). Opis pozycji może obiecać tylko „ustawia wartość, którą Microsoft podaje jako blokadę automatycznego szyfrowania
  urządzenia”, bez obietnicy skutku po OOBE. Odszyfrowanie to osobna akcja (sekcja 2).
- **Uwaga – licencja:** MS w dokumencie dla OEM pisze, że wyłączanie szyfrowania urządzenia poza scenariuszem OEM „is prohibited
  as it violates the Windows 11 licensing requirements”. Zdanie dotyczy producentów (dokument „for OEMs”); dla użytkownika końcowego Windows ma
  w Ustawieniach przełącznik „Szyfrowanie urządzenia”, więc samo wyłączenie szyfrowania nie jest obejściem licencji.
  To interpretacja, nie porada prawna. W docs pozycji cytat MS dosłownie.
- Ostrzeżenia: „We do not recommend setting this registry key on devices with Recall feature” (dosłownie); dane na dysku nie są chronione
  po kradzieży urządzenia; na komputerach firmowych polityka organizacji może wymagać szyfrowania.
- Detekcja: tylko rejestr (`registry[]`, API C#). `Applied` = wartość 1. To uczciwe, bo pozycja obiecuje tylko zapis wartości.
  `original` = `<RemoveEntry>`, jeśli VM pokaże brak wartości na Home i Pro. Bez `requiresReboot` (MS nic nie mówi o restarcie, a skutek dotyczy
  przyszłego OOBE/inicjalizacji szyfrowania urządzenia).
- Ryzyko `caution` (spec). Na VM: cykl apply/detect/undo/snapshot jak każdy tweak rejestrowy. Skutek „szyfrowanie nie startuje” → `Skipped`
  (wymaga nowego OOBE z kontem Microsoft i sprzętem z TPM; VM ma konto lokalne).

## 2. sec.bitlocker-decrypt-c (kind=action, nowa)

**Źródła**
- MS Learn, `Win32_EncryptableVolume.Decrypt` (namespace `Root\CIMV2\Security\MicrosoftVolumeEncryption`, ms.date 2018-05-31),
  https://learn.microsoft.com/en-us/windows/win32/secprov/decrypt-win32-encryptablevolume
  - „begins decryption of a fully encrypted volume, or resumes decryption of a partially encrypted volume”; „When encryption is paused or in-progress,
    this method reverts the encryption and begins decryption. After decryption completes, all key protectors on this volume are removed”.
  - „This method returns immediately. If the volume is already fully decrypted and no other errors exist, this method returns 0.”
  - Kody: `FVE_E_LOCKED_VOLUME` 0x80310000, `FVE_E_AUTOUNLOCK_ENABLED` 0x80310029 („keys used to automatically unlock data volumes are available.
    Use ClearAllAutoUnlockKeys to remove these keys”).
  - „Calling the Decrypt method leaves data unprotected.”
- MS Learn, `Win32_EncryptableVolume.GetConversionStatus` (ms.date 2018-05-31),
  https://learn.microsoft.com/en-us/windows/win32/secprov/getconversionstatus-win32-encryptablevolume
  - `ConversionStatus`: 0 FullyDecrypted, 1 FullyEncrypted, 2 EncryptionInProgress, 3 DecryptionInProgress, 4 EncryptionPaused, 5 DecryptionPaused.
  - `EncryptionPercentage` 0–100: „an encryption percentage of 0 or 100 does not necessarily indicate that the disk is fully decrypted or fully
    encrypted. Always use ConversionStatus”.
- MS Learn, `Disable-BitLocker` (moduł BitLocker): „removes all key protectors and begins decrypting”; „If the volume that hosts the operating system
  contains any automatic unlocking keys, the cmdlet does not proceed”. Przykład: „BitLocker begins decrypting data on C: immediately.”
  https://learn.microsoft.com/en-us/powershell/module/bitlocker/disable-bitlocker
- MS Learn, `Get-BitLockerVolume`: `VolumeStatus`, `EncryptionPercentage`, `ProtectionStatus`, `KeyProtector`.
  https://learn.microsoft.com/en-us/powershell/module/bitlocker/get-bitlockervolume

**Wnioski**
- Mechanizm: WMI `Win32_EncryptableVolume` (filtr `DriveLetter = 'C:'`), `GetConversionStatus` przed, `Decrypt`, `GetConversionStatus` po.
  Kody numeryczne i enum, bez tekstu zależnego od języka (spec 3). Cmdlety BitLocker jako alternatywa, jeśli VM pokaże brak klasy WMI.
- Dostępność na Home: **nie rozstrzygnięte.** Szyfrowanie urządzenia działa na Home, ale strony MS nie mówią, czy na Home jest klasa WMI
  i moduł BitLocker. Odczyt VM (Home i Pro): czy klasa istnieje, `ConversionStatus` na świeżym dysku, czy działa `Get-BitLockerVolume`.
- `Decrypt` wraca od razu, a Windows odszyfrowuje w tle. Wniosek do decyzji autora: akcja nie musi czekać godzinami. Wystarczy uruchomić,
  odczytać status i procent, a ponowne uruchomienie pokaże postęp. To spełnia „bez timeoutu” ze specu, bo nic nie jest zabijane po 30 s.
- Ścieżki wyniku (propozycja):
  - `ConversionStatus` = 0 przed → `Executed`, komunikat „C: nie jest zaszyfrowany – nic nie zmieniono” (cel już spełniony, inaczej niż
    `net.network-private`, gdzie brak profilu oznacza brak efektu).
  - 1/2/4 → `Decrypt` → status po: 3 (lub 0 dla bardzo małego woluminu) → `Executed` z procentem; inny status → `Failed`.
  - 3/5 → `Decrypt` (MS: zachowuje się jak wznowienie) → `Executed` „odszyfrowywanie w toku: N%”.
  - `FVE_E_AUTOUNLOCK_ENABLED` → `Failed` z komunikatem, bez automatycznego `ClearAllAutoUnlockKeys` (zmiana dla innych dysków; decyzja użytkownika).
  - `FVE_E_LOCKED_VOLUME` i inny kod ≠ 0 → `Failed` z kodem hex.
- Ostrzeżenia: brak cofnięcia (ponowne szyfrowanie to osobna decyzja użytkownika w Ustawieniach); dane bez ochrony; na komputerze z kontem
  Microsoft klucz odzyskiwania zostaje na koncie (znaczenie klucza po odszyfrowaniu nie wynika ze źródeł MS); polityka firmowa może ponownie zaszyfrować dysk;
  dopóki `sec.bitlocker-prevent-auto` nie jest zastosowany, auto-szyfrowanie może ruszyć ponownie (niezmierzone – MS nie opisuje).
- Potwierdzenie w UI (spec 8: każda akcja wymaga potwierdzenia w cienkim UI). CLI nie pyta.
- **Test VM – ścieżka główna da się sprawdzić na Pro:** VM Gen2 ma vTPM i Secure Boot, więc na Pro można w VM zaszyfrować C: oficjalnym cmdletem
  (`Enable-BitLocker -UsedSpaceOnly -TpmProtector` albo przez WMI), poczekać na `FullyEncrypted`, uruchomić akcję i sprawdzić `DecryptionInProgress`
  lub `FullyDecrypted`. Hipoteza: na 16 GB VHDX z szyfrowaniem tylko zajętego miejsca to minuty. Na Home tylko ścieżka „już odszyfrowany”
  (Home nie ma zarządzania BitLockerem). VM wraca do `czysta-26H2` (szyfrowanie znika razem z checkpointem).

## 3. sec.take-ownership-menu (aliasy `sec_take_ownership`, `sec_take_ownership_remove`)

**Źródła**
- MS Learn, „Creating Shortcut Menu Handlers” (ms.date 2018-05-31, aktualizacja 2025-07-03),
  https://learn.microsoft.com/en-us/windows/win32/shell/context-menu-handlers
  - Statyczny verb: `<ProgID>\shell\<verb>` z wartością domyślną = etykieta (dla verbów niekanonicznych) i podkluczem `command` (wartość domyślna =
    linia poleceń, `%1` = ścieżka elementu).
  - `Extended` (REG_SZ bez danych) – verb widoczny tylko z Shift; `Position = Top | Bottom`; `MultiSelectModel = Single | Document | Player`.
  - Menu kaskadowe z rejestru (Windows 7+): `MUIVerb` + `SubCommands` albo `MUIVerb` + podklucz `ExtendedSubCommandsKey\Shell\<verb>\command`.
  - `AppliesTo` (AQS) decyduje, czy verb jest widoczny; `HasLUAShield` pokazuje tarczę UAC; przykład MS: werby BitLockera w `HKCR\Drive`
    z `AppliesTo = System.Volume.BitlockerProtection:=2`. AQS działa tylko na „fast properties”.
- MS Learn, `ShellExecuteW`: verb `runas` – „Launches an application as Administrator. User Account Control (UAC) will prompt the user for consent”.
  https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shellexecutew
- MS Learn, „Merged View of HKEY_CLASSES_ROOT”: HKCR łączy `HKLM\Software\Classes` i `HKCU\Software\Classes`; `*`, `Drive` i `Folder` są na liście
  kluczy obecnych w obu gałęziach. Wniosek: tweak `scope=machine` pisze jawnie do `HKLM\SOFTWARE\Classes\...`, nie do `HKCR\`.
  https://learn.microsoft.com/en-us/windows/win32/sysinfo/merged-view-of-hkey-classes-root
- MS Learn, `takeown` (ms.date 2025-11-04): `/a` – „Gives ownership to the Administrators group instead of the current user”; `/r` rekurencja;
  `/d {Y|N}` tylko z `/r`, odpowiedź na monit przy braku List Folder/Read („Y – … The directory permissions are replaced with permission granting
  the user full control only”).
  https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/takeown
- MS Learn, `icacls` (ms.date 2025-06-06): „SIDs may be in either numerical or friendly name form. If you use a numerical form, affix the wildcard
  character * to the beginning of the SID”; `/grant` dodaje do jawnych uprawnień (`:r` zastępuje); `/t`, `/c`, `/l` (operacja na linku symbolicznym,
  nie na celu), `/q`; maski `F`, dziedziczenie `(OI)(CI)`.
  https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/icacls
- SID `S-1-5-32-544` = wbudowana grupa Administratorzy (well-known SID; nazwa zależna od języka, SID nie).

**Wnioski i projekt (do potwierdzenia na VM)**
- Klucz własny zamiast `runas`: `HKLM\SOFTWARE\Classes\*\shell\DSOptimizer.TakeOwnership` (pliki) i
  `HKLM\SOFTWARE\Classes\Directory\shell\DSOptimizer.TakeOwnership` (foldery). Undo usuwa tylko te klucze (A1: klucze utworzone przez Apply,
  od najgłębszego, tylko puste po przywróceniu wartości). Cudzy `runas` nie jest dotykany.
- Etykieta: wartość `MUIVerb` (REG_SZ) „Przejmij na własność” zapisana przez API rejestru C# (Unicode), bez `reg.exe` i bez argv.
- **Podniesienie uprawnień – najważniejsze otwarte pytanie.** Tylko verb o nazwie `runas` jest uruchamiany przez powłokę z UAC (ShellExecute).
  `HasLUAShield` pokazuje tarczę, ale nie podnosi uprawnień. Warianty:
  - **(A, rekomendowany) Menu kaskadowe z verbem `runas` we własnym kluczu:** `...\DSOptimizer.TakeOwnership` z `MUIVerb` i podkluczem
    `ExtendedSubCommandsKey\Shell\runas` (`MUIVerb`, `HasLUAShield`, `command`). Verb nazywa się `runas`, ale leży we własnym poddrzewie,
    więc nie nadpisuje niczego cudzego. Całość w `registry[]`, bez skryptów, bez dodatkowego interpretera. Koszt: dwa kliknięcia (pozycja → podmenu).
    Hipoteza: powłoka podnosi uprawnienia także dla verbu `runas` w podmenu – **VM musi to pokazać** (proces `cmd.exe`/`takeown.exe` z pełnym tokenem).
  - (B) Pozycja najwyższego poziomu i pośredni launcher (PowerShell `Start-Process -Verb RunAs`), który przekazuje ścieżkę do drugiego procesu.
    Odradzam: ścieżka pliku przechodzi przez dwa parsery linii poleceń, co jest wrażliwe na nietypowe nazwy plików i trudne do uczciwego
    przetestowania. Wymaga też PowerShella w menu kontekstowym (heurystyka AV).
  - (C) Bez podniesienia: komenda działa tylko z poziomu procesu administratora. Bezużyteczne przy UAC (Explorer działa bez podniesienia).
- Komenda (hipoteza, wiąże test VM na polskim 26H2):
  - pliki: `cmd.exe /c takeown /f "%1" /a && icacls "%1" /grant *S-1-5-32-544:F /c /l /q`
  - foldery: rekurencja `takeown /r /d Y` + `icacls /t` – **do decyzji autora** (patrz niżej). Otwarte: czy `/d Y` działa na polskim Windows
    (MS podaje `Y|N`, nie mówi o lokalizacji odpowiedzi). Alternatywa bez monitu: `icacls "%1" /setowner *S-1-5-32-544 /t /c /l` (MS: „Changes the
    owner of all matching files to the specified user”) – czy działa bez dodatkowych przywilejów, pokaże VM.
  - `/l` w `icacls`: operacja na linku, nie na celu (ochrona przed przejęciem celu dowiązania w drzewie).
  - Ścieżka w cudzysłowie: nazwy plików w Windows nie mogą zawierać `"`. Otwarte na VM: zachowanie `cmd.exe /c` dla nazw z `%`, `&`, `^`, `!`,
    spacjami i polskimi znakami (lista nazw testowych w eksploracji; tylko na VM).
- Wykluczenia (spec): korzenie dysków i katalog Windows.
  - Korzenie dysków: verb rejestrowany na `*` i `Directory`, nie na `Drive`. Hipoteza: verb z `Directory` nie pojawia się na korzeniu dysku
    (MS wymienia `Drive` jako osobny obiekt powłoki) – sprawdzenie na VM przez listę verbów obiektu (Shell.Application `Verbs()`), nie przez zrzut.
  - Katalog Windows: `AppliesTo` z warunkiem AQS na `System.ItemPathDisplay` (wyklucza `C:\Windows` i wszystko pod nim). Otwarte: składnia warunku
    „nie zaczyna się od” w AQS, czy `System.ItemPathDisplay` jest „fast property” w Explorerze, oraz to, że ścieżka jest stała (`C:\Windows`),
    a Windows może być na innej literze. Jeśli AQS nie da się uczciwie potwierdzić na VM → pozycja tylko dla plików albo bez rekurencji (decyzja).
  - Propozycja dodatkowa: `Extended` (pozycja widoczna tylko z Shift), żeby uprawnienia nie były zmieniane „jednym kliknięciem” z codziennego menu.
- Menu Windows 11: statyczne verby trafiają do „Pokaż więcej opcji” (klasyczne menu), nie do nowego menu kontekstowego. Hipoteza – do zapisania
  w docs po smoke na VM.
- Detekcja: wszystkie wartości z `registry[]` równe docelowym → `Applied`; brak kluczy → `NotApplied`; część → `Partial`. `original` =
  `<RemoveEntry>` dla każdej wartości (klucze nie istnieją na świeżym 26H2 – potwierdza odczyt VM).
- Ostrzeżenia: zmiana właściciela i uprawnień plików systemowych może zepsuć aktualizacje i Windows Resource Protection (plików nie da się potem
  łatwo przywrócić do stanu TrustedInstaller; undo tweaka usuwa tylko pozycję menu, nie cofa zmian uprawnień zrobionych z menu);
  rekurencja na dużym drzewie trwa długo.
- Test VM: cykl apply/detect/undo/snapshot (rejestr, A1 sprząta klucze). Działanie z menu (UAC, skutek na pliku testowym w `C:\DsoTest`)
  → eksploracja: verb wywołany programowo (`Shell.Application` → `InvokeVerb`) albo `Skipped` z powodem, jeśli UAC wymaga interakcji
  (PS Direct jako `Administrator` z pełnym tokenem może nie pokazać monitu – to też odpowiedź do zapisania).

## Tabela zbiorcza

| ID | Rodzaj | Ryzyko | Mechanizm | Detekcja | Undo | Test VM |
|---|---|---|---|---|---|---|
| `sec.bitlocker-prevent-auto` | tweak, machine | caution | DWORD `PreventDeviceEncryption` = 1 (MS OEM) | rejestr | snapshot / `<RemoveEntry>` | Home+Pro cykl; skutek Skipped |
| `sec.bitlocker-decrypt-c` | action | caution | WMI `Win32_EncryptableVolume.Decrypt` + `GetConversionStatus` | odczyt zwrotny statusu | brak (ostrzeżenie) | Pro: szyfrowanie w VM → akcja; Home: „już odszyfrowany” |
| `sec.take-ownership-menu` | tweak, machine | caution | własny klucz w `HKLM\SOFTWARE\Classes\{*,Directory}\shell`, verb `runas` w podmenu, `takeown /a` + `icacls *S-1-5-32-544` | rejestr | A1 usuwa utworzone klucze | Home+Pro cykl; działanie z menu w eksploracji |

## Otwarte pytania dla eksploracji VM (zbiorczo)

1. `HKLM\SYSTEM\CurrentControlSet\Control\BitLocker`: czy klucz istnieje, czy `PreventDeviceEncryption` istnieje (Home, Pro).
2. `Root\CIMV2\Security\MicrosoftVolumeEncryption` `Win32_EncryptableVolume` na Home i Pro: czy klasa jest, `GetConversionStatus` dla C:,
   `ProtectionStatus`; czy działa `Get-BitLockerVolume`. Pro: `Enable-BitLocker -MountPoint C: -UsedSpaceOnly -TpmProtector` → czas do
   `FullyEncrypted` → `Decrypt` → status po (kod, `ConversionStatus`, procent) → czas do `FullyDecrypted`.
3. `HKLM\SOFTWARE\Classes\*\shell` i `Directory\shell`: lista podkluczy na świeżym 26H2 (czy `runas` istnieje, czy jest coś do kolizji).
4. Menu: zapis kluczy wariantu A przez API (w eksploracji przez `New-Item`/`Set-ItemProperty` na `Registry::HKEY_LOCAL_MACHINE\...`), lista verbów
   dla pliku, folderu, `C:\` i `C:\Windows\System32` (Shell.Application), wywołanie verbu na pliku i folderze testowym, właściciel i ACL po
   (`Get-Acl`, SID właściciela), czy proces miał pełny token.
5. `takeown /r /d Y` na polskim 26H2 (kod wyjścia, czy pyta) i `icacls /setowner *S-1-5-32-544 /t /c /l` jako alternatywa.
6. `cmd.exe /c` z nazwami testowymi (spacje, polskie znaki, `%`, `&`, `^`, `!`) – czy cel jest właściwym plikiem.
7. CARRY z C3: polityka „Enable insecure guest logons” (wartość polityki ustawiona w VM) → odczyt `Get-SmbClientConfiguration`.
   Nazwę i ścieżkę wartości polityki brać ze źródła MS przed eksploracją (Policy CSP / ADMX), nie z pamięci.

## Wartości do ręcznego sprawdzenia (przed wpisaniem do katalogu)

- Typ `PreventDeviceEncryption` (DWORD) – z odczytu VM po zapisie i z tekstu MS „True (1)”.
- Nazwa podklucza `ExtendedSubCommandsKey` i struktura `Shell\<verb>\command` – z przykładu MS (strona context-menu-handlers) i z VM.
- Składnia `AppliesTo` dla wykluczenia katalogu Windows – z dokumentacji AQS MS przed eksploracją; bez potwierdzenia na VM nie wchodzi.

## Wyniki VM (2026-10-18, eksploracja C4)

Źródło: `tests/vm/explore/Invoke-C4Exploration.ps1` na dwóch czystych VM Hyper-V z checkpointu `czysta-26H2`
(zapis pomiaru w archiwum autora; JSON-y poza gitem). Zapis to surowe
fakty; poniżej przełożenie na pytania 1–7. `Exists=false` (wartość nie istnieje) to nie to samo co wartość `0`.

**Metadane (obie VM identyczne, gdzie nie zaznaczono):** build `26300`, UBR `9457` (26300.9457), `DisplayVersion=26H2`,
`Culture=UICulture=pl-PL`, `PSVersion=5.1.26100.9444`, `Is64BitProcess=true`. Home: `EditionID=Core`,
`ProductName="Windows 10 Home"` (ProductName w rejestrze wciąż „Windows 10" – cytat dosłowny), zebrane 2026-10-18 22:51.
Pro: `EditionID=Professional`, `ProductName="Windows 10 Pro"`, zebrane 2026-10-18 22:56.
**Token sondy (obie VM):** wbudowany `Administrator` (RID 500), `IsInRoleAdministrator=true`, `ImpersonationLevel=None`,
`GroupSids` zawiera `S-1-5-32-544`, `HasHighIntegritySid=false`, `IntegritySids=[]`. Puste `IntegritySids` to brak wylistowania
SID poziomu integralności przez `WindowsIdentity.Groups`, a nie dowód medium integrity: wbudowany Administrator działa z pełnym
tokenem i bez filtrowania UAC (PS Direct). Ma to wpływ na werdykt 4c (niżej).

### Pytanie 1 — klucz BitLocker i `PreventDeviceEncryption`
- Klucz `HKLM\SYSTEM\CurrentControlSet\Control\BitLocker`: **istnieje na Home i Pro** (`KeyExists=true`). Bez wartości wprost
  w kluczu (`Values=[]`). Podklucze: Home `[AutoDE, KeyBackupMonitor, PcrMon, RecoveryPasswordRotation, Telemetry]`,
  Pro `[AutoDE, PcrMon, RecoveryPasswordRotation, Telemetry]` (Home ma dodatkowo `KeyBackupMonitor`).
- `PreventDeviceEncryption`: `Exists=false` na Home i Pro (wartość nie istnieje; `Kind=Value=null`).
- **Odpowiedź (pewność wysoka, odczyt bezpośredni):** klucz obecny na czystym 26H2 (obie edycje), wartość `PreventDeviceEncryption`
  nieobecna. Stąd `original = <RemoveEntry>`; `KeyPresentAfterUndo = true` (undo usuwa tylko wartość, systemowy klucz
  `Control\BitLocker` przetrwa). Typ wartości potwierdzony osobno w pytaniu 7 jako `DWord` (mechanizm `CreateSubKey.SetValue`).

### Pytanie 2 — `Win32_EncryptableVolume`, `GetConversionStatus`, `ProtectionStatus`, `Get-BitLockerVolume`, moduł BitLocker
- Moduł BitLocker: katalog `...\Modules\BitLocker` **istnieje na Home i Pro**.
- Klasa `Win32_EncryptableVolume` (`root\CIMV2\Security\MicrosoftVolumeEncryption`, filtr `DriveLetter='C:'`): **jest i zwraca
  1 wolumin na Home i Pro** (`Ok=true, Count=1`; bez błędu). Pola instancji: `DeviceID, DriveLetter, EncryptionMethod,
  IsVolumeInitializedForProtection, PersistentVolumeID, ProtectionStatus, VolumeType`.
- `GetConversionStatus` — rzeczywiste pola (obie VM, typ `System.UInt32`): `ReturnValue`, `ConversionStatus`,
  `EncryptionPercentage`, `EncryptionFlags`, `WipingPercentage`, `WipingStatus` (+ `PSComputerName`). `GetProtectionStatus`:
  `ProtectionStatus`, `ReturnValue` (`UInt32`, + `PSComputerName`). Kształt identyczny na Home i Pro.
- Wartości:
  - **Home:** `ReturnValue=0`, `ConversionStatus=2` (EncryptionInProgress), `EncryptionPercentage=78`, `ProtectionStatus=0`;
    `IsVolumeInitializedForProtection=True`, `EncryptionMethod=6`. `Get-BitLockerVolume` działa: `VolumeStatus=EncryptionInProgress`
    (2), `ProtectionStatus=Off` (0), `EncryptionPercentage=78`, `EncryptionMethod=XtsAes128`, `KeyProtectorCount=0`.
  - **Pro:** `ReturnValue=0`, `ConversionStatus=0` (FullyDecrypted), `EncryptionPercentage=0`, `ProtectionStatus=0`;
    `IsVolumeInitializedForProtection=False`, `EncryptionMethod=0`. `Get-BitLockerVolume`: `VolumeStatus=FullyDecrypted` (0),
    `ProtectionStatus=Off` (0), `EncryptionPercentage=0`, `EncryptionMethod=None`, `KeyProtectorCount=0`.
- **Odpowiedź (pewność wysoka):** klasa WMI i moduł BitLocker są dostępne także na Home (obala hipotezę „Home nie ma zarządzania
  BitLockerem"); odczyt statusu i `Get-BitLockerVolume` działają na obu edycjach. Kody i enumy numeryczne (bez tekstu zależnego
  od języka) — zgodne ze spec 3.
- **Nie rozstrzygnięte (decyzja D1):** ścieżka główna akcji `sec.bitlocker-decrypt-c` (zaszyfrowanie C: → `Decrypt` → status po)
  NIE była testowana (BitLocker w VM tylko odczyt). Uwaga faktograficzna: na Home auto-DE było **w toku** (`ConversionStatus=2`,
  78%, `ProtectionStatus=Off`, 0 protektorów) — spójne z sekcją 1 (szyfrowanie startuje w OOBE, uzbrojenie dopiero po koncie MS);
  Pro w tej eksploracji miała `ConversionStatus=0`. Korekta po pełnym przebiegu VM `vm-run-20261019-002807` (2026-10-19):
  auto-szyfrowanie urządzenia działa po starcie checkpointu na Home i Pro (konto lokalne), a postęp zmienia się między
  uruchomieniami (eksploracja 2026-10-18 22:45/22:51: Home 78%, Pro 0%; przebieg 2026-10-19: Home 6%, Pro 78%).

### Pytanie 3 — podklucze shell pięciu klas (kolizje, `runas`)
Odczyt `HKLM\SOFTWARE\Classes\{*,Directory,Drive,Folder,AllFileSystemObjects}\shell` (wszystkie klucze istnieją na obu VM):

| Klasa | Home | Pro |
|---|---|---|
| `*` | pintohomefile, removeproperties, UpdateEncryptionSettingsWork | (jak Home) |
| `Directory` | cmd, find, Powershell, UpdateEncryptionSettings | (jak Home) |
| `Drive` | cmd, find, pintohome, Powershell, unlock-bde | change-passphrase, change-pin, cmd, encrypt-bde, encrypt-bde-elev, find, manage-bde, pintohome, Powershell, resume-bde, resume-bde-elev, unlock-bde |
| `Folder` | explore, open, opennewprocess, opennewtab, opennewwindow, pintohome | (jak Home) |
| `AllFileSystemObjects` | LaunchWorkfoldersControl, ModernProperties | LaunchWorkfoldersControl, ModernProperties, OfflineFilesLaunchSyncCenter |

- **Odpowiedź (pewność wysoka):** verb `runas` **nie istnieje** w żadnej z pięciu klas (ani `DSOptimizer.TakeOwnership`) — brak
  kolizji; własny klucz niczego nie nadpisze. Różnice edycji dotyczą tylko `Drive` (Pro ma pełny zestaw verbów BitLocker:
  `encrypt-bde`, `manage-bde`, `resume-bde` itd.) i jednego verbu w `AllFileSystemObjects` na Pro.

### Pytanie 4a/4b — lista `Verbs()` dla wariantów menu i AppliesTo
- Historia: w przebiegu 1 nie rozstrzygnięte, bo podproces dostał 7 celów jako jeden string (PS 5.1 `@(ConvertFrom-Json)` opakował
  tablicę) i każdy listing zwrócił `Found=false`; zapisy rejestru wariantów były kompletne. Odpowiedzi: **„Przebieg 2"** niżej.

### Pytanie 4c — `InvokeVerb`, właściciel po, token, werdykt
Próby identyczne na Home i Pro (struktury rejestrowane pod `*` i `Directory`, element testowy w `C:\DsoTest\p4c-*`):

| Struktura / verb | File/Folder Found+Invoked | Właściciel pliku/folderu | Werdykt skryptu |
|---|---|---|---|
| Plain / `DSOptimizer.TakeOwnership` | tak / tak | zmieniony seed→`S-1-5-32-544` | podniesienie potwierdzone |
| Subkey / `DSOptimizer.TakeOwnership` (nadrzędny) | nie wykonany | bez zmian | nie rozstrzygnięte |
| Subkey / `runas` | tak / tak | bez zmian | brak podniesienia |
| ValuePath / `DSOptimizer.TakeOwnership` (nadrzędny) | nie wykonany | bez zmian | nie rozstrzygnięte |
| ValuePath / `runas` | tak / tak | bez zmian | brak podniesienia |

- Fakty: tylko verb Plain (płaski, wywołany po nazwie klucza) uruchomił komendę i zmienił właściciela na `S-1-5-32-544`; verb
  jest widoczny w `Verbs()` jako „DSO: Przejmij na własność". Dla struktur Subkey/ValuePath `InvokeVerb('runas')` znalazł element
  i raportuje `Invoked=true`, ale właściciel **bez zmian** (komenda z podmenu nie wykonała się tą drogą); nadrzędny verb
  `DSOptimizer.TakeOwnership` w tych strukturach był nieinwokowalny (brak własnego `command`).
- **Rzetelna korekta werdyktu:** „podniesienie potwierdzone" dla Plain jest **niewiarygodne** — proces sondy to wbudowany
  Administrator z pełnym tokenem bez UAC (`IntegritySids=[]` to brak wylistowania, nie medium integrity), więc zmiana właściciela
  nie dowodzi, że powłoka dodała podniesienie; każda uruchomiona komenda zmieniłaby właściciela tak samo (dokładnie pułapka
  z projektu `Get-C4ElevationVerdict`, której detekcja `HasHighIntegritySid` tu zawiodła).
- **Odpowiedź (G1): nie rozstrzygnięte** — czy verb `runas` w podmenu `ExtendedSubCommandsKey` podnosi uprawnienia. Powód łączny:
  (a) brak UAC w PS Direct jako Administrator; (b) `InvokeVerb('runas')` na strukturach Subkey/ValuePath nie uruchomił komendy
  (właściciel bez zmian); (c) w przebiegu 1 sonda widoczności verbów (4a/b) była zepsuta (naprawiona w przebiegu 2, który
  powtórzył 4c z tym samym wynikiem). Wymaga testu z sesji użytkownika z UAC (nie PS Direct).

### Pytanie 5 — `takeown /r /d Y` i `icacls /setowner` na polskim 26H2
Drzewa z właścicielem wyjściowym = konto `dso` (RID 1000), `Deny ListDirectory` na „podfolder bez list" (dla bieżącego użytkownika).
Wyniki identyczne na Home i Pro:
- `takeown /f <folder> /a /r /d Y`: **exit 0, stderr pusty, bez monitu i bez zawieszenia** (Home 44 ms, Pro 101 ms). Wszystkie
  4 elementy (folder, `wewnatrz.txt`, `podfolder bez list`, `podfolder bez list\ukryty.txt`) → właściciel `S-1-5-32-544`.
  Rekurencja objęła podfolder bez List Folder **oraz** plik w nim. `/d Y` zadziałało na polskim Windows.
- `icacls <folder> /setowner *S-1-5-32-544 /t /c /l`: **exit 0** (dzięki `/c`), ale stderr
  „`...\podfolder bez list\*: Odmowa dostępu`" i „Successfully processed 3 files; Failed processing 1 files". Zmieniony właściciel:
  folder, `wewnatrz.txt`, `podfolder bez list`; **`ukryty.txt` pozostał przy seed** (RID 1000) — rekurencja nie dotarła do pliku
  w folderze bez List Folder.
- **Odpowiedź (G7, pewność wysoka):** `takeown /a /r /d Y` zadziałał bez monitu (exit 0) i objął pełne drzewo łącznie z podfolderem
  bez List Folder i jego plikiem; `icacls /setowner` zakończył exit 0, lecz z „Odmowa dostępu" i bez objęcia pliku w podfolderze
  bez List Folder. Do rekurencji na folderach pełne pokrycie daje `takeown`.

### Pytanie 6 — `cmd.exe /c` z nazwami specjalnymi (spacje, polskie, `%`, `&`, `^`, `!`)
`%1` podstawione dosłownie (jak powłoka), sprawdzany właściciel celu i wszystkie zmiany w `C:\DsoTest`. Identycznie Home i Pro:

| Nazwa | Wynik |
|---|---|
| spacje („spacja w nazwie") | exit 0, zmieniony tylko cel (plik; folder + `dziecko.txt`), 0 ubocznych |
| polskie („zażółć gęślą jaźń") | exit 0, tylko cel, 0 ubocznych |
| `&` („amper & koniec") | exit 0, tylko cel, 0 ubocznych |
| `^` („daszek ^ test") | exit 0, tylko cel, 0 ubocznych |
| `!` („wykrzyknik! test") | exit 0, tylko cel, 0 ubocznych |
| `%` („procent %PATH% 50%") | **exit 1, 0 zmian**, właściciel celu bez zmian |
| mieszane („mix %TEMP% & ^ ! zażółć") | **exit 1, 0 zmian**, właściciel celu bez zmian |

- **Odpowiedź (G8, pewność wysoka):** nie wszystkie nazwy dały właściwy cel — nazwy zawierające `%` (rozwijane przez `cmd.exe`
  jako zmienne środowiskowe, także wewnątrz cudzysłowu) **zawiodły** (exit 1, nic nie zmienione); spacje, polskie znaki, `&`, `^`, `!`
  trafiły poprawnie. Istotne dla bezpieczeństwa: w żadnym przypadku (również w błędnych) **nie zmienił się właściciel innego pliku**
  (`StrayChangedPaths` puste we wszystkich 14 przypadkach). Wniosek projektowy: linia `cmd.exe /c` jest niebezpieczna dla nazw z `%`
  (niezrealizowany cel), ale nie powoduje szkody ubocznej.

### Pytanie 7 — polityka „Enable insecure guest logons"
Wartość `AllowInsecureGuestAuth` w `HKLM\SOFTWARE\Policies\Microsoft\Windows\LanmanWorkstation`:
- Przed: `KeyExists=false`/`Exists=false` (obie VM). Po zapisie DWORD 1: `Exists=true`, **`Kind=DWord`**, `Value=1`
  (potwierdza typ REG_DWORD — MS go nie podaje). Po usunięciu: `Exists=false`.
- Druga ścieżka `HKLM\SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters\AllowInsecureGuestAuth`: `KeyExists=true`,
  `Exists=false` przez cały cykl (zapis polityki jej nie tworzy).
- `Get-SmbClientConfiguration` `EnableInsecureGuestLogons`:
  - **Home:** `true` przed → `true` po zapisie → `true` po usunięciu (domyślnie już `true`; zapis/usuń nie zmieniły obserwowanego stanu).
  - **Pro:** `false` przed → `true` po zapisie → `false` po usunięciu (wartość polityki steruje stanem).
- **Odpowiedź (G9, pewność wysoka):** `Kind=DWord`. Wpływ na `EnableInsecureGuestLogons`: na Pro czytelny (`false→true→false`),
  na Home niewidoczny, bo stan domyślny to już `true`. Domyślny stan różni się edycją (Pro `false`, Home `true`) — detekcja musi
  czytać wartość polityki w rejestrze, nie tylko `Get-SmbClientConfiguration`.

### Przebieg 2 (2026-10-18): menu i linia komendy
Ponownie tylko sondy `menu` i `cmdline` na obu VM (skrypt z `d4d6e75`: naprawione przekazywanie listy celów do podprocesu
`Verbs()`, sonda 6 zapisuje też ACL po komendzie), od checkpointu `czysta-26H2`, build 26300.9457, start Home 23:23, Pro 23:29.
Wyniki: zapis pomiaru w archiwum autora (poza gitem). **Home i Pro dały
identyczne wyniki** we wszystkich listingach, w 4c i w sondzie 6. Token sondy jak w przebiegu 1 (wbudowany Administrator, bez UAC).

**4a — widoczność w `Verbs()`** (osobny `powershell.exe` po `SHChangeNotify(SHCNE_ASSOCCHANGED)`; każdy cel `Found=true`, bez błędów).
„tak" = na liście jest pozycja „DSO: Przejmij na własność" (`MUIVerb` verbu nadrzędnego).

| Wariant (klasy) | plik testowy | folder testowy | `C:\` | `C:\Windows` | `System32` | `C:\Windows\Temp` | `win.ini` |
|---|---|---|---|---|---|---|---|
| p0 Plain, bez Extended (`*`, Directory) | tak | tak | nie | tak | tak | tak | tak |
| v1 Subkey + Extended (`*`, Directory) | nie | nie | nie | nie | nie | nie | nie |
| v2 Subkey, bez Extended (`*`, Directory) | tak | tak | nie | tak | tak | tak | tak |
| v3 ValuePath + Extended (`*`, Directory) | nie | nie | nie | nie | nie | nie | nie |
| v4 ValuePath, bez Extended (`*`, Directory) | pusta nazwa | pusta nazwa | nie | pusta nazwa | pusta nazwa | pusta nazwa | pusta nazwa |
| k1 Plain (Folder) | nie | tak | **tak** | tak | tak | tak | nie |
| k2 Plain (AllFileSystemObjects) | tak | tak | nie | tak | tak | tak | tak |
| k3 Plain (Drive) | nie | nie | **tak** | nie | nie | nie | nie |

- `Extended` (v1, v3): pozycji nie ma na liście `Verbs()` dla żadnego celu (liczba verbów o 1 mniejsza niż w p0/v2). `Verbs()`
  zachowuje się jak menu bez Shift. Sam efekt „widoczne z Shift" nie jest tą sondą sprawdzony (wymaga klikania w Explorerze).
- Podmenu: wpis podrzędny „DSO: Przejmij (administrator)" (`ExtendedSubCommandsKey\Shell\runas`) **nie pojawia się w `Verbs()`**
  w żadnym wariancie. Verb nadrzędny: Subkey widać jako „DSO: Przejmij na własność"; ValuePath jako pozycję z **pustą nazwą**
  (liczba verbów taka jak w p0, a nazwa na pozycji verbu to `""`). Uwaga: `Verbs()` może nie rozwijać menu kaskadowych. Brak wpisu
  podrzędnego na liście nie dowodzi, że podmenu jest niewidoczne w Explorerze.
- Korzeń `C:\`: verb z `Directory` i z `*` **nie** jest na `C:\` (p0, v2). Na `C:\` jest tylko przy rejestracji pod `Drive` (k3)
  i `Folder` (k1); `AllFileSystemObjects` (k2) nie trafia na `C:\`. `Folder` nie obejmuje plików, `AllFileSystemObjects` obejmuje
  pliki i foldery. Odpowiedź (G3, pewność wysoka dla `Verbs()`): rejestracja pod `*` + `Directory` sama omija korzeń dysku.

**4b — AppliesTo** (rejestracja tylko pod `Directory`, więc „nie" na plikach `plik testowy.txt`/`win.ini` wynika z klasy, nie z AQS):

| Wariant | folder testowy | `C:\` | `C:\Windows` | `System32` | `C:\Windows\Temp` |
|---|---|---|---|---|---|
| kontrola `System.ItemName:="folder testowy"` (Plain) | tak | nie | nie | nie | nie |
| A `NOT System.ItemPathDisplay:="C:\Windows"` (Plain i Subkey) | tak | nie | **nie** | tak | tak |
| B `NOT (ItemPathDisplay:="C:\Windows" OR ItemPathDisplay:~<"C:\Windows\" OR ItemPathDisplay:="C:\")` (Plain i Subkey) | tak | nie | **nie** | **nie** | **nie** |
| C `NOT System.ParsingPath:="C:\Windows"` (Plain i Subkey) | tak | nie | **nie** | tak | tak |

- Kontrola pozytywna działa: verb jest tylko na folderze o tej nazwie, więc `Verbs()` respektuje `AppliesTo`.
- A i C wykluczają tylko sam `C:\Windows` (równość), a podfoldery zostają. **B wyklucza `C:\Windows` i wszystko pod nim**
  (`System32`, `Temp`) i zostawia folder testowy. Wyniki dla Plain i Subkey są takie same.
- Odpowiedź (G2, pewność wysoka dla `Verbs()`): **wariant B** spełnia warunek. Ograniczenia, które zostają: ścieżka jest stała
  (`C:\Windows`, inna litera dysku nie jest objęta) i porównanie jest tekstowe. Wynik dotyczy `Directory`. Pliki pod `C:\Windows`
  (np. `win.ini` przez `*`) nie były w tym przebiegu filtrowane przez AQS, bo warianty AQS rejestrowano tylko pod `Directory`.
  Działanie w prawdziwym menu Explorera jest wnioskiem z `Verbs()`, a nie obserwacją.

**4c — powtórka:** wyniki takie same jak w przebiegu 1 (Plain zmienia właściciela, a `runas` w Subkey/ValuePath go nie zmienia).
G1 nadal **nie rozstrzygnięte** z tych samych powodów (a) i (b).

**6 — linia komendy, właściciel i ACL po komendzie** (folder: `takeown /f "%1" /a /r /d Y && icacls "%1" /grant *S-1-5-32-544:F /t /c /l /q`,
plik: bez `/r /d Y` i `/t`). „Jawne FC" = ACE `S-1-5-32-544`, `Allow`, `FullControl` (2032127), `IsInherited=false`. Pole
`AdministratorsFullControl` nie odróżnia ACE dziedziczonych (jest `true` także tam, gdzie komenda zawiodła), więc tu go nie używam.

| Nazwa | Plik: exit / właściciel / jawne FC | Folder (+ `dziecko.txt`): exit / właściciel / jawne FC |
|---|---|---|
| spacje | 0 / `S-1-5-32-544` / tak | 0 / `S-1-5-32-544` / tak (folder i dziecko) |
| polskie | 0 / `S-1-5-32-544` / tak | 0 / `S-1-5-32-544` / tak (folder i dziecko) |
| `&` | 0 / `S-1-5-32-544` / tak | 0 / `S-1-5-32-544` / tak (folder i dziecko) |
| `^` | 0 / `S-1-5-32-544` / tak | 0 / `S-1-5-32-544` / tak (folder i dziecko) |
| `!` | 0 / `S-1-5-32-544` / tak | 0 / `S-1-5-32-544` / tak (folder i dziecko) |
| `%` („procent %PATH% 50%") | **1** / seed (RID 1000) / **nie** | **1** / seed / **nie** (folder i dziecko) |
| mieszane („mix %TEMP% & ^ ! zażółć") | **1** / seed / **nie** | **1** / seed / **nie** (folder i dziecko) |

- Przy `%` stderr `takeown` to „ERROR: File or Directory not found.". `&&` nie uruchomił wtedy `icacls`, więc nie ma jawnego ACE.
  Mechanizm (rozwinięcie `%...%` przez `cmd.exe`) to wniosek, nie odczyt.
- Każdy cel z exit 0 ma po komendzie jedno jawne FC Administratorów i dodatkowo dziedziczone FC. Pliki i foldery zachowują się
  tak samo, a przy folderach jawne ACE dostaje też `dziecko.txt` (`/t`). Flagi dziedziczenia ACE nie są zapisane w rekordzie.
- Zmiany uboczne: `StrayChangedPaths` jest puste we wszystkich 14 przypadkach na obu VM. To porównanie obejmuje tylko **właściciela**
  elementów pod `C:\DsoTest`. Zmian ACL na innych elementach sonda nie porównuje.
- Odpowiedź (G8b, pewność wysoka): poza nazwami z `%` komenda daje właściwego właściciela i jawne FC Administratorów na celu
  (plik albo folder i dziecko), bez zmian właściciela czegokolwiek innego. Nazwy z `%` zawodzą bez skutków (nic się nie zmienia).

### Test ręczny UAC (autor, 2026-10-18/19)
Rozstrzyga G1 (czy verb `runas` w podmenu `ExtendedSubCommandsKey` podnosi uprawnienia i czy w ogóle się wykonuje). Test w sesji
użytkownika z UAC na VM `DSO-26H2-Pro` (checkpoint `czysta-26H2`, build 26300.9457, pl-PL), autor klikał w klasycznym menu Explorera
(Shift + „Pokaż więcej opcji”). Skrypt: tryby `Prepare` i `Finish` z `tests/vm/explore/Invoke-C4Exploration.ps1`; drzewo
`C:\DsoTest\manual-uac` (plik, folder, plik w folderze, „podfolder bez list” z `Deny ListDirectory` dla `dso` i plik w nim; właściciel
początkowy `dso`, RID 1000). Wyniki: zapis pomiaru w archiwum autora
(poza gitem). Oba warianty rejestrowano pod `*` i `Directory`, z `Extended`.

| Wariant | Struktura | Co zobaczył autor | Stan po (`Finish`) |
|---|---|---|---|
| 1 `Subkey` (Prepare 2026-10-18 23:38, Finish 23:48) | tylko podklucz `ExtendedSubCommandsKey\Shell\runas` (dosłownie krok 3 MS) | pozycja **bez strzałki podmenu**; kliknięcie: „Z tym plikiem nie jest skojarzona aplikacja…” | właściciel wszystkich elementów nadal `DSO-26H2-PRO\dso`, ACL bez zmian; nic nie zmieniono |
| 2 `ValuePath` (Prepare 2026-10-18 23:49, Finish 2026-10-19 00:00) | podklucz jak wyżej **oraz** wartość REG_SZ `ExtendedSubCommandsKey` = `<Klasa>\shell\DSOptimizer.TakeOwnership\ExtendedSubCommandsKey` (względem HKCR) | strzałka podmenu widoczna, po wyborze wpisu **pojawił się monit UAC** | po zgodzie właściciel `S-1-5-32-544` (`BUILTIN\Administratorzy`) na pliku, folderze, pliku w folderze, „podfolderze bez list” i pliku w nim; jawne ACE `BA` FullControl na każdym z nich |

- **Wniosek (zweryfikowany ręcznie, nie przez `Verbs()`):** do wysyłki wchodzi struktura `ValuePath` + `Extended`. Sam podklucz (wariant 1) nie
  tworzy podmenu w Explorerze 26H2. Verb `runas` w podmenu uruchamia komendę z podniesieniem (UAC) i zmienia właściciela.
- **Niespójność źródła MS** (Creating Shortcut Menu Handlers, https://learn.microsoft.com/en-us/windows/win32/shell/context-menu-handlers,
  aktualizacja 2025-07-03): krok 3 mówi „add an `ExtendedSubCommandsKey` subkey”, a we wstępie jest mowa o ponownym użyciu zestawów
  verbów przez „specifying the same `ExtendedSubCommandsKey`”, czyli o wartości wskazującej ścieżkę. VM rozstrzygnęła: wartość + podklucz
  działa, sam podklucz nie.
- **Foldery bez prawa listowania:** po komendzie rekurencyjnej „podfolder bez list” ma SDDL
  `O:BAG:S-1-5-21-…-513D:PAI(A;OI;FA;;;BA)(A;CI;FA;;;BA)`: tylko Administratorzy, dziedziczenie wyłączone (`P`). Wcześniej dziedziczył m.in.
  SYSTEM i Users (`(A;OICIID;FA;;;SY)`, `(A;OICIID;0x1200a9;;;BU)`). `takeown /r /d Y` **zastąpił całą listę ACL** tego folderu
  (MS: przy braku List Folder „The directory permissions are replaced with permission granting the user full control only”); SYSTEM i Users
  utracili dostęp. Plik w nim ma ACL dziedziczone `(A;;FA;;;BA)(A;ID;FA;;;BA)`.
- Zwykłe pliki i foldery (z prawem listowania) mają po komendzie jawne `(A;;FA;;;BA)` obok dziedziczonych ACE, bez utraty SYSTEM/Users.

### Źródła MS (przeniesione z `research-aqs-policy.md`, plik poza gitem; dostęp 2026-10-18)
- Context Menu Handlers [CMH]: https://learn.microsoft.com/en-us/windows/win32/shell/context-menu-handlers —
  `AppliesTo` liczony na „fast properties"; `ExtendedSubCommandsKey` („cascading menus within cascading menus");
  `HasLUAShield` „controls whether a UAC shield is displayed" (tylko wizualne); `extended` REG_SZ (verb widoczny tylko z Shift);
  przykład `AppliesTo = System.ItemName:"..."`; verby BitLockera w `HKEY_CLASSES_ROOT\drive`
  (`System.Volume.BitlockerProtection:=2`). MS NIE twierdzi, że verb `runas` w podmenu podnosi uprawnienia („brak źródła").
- Advanced Query Syntax [AQS]: https://learn.microsoft.com/en-us/windows/win32/search/-search-3x-advancedquerysyntax —
  `NOT` (wielkimi literami), `~<` = `COP_VALUE_STARTSWITH`, `=` = `COP_EQUAL`; składnia kanoniczna zalecana.
- System.ItemPathDisplay [IPD]: https://learn.microsoft.com/en-us/windows/win32/properties/props-system-itempathdisplay
  (pełna ścieżka; `InInvertedIndex=true`). System.ItemFolderPathDisplay [IFPD]:
  https://learn.microsoft.com/en-us/windows/win32/properties/props-system-itemfolderpathdisplay (ścieżka rodzica — nieprzydatna).
  System.ParsingPath [PP]: https://learn.microsoft.com/en-us/windows/win32/properties/props-system-parsingpath
  (`InInvertedIndex=false` — może nie być „fast").
- Predefined Shell Objects [HND]: https://learn.microsoft.com/en-us/windows/win32/shell/handlers —
  `Directory`=„File folders", `Drive`=„All drives in MyComputer, such as C:\", `Folder`=„All folders",
  `AllFileSystemObjects`, `*`=„All files". MS nie rozstrzyga, czy `Directory\shell` pojawia się na korzeniu dysku; po zmianie
  rejestru zalecany `SHChangeNotify`/restart explorera.
- ShellExecuteInfo [SEI]: https://learn.microsoft.com/en-us/windows/win32/api/shellapi/ns-shellapi-shellexecuteinfoa —
  verb `runas` „Launches an application as Administrator. UAC will prompt..." (kontekst `lpVerb`).
- Policy CSP LanmanWorkstation [LMW]: https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-lanmanworkstation —
  `EnableInsecureGuestLogons`; klucz `Software\Policies\Microsoft\Windows\LanmanWorkstation`, wartość `AllowInsecureGuestAuth`,
  `int` 0/1 (typ REG_DWORD nie podany — potwierdzony sondą jako `DWord`).
- Niewiążące (społecznościowe, NIE źródło): wątki o `runas`/`HasLUAShield` (winhelponline, textslashplain, fora) — tylko wskazówka,
  nie Microsoft Learn.
