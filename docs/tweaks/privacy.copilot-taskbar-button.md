# privacy.copilot-taskbar-button – Przycisk Copilot na pasku zadań (ShowCopilotButton)

## Werdykt 26H2
nie wchodzi: brak obserwowanego skutku na Home i Pro. Osobnego przycisku Copilot na pasku zadań 26H2 nie ma, więc nie dowiedziono, że wartość cokolwiek zmienia; przypięta aplikacja Copilot to inna rzecz (jej usunięcie jest w kategorii Aplikacje).

## Co zmienia
Pozycji nie ma w katalogu, nic nie zmienia. Rozważany wpis:
- `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced` (na VM `HKU\<SID>\...`), `ShowCopilotButton`, DWord, `0`.

## Źródła
- Test na VM 26H2 Home i Pro, 2026-11-07 (build 26300.9457, nieaktywowany Windows, konto lokalne, bez sieci): zapis przez API rejestru (.NET) i obserwacja paska zadań (UI Automation, zrzuty), także po restarcie Eksploratora.
- Brak dokumentacji MS dla tej wartości, dowód: test na VM (obserwacja niżej).
- Win11Debloat: https://github.com/Raphire/Win11Debloat (gałąź master), plik `Regfiles/Disable_Copilot.reg`, linie 2–4 (plik w UTF-16; numery linii po konwersji do UTF-8, bez pustych linii); dostęp 2026-11-06.
- RemoveWindowsAI: https://github.com/zoicware/RemoveWindowsAI (gałąź main), plik `RemoveWindowsAi.ps1`, linia 945; dostęp 2026-11-06.

## Wartości domyślne na świeżym 26H2
Odczyt z VM `DSO-EN-Home` i `DSO-EN-Pro`, 2026-11-07: klucz `Explorer\Advanced` konta istnieje, wartości `ShowCopilotButton` **brak**. Na Home jest przypięta aplikacja „Copilot” (`Microsoft.Copilot`), na Pro tej aplikacji nie ma, jest przypięta „Microsoft 365 Copilot”.

## Detekcja i undo
Nie dotyczy (pozycja nie jest w katalogu).

## Ryzyka i ostrzeżenia
Nie dotyczy. Pozycja nie weszła do katalogu.

## Test VM
Nie dotyczy (nie wchodzi): pozycji nie ma w katalogu, więc nie ma przypadku VM.

## Obserwacja skutku
- **Home** (`DSO-EN-Home`), 2026-11-07. Metoda: zapis wartości przez API rejestru, pasek zadań przed zapisem, po zapisie, po restarcie Eksploratora i po cofnięciu (20:52–20:53). Wynik: **brak skutku**. Osobnego przycisku Copilot na pasku nie ma w żadnej fazie; przypięta aplikacja „Copilot” (po polsku „Copilot — przypięto”) zostaje po zapisie i po restarcie Eksploratora.
- **Pro** (`DSO-EN-Pro`), 2026-11-07. Ta sama metoda. Wynik: **brak skutku**. Osobnego przycisku Copilot nie ma w żadnej fazie; aplikacji Copilot nie ma, przypięta „Microsoft 365 Copilot” bez zmian.
