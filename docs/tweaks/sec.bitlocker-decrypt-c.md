# sec.bitlocker-decrypt-c – Wyłącz szyfrowanie BitLocker na dysku C:

## Werdykt 26H2
Portować jako `kind=action`, `risk=caution` (pozycja nowa; v3.2 nie miała odszyfrowania, `sec_bitlocker_kill` to blokada automatycznego szyfrowania, czyli osobna pozycja `sec.bitlocker-prevent-auto`). Decyzje:
- Akcja nie ma detekcji, journala ani undo. Jedyny dowód skutku to **odczyt zwrotny** `ConversionStatus` po `Decrypt`; rozjazd odczytu daje kod wyjścia ≠ 0 (`Failed`), a nie zielone `Executed`.
- `Decrypt` wraca od razu, a Windows odszyfrowuje w tle (także po restarcie), więc akcja **nie czeka** na koniec. Sukces to „odszyfrowywanie rozpoczęte lub w toku”, a nie „dysk odszyfrowany”.
- Klucze automatycznego odblokowania innych dysków (`FVE_E_AUTOUNLOCK_ENABLED`) to `Failed`; akcja ich sama nie usuwa (zmieniłoby to inne dyski, decyzja użytkownika).

## Co zmienia
- Skrypt (`script.apply`, `-File`, PowerShell 5.1) woła WMI wyłącznie przez dwie funkcje pomocnicze: `Get-DsoVolumeConversionStatus` (zwraca obiekt z `ReturnValue`, `ConversionStatus`, `EncryptionPercentage`, a przy braku woluminu `$null`) i `Invoke-DsoVolumeDecrypt` (zwraca `ReturnValue` jako `uint32`). Prawdziwe ciała: `Get-CimInstance -Namespace root\CIMV2\Security\MicrosoftVolumeEncryption -ClassName Win32_EncryptableVolume -Filter "DriveLetter='C:'"` oraz `Invoke-CimMethod -MethodName 'GetConversionStatus'` / `'Decrypt'`. Testy jednostkowe mockują oba helpery; harness ma na nie i na `Invoke-CimMethod` bezpieczniki.
- Decyzje tylko po kodach liczbowych WMI i enumie `ConversionStatus` (0 FullyDecrypted, 1 FullyEncrypted, 2 EncryptionInProgress, 3 DecryptionInProgress, 4 EncryptionPaused, 5 DecryptionPaused), bez tekstu zależnego od języka. Procent (`EncryptionPercentage`) trafia tylko do komunikatu: MS „Always use ConversionStatus”.
- Ścieżki wyniku:
  - `ConversionStatus` = 0 przed: `Executed`, komunikat „C: nie jest zaszyfrowany – nic nie zmieniono”, `Decrypt` **nie jest wołany**.
  - 1, 2, 4: `Decrypt`, odczyt zwrotny; status po 3: `Executed` „Odszyfrowywanie C: rozpoczęte: N% zaszyfrowane. Windows kontynuuje w tle, także po restarcie.”; status po 0 (bardzo mały wolumin): `Executed` „Odszyfrowywanie C: zakończone (ConversionStatus = 0).”
  - 3, 5: `Decrypt` (MS: wznowienie), odczyt zwrotny; po 3: `Executed` „Odszyfrowywanie C: w toku: N% zaszyfrowane. …”.
  - `Decrypt` zwraca `0x80310029` (`FVE_E_AUTOUNLOCK_ENABLED`): `Failed` z komunikatem „Najpierw usuń klucze automatycznego odblokowania innych dysków (Clear-BitLockerAutoUnlock) – akcja tego nie robi”; `ClearAllAutoUnlockKeys` nie jest wołane.
  - Inny kod ≠ 0 z `Decrypt` (np. `0x80310000` `FVE_E_LOCKED_VOLUME`) albo status po `Decrypt` spoza {0, 3}: `Failed` z kodem hex albo wartością statusu.
  - Brak woluminu C: w WMI (pusty wynik): `Failed` „Nie znaleziono woluminu C: …”, nigdy `Executed`. Błąd każdego wywołania WMI (przed, `Decrypt`, po) to `Failed`, nie jest połykany. Nieznany `ConversionStatus` (> 5) albo `ReturnValue` ≠ 0 przy odczycie przed `Decrypt`: `Failed`, `Decrypt` nie jest wołany.
