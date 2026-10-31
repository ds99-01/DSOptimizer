# Etap 5 – pomiar WinUtil z siecią i aktualnymi sygnaturami Defendera (Task 1, bramka)

> Przebieg 2026-10-26 01:01–01:07 (Pro i Home równolegle, HEAD `8c32abc`), oba kod 0, stan końcowy VM `Off`, karta bez
> przełącznika. Bez danych prywatnych; surowe wyniki
> (`e5.json`, zrzuty `winutil-<wariant>.png`) w zapisie pomiaru w archiwum autora (poza gitem).

Skrypt: `tests/vm/explore/Invoke-E5Exploration.ps1 -Phase defender,winutil` (jedna VM na proces), od `czysta-26H2`,
`Default Switch` tylko na czas faz `defender` i `winutil`, na końcu revert, karta odłączona, VM `Off`.
Ustawienia Defendera wyłącznie czytane; jedyna akcja to `Update-MpSignature` (K1).

Warianty (każdy w osobnym widocznym oknie PowerShell z zadania Harmonogramu jako `dso`, Interactive + Highest):
- **A** – dzisiejsze polecenie katalogu: `powershell.exe -NoExit -Command "irm https://christitus.com/win | iex"`;
- **B** – `winutil.ps1` z `releases/latest` pobrany przez `HttpWebRequest` (bez MotW) do
  `C:\ProgramData\DSOptimizer\downloads\<guid>.ps1`, SHA256 = `digest` z API, `-NoExit -NoProfile -ExecutionPolicy RemoteSigned -File "<plik>"`;
- **C** – jak B, ale `-ExecutionPolicy Bypass`.

Uwaga do przebiegu: na czystej VM folderu `downloads` nie ma – skrypt go tworzy (`CreateDirectory`, ACL dziedziczony
z `C:\ProgramData`, nie zaufany ACL programu). Wpływu na Defendera nie zaobserwowano (zero detekcji).

## Wersje sygnatur

| VM | Edycja / kompilacja | Sygnatury przed | Sygnatury po (`Update-MpSignature`) | Data sygnatur po | `MAPSReporting` | `SubmitSamplesConsent` | `IsTamperProtected` | `signaturesUpdated` |
|---|---|---|---|---|---|---|---|---|
| DSO-26H2-Pro | Professional 26300.9457 | 1.437.1.0 (2025-09-17) | 1.461.204.0 (silnik 1.1.26100.4) | 2026-10-25 17:55 | 2 | 1 | true | true (50,4 s) |
| DSO-26H2-Home | Core 26300.9457 | 1.437.1.0 (2025-09-17) | 1.461.204.0 (silnik 1.1.26090.9) | 2026-10-25 17:55 | 2 | 1 | true | true (41,7 s) |

## Wydanie WinUtil w dniu pomiaru

Tag `26.10.07`, `winutil.ps1` 927 798 B, SHA256 pliku = `digest` z API (`HashMatches = true` w B i C na obu VM),
`Zone.Identifier` brak (`false`), `Get-AuthenticodeSignature` = `NotSigned`. Platforma Defendera bez zmian
(`AMProductVersion` 4.18.25080.5 – `Update-MpSignature` aktualizuje sygnatury i silnik, nie platformę).

## Wyniki A/B/C – Pro

| Wariant | Okno żyje po 60 s | Okno WinUtil (dokładny tytuł) | Detekcje od startu wariantu | Zrzut | Uwagi (etap blokady: pobranie / AMSI / skrypt) |
|---|---|---|---|---|---|
| A | tak | `WinUtil 26.10.07` | 0 (`Threats` i zdarzenia puste, bez błędów odczytu) | `winutil-A.png` | brak blokady na żadnym etapie |
| B | tak | `WinUtil 26.10.07` | 0 (`Threats` i zdarzenia puste, bez błędów odczytu) | `winutil-B.png` | plik pobrany, SHA256 zgodny, GUI WinUtil na zrzucie (Pro sprawdzony wzrokowo) |
| C | tak | `WinUtil 26.10.07` | 0 (`Threats` i zdarzenia puste, bez błędów odczytu) | `winutil-C.png` | jak B |

## Wyniki A/B/C – Home

| Wariant | Okno żyje po 60 s | Okno WinUtil (dokładny tytuł) | Detekcje od startu wariantu | Zrzut | Uwagi (etap blokady: pobranie / AMSI / skrypt) |
|---|---|---|---|---|---|
| A | tak | `WinUtil 26.10.07` | 0 (`Threats` i zdarzenia puste, bez błędów odczytu) | `winutil-A.png` | brak blokady na żadnym etapie |
| B | tak | `WinUtil 26.10.07` | 0 (`Threats` i zdarzenia puste, bez błędów odczytu) | `winutil-B.png` | plik pobrany, SHA256 zgodny, GUI WinUtil na zrzucie (Pro sprawdzony wzrokowo) |
| C | tak | `WinUtil 26.10.07` | 0 (`Threats` i zdarzenia puste, bez błędów odczytu) | `winutil-C.png` | jak B |

## Decyzja `ExecutionPolicy` dla Task 4

Reguła (plan, Task 1 Step 6): **B (`RemoteSigned`)**, jeśli B otwiera GUI WinUtil bez detekcji na obu edycjach; inaczej
**C (`Bypass`)**, jeśli C przechodzi; jeśli ani B, ani C – **STOP**, autor wraca do decyzji (spec 3.3 pkt 4), Tasków 2–4
nie zaczynamy.

Decyzja: **B – `RemoteSigned`** (B otwiera GUI WinUtil bez detekcji na Home i Pro). `WinUtilSource.ExecutionPolicy = "RemoteSigned"`.

## Wnioski

- Na czystej 26H2 z aktualnymi sygnaturami i ochroną w chmurze (`MAPSReporting` = 2, `SubmitSamplesConsent` = 1)
  **Defender nie blokuje żadnego wariantu, także dzisiejszego `irm … | iex`.** Blokada, którą autor widział przy WinUtil,
  nie odtwarza się w tych warunkach – możliwe przyczyny: inny antywirus u klienta, starsza wersja WinUtil albo
  sygnatur z tamtego dnia, reguły ASR/polityki firmowe. Nie da się tego rozstrzygnąć na VM.
- Mimo to zmiana zostaje (spec 3.3 pkt 4): nowa droga usuwa z exe i z linii poleceń znany wzorzec `irm | iex`
  (statyczne i behawioralne reguły innych silników), uruchamia dokładnie plik z wydania po sprawdzeniu SHA256 i daje
  uczciwy komunikat bez sieci zamiast okna z błędem.
- Ograniczenie pomiaru: warianty A → B → C szły po kolei w jednym stanie VM (bez detekcji, więc bez wpływu na wynik).
