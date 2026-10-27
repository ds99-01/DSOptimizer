# repair.windows-old – Usuń poprzednią instalację Windows

## Werdykt 26H2
Portować jako `kind=action`, `risk=caution` (kategoria „Naprawy”, Etap 4b partia 2, długa naprawa z oknem postępu). Mechanizm
zmierzony na VM 26H2 Home i Pro (`docs/zrodla/2026-10-22-etap4b-2-pomiary.md`, pkt 5, 5f i 6): program obsługi
„Previous Installations” Oczyszczania dysku (`cleanmgr /sagerun`) rozpoznaje przygotowany folder `C:\Windows.old` i go usuwa.
Tylko mechanizm A – bez przejmowania własności i zmiany ACL w C# (decyzja Dawida R8, Ruling R18). Przygotowany folder to zwykłe
pliki z właścicielem `BUILTIN\Administratorzy`; prawdziwy `Windows.old` po aktualizacji funkcji ma pliki z ACL `TrustedInstaller` i
tysiące elementów – ten scenariusz jest **do potwierdzenia na sprzęcie**.

## Co zmienia
Handler C# `windows-old` (`WindowsOldHandler`), bez skryptu katalogu:
1. Strażnik `ServicingGuard`: działający proces `dism`, `sfc` albo `cleanmgr` = `Refused` „Poprzednia operacja serwisowania jeszcze
   trwa – spróbuj za chwilę.”, nic nie startuje.
2. Brak `<SystemDrive>\Windows.old` (dysk z `%SystemRoot%`) = `NotSupported` „Nie ma poprzedniej instalacji Windows – nie ma czego
   usuwać.”, bez żadnego kroku (rejestr nawet nieczytany). Istnienie folderu sprawdza `IRepairFileSystem.ProbeDirectory`
   (`File.GetAttributes`), nie `Directory.Exists`: „nie ma” tylko przy `ERROR_FILE_NOT_FOUND`/`ERROR_PATH_NOT_FOUND`, każdy inny błąd
   (brak dostępu, folder w trakcie usuwania, dysk niegotowy) = `Failed` „Nie udało się sprawdzić folderu … – nic nie zmieniono.”
   (wynik jest pewny: niczego nie zmieniono ani nie uruchomiono). Potem wolne miejsce dysku systemowego (`IDiskSpace`).
3. Sprawdzenie ustawień Oczyszczania dysku przed jakimkolwiek zapisem: lista podkluczy
   `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches` (`IRegistry.SubKeyNames`, widok 64-bit) i odczyt wartości
   `StateFlags7351` pod każdym. Istniejąca wartość (cudze `cleanmgr /sageset:7351` albo przerwany wcześniej przebieg), błąd odczytu
   albo brak klucza `VolumeCaches\Previous Installations` = `Failed` „… – nic nie zmieniono.”; niczego nie zapisujemy i nie
   uruchamiamy. Numer `WindowsOldHandler.SageRunId = 7351` z pomiaru 5f (na świeżej 26H2 brak jakiejkolwiek wartości `StateFlags*`
   pod 31 programami obsługi; `cleanmgr` przyjmuje 0–9999).
4. Po potwierdzeniu przebiegu nie da się przerwać (Ruling R30): oba zdarzenia postępu mają `ActionProgress.Stoppable = false`, więc
   `ActionRun.CanStop` jest `false` przez cały przebieg i okno nie oferuje „Zatrzymaj” (test `GuiModel_CanStopFalseThroughoutRun`).
   Handler nie czyta tokenu anulowania; token anulowany przed startem obsługuje silnik (`TweakEngine.RunHandler`: „Anulowano przed
   uruchomieniem”), a CLI nie ma zatrzymania. Wycofać się można tylko w oknie potwierdzenia.
5. Krok 1 z 2 „Oczyszczanie dysku”: `StateFlags7351` = DWORD `2` (zaznaczone) **tylko** pod `VolumeCaches\Previous Installations`,
   potem `%SystemRoot%\System32\cleanmgr.exe /sagerun:7351` przez `IServicingToolRunner` (`CreateProcessW` pełnej ścieżki, poza Job
   Objectem, bez zabijania, limit = `timeoutSeconds` = 2700 s). Program czeka na koniec procesu (decyzja Tasku 2: folder znika w kilka
   sekund, ale w pomiarze Tasku 2 cleanmgr z `DismHost` pracował jeszcze ok. 11 min; czy usunięcie flagi przed końcem procesu
   zmienia wynik – niezmierzone). W przebiegach handlera (Task 8) cleanmgr kończył się sam po 7 s – sekcja „Obserwacja skutku”. W `finally` usunięcie własnej wartości `StateFlags7351` – także po błędzie startu, wyjątku i limicie. Nieudane
   zaznaczenie = `Failed`, cleanmgr nie startuje. Nieudane usunięcie = `Failed` z wpisem w „nie przywrócono” (inaczej kolejne
   uruchomienie odmówi przez punkt 3).
