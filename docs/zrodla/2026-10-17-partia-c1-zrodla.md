# Partia C1 - zrodla i mechanizmy (research 2026-10-17)

Zakres: 6 pozycji katalogu (ux.menu-delay, ux.game-dvr, disk.hibernation, disk.ntfs-last-access, disk.ntfs-8dot3, disk.reserved-storage).
Zasada: nic nie jest wymyslone. Co nie ma potwierdzenia w zrodle MS, jest oznaczone "niepotwierdzone w zrodlach MS" i trafia na liste kontroli VM.
Wszystkie URL: data dostepu 2026-10-17. Rodzaj zrodla: **MS** = learn.microsoft.com / support.microsoft.com; **MS-archiwum** = stara dokumentacja MS (moze byc nieaktualna); **hint** = ElevenForum / Winaero / WinUtil / Q&A spolecznosci (tylko wskazowka).
Nie wykonywano zadnych polecen zmieniajacych system. Wartosci domyslne "z VM" nie sa jeszcze zmierzone.

---

## 1. ux.menu-delay

**Mechanizm**
- `HKCU\Control Panel\Desktop`, wartosc `MenuShowDelay`, typ **REG_SZ** (tekst z liczba milisekund w zapisie dziesietnym), domyslnie **400**. Zrodlo MS-archiwum (Windows 2000 Server, TechNet "MenuShowDelay"): tabela "Data type REG_SZ / Range: Milliseconds in decimal / Default 400", opis: interwal od wskazania menu kursorem do wyswietlenia pozycji.
- API Win32: `SystemParametersInfo` z `SPI_GETMENUSHOWDELAY` (0x006A) i `SPI_SETMENUSHOWDELAY` (0x006B), wartosc w ms (MS). Pozwala zmienic wartosc w biezacej sesji, ale nie jest wymagane.
- Gorna granica zakresu: How-To Geek podaje 4000, Winaero (Win 8) 600 (hint, sprzeczne) - **niepotwierdzone w zrodlach MS**. MS-archiwum mowi tylko "milisekundy dziesietnie". Legacy v3.2 uzywa 10.

**Domyslna:** 400 (REG_SZ "400"). WinUtil (hint) zapisuje OriginalValue "400" typu String. Zachowanie przy braku wartosci: niepotwierdzone w zrodlach MS.

**Detekcja:** `Get-ItemProperty 'HKCU:\Control Panel\Desktop' -Name MenuShowDelay`, porownanie jako string/int. Brak wartosci = stan niezmieniony. Odczyt niezalezny od jezyka.

**Undo:** snapshot poprzedniej wartosci (REG_SZ albo brak). Przywrocic, a gdy brakowalo - usunac wartosc albo ustawic "400". Zapis zawsze jako **String**, nie DWord.

**Restart / wylogowanie:** wg spec ponowne zalogowanie (ostrzezenie). MS nie mowi tego wprost - niepotwierdzone w zrodlach MS. Natychmiastowy efekt przez SPI_SETMENUSHOWDELAY nie jest zweryfikowany; czesc aplikacji Win32 ignoruje wartosc (Wine bug, hint).

**Ryzyka:** minimalne (safe). Wartosc 0 moze powodowac przypadkowe otwieranie podmenu.

**Zrodla**
- https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-2000-server/cc978617(v=technet.10) - MS-archiwum, 2026-10-17 (typ, domyslna 400, opis; odczytane w calosci)
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfow - MS, 2026-10-17 (SPI_GET/SETMENUSHOWDELAY; tylko streszczenie z wyszukiwania, strony nie pobierano)
- https://raw.githubusercontent.com/ChristitusTech/winutil/main/config/tweaks.json (wpis MenuShowDelay: 200, String, original 400) - hint, 2026-10-17
- https://winaero.com/how-to-speed-up-menus-in-windows-10-and-change-the-menu-delay/ - hint (tylko wynik wyszukiwania), 2026-10-17

**Otwarte pytania (VM)**
1. Czy na swiezym 26H2 wartosc istnieje i jest REG_SZ "400"?
2. Czy zmiana dziala bez wylogowania i czy Eksplorator ja respektuje?
3. Co sie dzieje po usunieciu wartosci (czy wraca 400)?

---

## 2. ux.game-dvr

**Mechanizm (3 niezalezne klucze; spec: rozdzielic na osobne tweaki user/machine, regula 6)**