- Nie dotyka: rejestru, usług, innych dysków ani kluczy automatycznego odblokowania, protektorów, `manage-bde`, cmdletów modułu BitLocker.

## Źródła
- MS Learn, `Win32_EncryptableVolume.Decrypt` (namespace `Root\CIMV2\Security\MicrosoftVolumeEncryption`, ms.date 2018-05-31): https://learn.microsoft.com/en-us/windows/win32/secprov/decrypt-win32-encryptablevolume
  - „begins decryption of a fully encrypted volume, or resumes decryption of a partially encrypted volume”; „When encryption is paused or in-progress, this method reverts the encryption and begins decryption. After decryption completes, all key protectors on this volume are removed”.
  - „This method returns immediately. If the volume is already fully decrypted and no other errors exist, this method returns 0.”
  - Kody: `FVE_E_LOCKED_VOLUME` 0x80310000, `FVE_E_AUTOUNLOCK_ENABLED` 0x80310029 („keys used to automatically unlock data volumes are available. Use ClearAllAutoUnlockKeys to remove these keys”).
  - „Calling the Decrypt method leaves data unprotected.”
- MS Learn, `Win32_EncryptableVolume.GetConversionStatus` (ms.date 2018-05-31): https://learn.microsoft.com/en-us/windows/win32/secprov/getconversionstatus-win32-encryptablevolume
  - `ConversionStatus` 0–5 jak wyżej; `EncryptionPercentage` 0–100: „an encryption percentage of 0 or 100 does not necessarily indicate that the disk is fully decrypted or fully encrypted. Always use ConversionStatus”.
- MS Learn, `Disable-BitLocker` (moduł BitLocker, wskazówka, nie używamy): „removes all key protectors and begins decrypting”: https://learn.microsoft.com/en-us/powershell/module/bitlocker/disable-bitlocker
- Research partii C4: `docs/zrodla/2026-10-18-partia-c4-zrodla.md`, sekcja 2 oraz „Wyniki VM (2026-10-18, eksploracja C4)”.

## Wartości domyślne na świeżym 26H2
Odczyt z VM DSO-26H2-Home i DSO-26H2-Pro (build 26300.9457, 2026-10-18; szczegóły w research):
- Klasa `Win32_EncryptableVolume` (`root\CIMV2\Security\MicrosoftVolumeEncryption`, filtr `DriveLetter='C:'`) **istnieje i zwraca 1 wolumin na Home i Pro**. `GetConversionStatus` zwraca pola `ReturnValue`, `ConversionStatus`, `EncryptionPercentage`, `EncryptionFlags`, `WipingPercentage`, `WipingStatus` (typ `UInt32`; kształt taki sam na obu edycjach).
- **Eksploracja 2026-10-18 (22:45 Home, 22:51 Pro):** Home `ConversionStatus` = 2 (EncryptionInProgress), `EncryptionPercentage` = 78, `ProtectionStatus` = 0 (Off), 0 protektorów; Pro `ConversionStatus` = 0 (FullyDecrypted), `EncryptionPercentage` = 0, `ProtectionStatus` = 0.
- **Pełny przebieg VM `vm-run-20261019-002807` (2026-10-19):** akcja zastała automatyczne szyfrowanie urządzenia w toku na obu edycjach: Home 6% zaszyfrowane, Pro 78% zaszyfrowane (konto lokalne). Obserwacja: po starcie checkpointu `czysta-26H2` na Home i Pro działa automatyczne szyfrowanie urządzenia, a postęp w chwili akcji zmienia się między uruchomieniami. MS: szyfrowanie startuje w OOBE, a ochrona jest uzbrajana dopiero po zalogowaniu kontem Microsoft/Entra.

