# DSOptimizer

[English](README.md) | **Polski**

DSOptimizer to darmowy program do porządkowania i ustawiania Windows 11, najlepiej zaraz po instalacji systemu.
Zbiera w jednym oknie rzeczy, które zwykle robi się ręcznie w wielu miejscach: wyłączanie zbędnych funkcji
i reklam, usuwanie preinstalowanych aplikacji, instalację popularnych programów, proste naprawy systemu
i sprawdzenie sterowników. Każda zmiana ustawień trafia do historii i można ją cofnąć.

## Co jest w programie

- **Ustawienia systemu** – strony „Prywatność i AI”, „Wydajność”, „Dysk i system plików”, „Sieć i LAN”,
  „Bezpieczeństwo i szyfrowanie” oraz „Explorer i wygoda”. Przy każdej pozycji program pokazuje, czy jest już
  ustawiona na tym komputerze, co zmienia i co na tym zyskasz. Zaznaczasz, co chcesz, i klikasz „Zastosuj”.
- **Panele systemowe** – szybkie otwieranie paneli Windows, których szuka się zwykle najdłużej.
- **Naprawy** – m.in. reset Windows Update, sprawdzenie i naprawa plików systemowych, czyszczenie dysku,
  usunięcie poprzedniej instalacji Windows, reset ustawień sieci, naprawa winget i Microsoft Store.
- **Narzędzia** – np. ponowne uruchomienie paska zadań i Eksploratora oraz uruchomienie WinUtil (Chris Titus).
- **Specyfikacja** – podsumowanie sprzętu komputera z możliwością zapisania raportu.
- **Przygotowanie** – kroki po instalacji Windows: aktualizacje, sterowniki chipsetu i grafiki, strona wsparcia
  producenta laptopa, urządzenia bez sterownika. Program tylko sprawdza stan i podaje oficjalne linki.
- **Aplikacje** – lista zainstalowanych aplikacji z sugestiami, co można usunąć. Aplikacje ważne dla systemu
  są chronione i nie da się ich zaznaczyć.
- **Instalacja** – instalacja popularnych programów przez winget (Microsoft) oraz RustDesk z GitHuba jego autorów.
- **Historia zmian** – wszystko, co program zmienił, z przyciskiem „Cofnij” tam, gdzie da się cofnąć.

## Wymagania

- Windows 11, wersja 64-bitowa. Program sprawdzano na Windows 11 26H2 (Home i Pro); na innych wersjach może działać,
  ale nie był na nich sprawdzany.
- Konto z uprawnieniami administratora – przy starcie Windows zapyta o zgodę (okno UAC).
- Nic nie trzeba instalować. .NET Framework 4.8.1, którego program potrzebuje, jest już w Windows 11.
  Program działa także prosto z pendrive'a.

## Język

Program jest dostępny po polsku i po angielsku. Przy pierwszym uruchomieniu okno prosi o wybór języka;
wstępnie zaznaczony jest polski, gdy Windows jest po polsku, a w przeciwnym razie angielski. Język zmienisz
później w „Ustawieniach” → „Język”; zmiana zadziała po „Uruchom ponownie teraz” (restart dotyczy samego
programu, nie komputera). W trybie wiersza poleceń służy do tego `--lang pl` albo `--lang en`.
Nazwy stron i przycisków w tym dokumencie są takie jak w polskim interfejsie.

Program sprawdzano na polskim Windows 11 26H2. Na Windows w innych językach część pozycji może nie wykryć stanu
poprawnie. Teksty pochodzące z samego Windowsa (nazwy aplikacji i urządzeń, wyjście winget, DISM i SFC)
są w języku Windowsa.

## Pobieranie

