# privacy.inking-typing-improve – Wyłącz słownik niestandardowy pisania i pisma odręcznego

## Werdykt 26H2
Portować: przełącznik „Niestandardowe pisanie odręczne i słownik pisania” (strona `ms-settings:privacy-speechtyping`) przechodzi na wyłączony po zapisie czterech wartości, tych samych, które zapisuje sam przełącznik; test na VM 26H2 (Home i Pro) potwierdził stan przełącznika. dowód: stan przełącznika w Ustawieniach, treści nie obserwowano.

## Co zmienia
- Cztery wartości **DWord** dla zalogowanego użytkownika (w silniku `HKU\<SID zalogowanego>`), `scope=user`:
  - `HKCU\Software\Microsoft\InputPersonalization`, `RestrictImplicitInkCollection` ustawiana na `1`;
  - `HKCU\Software\Microsoft\InputPersonalization`, `RestrictImplicitTextCollection` ustawiana na `1`;
  - `HKCU\Software\Microsoft\InputPersonalization\TrainedDataStore`, `HarvestContacts` ustawiana na `0`;
  - `HKCU\Software\Microsoft\Personalization\Settings`, `AcceptedPrivacyPolicy` ustawiana na `0`.
- Mechanizm przełącznika `SystemSettings_Privacy_PersonalizeInkType_Toggle_ToggleSwitch` z różnicy rejestru na VM: wyłączenie zapisuje te cztery wartości z wartościami jak wyżej, włączenie zapisuje wartości z czystej VM (`0`, `0`, `1`, `1`).
- Przy wyłączaniu przełącznik zapisuje też inne wartości, których pozycja nie zmienia: `HKCU\Software\Microsoft\Input\Personalization` `UserDictKey` (binarna) i `UserDictName` (tekst, inny na każdej VM), `HKCU\Software\Microsoft\Windows\CurrentVersion\CPSS\Store\InkingAndTypingPersonalization` `Value` (`0`, przy włączonym `1`) oraz wartości `Value` w `…\CPSS\Store\IME\Bopomofo\…` i `…\CPSS\Store\IME\Japanese\LearningLevel` (ustawienia IME).
- Wpis ze źródeł `HKCU\Software\Microsoft\Input\TIPC` `Enabled` = 0 **nie wchodzi**. Faza „po zapisie” w teście obejmowała pięć wartości (te cztery i `TIPC\Enabled` = 0). To, że wystarczą cztery, wynika z mechanizmu przełącznika: różnica rejestru przy przełączeniu na wyłączony nie dotyka klucza `TIPC`. Pole, któremu `TIPC` może odpowiadać, na stronie Diagnostyka i opinie („Improve the language recognition and suggestion capabilities…”) było nieaktywne i wyłączone we wszystkich fazach, bo opcjonalne dane diagnostyczne są wyłączone, więc skutku `TIPC\Enabled` nie widać. Na czystej VM klucz `TIPC` istnieje, wartości `Enabled` nie ma.
- Bez restartu: stan przełącznika widać w świeżym oknie Ustawień.

## Źródła
- Test na VM 26H2 Home i Pro, 2026-11-07 (build 26300.9457, nieaktywowany Windows, konto lokalne, bez sieci): zapis przez API rejestru (.NET), różnica rejestru po przełączeniu przełącznika w obie strony i obserwacja w Ustawieniach (UI Automation, zrzuty).
- Win11Debloat: https://github.com/Raphire/Win11Debloat (gałąź master), plik `Regfiles/Disable_Telemetry.reg`, linie 11–21; dostęp 2026-11-06.
- RemoveWindowsAI: https://github.com/zoicware/RemoveWindowsAI (gałąź main), plik `RemoveWindowsAi.ps1`, linie 1120–1122; dostęp 2026-11-06.
- WinUtil: https://github.com/ChrisTitusTech/winutil (gałąź main), plik `config/tweaks.json`, sekcja telemetrii (bez numeru linii); dostęp 2026-11-06.
- Brak dokumentacji MS dla tych wartości, dowód: test na VM (obserwacja niżej).

