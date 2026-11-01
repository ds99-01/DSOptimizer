# Aplikacje – plik klasyfikacji `apps-bloat.json`

Plik `src/DSOptimizer.Catalog/catalog/apps-bloat.json` mówi stronie „Aplikacje”, co jest śmieciem, a czego nie wolno usuwać.
Ładuje go `AppsBloatCatalog.Load` (Core), osadzony jest jako `BuiltInCatalog.AppsBloatSource`. Nie należy do `BuiltInCatalog.Sources`
(to tylko tablice tweaków); test `Sources_MatchCatalogFilesOnDisk` pomija go z nazwy, a `AppsBloatSource_IsEmbeddedAndMatchesFileOnDisk`
pilnuje, że plik na dysku i zasób są tym samym.

## Format

```json
{
  "entries":   [ { "id", "match", "group", "suggested", "summary", "warning?", "source", "cleanupUrl?" } ],
  "protected": [ { "id", "match", "reason", "source?" } ]
}
```

- `id` – unikalny w całym pliku (wpisy i chronione razem), bez rozróżniania wielkości liter.
- `match` – reguła dopasowania; wzorce z `*`, bez regex, bez rozróżniania wielkości liter:
  - `packageName` – tylko pakiety AppX (Sklep), z `Get-AppxPackage`, pole `Name`; nie łączy się z pozostałymi;
  - `displayName` i/lub `publisher` – tylko programy Win32 (wpis Uninstall); gdy podane oba, muszą pasować oba.
- `group` – `microsoft`, `producent` albo `trial`.
- `suggested` – `true`: wiersz trafia do grupy „Sugerowane do usunięcia”, a zaznacza ją dopiero przycisk „Zaznacz sugerowane”; `false`: grupa „Pozostałe”
  (można zaznaczyć ręcznie), z `warning`, jeśli jest.
- `summary` – ≤ 160 znaków, po polsku, bez nazw kluczy rejestru i cmdletów.
- `warning` – opcjonalne ostrzeżenie pokazywane przy wierszu.
- `source` – skąd wiemy, że to śmieć (dokumentacja MS, odczyt VM, zapis decyzji projektowej, producent). Wpis sugerowany musi mieć źródło
  z adresem `https://`, odczytem VM albo zapisem decyzji projektowej w polu (tak mają WildTangent i Booking.com); pilnuje tego test `SuggestedEntries_HaveVerifiableSource`.
- `cleanupUrl` – opcjonalny adres `https` oficjalnego narzędzia producenta do czyszczenia resztek. Program niczego z niego nie pobiera,
  pokazuje go tylko jako tekst. Wolno wpisać wyłącznie adres sprawdzony pobraniem strony (tabela niżej); test `VerifiedCleanupUrls` pilnuje,
  że w pliku nie ma żadnego innego.
- `reason` (tylko `protected`) – dlaczego aplikacja jest chroniona, pokazywany przy kłódce.
- `source` w `protected` – opcjonalne, tylko dla czytającego plik (loader go nie czyta); mają je wpisy zależności uruchomieniowych.

Kolejność wpisów w `entries` ma znaczenie: **pierwszy pasujący** wyznacza grupę, więc wąskie reguły idą przed szerokimi.
Błąd w pliku daje `AppsCatalogException` z id wpisu i nazwą pola.

## Zasady klasyfikacji (`AppClassifier`)

1. Ukryte całkiem: AppX z `IsFramework`, pakiety zasobów, `NonRemovable`, `SignatureKind=System`; Win32 z `SystemComponent=1`,
   z `ParentKeyName`, z `ReleaseType` ∈ {`Update`, `Hotfix`, `Security Update`} i bez `UninstallString` oraz `QuietUninstallString`.
2. **Chroniona wygrywa z każdą regułą śmieci.** Wiersz ma grupę `Protected`, kłódkę i nie trafia do serii.
3. Pierwszy pasujący wpis z `entries` ustala grupę; brak dopasowania = „Pozostałe”.
4. Powód blokady (gdy pasuje kilka): chroniona, inne konto, niepewny użytkownik, brak bezpiecznego polecenia.

## Źródła rozpoznania

- Lista chroniona i punkt wyjścia sugestii: przyjęta w programie zasada czyszczenia aplikacji (debloat).
- **Pakiety Microsoftu:** odczyt świeżej VM 26H2 (Home i Pro, build 26300.9457, 2026-10-21). Wszystkie nazwy z VM są potwierdzone odczytem `Get-AppxPackage`.
  Poza VM zostały tylko `Microsoft.MicrosoftOfficeHub` i `Microsoft.Getstarted` (nazwy z MS Learn, archiwum forum
  „Which AppXPackage can be removed without causing system problems?”; brak na świeżym 26H2).