## Detekcja i undo
Brak detekcji (`kind=action`). **Brak cofnięcia** (brak journala): odszyfrowanie nie ma odwrotności w narzędziu; ponowne szyfrowanie to osobna decyzja użytkownika w Ustawieniach Windows (w tym nowe protektory klucza). Jedyny dowód skutku to odczyt zwrotny: kod wyjścia 0 tylko wtedy, gdy `ConversionStatus` po `Decrypt` to 3 albo 0.

## Ryzyka i ostrzeżenia
- `risk=caution`. Dosłownie z MS: „Calling the Decrypt method leaves data unprotected.”; po zakończeniu odszyfrowywania wszystkie protektory klucza na woluminie są usuwane.
- Brak cofnięcia; akcja nie czeka na koniec (postęp: kolejne uruchomienie albo Ustawienia).
- Klucze automatycznego odblokowania innych dysków (0x80310029): `Failed`, akcja ich nie usuwa. Ręcznie: `Clear-BitLockerAutoUnlock`, jeśli to świadoma decyzja.
- Bez `sec.bitlocker-prevent-auto` szyfrowanie urządzenia może ruszyć ponownie (niezmierzone; MS tego nie opisuje). Polityka firmowa lub szkolna może wymagać szyfrowania albo zaszyfrować dysk ponownie.
- Potwierdzenie w UI przed uruchomieniem (spec, sekcja 7); CLI nie pyta.

## Test VM
Ścieżka zaszyfrowanego dysku: prawdziwy `Decrypt` przećwiczony na VM na OBU edycjach (Home i Pro), bo po starcie checkpointu działa tam automatyczne szyfrowanie urządzenia; własnego szyfrowania w VM nie robimy (decyzja autora). Nie sprawdzamy więc na VM: `Decrypt` na w pełni zaszyfrowanym dysku (`ConversionStatus` = 1), kodu `0x80310029` ani wznowienia (3, 5); te ścieżki są pokryte tylko testami jednostkowymi na mockach helperów.

Wyniki przebiegów VM (build 26300.9457, 2026-10-19):
- Pełny przebieg `vm-run-20261019-002807` (HEAD `d589405`): **Home Passed** (`Executed`, „Odszyfrowywanie C: rozpoczęte: 6% zaszyfrowane…”, `Check` Passed). **Pro: akcja `Executed`** („Odszyfrowywanie C: rozpoczęte: 78% zaszyfrowane…”), ale `Check` **Failed**: stary `Check` wymagał na Pro statusu 0, a odczyt zwrotny dał 3. To był błąd przypadku (założenie stabilnego stanu BitLockera na checkpoincie), nie akcji; poprawka `962ae0e`.
- Powtórka samego przypadku po poprawce, `vm-run-decrypt-20261019-005135` (HEAD `962ae0e`): **Home 4 zaliczone / 0 błędów** („…78% zaszyfrowane…”), **Pro 4 / 0** („…76% zaszyfrowane…”), `Check` (`ReturnValue` 0, `ConversionStatus` ∈ {3, 0}) Passed. Obie VM po przebiegach `Off` na checkpoincie `czysta-26H2`.
- Sprawdzono więc prawdziwą ścieżkę `Decrypt` + odczyt zwrotny na OBU edycjach, dzięki automatycznemu szyfrowaniu urządzenia działającemu po starcie checkpointu. Ścieżka „już odszyfrowany” (status 0) nie została zmierzona na VM; jest pokryta tylko testami jednostkowymi na mockach (podobnie 1, 4, 3/5 i `0x80310029`).
- Przypadek `sec-bitlocker-decrypt-c` (`Flow=Execute`, `Catalog=BuiltIn`) jest w `tests/vm/cases.psd1`; ma jeden `Check` dla obu edycji: `GetConversionStatus` zwraca `ReturnValue` 0, a `ConversionStatus` jest 3 lub 0 (postęp auto-szyfrowania w momencie akcji jest niedeterministyczny).
