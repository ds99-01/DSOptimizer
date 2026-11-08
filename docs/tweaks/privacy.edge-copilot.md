# privacy.edge-copilot – Ukryj przycisk Copilota w Edge

## Werdykt 26H2
Portować jako pozycję **niesugerowaną**: sześć polityk Edge zapisywanych razem; test na VM 26H2 (Home i Pro) pokazał, że z paska narzędzi Edge znika przycisk „Chat” / „Czat” (Copilot). Której z sześciu polityk to zasługa, nie rozdzielano; pozostałe nie miały osobnej obserwacji, ich działanie opisują strony Microsoftu. Wariant bez `HubsSidebarEnabled`: test z tymi sześcioma politykami dał ten sam skutek co test z siedmioma, więc pozycja nie chowa paska bocznego Edge. Niesugerowana, bo Edge traktuje te ustawienia jako polityki zarządzane przez organizację.

## Co zmienia
- `HKLM\SOFTWARE\Policies\Microsoft\Edge`, sześć wartości **DWord** ustawianych na `0`: `CopilotPageContext`, `EdgeEntraCopilotPageContext`, `Microsoft365CopilotChatIconEnabled`, `NewTabPageBingChatEnabled`, `EdgeHistoryAISearchEnabled`, `ComposeInlineEnabled`; `scope=machine`.
- `HubsSidebarEnabled` (w źródłach razem z tymi politykami) nie wchodzi: po jej wyłączeniu Edge w ogóle nie pokazuje paska bocznego („If you disable this policy, the Sidebar is never shown.”), a bez niej skutek był ten sam (przycisk „Chat” znika). Ta sama strona mówi: „As of Microsoft Edge version 141, the Microsoft365CopilotChatIconEnabled policy is the only means of controlling the display of Copilot in the toolbar.”.
- Na świeżym 26H2 nie ma klucza `Edge` (klucz `HKLM\SOFTWARE\Policies\Microsoft` istnieje). Apply tworzy klucz, undo usuwa wartości. Klucz `Edge` znika przy cofnięciu tylko wtedy, gdy powstał przy zmianie i jest pusty; gdy są w nim wartości innej pozycji Edge, zostaje (program zgłasza to ostrzeżeniem).
- Bez restartu Windows. Edge w teście był uruchamiany od nowa w każdej fazie; czy otwarte już okno Edge pokazuje zmianę od razu, nie sprawdzono.

## Źródła
- Test na VM 26H2 Home i Pro, 2026-11-07 (build 26300.9457, nieaktywowany Windows, konto lokalne, z siecią): zapis przez API rejestru (.NET) i obserwacja paska narzędzi Edge oraz strony `edge://newtab` (UI Automation, zrzuty), a także odczyt tabeli `edge://policy`.
- Strony polityk Microsoft Edge (MS Learn, dostęp 2026-11-07; wszystkie: ścieżka `SOFTWARE\Policies\Microsoft\Edge`, `REG_DWORD`, bez oznaczenia jako przestarzałe, bez ograniczenia do komputerów w domenie albo w MDM):
  - indeks polityk: https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies (ms.date 2026-11-04);
  - `CopilotPageContext`: https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/copilotpagecontext (ms.date 2026-05-20): „This policy applies only to Microsoft Entra ID profiles in Microsoft Edge. It doesn't apply to Microsoft account (MSA) profiles.”;
  - `EdgeEntraCopilotPageContext`: https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/edgeentracopilotpagecontext (ms.date 2026-08-18): „applies only to users who are signed in to Microsoft Edge with a Microsoft Entra account”;
  - `Microsoft365CopilotChatIconEnabled`: https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/microsoft365copilotchaticonenabled (ms.date 2026-05-20): „For users in an Entra ID Microsoft Edge profile …”, „If the policy is disabled: Copilot doesn't appear in the toolbar.”;
  - `NewTabPageBingChatEnabled`: https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/newtabpagebingchatenabled (ms.date 2026-05-20): tytuł strony mówi o nowej karcie Edge Enterprise;
  - `EdgeHistoryAISearchEnabled`: https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/edgehistoryaisearchenabled (ms.date 2026-05-20): „When disabled, users can only perform exact match (verbatim) searches in their history.”;
  - `ComposeInlineEnabled`: https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/composeinlineenabled (ms.date 2026-05-20): „This policy applies only to Microsoft Entra accounts and doesn't apply to Microsoft accounts.”;
  - `HubsSidebarEnabled` (polityka poza pozycją): https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/hubssidebarenabled (ms.date 2026-05-20, dostęp 2026-11-08): „If you disable this policy, the Sidebar is never shown.”, „As of Microsoft Edge version 141, the Microsoft365CopilotChatIconEnabled policy is the only means of controlling the display of Copilot in the toolbar.”.
- Win11Debloat: https://github.com/Raphire/Win11Debloat (gałąź master), pliki `Config/Features.json` linie 770–774 i `Regfiles/Disable_Edge_AI_Features.reg` linie 2–11; dostęp 2026-11-06.
- RemoveWindowsAI: https://github.com/zoicware/RemoveWindowsAI (gałąź main), plik `RemoveWindowsAi.ps1`, linie 958–977; dostęp 2026-11-06.
- privacy.sexy: https://github.com/undergroundwires/privacy.sexy (gałąź master), plik `src/application/collections/windows.yaml`, linie 9945–10088; dostęp 2026-11-06.

## Wartości domyślne na świeżym 26H2
Odczyt z VM `DSO-EN-Home` i `DSO-EN-Pro`, 2026-11-07: klucz `HKLM\SOFTWARE\Policies\Microsoft` istnieje, klucza `HKLM\SOFTWARE\Policies\Microsoft\Edge` i wszystkich sześciu wartości **brak** (na pasku narzędzi Edge przycisk „Chat”).