- **Producenci (Lenovo, ASUS, Acer, HP, Dell):** nie było sprzętu OEM, więc nazwy pochodzą ze stron producentów i forów (adresy w polu
  `source` każdego wpisu). **Żaden wpis producenta nie jest sugerowany**, więc pomyłka w nazwie nie usunie niczego; w najgorszym razie
  aplikacja nie dostanie ostrzeżenia. Nazwa pakietu Lenovo Vantage (`E046963F.LenovoCompanion`) nie została potwierdzona w sieci
  (zostaje jako wpis niesugerowany z ostrzeżeniem, bo brak wpisu byłby gorszy niż jego pomyłka).
- **Trial:** wzorce McAfee i Norton zawężone do produktów konsumenckich. McAfee Agent / Endpoint / Drive Encryption / Data Loss Prevention
  i Trellix są na liście **chronionych** (mogą być oprogramowaniem firmowym). Norton Family i Utilities nie pasują do żadnego wzorca.
- Wzorce chronionych sterowników po wydawcy (`Intel*`, `NVIDIA*` itd.) są szerokie celowo: lepiej nie pozwolić usunąć niż pozwolić.
- **Zależności uruchomieniowe Win32:** Visual C++ Redistributable, .NET Runtime / Windows Desktop Runtime, ASP.NET Core Runtime
  i Shared Framework, Microsoft Update Health Tools, Microsoft GameInput. Na świeżej VM 26H2 ich nie ma (są tylko ukryte pakiety AppX
  VCLibs/.NET Native), więc nazwy wpisów Uninstall pochodzą z odczytu hosta Windows 11 (2026-10-21, tylko odczyt), a uzasadnienie
  z Microsoft Learn / Microsoft Support (adresy w polu `source`, sprawdzone pobraniem strony 2026-10-21). GameInput to program MSI
  („Microsoft GameInput”), nie pakiet AppX – dlatego reguła `displayName`. Wpisy z `SystemComponent=1` (np. „Microsoft .NET Host”,
  „Visual C++ … Minimum Runtime”) są i tak ukryte; reguły łapią widoczne wpisy pakietów zbiorczych. Narzędzia programisty (.NET SDK,
  Targeting Pack) nie pasują do reguł.

### Adresy `cleanupUrl` (sprawdzone pobraniem strony)

| Adres | Sprawdzono | Wynik |
|---|---|---|
| `https://support.norton.com/sp/en/us/home/current/solutions/v60392881` | 2026-10-21 | działa; tytuł „Download and run the Norton 360 Remover tool for Windows”, opis: usuwa lub usuwa i instaluje ponownie aplikację Norton device security |
| `https://www.mcafee.com/support/s/article/000002766?language=pl` | 2026-10-21 | artykuł McAfee o narzędziu usuwania (MCPR); pobranie strony: timeout, curl: 403 (blokada botów) – adres sprawdzony ręcznie. Przypisany do LiveSafe, Total Protection, AntiVirus Plus, Internet Security i Personal Security |

### Niedopisane, bo nie dało się potwierdzić

- Trial Adobe, Office trial, Spotify, Dropbox, ExpressVPN, LastPass, Amazon: brak ustalonych nazw wpisów Uninstall; część z nich ludzie kupują
  i używają, więc bez nazwy z prawdziwego sprzętu wpisu nie ma. Booking.com jest (wzorzec po nazwie, źródło: Which? i borncity.com); jeśli na laptopie
  istnieje tylko skrót w menu Start, a nie wpis Uninstall, program go nie zobaczy.
- Acer Quick Access, Acer Jumpstart, Dell SupportAssist w wersji Sklep, `AD2F1837.myHP`: nazwy pakietów bez wiarygodnego źródła.
- LinkedIn (`7EE7776C.LinkedInforWindows`) i stary Teams osobisty (`MicrosoftTeams`): nazwy niepotwierdzone, wpisy usunięte ze szkicu.

## Decyzje na granicy

