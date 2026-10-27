# sec.bitlocker-prevent-auto – Zablokuj automatyczne szyfrowanie dysku (BitLocker)

## Werdykt 26H2
Portować jako `caution`: mechanizm i typ wartości są wprost udokumentowane przez MS (dokument dla OEM), ale **skutek na działającym systemie nie jest opisany**. Auto-szyfrowanie startuje w OOBE, więc pozycja nie odszyfrowuje dysku i nie wiadomo z dokumentacji, czy zatrzyma szyfrowanie już rozpoczęte. Opis pozycji obiecuje tylko zapis wartości, którą MS podaje jako blokadę. Alias v3.2: `sec_bitlocker_kill`. Odszyfrowanie dysku to osobna akcja `sec.bitlocker-decrypt-c`.

**RED FLAG – licencja:** MS pisze, że wyłączanie szyfrowania urządzenia poza scenariuszem OEM „is prohibited as it violates the Windows 11 licensing requirements” (pełny cytat niżej). Zdanie występuje w dokumencie „for OEMs”, więc dotyczy producentów; dla użytkownika końcowego Windows ma w Ustawieniach przełącznik „Szyfrowanie urządzenia”. To interpretacja, nie porada prawna.

## Co zmienia
- `HKLM\SYSTEM\CurrentControlSet\Control\BitLocker`, wartość `PreventDeviceEncryption`, **DWord**: brak -> `1`. `scope=machine`, zapis przez API rejestru w C#, bez skryptu. Bez `requiresReboot` (MS nic nie mówi o restarcie; skutek dotyczy przyszłego OOBE/inicjalizacji szyfrowania urządzenia).
- Nie dotyka woluminów ani usług BitLockera; nie odszyfrowuje niczego.

## Źródła
- MS Learn, „BitLocker drive encryption in Windows 11 for OEMs” (ms.date 2023-04-26, aktualizacja strony 2025-08-12): https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/oem-bitlocker
  - „BitLocker automatic device encryption starts during Out-of-box (OOBE) experience. However, protection is enabled (armed) only after users sign in with a Microsoft Account or an Azure Active Directory account. Until that, protection is suspended and data is not protected. BitLocker automatic device encryption is not enabled with local accounts”.
  - Sekcja „Disable BitLocker automatic device encryption”: „OEMs can choose to disable device encryption and instead implement their own encryption technology on a device. Disabling encryption outside of this scenario is prohibited as it violates the Windows 11 licensing requirements and the Windows 11 *secure by default* principle.”
  - Mechanizm: Unattend `PreventDeviceEncryption` = True albo „update the HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\BitLocker registry key: Value: PreventDeviceEncryption equal to True (1).” Dalej: „We do not recommend setting this registry key on devices with Recall feature.”
- MS Learn, Unattend „PreventDeviceEncryption” (ms.date 2017-05-02): wartości `false` (domyślna, szyfruje automatycznie) / `true`; „These settings only apply to Windows 8” (strona nieaktualizowana; strona OEM z 2025 nadal wskazuje to ustawienie dla Windows 11): https://learn.microsoft.com/en-us/windows-hardware/customize/desktop/unattend/microsoft-windows-securestartup-filterdriver-preventdeviceencryption
- Research partii C4: `docs/zrodla/2026-10-18-partia-c4-zrodla.md`, sekcja 1. Wątek ElevenForum (ta sama wartość) to wskazówka, nie dowód.

## Wartości domyślne na świeżym 26H2
Odczyt z VM DSO-26H2-Home i DSO-26H2-Pro (build 26300.9457, eksploracja partii C4, 2026-10-18; obie tak samo):
- Klucz `HKLM\SYSTEM\CurrentControlSet\Control\BitLocker` **istnieje** na Home i Pro (podklucze m.in. `AutoDE`, `PcrMon`, `RecoveryPasswordRotation`, `Telemetry`; Home ma dodatkowo `KeyBackupMonitor`), bez wartości wprost w kluczu.
- `PreventDeviceEncryption`: **`Exists=false`** na Home i Pro (wartość nie istnieje; to co innego niż wartość `0`). Stąd `original` = `<RemoveEntry>` jest potwierdzone odczytem, a systemowy klucz `Control\BitLocker` przetrwa cofnięcie (undo usuwa tylko naszą wartość), więc `Expect.KeyPresentAfterUndo` w przypadku VM jest poprawne.
- Typ **DWord** wynika z tekstu MS („PreventDeviceEncryption equal to True (1)”, sekcja Źródła). Odczyt VM po zapisie przez API rejestru pokazuje tylko typ naszego własnego zapisu, więc nie jest niezależnym dowodem typu.
- **Rozbieżność MS i obserwacji:** MS pisze, że auto-szyfrowanie „is not enabled with local accounts”, a na VM z kontem lokalnym było w toku na Home i Pro. Nie wolno stąd wnioskować, że pozycja jest zbędna przy koncie lokalnym.
- Stan zaobserwowany po starcie checkpointu (konto lokalne): automatyczne szyfrowanie urządzenia było **w toku** na Home i Pro, a postęp zmienia się między uruchomieniami. Eksploracja 2026-10-18: Home `ConversionStatus` = 2 (EncryptionInProgress), 78%, `ProtectionStatus` = 0 (Off), 0 protektorów (22:45); Pro `ConversionStatus` = 0, 0% (22:51). Pełny przebieg VM `vm-run-20261019-002807` (2026-10-19): Home 6% zaszyfrowane, Pro 78% zaszyfrowane. Podajemy to jako obserwację; zgodnie z MS szyfrowanie startuje w OOBE, a ochrona jest uzbrajana dopiero po zalogowaniu kontem Microsoft/Entra. Nie wyciągamy stąd wniosków o działaniu tej wartości.