## Wartości domyślne na świeżym 26H2
Odczyt z VM `DSO-EN-Home` i `DSO-EN-Pro`, 2026-11-07 (przed jakimkolwiek kliknięciem), Home i Pro tak samo: klucze `InputPersonalization`, `InputPersonalization\TrainedDataStore` i `Personalization\Settings` istnieją; `RestrictImplicitInkCollection` = 0, `RestrictImplicitTextCollection` = 0, `HarvestContacts` = 1, `AcceptedPrivacyPolicy` = 1 (przełącznik włączony).

## Detekcja i undo
- Detekcja: wszystkie cztery wartości równe zapisanym -> `Applied`; żadna -> `NotApplied`; część -> `Partial`.
- Undo przywraca snapshot sprzed zmiany z journala (na czystym Windows `0`, `0`, `1`, `1`, czyli przełącznik włączony); `original` z katalogu (te same wartości z czystej VM) to tylko fallback.
- Wszystkie trzy klucze istniały przed apply i zostają (`KeyPresentAfterUndo`).

## Ryzyka i ostrzeżenia
- `risk=caution`, `suggested=false`: pozycja nie jest sugerowana, bo wyłączenie może wyczyścić słownik niestandardowy (a jego powrotu po cofnięciu nie sprawdzono). Według opisu Windows pod przełącznikiem wyłączenie czyści słownik niestandardowy, a sugestie przestają być spersonalizowane. Opis po angielsku: „Get better suggestions by creating a custom dictionary using your typing and handwriting info. If you turn off this setting, your dictionary will be cleared and your suggestions won’t be personalized.”; po polsku: „Uzyskaj lepsze sugestie, tworząc słownik niestandardowy przy użyciu informacji o pisaniu i piśmie ręcznym. Jeśli wyłączysz to ustawienie, słownik zostanie wyczyszczony, a Twoje sugestie nie będą spersonalizowane.”.
- Ostrzeżenie w katalogu: „Według opisu Windows wyłączenie czyści słownik niestandardowy pisania i pisma odręcznego; cofnięcie może go nie przywrócić.”
- Czy słownik faktycznie znika i czy cofnięcie go przywraca: nie sprawdzono (na VM słownik był pusty).
- Pozycja nie zapisuje `UserDictKey` ani `UserDictName` (`HKCU\Software\Microsoft\Input\Personalization`), które przełącznik Windows zmienia przy wyłączaniu, więc czy sam zapis czterech wartości pozycji czyści słownik: nie sprawdzono.

## Test VM
Przypadki `privacy-inking-typing-improve` i `privacy-inking-typing-improve-prev-value` (`Flow=Full`) w `tests/vm/cases.psd1`; przebieg VM z 2026-11-08 (build 26300.9457, `Invoke-VmTests.ps1`): oba przypadki **Passed** na `DSO-EN-Home` (Core), `DSO-EN-Pro` i `DSO-PL-Pro` (Professional). Pełne przebiegi: 915 zaliczonych, 2 błędy, 25 pominiętych na obu VM angielskich oraz 917 zaliczonych, 0 błędów, 25 pominiętych na polskiej. Oba błędy dotyczyły przypadków dwóch pozycji powiadomień (`privacy.toast-suggested`, `privacy.toast-backup-reminder`; opis w ich dokumentacji), nie tej pozycji.

## Obserwacja skutku
- **Home** (`DSO-EN-Home`), 2026-11-07, 20:29–20:30. Metoda: strony `ms-settings:privacy-speechtyping` i `ms-settings:privacy-feedback` w świeżym oknie Ustawień (UI Automation, zrzuty), przełącznik „Custom inking and typing dictionary” („Niestandardowe pisanie odręczne i słownik pisania” w polskim Windows) przed zapisem, po zapisie i po cofnięciu. Wynik: przed zapisem włączony; po zapisie wyłączony; po cofnięciu włączony. dowód: stan przełącznika w Ustawieniach, treści nie obserwowano.
- **Pro** (`DSO-EN-Pro`), 2026-11-07, 20:29–20:31. Ta sama metoda i ten sam wynik.
- Nie sprawdzono: czy słownik użytkownika faktycznie znika i czy cofnięcie go przywraca (na VM słownik był pusty), ani samych sugestii przy pisaniu.