6. Krok 2 z 2 „Sprawdzenie” (odczyt zwrotny): wolne miejsce po i istnienie folderu.
   - folder nie istnieje = `Executed` „Usunięto poprzednią instalację Windows. Zwolniono: <DiskSpaceText.Freed>.” (kod cleanmgr ≠ 0
     dopisany jako uwaga; kodów cleanmgr MS nie opisuje);
   - folder istnieje = `Failed` „Folder Windows.old nadal istnieje (zwolniono: …).”;
   - błąd sprawdzenia folderu (`ProbeDirectory` rzuca) = `NotConfirmed` „Oczyszczanie dysku zakończyło pracę, ale nie udało się
     sprawdzić, czy folder … zniknął (<błąd>) – sprawdź go ręcznie”; uwaga o kodzie cleanmgr zostaje, a nieusunięta flaga i tak daje
     `Failed` z wpisem w „nie przywrócono” (Ruling R27);
   - po limicie = `NotConfirmed` „Czyszczenie nadal działa w tle – program przestał czekać po 45 min.” + stan folderu („jeszcze
     istnieje – sprawdź go później”, „już nie istnieje (zwolniono: …)” albo „nie udało się sprawdzić folderu … (<błąd>)”); cleanmgr
     pracuje dalej.

Handler niczego sam nie usuwa, nie przejmuje własności i nie zmienia ACL (test `NeverDeletesFilesItself`).

## Źródła
- Test na VM 26H2 Home i Pro (build 26300.9457, pl-PL), 2026-10-22: `docs/zrodla/2026-10-22-etap4b-2-pomiary.md`,
  pkt 5, 5f i 6, sekcja 5 (skrypt `tests/vm/explore/Invoke-E4B2Exploration.ps1`, `Invoke-E4B2WindowsOldProbe`).
- `cleanmgr` (`/sageset:n`, `/sagerun:n`, n = 0–9999): https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/cleanmgr
  (MS Learn, za pomiarem z 2026-10-22).
- `StateFlagsNNNN` pod `VolumeCaches\<program obsługi>` (0 = niezaznaczone, 2 = zaznaczone):
  https://learn.microsoft.com/en-us/troubleshoot/windows-server/backup-and-storage/automating-disk-cleanup-tool (MS Learn, za pomiarem).
- Windows.old i brak powrotu po usunięciu („this can't be undone (you won't be able to go back to your previous version of
  Windows)”; system sam usuwa poprzednią wersję 10 dni po aktualizacji):
  https://support.microsoft.com/en-us/windows/delete-your-previous-version-of-windows-f8b26680-e083-c710-b757-7567d69dbb74
  (Microsoft Support, dostęp 2026-10-23).

## Wartości domyślne na świeżym 26H2
`C:\Windows.old` nie istnieje (pozycja daje `NotSupported`). `VolumeCaches`: 31 programów obsługi, w tym `Previous Installations`,
żadnej wartości `StateFlags*` – odczyt 2026-10-22 (Home i Pro).

## Detekcja i undo
Nie dotyczy: `kind=action` bez detekcji i cofnięcia (`reversible` nie dotyczy akcji). Odczytem zwrotnym jest istnienie folderu po
zakończeniu cleanmgr i wolne miejsce przed/po. Usuniętej poprzedniej instalacji nie da się przywrócić.

## Ryzyka i ostrzeżenia
- `risk=caution`: po usunięciu nie da się wrócić do poprzedniej wersji Windows (Microsoft Support).
- Czas: folder znika w kilka sekund; Oczyszczanie dysku kończyło pracę po 7 s (przebiegi handlera, Task 8) albo po ok. 11 min
  (pomiar K2 Tasku 2); przy prawdziwym, dużym Windows.old – niezmierzone. Limit czekania 45 min, po nim cleanmgr pracuje dalej w tle.
- Po potwierdzeniu nie da się przerwać: „Zatrzymaj” jest nieaktywne przez cały przebieg (cleanmgr nie jest przerywany).
- Ustawienie `StateFlags7351` istnieje tylko na czas przebiegu i dotyczy jednego programu obsługi; cudza wartość z tym numerem =
  odmowa bez zmian.
- Okno cleanmgr w sesji użytkownika: **niezmierzone** (pomiar K2 bez zrzutu ekranu).
- Z sesji 0 (PowerShell Direct, K1) cleanmgr usuwa folder, ale nie kończy się w 1200 s – program uruchamiany z sesji użytkownika
  (GUI po UAC albo zadanie Harmonogramu) tego problemu nie ma (pomiar K2). Uruchomienie handlera z sesji 0 – niezmierzone; według
  pomiaru K1 spodziewany wynik to `NotConfirmed` po 45 min, z folderem już usuniętym.

