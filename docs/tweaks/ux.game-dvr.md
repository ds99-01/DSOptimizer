# ux.game-dvr – nagrywanie w tle Game DVR (użytkownik)

## Werdykt 26H2
Nie portowane: brak źródła MS dla skutku `GameDVR_Enabled = 0` (VM potwierdziła tylko istnienie klucza, typ DWORD i wartość domyślną 1), a spec wymaga udokumentowanego przez MS mechanizmu; zamiast tego alias `ux_gamedvr` z v3.2 wskazuje na `ux.game-dvr-policy`.

## Co zmienia
Pozycji nie ma w katalogu, nic nie zmienia. Rozważany wpis: `HKCU\System\GameConfigStore`, wartość `GameDVR_Enabled`, DWord, `1` -> `0` (`scope=user`).

Powody odrzucenia:
- Znaczenie „0 = nagrywanie w tle wyłączone” pochodzi wyłącznie z odpowiedzi na Microsoft Q&A (hint, nie wiadomo, czy autor jest pracownikiem MS). Skutek nie był mierzony na VM.
- `AppCaptureEnabled` (`HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR`): klucz `GameDVR` istnieje na świeżym 26H2, ale wartości **nie ma** (VM, Home i Pro), a źródła MS brak. Nie wchodzi tak czy inaczej.
- `GameDVR_FSEBehaviorMode` (v3.2): bez źródła MS i bez odczytu VM.

Polityka maszynowa udokumentowana przez MS jest osobną pozycją: `ux.game-dvr-policy`.

## Źródła
- https://learn.microsoft.com/en-us/answers/questions/443550/my-xbox-game-bar-automatically-disabled-and-doesnt – hint (Microsoft Q&A), dostęp 2026-10-17: `GameDVR_Enabled` 1 = włączone.
- https://raw.githubusercontent.com/ChristitusTech/winutil/main/config/tweaks.json – hint, dostęp 2026-10-17 (brak wpisu GameDVR).
- Odczyt VM 26H2 Home i Pro, 2026-10-18 (istnienie i typ).
- Research: `docs/zrodla/2026-10-17-partia-c1-zrodla.md`, sekcja 2.

## Wartości domyślne na świeżym 26H2
Odczyt z VM DSO-26H2-Home i DSO-26H2-Pro (build 26300.9457), 2026-10-18, konto `dso`:
- `HKU\<SID>\System\GameConfigStore\GameDVR_Enabled` = **DWORD 1** (Home i Pro).
- `HKU\<SID>\Software\Microsoft\Windows\CurrentVersion\GameDVR\AppCaptureEnabled`: klucz istnieje, **wartość: brak**.

## Detekcja i undo
Nie dotyczy (pozycja nie jest w katalogu).

## Ryzyka i ostrzeżenia
Nie dotyczy. Pozycja odrzucona w partii C1. Powrót możliwy, gdy pojawi się źródło MS albo pomiar skutku na VM oficjalnym narzędziem (np. przełącznik w Ustawieniach -> odczyt `GameDVR_Enabled`).

## Test VM
Nie dotyczy (nie portowane): pozycji nie ma w katalogu, więc nie było przypadku VM.
