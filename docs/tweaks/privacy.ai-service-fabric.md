# privacy.ai-service-fabric – Usługa Windows AI Components Host na ręczny (WSAIFabricSvc)

## Werdykt 26H2
nie wchodzi: zysk pomijalny. Usługa po 4 min bezczynności zajmuje 1,8 MB pamięci prywatnej przy 0,0 s czasu CPU, więc zmiana typu uruchomienia daje pomijalny zysk; samej zmiany na ręczny nie sprawdzono (nie wiadomo, czy usługa i tak startuje na żądanie ani czy coś przestaje działać).

## Co zmienia
Pozycji nie ma w katalogu, nic nie zmienia. Rozważany wpis:
- `HKLM\SYSTEM\CurrentControlSet\Services\WSAIFabricSvc`, `Start`, DWord, `3` (uruchamianie ręczne).

## Źródła
- Test na VM 26H2 Home i Pro, 2026-11-07 (build 26300.9457, nieaktywowany Windows, konto lokalne, VM bez NPU): tylko odczyt stanu usługi i zużycia zasobów jej procesu, bez zmiany wartości.
- Brak dokumentacji MS dla tej zmiany.
- Win11Debloat: https://github.com/Raphire/Win11Debloat (gałąź master), plik `Regfiles/Disable_AI_Service_Auto_Start.reg`, linie 2–3 (plik w UTF-16; numery linii po konwersji do UTF-8, bez pustych linii); dostęp 2026-11-06.
- WinUtil: https://github.com/ChrisTitusTech/winutil (gałąź main), plik `config/tweaks.json`, linie 953–1008; dostęp 2026-11-06.

## Wartości domyślne na świeżym 26H2
Odczyt z VM `DSO-EN-Home` i `DSO-EN-Pro`, 2026-11-07: usługa `WSAIFabricSvc` („Windows AI Components Host”) ma typ uruchomienia automatyczny (Auto) i działa (Running) we własnym procesie `svchost.exe -k WSAIFabricSvcGroup -p`.

## Detekcja i undo
Nie dotyczy (pozycja nie jest w katalogu).

## Ryzyka i ostrzeżenia
Nie dotyczy. Pozycja nie weszła do katalogu.

## Test VM
Nie dotyczy (nie wchodzi): pozycji nie ma w katalogu, więc nie ma przypadku VM.

## Obserwacja skutku
- **Home** (`DSO-EN-Home`), 2026-11-07. Metoda: tylko odczyt stanu usługi oraz pamięci i czasu CPU jej procesu po 4 min bezczynności, bez zapisu; potem powrót VM do punktu kontrolnego. Wynik: usługa Auto, Running; **1,8 MB** pamięci prywatnej (zestaw roboczy 9,4 MB), **0,0 s** czasu CPU (liczby pamięci i CPU zapisane raz dla obu VM, bez rozbicia na Home i Pro).
- **Pro** (`DSO-EN-Pro`), 2026-11-07. Ta sama metoda i ten sam wynik.
