# privacy.notepad-ai – Wyłącz funkcje AI w Notatniku

## Werdykt 26H2
Portować jako pozycję **niesugerowaną**: jedna wartość opisana przez Microsoft; test na VM 26H2 (Home i Pro) pokazał, że z Notatnika znika przycisk „Writing tools” / „Narzędzia do pisania” (funkcje AI), a po zmianie Notatnik się uruchamia i działa. `risk=caution`, bo znika funkcja Notatnika.

## Co zmienia
- `HKLM\SOFTWARE\Policies\WindowsNotepad`, wartość **DWord** `DisableAIFeatures` ustawiana na `1`; `scope=machine`.
- Na świeżym 26H2 nie ma klucza `WindowsNotepad` (klucz `HKLM\SOFTWARE\Policies` istnieje). Apply tworzy klucz, undo usuwa wartość i klucz, jeśli powstał przy zmianie i jest pusty.
- Bez restartu Windows. Notatnik w teście był uruchamiany od nowa w każdej fazie; czy otwarte już okno Notatnika pokazuje zmianę od razu, nie sprawdzono.

## Źródła
- Test na VM 26H2 Home i Pro, 2026-11-07 (build 26300.9457, nieaktywowany Windows, konto lokalne, z siecią): zapis przez API rejestru (.NET) i obserwacja okna Notatnika (UI Automation, zrzuty).
- Microsoft Learn, „Manage AI features in Notepad”: https://learn.microsoft.com/en-us/windows/client-management/manage-notepad (ms.date 2025-04-30, dostęp 2026-11-07): „To disable AI features in Notepad, set the `DisableAIFeatures` registry value to `1` under `HKLM:\SOFTWARE\Policies\WindowsNotepad`.”; wymagania: „Windows 11, version 22H2 or later.”, „Notepad version 11.2503.16.0 or later.”. Typu danych strona nie podaje; **DWord** za Win11Debloat i WinUtil.
- Win11Debloat: https://github.com/Raphire/Win11Debloat (gałąź master), plik `Regfiles/Disable_Notepad_AI_Features.reg`, linie 2–3; dostęp 2026-11-06.
- WinUtil: https://github.com/ChrisTitusTech/winutil (gałąź main), plik `config/tweaks.json`, linie 953–975; dostęp 2026-11-06.

## Wartości domyślne na świeżym 26H2
Odczyt z VM `DSO-EN-Home` i `DSO-EN-Pro`, 2026-11-07: klucz `HKLM\SOFTWARE\Policies` istnieje, klucza `HKLM\SOFTWARE\Policies\WindowsNotepad` i wartości `DisableAIFeatures` **brak** (w Notatniku przycisk „Writing tools”).

## Detekcja i undo
- Detekcja: `DisableAIFeatures` = `1` -> `Applied`; brak wartości lub inna wartość -> `NotApplied`.
- Undo przywraca snapshot sprzed zmiany z journala (brak wartości = jej usunięcie; wartość, która już była, wraca) i usuwa klucz `WindowsNotepad`, jeśli utworzyło go apply i jest pusty (`KeyAbsentAfterUndo`). `original` z katalogu (`<RemoveEntry>`) to tylko fallback.
- Klucz `HKLM\SOFTWARE\Policies` istniał przed apply i zostaje (`KeyPresentAfterUndo`).

## Ryzyka i ostrzeżenia
- `risk=caution`, `suggested=false`: z Notatnika znika przycisk „Writing tools” / „Narzędzia do pisania” (narzędzia AI).
- Ostrzeżenie w katalogu: „Funkcje AI Notatnika (przycisk „Narzędzia do pisania”) będą niedostępne, aż cofniesz tę pozycję.”
- Według Microsoftu wartość działa w Notatniku od wersji 11.2503.16.0; w teście był Notatnik 11.2605.34.0.

## Test VM
Przypadek `privacy-notepad-ai` (`Flow=Full`) w `tests/vm/cases.psd1`; przebieg VM z 2026-11-08 (build 26300.9457, `Invoke-VmTests.ps1`): **Passed** na `DSO-EN-Home` (Core), `DSO-EN-Pro` i `DSO-PL-Pro` (Professional). Pełne przebiegi: 915 zaliczonych, 2 błędy, 25 pominiętych na obu VM angielskich oraz 917 zaliczonych, 0 błędów, 25 pominiętych na polskiej. Oba błędy dotyczyły przypadków dwóch pozycji powiadomień (`privacy.toast-suggested`, `privacy.toast-backup-reminder`; opis w ich dokumentacji), nie tej pozycji.

## Obserwacja skutku
Notatnik (11.2605.34.0) uruchamiany od nowa w każdej fazie i zamykany po odczycie (UI Automation, zrzuty), przed zapisem, po zapisie i po cofnięciu.
- **Home** (`DSO-EN-Home`), 2026-11-07, 20:50–20:51. Wynik: przed zapisem przycisk „Writing tools” jest; po zapisie go nie ma; po cofnięciu jest. Notatnik uruchomił się i działał po 10 s w każdej fazie, czyli po zmianie Notatnik się uruchamia.
- **Pro** (`DSO-EN-Pro`), 2026-11-07, 20:50–20:51. Ten sam wynik co na Home; po zmianie Notatnik się uruchamia i działa po 10 s.
- Brzmienia: przycisk „Writing tools” / „Narzędzia do pisania” (odczyt po angielsku i po polsku).
- Nie sprawdzono: samych funkcji AI Notatnika poza obecnością przycisku, innych wersji Notatnika niż 11.2605.34.0.
