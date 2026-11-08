# privacy.paint-ai – Funkcje AI w Paincie (DisableCocreator, DisableGenerativeFill, DisableImageCreator)

## Werdykt 26H2
nie wchodzi: nieobserwowalne na VM (funkcje AI Painta wymagają aktualizacji aplikacji). Paint w wersji z obrazu nie udostępnia funkcji AI bez aktualizacji, więc nie dowiedziono, że polityki je wyłączają; nie sprawdzono nowszej wersji Painta.

## Co zmienia
Pozycji nie ma w katalogu, nic nie zmienia. Rozważane wpisy:
- `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Paint`, `DisableCocreator`, DWord, `1`.
- `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Paint`, `DisableGenerativeFill`, DWord, `1`.
- `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Paint`, `DisableImageCreator`, DWord, `1`.

## Źródła
- Test na VM 26H2 Home i Pro, 2026-11-07 (build 26300.9457, nieaktywowany Windows, konto lokalne, z siecią): zapis przez API rejestru (.NET) i obserwacja w Paincie (UI Automation, zrzuty).
- Microsoft Learn, Policy CSP WindowsAI: https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-windowsai (ms.date 2026-10-14, dostęp 2026-11-07).
- Win11Debloat: https://github.com/Raphire/Win11Debloat (gałąź master), plik `Regfiles/Disable_Paint_AI_Features.reg`, linie 2–4 (plik w UTF-16; numery linii po konwersji do UTF-8, bez pustych linii); dostęp 2026-11-06.
- RemoveWindowsAI: https://github.com/zoicware/RemoveWindowsAI (gałąź main), plik `RemoveWindowsAi.ps1`, linie 1134–1138; dostęp 2026-11-06.

## Wartości domyślne na świeżym 26H2
Odczyt z VM `DSO-EN-Home` i `DSO-EN-Pro`, 2026-11-07: klucza `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Paint` i wszystkich trzech wartości **brak**. Paint 11.2601.440.0.

## Detekcja i undo
Nie dotyczy (pozycja nie jest w katalogu).

## Ryzyka i ostrzeżenia
Nie dotyczy. Pozycja nie weszła do katalogu.

## Test VM
Nie dotyczy (nie wchodzi): pozycji nie ma w katalogu, więc nie ma przypadku VM.

## Obserwacja skutku
- **Home** (`DSO-EN-Home`), 2026-11-07. Metoda: zapis trzech wartości przez API rejestru; Paint 11.2601.440.0 uruchamiany od nowa przed zapisem, po zapisie i po cofnięciu (20:49–20:50) i zamykany po odczycie. Wynik: **nieobserwowalne**. W każdej fazie Paint pokazuje „Download updates to use AI features – You need the latest updates to use new AI features.”, a przycisk „Copilot” jest przed zapisem, po zapisie i po cofnięciu. Jedyna różnica: przycisk „Sign in” znika po zapisie i wraca po cofnięciu, co nie dowodzi wyłączenia funkcji AI. Paint uruchomił się i działał po 10 s w każdej fazie.
- **Pro** (`DSO-EN-Pro`), 2026-11-07. Ta sama metoda i ten sam wynik.