| Pozycja | Decyzja | Dlaczego |
|---|---|---|
| Uzyskaj pomoc (`Microsoft.GetHelp`) | Pozostałe (niesugerowane) | Aplikacja kontaktu z pomocą; nie jest reklamą, choć jako odpowiednik Porad mogłaby być „sugerowana” |
| Microsoft Bing (`Microsoft.BingSearch`) | Pozostałe | Nie ma go wśród sugerowanych; niejednoznaczny |
| Dev Home (`Microsoft.Windows.DevHome`) | Pozostałe | Narzędzie dla programistów, może być używane |
| Family (`MicrosoftCorporationII.MicrosoftFamily`) | Pozostałe | Kontrola rodzicielska, może być w użyciu |
| Sticky Notes (`Microsoft.MicrosoftStickyNotes`) | **Chronione** | Aplikacja użytkowa (decyzja projektowa) |
| Xbox TCUI, Xbox Identity Provider, Game Speech Window | Pozostałe | Zależności Xbox, mogą być potrzebne grom i Game Bar (decyzja projektowa) |
| Game Bar (`Microsoft.XboxGamingOverlay`) | Pozostałe, jawny wpis | Decyzja z 2026-10-21 |
| Łącze z telefonem, Urządzenia przenośne, Windows Web Experience Pack | Pozostałe | Bez wpisu; Widżety wyłącza poprawka `privacy.widgets` |
| Ulepszenia zgodności, kodeki AVC/MPEG-2/VP9/RAW/WebMedia, Zakupy w Sklepie | Chronione | Składniki systemu i kodeki, jak reszta chronionych kodeków |
| Edge jako pakiet (`Microsoft.MicrosoftEdge.Stable`) | Chronione | Na VM pakiet istnieje jako osobna pozycja obok wpisu Win32 Edge |
| Podłączanie pulpitu zdalnego | Chronione (po polsku i po angielsku) | Wpis Uninstall `mstsc` ma nazwę tylko w `DisplayName_Localized`; wzorzec zależy od języka systemu |
| HP Support Assistant, Dell SupportAssist | **Pozostałe** (szkic miał „sugerowane”) | Mogą aktualizować sterowniki; brak sprzętu OEM do sprawdzenia |
| MyASUS, Armoury Crate, MyDell, Dell Optimizer | Pozostałe + ostrzeżenie o baterii i Fn | Sterują baterią i wydajnością |
| Dell Digital Delivery | Pozostałe + ostrzeżenie | Może trzymać instalatory kupionego oprogramowania |
| McAfee WebAdvisor, Security Scan Plus | Pozostałe + ostrzeżenie | Program nie odróżni próby od płatnej licencji; bez niezależnego źródła; decyzja z 2026-10-21 po przeczytaniu listy |
| Konsumenckie McAfee i Norton | Pozostałe + ostrzeżenie „jeśli to płatny antywirus, nie usuwaj” | Program nie odróżni próby od płatnej licencji |
| McAfee Agent, Endpoint, Drive Encryption, DLP, Trellix | **Chronione** | Mogą być oprogramowaniem firmowym |
| McAfee LiveSafe, Total Protection, AntiVirus Plus, Internet Security | Pozostałe + ostrzeżenie; w `source` oficjalne strony mcafee.com | Zwykle preinstalowana wersja próbna; program nie odróżni próby od płatnej licencji; wzorce to nazwy produktów, nie całe „McAfee*” |
| McAfee Personal Security | Pozostałe + ostrzeżenie | Program nie odróżni próby od płatnej licencji; nie znaleziono oficjalnej strony McAfee dla tej nazwy |
| WildTangent, HP Games by WildTangent | Sugerowane; źródło: decyzja projektowa, bez adresu | Brak oficjalnej strony producenta w sprawdzonych źródłach; wzorce `WildTangent*` i `HP Games by WildTangent*` (bez `*WildTangent*`) |
| Booking.com | Sugerowane; źródło: decyzja projektowa, bez adresu | Brak oficjalnej strony wpisu; nazwa z Which? |
| Microsoft 365 (Office Hub), Porady | Sugerowane; źródło MS Learn (adres w `source`) | Nazw nie ma na świeżym 26H2, tylko w archiwalnej liście z Windows 10 |
| Microsoft Visual C++ Redistributable (`Microsoft Visual C++ * Redistributable*`) | **Chronione** | Wiele programów i gier ich wymaga; po usunięciu przestaną się uruchamiać |
| .NET Runtime, Windows Desktop Runtime (`Microsoft .NET*Runtime*`, `Microsoft Windows Desktop Runtime*`) | **Chronione** | Bez nich programy .NET się nie uruchomią |
| ASP.NET Core Runtime / Shared Framework (`Microsoft ASP.NET Core*Runtime*`, `…*Shared Framework*`) | **Chronione** | Bez nich programy i usługi ASP.NET Core się nie uruchomią |
| Microsoft Update Health Tools | **Chronione** | Składnik usługi Windows Update (KB4023057) |
| Microsoft GameInput (`Microsoft GameInput*`, program MSI) | **Chronione** | Obsługa kontrolerów w grach; gry instalują go z powrotem |
