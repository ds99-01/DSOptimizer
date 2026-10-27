# repair.winget – Napraw winget (Instalator aplikacji)

## Werdykt 26H2
Portować jako `kind=action`, `risk=safe` (kategoria „Naprawy”, Etap 4b partia 1): na świeżej instalacji `winget` bywa niedostępny, bo brakuje aliasu `winget.exe` w profilu konta. Rejestracja istniejącego pakietu „Instalator aplikacji” dla konta przywraca alias bez pobierania czegokolwiek i działa na 26H2 Home i Pro. Bez cofnięcia.

## Co zmienia
Skrypt (`script.apply`), tylko dla konta, które uruchomiło DSOptimizer:
1. Odmowa, gdy SID procesu ≠ SID zalogowanego użytkownika: „Naprawa winget działa tylko dla konta, na którym działa DSOptimizer: uruchom DSOptimizer na koncie <nazwa>.” Nic nie zmieniono.
2. Odczyt: pakiet `Microsoft.DesktopAppInstaller` w stanie `Installed` dla konta (`Get-AppxPackage -AllUsers`, wpis `PackageUserInformation`) oraz `winget --version` z aliasu `%LOCALAPPDATA%\Microsoft\WindowsApps\winget.exe` konta.
3. Oba warunki spełnione (kod 0 i numer wersji `v<cyfry>` w wyjściu) → „winget działa (wersja <v>), nic nie zmieniono.” bez rejestracji.
4. W przeciwnym razie `Add-AppxPackage -RegisterByFamilyName -MainPackage Microsoft.DesktopAppInstaller_8wekyb3d8bbwe` (rejestracja pakietu już obecnego w systemie; bez pobierania, bez `-Path`, bez modułów z internetu) i ponowny odczyt według tego samego kryterium.
5. Sukces (`Executed`) tylko po odczycie zwrotnym: „Zarejestrowano ponownie „Instalator aplikacji” dla konta <SID>: winget działa (wersja <v>, odczyt zwrotny).”
6. Gdy po rejestracji dalej nie działa albo brak pakietu: błąd (`Failed`) „Rejestracja nie pomogła: <stan>. Zaktualizuj „Instalator aplikacji” w Microsoft Store: ms-windows-store://pdp/?ProductId=9NBLGGH4NNS1.” Ten adres jest jedynym dodanym do `ExternalLinkPolicy` (dokładny napis; inny `ProductId` i inny schemat = odmowa).

Wersji minimalnej nie porównujemy: niezmierzona (obraz 26H2 ma jedną wersję, a starszego pakietu nie pobieramy).

## Źródła
- Pomiary na VM 26H2 Home i Pro (build 26300.9457), 2026-10-22: `docs/zrodla/2026-10-22-etap4b-1-pomiary.md`, sekcja 6 i wiersze 6, 6i tabeli decyzji.
- Id „Instalatora aplikacji” w Sklepie `9NBLGGH4NNS1`: strona `https://apps.microsoft.com/detail/9NBLGGH4NNS1` („App Installer”) i strona Microsoft „Use WinGet” podają ten sam identyfikator (sprawdzone 2026-10-22).
- Brak innej dokumentacji MS sprawdzonej w tym zadaniu; dowód: test na VM opisany niżej.

## Wartości domyślne na świeżym 26H2
Pakiet `Microsoft.DesktopAppInstaller_1.26.509.0_x64__8wekyb3d8bbwe` (w obrazie `…_2025.926.104.0_neutral_~_…`) w stanie `Installed` dla konta, alias `winget.exe` jest (ReparsePoint, długość 0), `winget --version` = `v1.11.510`, kod 0. Na świeżej VM akcja więc kończy się „winget działa …, nic nie zmieniono.”.

## Detekcja i undo
Nie dotyczy: `kind=action` nie ma detekcji ani cofnięcia. Dowodem skutku jest odczyt zwrotny (`winget --version` kod 0 z wersją i pakiet `Installed` dla konta).

## Ryzyka i ostrzeżenia
- `risk=safe`: rejestracja istniejącego pakietu przywraca alias; gdy winget już działa, nic się nie zmienia.
- Akcja niczego nie pobiera i nie instaluje; bez pakietu w systemie kończy się błędem i odesłaniem do Sklepu.
- Potwierdza tylko uruchomienie `winget --version`; działanie źródeł (`winget search`, `install`) wymaga sieci i nie jest weryfikowane (sam `winget source list` kończy się kodem 0 także offline).
- Tylko konto, na którym działa DSOptimizer; z innego konta administratora odmawia.

## Test VM
Przypadki w `tests/vm/cases.psd1` (Task 8):
- `repair-winget-other-account` (Administrator PS Direct, `MessageContains='uruchom DSOptimizer na koncie'`, `Check`: stan pakietu dla kont bez zmian).
- `repair-winget-dso` (`RunAs='dso'`; `Setup` psuje stan usunięciem aliasu `winget.exe` w profilu `dso`, jak w pomiarze; `Check`: `winget --version` uruchomione jako `dso` przez zadanie Harmonogramu kończy się kodem 0 z wersją). Stanu „Staged” dla App Installera nie da się przygotować (`Remove-AppxPackage` = 0x80073CFA, „part of Windows”), więc „zepsucie” to sam alias.

Przebieg VM (Task 10, 2026-10-22, pełny `tests/vm/Invoke-VmTests.ps1` na gałęzi `etap4b-1`; raporty poza gitem): Home i Pro po 722 zaliczone / 0 błędów / 25 pominiętych (6 przypadków ze `Skip` spoza kategorii Naprawy). `repair-winget-other-account`: **Passed** na Home i Pro (`Failed`, „… uruchom DSOptimizer na koncie <PC>\dso.”, stan pakietu bez zmian). `repair-winget-dso`: **Passed** na Home i Pro (`Executed`, „Zarejestrowano ponownie „Instalator aplikacji” … winget działa (wersja v1.11.510, odczyt zwrotny).”, `Check` `winget --version` jako `dso` = kod 0).

## Obserwacja skutku
Metoda: w procesie konta `dso` (zadanie Harmonogramu Interactive + Highest, jak DSOptimizer po UAC) usunięcie aliasu `winget.exe`, potem `Add-AppxPackage -RegisterByFamilyName -MainPackage Microsoft.DesktopAppInstaller_8wekyb3d8bbwe` i `winget --version`; surowe wyniki poza gitem.
- **Home** (`DSO-26H2-Home`, 26300.9457), 2026-10-22: po usunięciu aliasu `winget` = „Nie można odnaleźć określonego pliku”, pakiet dalej `Installed`; rejestracja po nazwie rodziny (0,4–0,5 s) przywraca alias, `winget --version` = `v1.11.510`, kod 0. `Remove-AppxPackage` dla konta: odmowa 0x80073CFA.
- **Pro** (`DSO-26H2-Pro`, 26300.9457), 2026-10-22: identycznie (przebieg A2: rejestracja 0,53 s). Z siecią (N1): `winget source list` kod 0, `winget search PowerToys` pobrał źródło i zwrócił wynik, kod 0 (akcja tego nie sprawdza).

## Reddit
Do uzupełnienia w części E.
