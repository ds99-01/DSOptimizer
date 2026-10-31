# perf.paging-executive – Trzymaj kod jądra i sterowników w pamięci

## Werdykt 26H2
Portować jako eksperymentalne: wartość jest udokumentowana przez MS, ale tylko w źródłach archiwalnych (Windows Server 2003, blog z 2007). Brak dowodu korzyści na 26H2: MS nie wypowiada się o Windows 10/11, a na VM nie ma wskaźnika, który skutek by pokazał.

## Co zmienia
- `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management`, wartość `DisablePagingExecutive`, **DWord**: `0` -> `1`. `scope=machine`, zapis przez API rejestru w C#, bez skryptu.
- Wg MS (archiwum): 0 = sterowniki i kod systemowy mogą być stronicowane na dysk (domyślnie), 1 = muszą pozostać w pamięci fizycznej. Zmiana działa dopiero po restarcie, więc `requiresReboot=true`.
- Skutek uboczny: kod jądra i sterowników zajmuje pamięć fizyczną na stałe.

## Źródła
- https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-server-2003/cc757875(v=ws.10) – MS, archiwum (Windows Server 2003), dostęp 2026-10-18: typ REG_DWORD, zakres 0/1, domyślnie 0, wymagany restart; ustawienie 1 przydaje się przy debugowaniu sterowników i „improves performance on machines with a lot of memory”.
- https://learn.microsoft.com/en-us/archive/blogs/irenak/sysk-318-registry-key-that-controls-device-driver-and-system-code-pagination-to-disk – MS, archiwalny blog (SYSK 318, 2007-03-30), dostęp 2026-10-18: ten sam opis; na maszynach z dużą ilością pamięci może poprawić wydajność, ale „would depend on the installed applications and their resource utilization”.
- Brak źródła MS dla Windows 10, 11 i 26H2 (nie wiadomo, czy współczesne jądro wartość honoruje ani jak działa przy VBS/HVCI).
- Odczyt VM 26H2 Home i Pro, 2026-10-18 (`docs/zrodla/2026-10-18-partia-c2-zrodla.md`, „Wyniki VM”, sekcja 2).

## Wartości domyślne na świeżym 26H2
Odczyt z VM DSO-26H2-Home i DSO-26H2-Pro (build 26300.9457), 2026-10-18 (Home = Pro):
- `DisablePagingExecutive` **istnieje i wynosi DWORD 0**. Zatem `original` = 0, a snapshot na takim obrazie zapisze 0, nie brak wartości.
- Nie rozstrzygnięto: jaki jest mierzalny skutek i zachowanie przy VBS/HVCI (na VM VBS nie jest włączone). To, że wartość zostaje po zapisie 1 i prawdziwym restarcie, potwierdził przebieg VM (sekcja „Test VM”).

## Detekcja i undo
- Detekcja: tylko rejestr; wartość DWORD 1 -> `Applied`, każda inna lub brak -> `NotApplied`. Realny stan jądra nie jest dostępny przez żadne udokumentowane API, więc `Applied` oznacza „wartość ustawiona wg MS”, a nie „zmierzony efekt”. Apply zwraca `Applied` z flagą wymaganego restartu (`requiresReboot`); `PendingReboot` tylko wtedy, gdy detekcja po zapisie nie potwierdzi wartości.
- Undo: przywraca wartość ze snapshotu z journala. Jeśli na jakimś obrazie wartości nie było, undo ją usuwa (`<RemoveEntry>` ze snapshotu), a nie zapisuje 0. Fallback `original` = 0 (potwierdzony odczytem VM).

## Ryzyka i ostrzeżenia
- `risk=experimental`.
- Ostrzeżenia w katalogu: „Wymaga restartu komputera.” oraz „Część pamięci RAM zostaje zajęta na stałe.”
- Poza uwagami w programie (w szczegółach technicznych albo tylko tutaj): kod jądra i sterowników zostaje w pamięci fizycznej; źródła MS są tylko archiwalne (2003/2007) i mówią o zysku jedynie na maszynach z dużą ilością pamięci i zależnie od aplikacji; brak dowodu korzyści na 26H2.
- Opis v3.2 („drastycznie przyspiesza syscalle”) i próg „32 GB RAM” nie mają źródła MS, więc nie wchodzą do katalogu.

## Test VM
Przebieg VM z 2026-10-18 (13:51, build 26300.9457, HEAD `da78bd7`), DSO-26H2-Home (Core) i DSO-26H2-Pro (Professional), oba bez błędów (212 zaliczonych / 0 błędów / 10 pominiętych na każdej VM; pominięte to wyłącznie `disk-hibernation`). Raporty lokalne: `tests/vm/out/20261018-135146/<VM>/<RunLabel>/report.json` (katalog w `.gitignore`).

Przypadek `perf-paging-executive` (`Flow=Reboot`, przebieg `reboot-perf-paging-executive`, osobny przebieg od czystego checkpointu; Pester 14/14 na Home i Pro), Home i Pro identycznie:
- Sprawdzono: `detect-before` = `NotApplied`, `apply` = `Applied` (`requiresReboot` = true), `detect-after-apply` = `Applied`, **prawdziwy restart** (potwierdzony późniejszym `LastBootUpTime` i zniknięciem ulotnego znacznika), `detect-after-reboot` = `Applied`, `undo` = `Undone` (`requiresReboot` = true, `finalState` = `NotApplied`), `detect-after-undo` = `NotApplied`. Stan końcowy = stan przed (DWORD 0).
- Przypadku „wartości brak na starcie” nie ma, bo na Home i Pro wartość istnieje; ścieżkę `<RemoveEntry>` pokrywa test jednostkowy na fake'ach.
- **Nie sprawdzono:** skutku `DisablePagingExecutive` (system nie daje wskaźnika, a pozycja nie ma dowodu korzyści na 26H2), zachowania pod obciążeniem pamięci ani na sprzęcie. Sprawdzana jest tylko wartość rejestru i jej trwałość po restarcie.