## Detekcja i undo
- Detekcja: wszystkie wartości z sekcji „Co zmienia” równe zapisanym -> `Applied`; brak wartości lub inna wartość -> `NotApplied`.
- Undo przywraca snapshot sprzed zmiany z journala (brak wartości = jej usunięcie) i usuwa klucz `Edge`, jeśli utworzyło go apply i jest pusty (`KeyAbsentAfterUndo`); gdy są w nim wartości innej pozycji Edge, klucz zostaje, a program zgłasza to ostrzeżeniem. `original` z katalogu (`<RemoveEntry>`) to tylko fallback.
- Klucz `HKLM\SOFTWARE\Policies\Microsoft` istniał przed apply i zostaje (`KeyPresentAfterUndo`).
- Klucz `Edge` jest wspólny z innymi pozycjami Edge (np. `privacy.edge-shopping-recommendations`); gdy przed apply istniał z wartościami innej pozycji, undo zostawia klucz i te wartości. Ten sam mechanizm sprawdza przypadek VM `privacy-edge-shared-key` (dla `privacy.edge-newtab-content`).

## Ryzyka i ostrzeżenia
- `risk=caution`, `suggested=false`: z paska narzędzi Edge znika przycisk „Chat” (Copilot).
- Ostrzeżenia w katalogu: „Przycisku „Czat” (Copilot) nie będzie na pasku narzędzi Edge, aż cofniesz tę pozycję.” oraz „Edge będzie traktować te ustawienia Copilota jako narzucone przez organizację, aż cofniesz tę pozycję.”
- Podstawą drugiego ostrzeżenia jest tabela `edge://policy`: wszystkie sześć polityk jako Platform, Device, Mandatory, status OK. Informacji o zarządzaniu przez organizację przy ustawieniach Copilota nie odczytywano; przy innych politykach Edge taka informacja pojawia się obok zablokowanego ustawienia (`privacy.edge-shopping-recommendations`).
- Według stron Microsoftu `CopilotPageContext`, `EdgeEntraCopilotPageContext` i `ComposeInlineEnabled` dotyczą tylko profili Edge zalogowanych kontem Microsoft Entra, a `Microsoft365CopilotChatIconEnabled` opisuje użytkowników w profilu Entra ID. Profil Edge na VM był lokalny, bez logowania, a przycisk „Chat” mimo to zniknął.
- Brzmienia: przycisk „Chat” / „Czat”, przycisk „Copilot menu” / „Menu Copilot” (odczyt po angielsku i po polsku).

## Test VM
Przypadek `privacy-edge-copilot` (`Flow=Full`) w `tests/vm/cases.psd1`; przebieg VM z 2026-11-08 (build 26300.9457, `Invoke-VmTests.ps1`): **Passed** na `DSO-EN-Home` (Core), `DSO-EN-Pro` i `DSO-PL-Pro` (Professional). Pełne przebiegi: 915 zaliczonych, 2 błędy, 25 pominiętych na obu VM angielskich oraz 917 zaliczonych, 0 błędów, 25 pominiętych na polskiej. Oba błędy dotyczyły przypadków dwóch pozycji powiadomień (`privacy.toast-suggested`, `privacy.toast-backup-reminder`; opis w ich dokumentacji), nie tej pozycji.

## Obserwacja skutku
Edge uruchamiany od nowa w każdej fazie z flagą `--no-first-run` (przełącznik Chromium, nie polityka; bez niej ekran powitalny zasłaniał stronę), pasek narzędzi i strona `edge://newtab` (UI Automation, zrzuty) oraz tabela `edge://policy`, przed zapisem, po zapisie i po cofnięciu. Edge 157.0.4341.42 we wszystkich fazach.
- **Home** (`DSO-EN-Home`), 2026-11-07, 20:44–20:48.
  - Siedem polityk z `HubsSidebarEnabled`. Wynik: przed zapisem przyciski „Chat” i „Copilot menu”; po zapisie ani „Chat”, ani „Copilot menu”; po cofnięciu „Chat”.
  - Sześć polityk bez `HubsSidebarEnabled` (wariant pozycji). Wynik: przed zapisem „Chat”; po zapisie brak; po cofnięciu „Chat”.
  - W `edge://policy` każda z sześciu polityk: `false`, Platform, Device, Mandatory, status OK.
- **Pro** (`DSO-EN-Pro`), 2026-11-07, 20:44–20:48.
  - Siedem polityk z `HubsSidebarEnabled`. Wynik: przed zapisem „Chat”; po zapisie ani „Chat”, ani „Copilot menu”; po cofnięciu „Chat”.
  - Sześć polityk bez `HubsSidebarEnabled`. Wynik: przed zapisem „Chat”; po zapisie brak; po cofnięciu „Chat” i „Copilot menu”. `edge://policy` jak na Home.
- Przycisk „Copilot menu” pojawiał się w Edge 157 także w fazach bez polityk, więc o skutku świadczy „Chat”. Polityki zapisano razem; której z nich to zasługa, nie rozdzielano. Według strony `HubsSidebarEnabled` wyświetlaniem Copilota na pasku steruje `Microsoft365CopilotChatIconEnabled`; pozostałe pięć polityk nie miało osobnej obserwacji; elementów Bing Chat na nowej karcie nie rozpoznano ani przed, ani po zapisie.
- Nie sprawdzono: profilu Edge zalogowanego kontem Microsoft albo Microsoft Entra, informacji o zarządzaniu przez organizację przy ustawieniach Copilota, innych wersji Edge niż 157.0.4341.42.