1. Wejdź na stronę [wydań](https://github.com/ds99-01/DSOptimizer/releases) i pobierz plik `DSOptimizer-<wersja>.zip`
   oraz `SHA256SUMS.txt`.
2. Sprawdź, czy plik nie został uszkodzony ani podmieniony. W PowerShellu, w folderze z pobranym plikiem:

   ```powershell
   Get-FileHash .\DSOptimizer-<wersja>.zip -Algorithm SHA256
   ```

   Wynik musi być taki sam jak suma przy nazwie pliku w `SHA256SUMS.txt` (wielkość liter nie ma znaczenia).
   Jeśli jest inny, nie rozpakowuj pliku – pobierz go jeszcze raz.
3. Rozpakuj zip. Powstanie folder `DSOptimizer` – trzymaj go w całości, bo `DSOptimizer.exe` potrzebuje plików,
   które leżą obok niego.
4. Uruchom `DSOptimizer.exe` i potwierdź okno UAC.

### Ostrzeżenie SmartScreen

Program nie ma jeszcze podpisu cyfrowego, więc przy pierwszym uruchomieniu pliku pobranego z internetu Windows
pokaże niebieskie okno „System Windows ochronił ten komputer”. Kliknij **„Więcej informacji”**, a potem
**„Uruchom mimo to”**. Okno pojawia się także po rozpakowaniu zipu. Jeśli pliki trafiły na pendrive inną drogą
niż pobranie z internetu, okna zwykle nie ma.

Microsoft Defender z aktualnymi sygnaturami nie blokuje programu ani żadnej jego funkcji – szczegóły sprawdzenia
są w raportach antywirusowych dla [wersji 4.0](docs/zrodla/2026-10-26-etap5-raport-antywirus.md)
i [wersji 4.0.1](docs/zrodla/2026-10-30-v4-0-1-raport-antywirus.md).

## Prywatność

Program nie ma telemetrii i nie aktualizuje się sam. Sam łączy się z siecią tylko na Twoje żądanie: WinUtil
i RustDesk pobiera z GitHuba ich autorów, a instalacja i wyszukiwanie aplikacji idą przez winget ze źródła
Microsoftu. Strona „Przygotowanie” otwiera w przeglądarce strony producentów i Google, ale sama niczego nie wysyła.

Historia zmian, logi i raporty zostają na komputerze, w folderze `%ProgramData%\DSOptimizer`. Gdy program
uruchomisz z pendrive'a, kopia logu i raportów trafia też obok `DSOptimizer.exe`.

## Co da się cofnąć, a co nie

- **Ustawienia systemu** – każdą zmianę cofniesz przyciskiem „Cofnij” przy pozycji albo na stronie
  „Historia zmian”. Program zapamiętuje stan sprzed zmiany i do niego wraca. Nieliczne pozycje, których
  nie da się cofnąć, mają o tym wyraźne ostrzeżenie. W Ustawieniach programu jest też opcja tworzenia punktu
  przywracania Windows przed zmianami.
- **Usuwanie aplikacji** – usuniętej aplikacji program nie przywraca. Przed usunięciem programu (nie aplikacji
  ze Sklepu) tworzy punkt przywracania Windows, a gdy to się nie uda, pyta, czy kontynuować bez niego.
- **Naprawy i akcje** – działają jednorazowo i nie mają „Cofnij” (np. wyczyszczonych plików nie da się
  odzyskać). Przed uruchomieniem przeczytaj opis pozycji.
- **Instalacja** – zainstalowany program odinstalujesz na stronie „Aplikacje”.

## Budowanie ze źródeł

Potrzebny jest .NET SDK w wersji podanej w `global.json` (np. `winget install Microsoft.DotNet.SDK.10`).

```powershell
dotnet build DSOptimizer.sln -c Release
```

Gotowy program: `src\DSOptimizer.App\bin\Release\net481\DSOptimizer.exe`. Testy (nieobowiązkowo; część z nich
wymaga PowerShella uruchomionego jako administrator):

```powershell
dotnet test DSOptimizer.sln -c Release
```

## Tryb wiersza poleceń

`DSOptimizer.exe` uruchomiony z argumentami nie otwiera okna: wykonuje jedno polecenie, zapisuje raport JSON do pliku `--out` i kończy proces z kodem wyjścia. Bez argumentów otwiera okno. `--out` jest wymagany (wynik trafia do pliku, nie na stdout), a polecenie ma być dokładnie jedno.

```text
DSOptimizer.exe --detect <id> --out <plik.json>
DSOptimizer.exe --apply <id> [--restore-point required|ifavailable|skip] --out <plik.json>
DSOptimizer.exe --undo <id> --out <plik.json>
DSOptimizer.exe --execute <id> [--catalog <plik.json>] --out <plik.json>
DSOptimizer.exe --list-apps --out <plik.json>
DSOptimizer.exe --remove-app <id> [--restore-point required|ifavailable|skip] --out <plik.json>
DSOptimizer.exe --list-install [--apps-catalog <plik.json>] --out <plik.json>
DSOptimizer.exe --install-apps <id>[,<id>…] [--apps-catalog <plik.json>] --out <plik.json>
DSOptimizer.exe --search-apps <tekst> --out <plik.json>
```

- `--detect`, `--apply` i `--undo` dotyczą pozycji `kind=tweak`; `--execute` dotyczy `kind=action` i `kind=panel` (`--apply` na akcji lub panelu daje kod 2).
- `--list-apps` zwraca widoczne wiersze strony „Aplikacje”, a `--remove-app` usuwa jedną aplikację po jej id (`appx:<PackageFamilyName>` albo `win32:<klucz>`); wiersz chroniony lub zablokowany jest odrzucany. `--restore-point` domyślnie ma wartość `required`: bez punktu przywracania nic nie jest usuwane.
- `--list-install` zwraca listę instalacji ze stanem winget, `--install-apps` instaluje podane pozycje (`install.<nazwa>` z listy albo `winget:<id>`) bez pytania, a `--search-apps` zwraca wyniki `winget search`. `--apps-catalog` podmienia wbudowaną listę instalacji.
- Instalacja i wyszukiwanie przez winget działają tylko na koncie zalogowanego użytkownika; na innym koncie kończą się odmową (kod 1), a winget nie jest uruchamiany.

### Kody wyjścia

| Kod | Znaczenie |
|---|---|
| 0 | Sukces (`--detect` zawsze zwraca 0, niezależnie od stanu) |
| 1 | Operacja niewykonana, stan bez zmian (`Failed`, `FailedRolledBack`, `Refused`, `NotSupported`, `Skipped`; seria przerwana, np. brak punktu przywracania przy `--restore-point required`, pole `aborted`) |
| 2 | Błędne argumenty albo polecenie niepasujące do rodzaju (`kind`) pozycji |
| 3 | Błąd katalogu (plik, JSON, walidacja, nieznane id) |
| 4 | Stan do ręcznej kontroli (`FailedRollbackIncomplete`, `UndoIncomplete`, `NotConfirmed`, `InstalledUnverified`) |
| 5 | Błąd wewnętrzny (wyjątek, nie da się zabezpieczyć korzenia `%ProgramData%\DSOptimizer`) |
| 6 | Nie da się zapisać pliku `--out` |

### Raport JSON

Najważniejsze pola: `exitCode` (to samo co kod procesu), `result` (nazwa kodu, np. `UsageError`), `state` (wykryty stan przed operacją), `outcome` (wynik operacji), `finalState` (stan po operacji), `warnings`, `restorePoint` (status punktu przywracania), `user` (SID użytkownika docelowego), `machine` (build, edycja, nazwa maszyny).

Plik `--out` to UTF-8 bez BOM. Windows PowerShell 5.1 czyta taki plik poprawnie (polskie znaki) tylko z jawnym kodowaniem: `Get-Content <plik> -Raw -Encoding UTF8`.

### Katalog zewnętrzny (`--catalog`)

`--catalog <plik.json>` **zastępuje** katalog wbudowany i wykonuje skrypty z tego pliku jako administrator. Używaj wyłącznie zaufanych plików. Użycie katalogu zewnętrznego jest odnotowane w `warnings` raportu, razem z hashem katalogu (z prefiksem `sha256:`). Hash to SHA-256 z nazwy pliku i zdekodowanego tekstu JSON, a nie z bajtów pliku, więc nie zgadza się z wynikiem `Get-FileHash`.

## Dokumentacja

Dokumentacja pozycji jest po polsku w `docs/tweaks`, `docs/apps` i `docs/prep` (oryginał) oraz po angielsku w `docs/en`. Zapisy źródeł (`docs/zrodla`) są tylko po polsku.

## Licencja

DSOptimizer jest udostępniony na licencji Mozilla Public License 2.0 – pełny tekst w pliku [LICENSE](LICENSE).
W skrócie: program możesz używać, zmieniać i rozpowszechniać, ale jeśli rozpowszechniasz zmienioną wersję,
kod zmienionych plików programu musi być dostępny na tej samej licencji.

Nazwa DSOptimizer i znak DŚO nie są objęte licencją MPL 2.0; przerobione wersje muszą nosić inną nazwę.

Program korzysta z bibliotek i czcionek innych autorów, na ich własnych licencjach – lista w
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## Autor

Dawid Świeboda – [github.com/ds99-01](https://github.com/ds99-01)