## Detekcja i undo
- Detekcja: tylko rejestr, przez równość: dokładnie DWORD `1` -> `Applied`, inna wartość lub brak -> `NotApplied`. To uczciwe, bo pozycja obiecuje tylko zapis wartości; realny skutek (czy szyfrowanie się włączy) nie jest wykrywany.
- Undo z journala przywraca snapshot sprzed zmiany: wartość, która istniała (np. `0`), wraca; jeśli wartości nie było, jest usuwana (nie zapisuje `0`).
- Jeśli `PreventDeviceEncryption` = 1 istniało już przed apply (np. ustawione przez OEM), detekcja pokazuje `Applied`, a apply kończy się `AlreadyApplied` bez zapisu i bez wpisu w journalu: narzędzie nie przejmuje tej wartości. Undo **bez wpisu w journalu** (undo z wartości domyślnych) usuwa wartość (fallback `original` = `<RemoveEntry>`), także tę ustawioną poza narzędziem.

## Ryzyka i ostrzeżenia
- `risk=caution`.
- Ostrzeżenia w katalogu: „Dysk bez szyfrowania nie chroni danych po kradzieży lub zgubieniu komputera; polityka firmy lub szkoły może wymagać szyfrowania.” oraz „Według Microsoftu wyłączanie szyfrowania przez kogoś innego niż producent sprzętu narusza licencję Windows 11; odradza je też przy Recall.”
- Poza uwagami w programie (w szczegółach technicznych albo tylko tutaj): pełne cytaty MS w sekcji „Źródła” (licencja poza scenariuszem OEM; brak zalecenia przy funkcji Recall); pozycja nie odszyfrowuje dysku (do tego `sec.bitlocker-decrypt-c`); MS opisuje wartość jako blokadę auto-szyfrowania przy pierwszym uruchomieniu (OOBE), a skutek na działającym systemie nie jest opisany.
- Bez obietnic ochrony ani wydajności.

## Test VM
Pełny przebieg VM `vm-run-20261019-002807` (2026-10-19, ok. 00:28–00:46, build 26300.9457, HEAD `d589405`), Home i Pro, kod 0 dla przypadków tej pozycji. Raporty lokalne poza gitem (`tests/vm/out/<data>/<VM>/<RunLabel>/report.json`).

- `sec-bitlocker-prevent-auto` (`Flow=Full`, `Catalog=BuiltIn`, snapshot wartości, `KeyPresentAfterUndo` dla klucza `Control\BitLocker`): **Passed** na Home i Pro. Sprawdzono: detekcja przed `NotApplied`, apply `Applied` (DWORD `PreventDeviceEncryption` = 1 w snapshocie po apply), detekcja po apply `Applied`, undo `Undone`, detekcja po undo `NotApplied`; stan końcowy = stan przed (wartość usunięta, `SnapshotDiff` pusty), klucz `Control\BitLocker` zostaje.
- `sec-bitlocker-prevent-auto-effect` (`Flow=DetectOnly`): **Skipped** na Home i Pro. Powód: skutek „szyfrowanie nie startuje” wymaga nowego OOBE z kontem Microsoft, a VM ma konto lokalne i checkpoint po OOBE. Raportowane jako pominięte, nie zaliczone.
- Podsumowanie przebiegu (wszystkie przypadki): Home 309 zaliczonych / 0 błędów / 21 pominiętych (`main` 221/0/21 + 6 przebiegów Reboot zielonych), Pro 308 / 1 / 21 (jedyny błąd to stary `Check` przypadku `sec-bitlocker-decrypt-c`, poprawiony w `962ae0e`; zob. `sec.bitlocker-decrypt-c`).
- **Nie testowane na VM:** skutek wartości (czy auto-szyfrowanie nie startuje w OOBE).

## Reddit
Do uzupełnienia w części E.