| Klucz | Zasieg | Typ | Wartosc "wylaczone" | Domyslna | Status zrodla |
|---|---|---|---|---|---|
| `HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR` `AllowGameDVR` | maszyna, polityka | DWORD (CSP `int`) | 0 | brak wartosci = "nie skonfigurowano" = dozwolone (CSP: Default Value 1) | **MS udokumentowane** |
| `HKCU\System\GameConfigStore` `GameDVR_Enabled` | uzytkownik | DWORD (typ nie podany w zrodle) | 0 | niepotwierdzone w zrodlach MS (hint: 1 = wlaczone) | tylko hint |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR` `AppCaptureEnabled` | uzytkownik | DWORD (typ nie podany w zrodle) | 0 | niepotwierdzone w zrodlach MS (hint: 1 = wlaczone) | tylko hint |

- MS Policy CSP `ApplicationManagement/AllowGameDVR`: format `int`; 0 = Not allowed, 1 = Allowed (domyslne). Mapowanie GP: "Enables or disables Windows Game Recording and Broadcasting", Computer Configuration > Windows Components > Windows Game Recording and Broadcasting, klucz `Software\Policies\Microsoft\Windows\GameDVR`, wartosc `AllowGameDVR`, plik GameDVR.admx. Edycje na liscie CSP: Pro, Enterprise, Education, IoT Enterprise (**Home nie jest wymieniony**), Windows 10 1507+. Uwaga MS: "The policy is only enforced in Windows 10 for desktop" - egzekwowanie na Windows 11 niepotwierdzone w zrodlach MS.
- `GameDVR_Enabled` i `AppCaptureEnabled`: jedyne zrodlo to odpowiedz na Microsoft Q&A (autor Jenny Feng; strona nie pokazuje, czy to pracownik MS) - **hint**. Odpowiedz podaje trzy klucze z wartoscia 1 = wlaczone.
- Legacy v3.2 ustawial dodatkowo `GameDVR_FSEBehaviorMode = 2` w `HKCU\System\GameConfigStore` - niepotwierdzone w zrodlach MS, nie wchodzi do C1 bez weryfikacji. WinUtil kiedys ruszal `GameDVR_FSEBehavior` / `GameDVR_EFSEFeatureFlags` (informacja z wyszukiwania); aktualny `tweaks.json` WinUtil nie zawiera wpisu GameDVR (przeszukano 2026-10-17), a strona `winutil.christitus.com/dev/tweaks/essential-tweaks/dvr` zwrocila 404 - nie opierac sie na tym.

**Detekcja:** odczyt rejestru osobno dla kazdego klucza. Brak wartosci = stan domyslny (dla polityki: dozwolone). Nie wnioskowac "wylaczone" z braku klucza polityki.

**Undo:** snapshot poprzedniej wartosci lub jej braku. Dla polityki: usunac wartosc `AllowGameDVR` (powrot do "nie skonfigurowano"), nie ustawiac 1. Pusty klucz `GameDVR` utworzony przez nas mozna usunac.

**Restart / wylogowanie:** niepotwierdzone w zrodlach MS; sprawdzic na VM.

**Ryzyka:** `AllowGameDVR=0` wylacza nagrywanie i transmisje, nie usuwa Game Bar. Moze wplynac na Win+G i komunikaty "ms-gamingoverlay" (hint). HKCU zmienia uzytkownik, HKLM wymaga admina i moze pokazac "Some settings are managed by your organization". Propozycja: HKCU = safe, polityka HKLM = caution.

**Zrodla**
- https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-applicationmanagement - MS, 2026-10-17 (sekcja AllowGameDVR; strona z updated_at 2026-02-28; odczytana)
- https://learn.microsoft.com/en-us/answers/questions/443550/my-xbox-game-bar-automatically-disabled-and-doesnt - hint (Q&A), 2026-10-17
- https://raw.githubusercontent.com/ChristitusTech/winutil/main/config/tweaks.json - hint, 2026-10-17 (brak wpisu GameDVR)
- ElevenForum "turn-on-or-off-xbox-game-bar" - HTTP 403, nie odczytano

**Otwarte pytania (VM, Home i Pro)**
1. Domyslne wartosci i typy `GameDVR_Enabled` i `AppCaptureEnabled` na swiezym 26H2 (czy istnieja).
2. Czy `AllowGameDVR=0` jest egzekwowane na Home i na Windows 11 (CSP wylacza Home, wspomina tylko Windows 10)?
3. Czy sama zmiana HKCU wystarcza do zatrzymania nagrywania w tle, czy potrzebny relog?
4. Rola `GameDVR_FSEBehaviorMode` - zostawic poza C1.

---

## 3. disk.hibernation

**Mechanizm**
- `powercfg /hibernate off` i `powercfg /hibernate on` (alias `/h`). MS: "Enables or disables the hibernate feature; also, sets the hiberfile size." Dodatkowo `/hibernate /size <percent>` (wlacza takze hibernacje) oraz `/hibernate /type reduced|full`.
- MS (powercfg): "A reduced hiberfile only supports hiberboot"; plik o niestandardowym rozmiarze lub `HiberFileSizePercent >= 40` jest traktowany jako pelny. Zmiana na reduced wymaga wczesniej `powercfg /hibernate /size 0`. `HiberFileSizePercent` leży w `HKLM\SYSTEM\CurrentControlSet\Control\Power` (MS).
- MS (System power states): pelny plik 40% RAM (hibernate, hybrid sleep, fast startup), reduced 20% RAM (tylko fast startup). Fast startup = hibernacja po wylogowaniu uzytkownika i jest domyslnym sposobem zamkniecia systemu.
- MS KB (How to disable and re-enable hibernation): `powercfg.exe /hibernate off` / `on`; po wylaczeniu hibernacji hybrid sleep nie dziala (ostrzezenie o utracie danych przy zaniku zasilania).

**Wplyw na Szybkie uruchamianie:** z dokumentacji MS wynika, ze fast startup uzywa pliku hibernacji, wiec bez niego nie dziala. Dosłowne zdanie "powercfg /h off wylacza Fast Startup" nie zostalo znalezione w zrodlach MS (potwierdzaja je tylko hinty) - **niepotwierdzone w zrodlach MS**. Lagodniejsza alternatywa wg MS: `/hibernate /type reduced` zachowuje fast startup bez pelnej hibernacji.

**Rejestr stanu (wszystko hint, niepotwierdzone w zrodlach MS):**
- `HKLM\SYSTEM\CurrentControlSet\Control\Power` `HibernateEnabled` i `HibernateEnabledDefault` (DWORD): ElevenForum - oba ustawia sie razem, 0 = wylaczone, 1 = wlaczone, zalecany restart. Nie wiadomo, co `powercfg /h off` zapisuje w ktorej wartosci.
- WinUtil uzywa innej sciezki: `HKLM\System\CurrentControlSet\Control\Session Manager\Power` `HibernateEnabled` (DWord, original 1). Sprzeczne z ElevenForum co do sciezki - rozstrzygnac na VM.
- `HiberbootEnabled` (Fast Startup): wg NTLite `...\Control\Session Manager\Power`. ElevenForum: po `powercfg -h off` HiberbootEnabled moze nadal wynosic 1, wiec sama wartosc nie dowodzi, ze fast startup dziala.

**Domyslna:** hibernacja zwykle wlaczona (hint); domyslny typ pliku na Home/Pro 26H2 - niepotwierdzone w zrodlach MS, zmierzyc na VM. Na VM Hyper-V hibernacja moze byc niedostepna z natury.

**Detekcja (bez parsowania tekstu):**
1. Istnienie `"$env:SystemDrive\hiberfil.sys"` (`Test-Path -LiteralPath ... -Force`; plik ukryty/systemowy). Niezalezne od jezyka; nie jest to dokumentacja MS, weryfikacja VM.
2. Pomocniczo rejestr `HibernateEnabled` (zgodnie ze spec `detectOnly`) po ustaleniu na VM prawdziwej sciezki.
3. Unikac `powercfg /a` (tekst lokalizowany, parsowanie w PL sie zepsuje).

**Undo:** `powercfg /hibernate on`; jesli snapshot mowil reduced/full - dodatkowo `/hibernate /type ...` (przed reduced `/size 0`). Snapshot: `HiberFileSizePercent` + stan pliku.

**Restart:** MS nie podaje. Zmiana pliku jest natychmiastowa; zalecenie restartu dotyczy edycji rejestru (hint). Niepotwierdzone w zrodlach MS.

**Ryzyka (caution):** laptop - utrata hibernacji, hybrid sleep i Szybkiego uruchamiania; utrata sesji przy rozladowaniu baterii w uspieniu. `off` kasuje hiberfil.sys (zwalnia ok. 20-40% RAM). Samo usuniecie pliku nie wylacza opcji (hint).

**Zrodla**
- https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/powercfg-command-line-options - MS, 2026-10-17 (odczytane; updated_at 2021-12-15)
- https://learn.microsoft.com/en-us/windows/win32/power/system-power-states - MS, 2026-10-17 (odczytane)
- https://learn.microsoft.com/en-us/troubleshoot/windows-client/setup-upgrade-and-drivers/disable-and-re-enable-hibernation - MS, 2026-10-17 (odczytane)
- https://www.elevenforum.com/t/enable-or-disable-hibernate-in-windows-11.1894/ - hint (HTTP 403 przy pobraniu; tresc z wynikow wyszukiwania), 2026-10-17
- https://raw.githubusercontent.com/ChristitusTech/winutil/main/config/tweaks.json (WPFTweaksHiber) - hint, 2026-10-17
- https://ntlite.com/community/threads/hibernate-settings-in-control-panel-not-showing.3649/ - hint (wynik wyszukiwania), 2026-10-17

**Otwarte pytania (VM)**
1. Po `powercfg /h off` - jakie wartosci maja `Control\Power\HibernateEnabled`, `HibernateEnabledDefault`, `Control\Session Manager\Power\HibernateEnabled` i `HiberbootEnabled`? Ktora sciezka jest prawdziwa?
2. Czy hiberfil.sys znika od razu i czy Szybkie uruchamianie faktycznie przestaje dzialac (czas bootu)?
3. Domyslny typ pliku (full/reduced) i `HiberFileSizePercent` na Home i Pro 26H2.
4. Czy `on` po `off` przywraca poprzedni typ i rozmiar?

---

## 4. disk.ntfs-last-access

**Mechanizm**
- `fsutil behavior query disablelastaccess` / `fsutil behavior set disablelastaccess <wartosc>`.
- **MS (fsutil behavior)** dokumentuje tylko `{1|0}`: 1 = wylacza, 0 = wlacza aktualizacje Last Access Time; parametr aktualizuje `HKLM\SYSTEM\CurrentControlSet\Control\FileSystem\NtfsDisableLastAccessUpdate`; "You must restart your computer for this parameter to take effect." Poza tym: odczyt wartosci jest dokladny (wartosc w pamieci), zapis na dysk moze byc odroczony do 1 godziny; parametr moze wplywac na Backup i Remote Storage.
- **Wartosci 2 i 3 oraz tryby user/system managed NIE wystepuja w dokumentacji MS** (sprawdzono strone fsutil-behavior). Zrodlo hint (Winaero, zgodne z Resilio):
  - 0 = User Managed, aktualizacje wlaczone, rejestr `0x80000000`
  - 1 = User Managed, aktualizacje wylaczone, rejestr `0x80000001`
  - 2 = System Managed, aktualizacje wlaczone, rejestr `0x80000002` (domyslne wg Winaero)
  - 3 = System Managed, aktualizacje wylaczone, rejestr `0x80000003`
  - Wg Winaero w trybie system managed znacznik czasu jest modyfikowany przy starcie systemu, a dla duzych woluminow (>128 GB) Windows nie zmienia ustawienia - niepotwierdzone w zrodlach MS.
  - Komentarze pod Winaero (pojedyncze, niepotwierdzone): `0x80000003` wracalo do `0x80000002` po restarcie, `0x80000001` sie utrzymywal; opis w `query` odwrocony wzgledem pomocy.
- Stare MS (Windows Embedded blog, 2006, MS-archiwum): `NtfsDisableLastAccessUpdate` REG_DWORD = 1 wylacza. Stare TechNet (Server 2003) podaje domyslnie brak wartosci - nieaktualne dla Win 10 1803+.

**Domyslna Windows 10 1803+/11:** wg hintow 2 (system managed, aktualizacje wlaczone; rejestr `0x80000002`). Niepotwierdzone w zrodlach MS - zmierzyc na VM.

**Detekcja (rejestr, nie tekst):**
`(Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem' -Name NtfsDisableLastAccessUpdate).NtfsDisableLastAccessUpdate`
- Mapowac cale wartosci, nie bity: `0`/`0x80000000` = user, wlaczone; `1`/`0x80000001` = user, wylaczone; `0x80000002` = system, wlaczone; `0x80000003` = system, wylaczone. Czy zwykle `0`/`1` (bez bitu 31) tez sa akceptowane - MS tak (0/1); `0x8000000N` tylko hint.
- PowerShell zwroci DWORD jako `int` ze znakiem (`0x80000002` = -2147483646); porownywac przez `[uint32]`.
- Brak wartosci = stan domyslny systemu, traktowac jako "wlaczone" do czasu pomiaru na VM.
- Spec wymaga rozroznienia trybow 2/3 - detekcja ma zwracac stan "zarzadzane przez system" osobno.

**Undo:** `fsutil behavior set disablelastaccess <poprzednia wartosc 0-3>` ze snapshotu rejestru; dla stanu domyslnego `2`.

**Restart:** MS: tak ("You must restart your computer"). Czy `fsutil set` dziala natychmiast - niepotwierdzone w zrodlach MS; sprawdzic na VM.

**Ryzyka (safe/caution):** narzedzia uzywajace czasu ostatniego dostepu (Backup, Remote Storage, skrypty czyszczace/archiwizacja) dostaja nieaktualne dane. Wartosc 3 moze byc cofana do 2 (raporty).

**Zrodla**
- https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/fsutil-behavior - MS, 2026-10-17 (odczytane; updated_at 2026-02-16)
- https://learn.microsoft.com/en-us/archive/blogs/embedded/preventing-last-access-chatter - MS-archiwum (2006), 2026-10-17
- https://winaero.com/disable-ntfs-last-access-time-updates-in-windows-10 - hint, 2026-10-17 (tryby 0-3, 0x8000000N, domyslne 2)
- https://docs.resilio.com/content/troubleshooting/error-messages/Recording_of_files_access_time_is_not_enabled_on_Windows - hint (wynik wyszukiwania), 2026-10-17

**Otwarte pytania (VM)**
1. Domyslna wartosc rejestru i wyjscie `query` (EN) na Home i Pro 26H2.
2. Co `set disablelastaccess 0/1/2/3` zapisuje w rejestrze (`0x80000001` czy `1`)?
3. Czy `3` jest stabilne po restarcie?
4. Czy zmiana dziala przed restartem (test: LastAccessTime pliku po odczycie)?
5. Czy `fsutil` akceptuje 2 i 3 na 26H2 (MS opisuje tylko 0/1)?

---

## 5. disk.ntfs-8dot3

**Mechanizm**
- System: `fsutil behavior set disable8dot3 <0|1|2|3>` (rownowazne: `fsutil 8dot3name set <0-3>`). Wolumin: `fsutil 8dot3name set <volumepath> <0|1>` lub `fsutil behavior set disable8dot3 <volumepath> <0|1>`. Zapytanie: `fsutil 8dot3name query [<volumepath>]` / `fsutil behavior query disable8dot3 [<volumepath>]`.
- **Wartosci systemowe (MS, fsutil 8dot3name):** 0 = wlacza tworzenie nazw 8dot3 dla wszystkich woluminow; 1 = wylacza dla wszystkich; 2 = ustawienie per wolumin; 3 = wylacza dla wszystkich poza woluminem systemowym. Ustawia `HKLM\System\CurrentControlSet\Control\FileSystem\NtfsDisable8dot3NameCreation` (MS).
- **Per wolumin (MS):** "You must set the default file system behavior for 8dot3 name creation to the value 2 before you can enable or disable 8dot3 name creation for a specified volume." Flaga woluminu jest zapisana na dysku, nie w rejestrze.
- **Dotyczy tylko nowych plikow.** Istniejace nazwy zostaja do `fsutil 8dot3name strip` (poza C1). MS ostrzega: trwale usuniecie nazw bez poprawy kluczy rejestru moze zepsuc aplikacje, w tym dezinstalacje.
- Legacy v3.2 (`io_ntfs`): `fsutil behavior set disable8dot3 1`, detekcja `NtfsDisable8dot3NameCreation == 1`.

**Domyslna:** MS nie podaje na stronach 8dot3name/behavior. Z doswiadczenia: 2 (per wolumin, systemowy ma nazwy) - **niepotwierdzone w zrodlach MS**, zmierzyc na VM Home i Pro.

**Detekcja (rejestr):** `(Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem' -Name NtfsDisable8dot3NameCreation).NtfsDisable8dot3NameCreation` -> 0/1/2/3, mapowanie wg MS. Dla 2 i 3 stan woluminu nie wynika z rejestru; `fsutil 8dot3name query C:` jest tekstem lokalizowanym - uzywac tylko ostroznie. Propozycja C1: tweak systemowy do wartosci 1, "zastosowane" gdy rejestr == 1, inne wartosci raportowac jako niestandardowe.

**Undo:** przywrocic wartosc ze snapshotu (`fsutil behavior set disable8dot3 <poprzednia>`, zwykle 2). Ponowne wlaczenie nie odtwarza nazw dla plikow utworzonych w czasie wylaczenia.

**Restart:** w tabeli `fsutil behavior` parametr `disable8dot3` nie ma zdania "You must restart your computer" (w odroznieniu od disablelastaccess). Wniosek z braku zdania - potwierdzic na VM.

**Ryzyka (caution):** stare instalatory (16-bit/MSI), aplikacje i backupy odwolujace sie do nazw 8.3. Wartosc 1 dotyczy tez woluminow zewnetrznych.

**Zrodla**
- https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/fsutil-8dot3name - MS, 2026-10-17 (odczytane; updated_at 2026-02-16)
- https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/fsutil-behavior - MS, 2026-10-17 (odczytane)

**Otwarte pytania (VM)**
1. Domyslna wartosc `NtfsDisable8dot3NameCreation` na swiezym 26H2 Home/Pro i stan woluminu C:.
2. Czy `set disable8dot3 1` dziala natychmiast (nowy plik z dluga nazwa, `dir /x`)?
3. Wyjscie `query` w PL i EN - czy wystarczy rejestr.
4. Czy powrot do `2` przywraca poprzednie zachowanie C:.

---

## 6. disk.reserved-storage

**Mechanizm**
- DISM: `DISM.exe /Online /Set-ReservedStorageState /State:Enabled|Disabled` i `DISM.exe /Online /Get-ReservedStorageState` (MS; od Windows 10 2004; "Only supported for online Windows images").
- PowerShell (modul Dism): `Get-WindowsReservedStorageState` i `Set-WindowsReservedStorageState -State Enabled|Disabled -Online` (MS; w Notes: "Supported on Windows 10, version 2004"; wyjscie `Microsoft.Dism.Commands.ReservedStorageStateObject`). Uwaga: przyklad MS uzywa `-Online`, ale blok Syntax tego parametru nie zawiera - niezgodnosc w dokumentacji, sprawdzic na VM.
- **Przy oczekujacej aktualizacji / serwisowaniu (MS):** "If reserved storage is in use, it may not be disabled, and the following error is returned: This operation is not supported when reserved storage is in use. Please wait for any servicing operations to complete and then try again later." (DISM i cmdlet). Winaero (hint): podczas serwisowania nie mozna ani wlaczyc, ani wylaczyc; DISM dziala bez restartu.
- Zmiany sa odzwierciedlane w obrazach Sysprep generalized (MS).
- MS (strona DISM): w srodowiskach zarzadzanych (WSUS/ConfigMgr) rezerwa nie dziala automatycznie; narzedzia wylaczaja ja przed instalacja aktualizacji i wlaczaja po.
- **Rejestr:** `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\ReserveManager` wartosc `ShippedWithReserves` (DWORD; 1 = wlaczone, 0 = wylaczone) - tylko **hint** (Winaero), dotyczy Win 10 1903/1909, wymaga restartu, komentujacy zglaszaja, ze nie dziala na buildach 18305/18343/1909. **Niepotwierdzone w zrodlach MS**; support.microsoft.com/help/4496396 nie wspomina ReserveManager. Wyszukiwanie `PassedPolicy`/`MiscPolicyInfo`/`ActiveScenario` nic nie dalo. Nie uzywac rejestru do zapisu.

**Domyslna:** wlaczone na urzadzeniach z fabrycznym Windows 10 1903+/11 i czystych instalacjach (MS: system czysci rezerwe, gdy brakuje miejsca). Rozmiar rezerwy: strona MS nie podaje; legacy v3.2 mowil ok. 7-8 GB (niezweryfikowane).

**Dostepnosc w Windows PowerShell 5.1 na Windows 11 Home:** MS nie podaje listy edycji dla cmdletow; strony cmdletow maja tylko monikery windowsserver2016/2019/2022/2025-ps. Obecnosc w PS 5.1 na Win 11 Home - **niepotwierdzone w zrodlach MS**, sprawdzic `Get-Command Get-WindowsReservedStorageState` na VM. `DISM.exe` jest narzedziem wbudowanym i sluzy jako fallback.

**Detekcja (bez parsowania tekstu):**
1. `Get-WindowsReservedStorageState` - zwraca obiekt `ReservedStorageStateObject`; nazwy wlasciwosci niepotwierdzone w zrodlach MS (sprawdzic `Get-Member` na VM). Prawdopodobnie enum Enabled/Disabled, niezalezny od jezyka.
2. Fallback: `DISM /Online /Get-ReservedStorageState` - tekst lokalizowany; czy slowa Enabled/Disabled sa tlumaczone w PL - niepotwierdzone, sprawdzic na VM pl-PL.
3. `ShippedWithReserves` tylko pomocniczo, po weryfikacji.

**Undo:** `Set-WindowsReservedStorageState -State Enabled` (lub DISM `/State:Enabled`) wg snapshotu. Po wylaczeniu i ponownym wlaczeniu rezerwa moze pojawic sie dopiero po kolejnej aktualizacji (hint). Spec: przy oczekujacej aktualizacji pokazac blad z MS, bez falszywego sukcesu.

**Restart:** DISM/cmdlet bez restartu (Winaero); MS nie mowi wprost - niepotwierdzone w zrodlach MS. Rejestr (hint) wymaga restartu.

**Ryzyka (caution):** po wylaczeniu rosnie ryzyko niepowodzenia aktualizacji przy malym wolnym miejscu; zwolnione miejsce moze byc widoczne dopiero po zakonczeniu serwisowania; operacja niedozwolona w trakcie serwisowania.

**Zrodla**
- https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/dism-storage-reserve?view=windows-11 - MS, 2026-10-17 (odczytane; updated_at 2021-09-06)
- https://learn.microsoft.com/en-us/powershell/module/dism/set-windowsreservedstoragestate?view=windowsserver2025-ps - MS, 2026-10-17 (odczytane)
- https://learn.microsoft.com/en-us/powershell/module/dism/get-windowsreservedstoragestate?view=windowsserver2025-ps - MS, 2026-10-17 (odczytane)
- https://support.microsoft.com/help/4496396/windows-10-how-reserved-storage-works - MS, 2026-10-17 (odczytane; brak ReserveManager i rozmiaru)
- https://techcommunity.microsoft.com/blog/windows-itpro-blog/managing-reserved-storage-in-windows-10-environments/1297070 - MS, 2026-10-17 (tresci nie udalo sie pobrac, tylko tytul i wzmianka z wyszukiwania)
- https://winaero.com/disable-reserved-storage-windows-10/ - hint, 2026-10-17 (ShippedWithReserves)

**Otwarte pytania (VM, Home i Pro)**
1. Czy cmdlety istnieja w PS 5.1 na Home; czy `-Online` jest potrzebne; jakie wlasciwosci ma wynik `Get-`?
2. Wyjscie DISM po polsku (czy Enabled/Disabled sa tlumaczone)?
3. Zachowanie przy oczekujacej aktualizacji (blad vs cichy brak efektu) - odtworzyc z zalegla aktualizacja.
4. Czy `ReserveManager\ShippedWithReserves` istnieje na 26H2 i odzwierciedla stan (tylko odczyt)?
5. Rzeczywisty rozmiar rezerwy na swiezym 26H2 (Ustawienia > System > Pamiec > Pokaz wiecej kategorii).

---

## Tabela zbiorcza

| Pozycja | Mechanizm udokumentowany przez MS | Zalecane ryzyko | Wymaga sprawdzenia na VM |
|---|---|---|---|
| ux.menu-delay | czesciowo (typ i domyslna tylko MS-archiwum 2000; SPI w Win32) | safe | efekt bez wylogowania; istnienie wartosci i typ na 26H2 |
| ux.game-dvr | czesciowo (tylko polityka AllowGameDVR; klucze HKCU tylko hint; Home poza lista CSP) | safe (HKCU), caution (polityka HKLM) | domyslne i typy HKCU, egzekwowanie polityki na Home i Win 11, potrzeba relogu |
| disk.hibernation | czesciowo (powercfg on/off/type tak; rejestr stanu i skutek dla Fast Startup nie) | caution | prawdziwa sciezka `HibernateEnabled`, `HiberbootEnabled`, domyslny typ pliku, wplyw na boot |
| disk.ntfs-last-access | czesciowo (0/1, klucz, restart w MS; tryby 2/3 i `0x8000000N` tylko hint) | safe | domyslna wartosc, zapis rejestru po set 0-3, stabilnosc 3, restart |
| disk.ntfs-8dot3 | tak (wartosci 0-3, per wolumin, klucz; domyslna i restart nie) | caution | domyslna wartosc, natychmiastowosc, `query` w PL |
| disk.reserved-storage | tak dla DISM, cmdletow i bledu "in use"; rejestr ReserveManager nie | caution | cmdlety w PS 5.1 Home, `-Online`, wlasciwosci wyniku, zachowanie przy aktualizacji, wyjscie PL |

---

## Wyniki VM (2026-10-18)

Źródło: `c1-exploration.json` z przebiegu `Invoke-C1Exploration.ps1` (kod wyjścia 0, obie VM wróciły do `czysta-26H2`), zapis pomiaru w archiwum autora. Odwołania w nawiasach kwadratowych to nazwy kroków w JSON (`probes.<sonda>[].Name`). Wszystkie dane pochodzą z odczytu na VM; to, czego odczyt nie rozstrzygnął, jest oznaczone „nie rozstrzygnięte”.

### Metadane

| | DSO-26H2-Home | DSO-26H2-Pro |
|---|---|---|
| CurrentBuild.UBR | 26300.9457 | 26300.9457 |
| EditionID | Core | Professional |
| DisplayVersion | 26H2 | 26H2 |
| ProductName (stary napis) | „Windows 10 Home” | „Windows 10 Pro” |
| Culture / UICulture | pl-PL / pl-PL | pl-PL / pl-PL |
| PowerShell | 5.1.26100.9444, proces 64-bit | to samo |
| Hive użytkownika dso | załadowany (`hiveLoaded=true`) | załadowany |
| Czas zbierania (z JSON: pierwszy krok `probes.hibernation[0].StartedAt` – `meta.CollectedAt`) | 2026-10-18 00:29:15,9–00:29:18,6 | 00:29:28,7–00:29:31,4 |

Wniosek ogólny: **Home i Pro dały identyczne wyniki** we wszystkich krokach (kody wyjścia, surowe stdout/stderr, wartości rejestru). Różnice poza metadanymi i SID-ami: `ReserveManager\HardReserveAdjustment` (Home 7565312, Pro 7626752) oraz `Control\Session Manager\Power\POSTTime` (Home 261, Pro 253; wartość czasowa, bez znaczenia dla tweaków). Żaden krok nie zgłosił pola `Error`.

### 1. ux.menu-delay (tylko odczyt, bez zmiany) [odczyt-domyslnych-ux]

| Wartość | Home | Pro |
|---|---|---|
| `HKU\<SID dso>\Control Panel\Desktop\MenuShowDelay` | istnieje, **REG_SZ `"400"`** | to samo |

Przed/po/po przywróceniu: nie dotyczy (sonda nie zmieniała wartości).

Odpowiedzi na pytania (VM):
1. Czy na świeżym 26H2 wartość istnieje i jest REG_SZ „400”? **Tak**, Home i Pro [odczyt-domyslnych-ux].
2. Czy zmiana działa bez wylogowania i czy Eksplorator ją respektuje? **Nie rozstrzygnięte**: sonda nie zmieniała wartości.
3. Co po usunięciu wartości (czy wraca 400)? **Nie rozstrzygnięte**: nie testowano.

### 2. ux.game-dvr (tylko odczyt, bez zmiany) [odczyt-domyslnych-ux]

| Klucz | Home | Pro |
|---|---|---|
| `HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR\AllowGameDVR` | **brak** (nie istnieje nawet klucz `GameDVR` w Policies) | to samo |
| `HKU\<SID>\System\GameConfigStore\GameDVR_Enabled` | istnieje, **DWORD 1** | to samo |
| `HKU\<SID>\Software\Microsoft\Windows\CurrentVersion\GameDVR\AppCaptureEnabled` | klucz istnieje, **wartość: brak** (nie 0, nie 1) | to samo |

Odpowiedzi na pytania (VM, Home i Pro):
1. Domyślne wartości i typy: `GameDVR_Enabled` = DWORD 1 (istnieje); `AppCaptureEnabled` = **brak wartości** przy istniejącym kluczu `GameDVR` [odczyt-domyslnych-ux].
2. Czy `AllowGameDVR=0` jest egzekwowane na Home i Win 11? **Nie rozstrzygnięte**: nie ustawiano polityki.
3. Czy sama zmiana HKCU wystarcza, czy potrzebny relog? **Nie rozstrzygnięte**: nie zmieniano.
4. `GameDVR_FSEBehaviorMode`: poza zakresem, nie badano.

### 3. disk.hibernation [baseline-powercfg-a … porownanie-z-baseline]

**Ważne ograniczenie:** `powercfg /a` na obu VM mówi „The system firmware does not support hibernation” (także S1/S2/S3, S0 Low Power Idle). `C:\hiberfil.sys` **nigdy nie istniał** w żadnym kroku, a `powercfg /h on` kończy się błędem. Hibernacji na tych VM (firmware Hyper-V) nie da się włączyć, więc część pytań nie ma odpowiedzi z tego przebiegu.

Wartości rejestru (Home = Pro). „brak” oznacza brak wartości:

| Wartość | przed (baseline) | po `/h off` | po `/h on` (exit 1) | po przywróceniu* |
|---|---|---|---|---|
| `Control\Power\HibernateEnabled` | **brak** | **DWORD 0** | DWORD 0 | DWORD 0 |
| `Control\Power\HibernateEnabledDefault` | DWORD 1 | DWORD 1 | DWORD 1 | DWORD 1 |
| `Control\Power\HiberFileSizePercent` | DWORD 0 | DWORD 0 | DWORD 0 | DWORD 0 |
| `Control\Session Manager\Power\HiberbootEnabled` | DWORD 1 | DWORD 1 | DWORD 1 | DWORD 1 |
| `Control\Session Manager\Power\HibernateEnabled` | **brak** | brak | brak | brak |
| `C:\hiberfil.sys` | nie istnieje | nie istnieje | nie istnieje | nie istnieje |

\* Sekwencja z założeń zadania kończy się `/h on`; osobnego kroku przywracającego nie było (baseline `HibernateEnabled` nie istniał, więc warunek „startowo =0” nie zaszedł).

Kody wyjścia: `powercfg /a` = 0 (we wszystkich trzech wywołaniach), `/h off` = 0 (stdout i stderr puste), **`/h on` = 1** (stderr: „Hibernation failed with the following error: Żądanie nie jest obsługiwane. The following items are preventing hibernation on this system. The system firmware does not support hibernation.”).

`porownanie-z-baseline`: **`MatchesBaseline=false`** na obu VM. Różnica jedyna: `Control\Power\HibernateEnabled: brak -> DWORD 0` (utworzone przez `/h off`, nieusunięte, bo `/h on` zawiodło). Pełny zrzut obu kluczy (`Control\Power`, `Session Manager\Power`) po każdym kroku różni się od baseline wyłącznie dodaniem `HibernateEnabled=0` w `Control\Power`; żadna inna wartość się nie zmieniła (dla `Control\Power` obecne m.in. `HibernateEnabledDefault`, `HiberFileSizePercent`; w `Session Manager\Power`: `HiberbootEnabled`, `HBFlagsSwitch`, polityki binarne).

Odpowiedzi na pytania (VM):
1. Wartości po `/h off` i prawdziwa ścieżka: `/h off` **tworzy `Control\Power\HibernateEnabled` = DWORD 0**; `HibernateEnabledDefault` zostaje 1; `Session Manager\Power\HibernateEnabled` **nie istnieje** (ścieżka WinUtil niepotwierdzona, ścieżka z ElevenForum `Control\Power` potwierdzona); `HiberbootEnabled` zostaje 1 [powercfg-h-off].
2. Czy `hiberfil.sys` znika od razu i czy Szybkie uruchamianie przestaje działać? **Nie rozstrzygnięte**: plik nie istniał przed ani po. Czasu bootu nie mierzono. Obserwacja uboczna: `powercfg /a` już na baseline pisze „Fast Startup: Hibernation is not available”, mimo `HiberbootEnabled=1` (czyli ta wartość nie dowodzi, że Fast Startup działa).
3. Domyślny typ pliku (full/reduced) i `HiberFileSizePercent`: `HiberFileSizePercent` = **DWORD 0** na Home i Pro; typ pliku **nie rozstrzygnięty** (brak pliku, nie wywoływano `/h /type`). Wartości 0 nie mapujemy na „full”/„reduced” bez źródła.
4. Czy `on` po `off` przywraca typ i rozmiar? **Nie rozstrzygnięte**: `/h on` zawiodło (exit 1).

### 4. disk.ntfs-last-access [baseline-rejestr … przywroc-disablelastaccess]

Wartość `NtfsDisableLastAccessUpdate` (DWORD, odczyt jako Int32 ze znakiem), Home = Pro:

| Krok | Rejestr | Hex | stdout (przed: `fsutil behavior query`; po zmianach: stdout samego `fsutil behavior set`, bez osobnego `query`) | exit |
|---|---|---|---|---|
| przed [baseline-rejestr] | -2147483646 | **0x80000002** | `DisableLastAccess = 2  (System Managed, Last Access Time Updates ENABLED)` [query-disablelastaccess] | 0 |
| po `set disablelastaccess 1` | -2147483647 | 0x80000001 | `DisableLastAccess = 1  (User Managed, Last Access Time Updates DISABLED)` + „This operation takes effect immediately (no reboot required)” | 0 |
| po `set disablelastaccess 0` | -2147483648 | 0x80000000 | `DisableLastAccess = 0  (User Managed, Last Access Time Updates ENABLED)` + „takes effect immediately (no reboot required)” | 0 |
| po przywróceniu `set disablelastaccess 2` | -2147483646 | **0x80000002** | `DisableLastAccess = 2  (System Managed, Last Access Time Updates ENABLED)` + „takes effect immediately” | 0 |

`MatchesBaseline=true` na obu VM; argument przywrócenia (2) wyprowadzony z wartości startowej potwierdził się.

Odpowiedzi na pytania (VM):
1. Domyślna wartość i `query` (EN): rejestr **0x80000002**; `query` = `DisableLastAccess = 2  (System Managed, Last Access Time Updates ENABLED)` (Home i Pro).
2. Co zapisuje `set 0/1`: **0x80000000 / 0x80000001**, nie gołe 0/1 [set-disablelastaccess-1/0]. Flaga 0x80000000 była ustawiona w każdym zaobserwowanym stanie (0, 1, 2); o trybie decydują dolne 2 bity.
3. Czy `3` jest stabilne po restarcie? **Nie rozstrzygnięte**: nie testowano 3, nie restartowano.
4. Czy zmiana działa przed restartem (test `LastAccessTime` pliku)? **Nie rozstrzygnięte** w praktyce; `fsutil` sam wypisuje „takes effect immediately (no reboot required)”, co **kłóci się** z zapisem w dokumentacji MS („must restart”). Skutek na plikach nie był mierzony.
5. Czy `fsutil` akceptuje 2 (i 3)? **2: tak** (exit 0, zapis 0x80000002). Obserwowano tylko wartości 0, 1 i 2; **trybu 3 nie testowano**.

### 5. disk.ntfs-8dot3 [query-disable8dot3, query-8dot3name-C, set-8dot3name-1, przywroc-8dot3name]

`NtfsDisable8dot3NameCreation` (DWORD), Home = Pro:

| Krok | Rejestr | stdout | exit |
|---|---|---|---|
| przed | **DWORD 2** | `fsutil behavior query disable8dot3`: `The registry state is: 2 (Per volume setting - the default)` | 0 |
| przed, wolumin | (rejestr 2) | `fsutil 8dot3name query C:`: `The volume state is: 1 (8dot3 name creation is DISABLED)` / `The registry state is: 2 (Per volume setting - the default)` / `Based on the above settings, 8dot3 name creation is DISABLED on "C:"` | 0 |
| po `8dot3name set 1` | **DWORD 1** | `The registry state is now: 1 (Disable 8dot3 name creation on all volumes).` | 0 |
| po przywróceniu `8dot3name set 2` | **DWORD 2** | `The registry state is now: 2 (Per volume setting - the default).` | 0 |

`MatchesBaseline=true` (rejestr).

Odpowiedzi na pytania (VM):
1. Domyślna wartość: rejestr **2**. Stan woluminu C: na świeżym obrazie to **1 (nazwy 8dot3 wyłączone)**, czyli **rejestr = 2 nie opisuje stanu C:** (wbrew założeniu z researchu „system ma nazwy”).
2. Czy `set 1` działa natychmiast (nowy plik, `dir /x`)? Rejestr zmienia się od razu; działanie na plikach **nie rozstrzygnięte** (nie tworzono plików).
3. Wyjście `query` w PL i EN: **nie rozstrzygnięte**, całe wyjście było angielskie (patrz sekcja o języku).
4. Czy powrót do 2 przywraca zachowanie C:? W rejestrze tak (2). Stanu woluminu po przywróceniu **nie odczytano ponownie**; nie rozstrzygnięte.

### 6. disk.reserved-storage [dostepnosc-cmdletow … dism-get-po]

Cmdlety: `Get-WindowsReservedStorageState` i `Set-WindowsReservedStorageState` **dostępne w PS 5.1 na Home i Pro** (moduł `Dism` 3.0, `CommandType=Cmdlet`). `Set-` ma parametr **`State`** (plus `LogPath`, `ScratchDirectory`, `LogLevel` i wspólne); `Get-` nie ma `State`. Żaden z nich **nie ma parametru `-Online`** (nie jest potrzebny). `Set-` zwrócił obiekt, bez błędu.

Obiekt z `Get-`: typ `Microsoft.Dism.Commands.ReservedStorageStateObject`, właściwości: `ReservedStorageState` (enum `Microsoft.Dism.Commands.ReservedStorageState`, wartości **Disabled, Enabled**), `LogPath` (String, `C:\Windows\Logs\DISM\dism.log`), `ScratchDirectory` (brak typu/wartości), `LogLevel` (enum `Microsoft.Dism.Commands.LogLevel`: Errors, Warnings, WarningsInfo, Debug; wartość `WarningsInfo`).

| Krok | `ReservedStorageState` | `ReserveManager\PassedPolicy` | `MiscPolicyInfo` | DISM exe |
|---|---|---|---|---|
| przed [get-baseline, dism-get-przed] | **Enabled** | 1 | **brak** | exit 0, „Reserved storage is enabled.” |
| po `Set -State Disabled` | **Disabled** | **0** | **2** | (nie wołano) |
| po `Set -State Enabled` | **Enabled** | 1 | **1** | |
| po przywróceniu [przywroc-stan-startowy] | Enabled | 1 | 1 | exit 0, „Reserved storage is enabled.” [dism-get-po] |

Pozostałe wartości `ReserveManager` bez zmian w całym przebiegu (Home = Pro, poza `HardReserveAdjustment`): `BaseHardReserveSize` = QWORD 5368709120, `BaseSoftReserveSize` = QWORD 1610612736, `MinDiskSize` = QWORD 21474836480, `ShippedWithReserves` = DWORD 1, `SoftParentingValidated` = 1, `TiAttemptedInitialization` = 1; podklucz `PendingAdjustments`.

`MatchesBaseline=true` (porównanie obiektu `Get-`), ale rejestr po cyklu **nie jest identyczny z baseline**: pojawiła się wartość `MiscPolicyInfo=1`, której na starcie nie było.

Odpowiedzi na pytania (VM, Home i Pro):
1. Cmdlety w PS 5.1 na Home: **tak**; `-Online` **niepotrzebne** (brak parametru); właściwości wyniku jak wyżej.
2. Wyjście DISM po polsku? **Nie**: „Reserved storage is enabled.” po angielsku (patrz język).
3. Zachowanie przy oczekującej aktualizacji: **nie rozstrzygnięte** (nie odtwarzano).
4. `ShippedWithReserves`: **istnieje, DWORD 1, nie zmienia się** po Disabled/Enabled, więc **nie odzwierciedla stanu**. Korelują ze stanem `PassedPolicy` (1/0/1) i `MiscPolicyInfo` (brak/2/1), ale to obserwacja, nie dokumentacja.
5. Rzeczywisty rozmiar rezerwy w Ustawieniach: **nie rozstrzygnięte**. W rejestrze `BaseHardReserveSize` = 5 GiB, `BaseSoftReserveSize` = 1,5 GiB (to wartości bazowe, nie zmierzony rozmiar).

### Język wyjścia (fsutil / powercfg / DISM)

Kultura i język interfejsu obu VM to pl-PL (`meta.Culture`, `meta.UICulture`), a mimo to **cały tekst `fsutil … query/set`, `powercfg /a` i `DISM /Get-ReservedStorageState` jest po angielsku**. Jedyny polski fragment to komunikat systemowy w stderr `powercfg /h on`: „Żądanie nie jest obsługiwane.” (obok angielskiego tekstu narzędzia). **Przyczyna nie jest rozstrzygnięta** (nie sprawdzano zainstalowanych pakietów językowych ani źródła tych tekstów), więc nie wiadomo, jak wyglądałoby to wyjście w innej konfiguracji językowej. Strona kodowa OEM zapisana w rekordach: 852 (polskie znaki w „Żądanie” odczytane poprawnie, czyli ustawienie kodowania zadziałało).

Czy detekcja może obyć się bez parsowania tekstu:
- `disablelastaccess`: tak, rejestr (0x80000000 + N) jest zgodny z `query` dla zaobserwowanych 0/1/2; tryb 3 nie testowany.
- 8dot3: **tylko dla ustawienia systemowego**. Stan woluminu C: (1) różni się od rejestru (2) i jest dostępny wyłącznie w tekście `fsutil 8dot3name query C:`. Alternatywy bez tekstu nie sprawdzano (nie rozstrzygnięte).
- reserved storage: tak, obiekt `Get-WindowsReservedStorageState` (`ReservedStorageState` jako enum). Kodu wyjścia DISM nie używać do detekcji: exit 0 zaobserwowano tylko w stanie Enabled (kroków DISM w stanie Disabled nie było).
- hibernacja: `powercfg /a` zwraca exit 0 także wtedy, gdy hibernacja jest niedostępna (zaobserwowano na tych VM), więc kod wyjścia nie wystarcza; tekst nieprzydatny jako dowód (zgodnie z założeniem zadania).

### Anomalie i rozjazdy (jawnie)

1. Hibernacja niedostępna na VM (firmware): `powercfg /h on` = exit 1 na Home i Pro, `hiberfil.sys` nigdy nie powstał. Przebieg off→on nie jest reprezentatywny dla sprzętu z hibernacją.
2. `MatchesBaseline=false` dla hibernacji na obu VM: `/h off` utworzyło `HibernateEnabled=0` (baseline: brak), `/h on` tego nie cofnęło (błąd).
3. `HiberbootEnabled=1` mimo „Fast Startup: Hibernation is not available” w `powercfg /a`.
4. 8dot3: rejestr 2, a stan C: = 1 (wyłączone). Po przywróceniu stan woluminu nie był ponownie czytany.
5. `fsutil … disablelastaccess` mówi „takes effect immediately (no reboot required)”, dokumentacja MS mówi o restarcie. Skutek na plikach niezmierzony.
6. Reserved storage: krok `przywroc-stan-startowy` **nie miał wartości do przywrócenia** („brak jednoznacznej wartości startowej”), bo obiekt ma dwie właściwości-enumy (`ReservedStorageState` i `LogLevel`), a skrypt wymaga dokładnie jednej. Przebieg zakończył się poprawnie tylko dlatego, że stan końcowy (Enabled) równał się startowemu. Gdyby baseline było Disabled, skrypt nie wróciłby do niego. Kodu nie zmieniano (zgodnie z poleceniem).
7. Reserved storage: po Disabled → Enabled w `ReserveManager` zostaje nowa wartość `MiscPolicyInfo=1` (na starcie brak). `MatchesBaseline` porównuje tylko obiekt `Get-`, więc tej różnicy nie zgłosił.
8. Nie wykonywano restartu VM ani żadnych testów na plikach; wszystko, co zależy od restartu lub skutku na plikach, jest „nie rozstrzygnięte”. Nie monitorowano kluczy poza wymienionymi (np. czy `fsutil` pisze coś poza `Control\FileSystem`): **nie rozstrzygnięte**.

### Wnioski dla Tasków 3–4

- **Hibernacja:** wiernym wskaźnikiem skutku `/h off` jest `Control\Power\HibernateEnabled` (DWORD 0, tworzony przez `powercfg /h off`). `Session Manager\Power\HibernateEnabled` nie istnieje, `HibernateEnabledDefault` i `HiberbootEnabled` pozostały niezmienione (1) po `/h off` na VM bez hibernacji; czy odzwierciedlają stan na sprzęcie z hibernacją, nie wiadomo. Porównanie z `hiberfil.sys` nie jest możliwe na tych VM (plik nie powstaje). Brak wartości `HibernateEnabled` nie oznacza „wyłączone”; wartość „włączone” (1) nie została tu zaobserwowana. Undo przy baseline „brak” wymaga świadomej decyzji (usunąć wartość vs `/h on`); `/h on` zwraca exit 1 na sprzęcie bez hibernacji, więc tweak musi to obsłużyć jako błąd, nie sukces. Wymaga potwierdzenia na sprzęcie z hibernacją.
- **ux.menu-delay:** `original` = REG_SZ `"400"` (istnieje na świeżym 26H2, Home i Pro); zapis jako String.
- **ux.game-dvr:** `GameDVR_Enabled` istnieje = DWORD 1; `AppCaptureEnabled` **nie ma wartości** (undo = usunięcie, nie ustawianie 1); `AllowGameDVR` i cały klucz Policies `GameDVR` nie istnieją (undo = usunięcie wartości). Egzekwowanie polityki na Home/Win 11 niezweryfikowane.
- **disk.ntfs-last-access:** `original` = 0x80000002 (Int32 -2147483646; czytać jako DWORD ze znakiem lub uint). `set 1` → 0x80000001, `set 0` → 0x80000000, `set 2` przywraca 0x80000002. Detekcja z rejestru wystarcza dla zaobserwowanych trybów 0/1/2 (flaga 0x80000000 była zawsze ustawiona, tryb w dolnych 2 bitach; tryb 3 nie testowany). Restart według `fsutil` niepotrzebny, ale skutek niezmierzony.
- **disk.ntfs-8dot3:** `original` rejestru = 2; `8dot3name set 1` → 1, `set 2` przywraca. Rejestr 2 nie mówi nic o woluminie C: (tam 1). Tweak systemowy do 1 jest zgodny z detekcją rejestrem, ale ostrzeżenie powinno wspomnieć, że C: był już wyłączony.
- **disk.reserved-storage:** cmdlety działają na Home i Pro w PS 5.1, bez `-Online`, z `-State Enabled|Disabled`. Detekcja: `(Get-WindowsReservedStorageState).ReservedStorageState` (enum Disabled/Enabled); rejestr `ReserveManager` nie nadaje się jako jedyne źródło (`ShippedWithReserves` stałe; `PassedPolicy`/`MiscPolicyInfo` zmieniają się, ale nieudokumentowane). Uwaga: przy snapshocie nie wybierać „jedynej właściwości-enum” – obiekt ma dwie (`ReservedStorageState`, `LogLevel`); używać nazwy `ReservedStorageState`. Po cyklu zostaje `MiscPolicyInfo=1`, więc przywrócenie stanu nie jest bitowo identyczne w rejestrze.
- **Restart:** nie restartowano VM i nie sprawdzano flag oczekującego restartu (pending reboot). Wiadomo tylko, że stdout narzędzi nie zawierał komunikatu o potrzebie restartu, a przy `fsutil … disablelastaccess` jawnie pisało „no reboot required”.
