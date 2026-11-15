# DSOptimizer: szczegóły

[Wróć do README](../../README.pl.md) · [English](details.md) | **Polski**

Język, co jest w programie, wymagania, pobieranie, prywatność i co da się cofnąć. Każda pozycja jest też opisana na [dsoptimizer.com/pl/dokumentacja](https://dsoptimizer.com/pl/dokumentacja/).

## Co jest w programie

- **Ustawienia systemu** – strony „Prywatność i AI”, „Wydajność”, „Dysk i system plików”, „Sieć i LAN”,
  „Bezpieczeństwo i szyfrowanie” oraz „Eksplorator i wygoda”. Przy każdej pozycji program pokazuje, czy jest już
  ustawiona na tym komputerze, co zmienia i co na tym zyskasz. Zaznaczasz, co chcesz, i klikasz „Zastosuj”.
  Lista „Pokaż” zawęża widok do pozycji niezastosowanych, zastosowanych albo zaznaczonych ze wszystkich stron.
- **Specyfikacja** – podsumowanie sprzętu komputera z możliwością zapisania raportu.
- **Przygotowanie** – kroki po instalacji Windows: aktualizacje, sterowniki chipsetu i grafiki, strona wsparcia
  producenta laptopa, urządzenia bez sterownika. Program tylko sprawdza stan i podaje oficjalne linki.
- **Aplikacje** – lista zainstalowanych aplikacji z sugestiami, co można usunąć. Aplikacje ważne dla systemu
  są chronione i nie da się ich zaznaczyć.
- **Autostart** – programy, które startują same po zalogowaniu użytkownika (z rejestru, folderów Autostart
  i aplikacji ze Sklepu), z czasem procesora zużytym przy starcie według pomiaru Windows. Można je wyłączać
  i włączać: program używa tego samego przełącznika co Menedżer zadań i Ustawienia i niczego nie usuwa. Pozycje
  ważne dla systemu są chronione; część znanych programów i pozycje, których plik już nie istnieje, są sugerowane
  do wyłączenia.
- **Instalacja** – instalacja popularnych programów przez winget (Microsoft) oraz RustDesk z GitHuba jego autorów.
- **Naprawy i narzędzia** – jedna strona z trzema częściami. Naprawy, m.in.: reset Windows Update, sprawdzenie
  i naprawa plików systemowych, czyszczenie dysku, usunięcie poprzedniej instalacji Windows, reset ustawień sieci,
  naprawa winget i Microsoft Store. Narzędzia, np. ponowne uruchomienie paska zadań i Eksploratora oraz uruchomienie
  WinUtil (Chris Titus). Panele systemowe: szybkie otwieranie paneli Windows, których szuka się zwykle najdłużej.
- **Historia zmian** – zmiany ustawień, zmiany w autostarcie, usunięcia i instalacje aplikacji w jednym miejscu. Zmiany ustawień i autostartu można tu cofnąć; usunięcia i instalacje aplikacji nie mają „Cofnij”.

## Wymagania

- Windows 11 x64 (na ARM64 nietestowane). Program sprawdzano na Windows 11 26H2 (Home i Pro); na innych wersjach może działać,
  ale nie był na nich sprawdzany.
- Konto z uprawnieniami administratora – przy starcie Windows zapyta o zgodę (okno UAC).
- Wersja przenośna: nic nie trzeba instalować. .NET Framework 4.8.1, którego program potrzebuje, jest już w Windows 11.
  Wersja przenośna działa także prosto z pendrive'a.

## Język

Program jest dostępny po polsku i po angielsku. Przy pierwszym uruchomieniu okno prosi o wybór języka;
wstępnie zaznaczony jest polski, gdy Windows jest po polsku, a w przeciwnym razie angielski. Język zmienisz
później w „Ustawieniach” → „Język”; zmiana zadziała po „Uruchom ponownie teraz” (restart dotyczy samego
programu, nie komputera). W trybie wiersza poleceń służy do tego `--lang pl` albo `--lang en`.
Nazwy stron i przycisków w tym dokumencie są takie jak w polskim interfejsie.

Program sprawdzano na Windows 11 26H2 po angielsku i po polsku. Na Windows w innych językach część pozycji może nie wykryć stanu
poprawnie. Teksty pochodzące z samego Windowsa (nazwy aplikacji i urządzeń, wyjście winget, DISM i SFC)
są w języku Windowsa.

## Pobieranie

Każde wydanie ma dwa pliki zbudowane z tych samych plików programu:

- `DSOptimizer-<wersja>.zip` – wersja przenośna: rozpakuj i uruchom, niczego nie instaluje, działa też z pendrive'a.
- `DSOptimizer-<wersja>-setup.exe` – instalator: instaluje w `C:\Program Files\DSOptimizer` dla wszystkich użytkowników,
  dodaje skróty w Menu Start i na pulpicie. Odinstalowanie w Ustawieniach → Aplikacje.

Odinstalowanie zostawia historię zmian w `%ProgramData%\DSOptimizer`, więc wcześniej zmienione ustawienia można
cofnąć także po ponownej instalacji programu albo w wersji przenośnej. Instalator odmawia instalacji, aktualizacji
i odinstalowania, gdy DSOptimizer działa, także gdy trwa polecenie w trybie wiersza poleceń (również tylko do odczytu);
najpierw zamknij program i poczekaj na koniec takich poleceń.

Instalator jest zbudowany w Inno Setup autorstwa Jordana Russella i Martijna Laana ([jrsoftware.org](https://jrsoftware.org/)).

1. Wejdź na stronę [wydań](https://github.com/ds99-01/DSOptimizer/releases) i pobierz wybrany plik
   (`DSOptimizer-<wersja>.zip` albo `DSOptimizer-<wersja>-setup.exe`) oraz `SHA256SUMS.txt`.
2. Sprawdź, czy plik nie został uszkodzony ani podmieniony. W PowerShellu, w folderze z pobranym plikiem
   (dla instalatora podaj nazwę pliku `-setup.exe`):

   ```powershell
   Get-FileHash .\DSOptimizer-<wersja>.zip -Algorithm SHA256
   ```

   Wynik musi być taki sam jak suma przy nazwie pliku w `SHA256SUMS.txt` (wielkość liter nie ma znaczenia).
   Jeśli jest inny, nie rozpakowuj ani nie uruchamiaj pliku – pobierz go jeszcze raz.
3. Wersja przenośna: rozpakuj zip. Powstanie folder `DSOptimizer` – trzymaj go w całości, bo `DSOptimizer.exe`
   potrzebuje plików, które leżą obok niego. Instalator: uruchom `DSOptimizer-<wersja>-setup.exe` i przejdź kreator.
4. Uruchom `DSOptimizer.exe` (albo skrót z Menu Start) i potwierdź okno UAC.

### Ostrzeżenie SmartScreen

Program nie ma jeszcze podpisu cyfrowego, więc przy pierwszym uruchomieniu pliku pobranego z internetu Windows
pokaże niebieskie okno „System Windows ochronił ten komputer”. Kliknij **„Więcej informacji”**, a potem
**„Uruchom mimo to”**. Okno pojawia się także po rozpakowaniu zipu i przy uruchomieniu instalatora. Jeśli pliki trafiły na pendrive inną drogą
niż pobranie z internetu, okna zwykle nie ma.

Microsoft Defender z aktualnymi sygnaturami nie blokuje programu ani żadnej jego funkcji – szczegóły sprawdzenia
są w raportach antywirusowych dla [wersji 4.0](../zrodla/2026-10-26-etap5-raport-antywirus.md)
i [wersji 4.0.1](../zrodla/2026-10-30-v4-0-1-raport-antywirus.md).

## Prywatność

Program nie ma telemetrii i nie aktualizuje się sam: niczego sam nie pobiera ani nie instaluje. Przy otwarciu okna
pyta GitHuba (api.github.com) o numer najnowszej wersji, a GitHub widzi przy tym Twój adres IP. Możesz to wyłączyć
w Ustawieniach; tryb wiersza poleceń nigdy tego nie sprawdza. Poza tym łączy się z siecią tylko na Twoje żądanie: WinUtil
i RustDesk pobiera z GitHuba ich autorów, a instalacja i wyszukiwanie aplikacji idą przez winget ze źródła
Microsoftu. Strona „Przygotowanie” otwiera w przeglądarce strony producentów i Google, ale sama niczego nie wysyła. Pomiar połączeń programu jest w [raporcie sieciowym wersji 4.0.6](../zrodla/2026-11-08-v4-0-6-pomiar-sieci.md).

Historia zmian i logi zostają na komputerze, w folderze `%ProgramData%\DSOptimizer`. Każde konto na tym komputerze może
go przeglądać i czytać logi, ale zapisywać w nim mogą tylko administratorzy i tylko oni otworzą pliki historii zmian i
ustawień. Gdy uprawnienia folderu zostaną mimo to zmienione, program przy następnym starcie odkłada go na bok i przenosi
historię zmian, jeśli nikt jej nie ruszał. Plik z tego folderu pokazany w dzienniku (np. log winget) zapiszesz w innym
miejscu przyciskiem „Zapisz kopię…”. Raport ze specyfikacją zapisuje się tam, gdzie wskażesz w oknie „Zapisz jako”. Gdy
program uruchomisz z pendrive'a, kopia logu i raportu trafia też obok `DSOptimizer.exe`.
Raport ze specyfikacją zawiera numery seryjne dysków – pamiętaj o tym, zanim udostępnisz pendrive'a albo raport.

## Co da się cofnąć, a co nie

- **Ustawienia systemu** – większość zmian cofniesz przyciskiem „Cofnij” przy pozycji albo na stronie
  „Historia zmian”. Program zapamiętuje stan sprzed zmiany i do niego wraca. Nieliczne pozycje, których
  nie da się cofnąć, mają o tym wyraźne ostrzeżenie (nie ma tam przycisku „Cofnij”). W Ustawieniach programu jest też opcja tworzenia punktu
  przywracania Windows przed zmianami. Przy otwarciu okna program sprawdza, czy zastosowane wcześniej ustawienia są nadal zastosowane; gdy któreś nie jest, baner pozwala je zaznaczyć i zastosować ponownie – nic nie zmienia się bez Twojego kliknięcia.
- **Usuwanie aplikacji** – usuniętej aplikacji program nie przywraca. Przed usunięciem programu (nie aplikacji
  ze Sklepu) tworzy punkt przywracania Windows, a gdy to się nie uda, pyta, czy kontynuować bez niego.
- **Naprawy i akcje** – działają jednorazowo i nie mają „Cofnij” (np. wyczyszczonych plików nie da się
  odzyskać). Przed uruchomieniem przeczytaj opis pozycji.
- **Instalacja** – zainstalowany program odinstalujesz na stronie „Aplikacje”.
- **Autostart** – wyłączenie i włączenie zmienia tylko przełącznik, którego używa sam Windows (ten sam co
  w Menedżerze zadań i Ustawieniach); program nigdy nie usuwa wpisu autostartu ani pliku. Zmianę cofniesz na stronie
  „Historia zmian” albo przełączając pozycję z powrotem. Zmiany działają od następnego logowania, a punkt
  przywracania nie jest tworzony.


## Dokumentacja

Dokumentacja pozycji jest po polsku w `docs/tweaks`, `docs/apps`, `docs/prep` i `docs/autostart` (oryginał) oraz po angielsku w `docs/en`. Baza znanych pozycji autostartu (chronione i sugerowane, ze źródłami) jest opisana w [docs/autostart/README.md](../autostart/README.md). Zapisy źródeł (`docs/zrodla`) są tylko po polsku.