## Test VM
Przypadki w `tests/vm/cases.psd1` (Task 8), `Flow='Execute'`, wbudowany katalog, bez sieci:
- `repair-windows-old-absent` (bez `RunAs`): świeża VM bez `Windows.old` → `NotSupported`, kod 1, komunikat „nie ma czego usuwać”;
  `Check`: folderu nie ma, żadnej wartości `StateFlags7351` pod `VolumeCaches`, cleanmgr nie działa.
- `repair-windows-old` (`RunAs='dso'`, CLI z zadania Harmonogramu konta `dso` – z PS Direct cleanmgr się nie kończy, pomiar 5,
  Ruling R18), `CliTimeoutSeconds = 3000` (powyżej limitu handlera 2700 s, żeby wynik `NotConfirmed` też dotarł do raportu).
  `Setup`: zwykły folder `C:\Windows.old` jak w pomiarze 5 (pliki testowe w `Windows\System32`, `Windows\SysWOW64`,
  `Users\Public\Documents`, `Program Files\DsoTest`, `ProgramData\DsoTest`, właściciel Administratorzy, bez zmiany ACL).
  `Check`: `C:\Windows.old` nie istnieje, brak `StateFlags7351` pod wszystkimi `VolumeCaches\*`, cleanmgr nie działa.
  `Expect`: `Executed`, kod 0, komunikat „Usunięto poprzednią instalację Windows.”.

Wyniki przebiegu: sekcja „Obserwacja skutku”.

## Obserwacja skutku
Metoda: pomiar mechanizmu (Task 2): przygotowany folder 200 MB, `StateFlags7351` tylko pod `Previous Installations`,
`cleanmgr /sagerun:7351`, próbki co 5 s (istnienie folderu, wolne miejsce, procesy), po przebiegu odczyt wszystkich `VolumeCaches`.
- **Home** (`DSO-26H2-Home`, 26300.9457), 2026-10-22: K1 (PS Direct, sesja 0) – folder znika w ≤ 5 s, miejsce wraca (+208,8 MB),
  cleanmgr z `DismHost` żyje do limitu 1200 s (zabity); K2 (zadanie `dso`, sesja 1) – folder znika w ≤ 5 s, cleanmgr kończy się sam
  po 641 s z wynikiem 0x0; flaga po każdym przebiegu nieobecna pod 31 kluczami.
- **Pro** (`DSO-26H2-Pro`, 26300.9457), 2026-10-22: K1 jak Home; K2 – folder znika w ≤ 5 s, koniec po 665 s, wynik 0x0.
- Co robi `DismHost` przez ok. 11 min po usunięciu folderu – niezmierzone (wolne miejsce zmienia się w tym czasie o mniej niż 1 MB).
- Sam handler (Task 8, 2026-10-23, `tests/vm/Invoke-VmTests.ps1 -CasesPath <tylko dwa przypadki>`, gałąź `etap4b-2`, bez sieci,
  raporty poza gitem): **Passed** na Home i Pro (po 12 zaliczonych / 0 błędów). `repair-windows-old-absent`: `NotSupported`, kod 1,
  „Nie ma poprzedniej instalacji Windows – nie ma czego usuwać.”. `repair-windows-old` (CLI z zadania konta `dso`):
  `cleanmgr.exe /sagerun:7351` zakończony **kodem 0 po 7 s** (Home i Pro), `Executed` „Usunięto poprzednią instalację Windows.
  Zwolniono: 158 MB.” (Home) / „… 193 MB.” (Pro); niezależny `Check` w VM: folderu nie ma, brak `StateFlags7351` pod wszystkimi
  `VolumeCaches\*`, cleanmgr nie działa. Drugi, diagnostyczny przebieg (ten sam przypadek, `Check` dodatkowo bez działającego
  `DismHost`): Passed na obu, znowu kod 0 po 7 s, zwolniono 192 MB / 198 MB, a po wyjściu cleanmgr `DismHost` nie działa – po
  zakończeniu nic nie pracuje w tle. Dlaczego pomiar Tasku 2 (K2: `Process` z `CreateNoWindow` w zadaniu Harmonogramu) trwał
  641/665 s, a przebieg handlera (`CreateProcessW` z DSOptimizera w zadaniu konta `dso`) 7 s – **niezmierzone**.
- Prawdziwy `Windows.old` po aktualizacji funkcji (pliki `TrustedInstaller`, kilka–kilkanaście GB, opcja powrotu w Ustawieniach):
  **do potwierdzenia na sprzęcie** – na VM nie da się go uczciwie odtworzyć bez zmiany ACL (R8).

## Reddit
Do uzupełnienia w części E.
