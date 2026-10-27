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
- `suggested` – `true`: wiersz trafia do grupy „Sugerowane do usunięcia” i sam się zaznacza; `false`: grupa „Pozostałe”
  (można zaznaczyć ręcznie), z `warning`, jeśli jest.
- `summary` – ≤ 160 znaków, po polsku, bez nazw kluczy rejestru i cmdletów.
- `warning` – opcjonalne ostrzeżenie pokazywane przy wierszu.
- `source` – skąd wiemy, że to śmieć (dokumentacja MS, odczyt VM, spec, producent). Wpis sugerowany musi mieć źródło
  z adresem `https://`, znacznikiem „spec 6.2 (decyzja Dawida)” albo odczytem VM; pilnuje tego test `SuggestedEntries_HaveVerifiableSource`.
- `cleanupUrl` – opcjonalny adres `https` oficjalnego narzędzia producenta do czyszczenia resztek. Program niczego z niego nie pobiera,
  pokazuje go tylko jako tekst. Wolno wpisać wyłącznie adres sprawdzony pobraniem strony (tabela niżej); test `VerifiedCleanupUrls` pilnuje,
  że w pliku nie ma żadnego innego.
- `reason` (tylko `protected`) – dlaczego aplikacja jest chroniona, pokazywany przy kłódce.
- `source` w `protected` – opcjonalne, tylko dla czytającego plik (loader go nie czyta); mają je wpisy zależności uruchomieniowych (R21).

Kolejność wpisów w `entries` ma znaczenie: **pierwszy pasujący** wyznacza grupę, więc wąskie reguły idą przed szerokimi.
Błąd w pliku daje `AppsCatalogException` z id wpisu i nazwą pola.

## Zasady klasyfikacji (`AppClassifier`)

1. Ukryte całkiem: AppX z `IsFramework`, pakiety zasobów, `NonRemovable`, `SignatureKind=System`; Win32 z `SystemComponent=1`,
   z `ParentKeyName`, z `ReleaseType` ∈ {`Update`, `Hotfix`, `Security Update`} i bez `UninstallString` oraz `QuietUninstallString`.
2. **Chroniona wygrywa z każdą regułą śmieci.** Wiersz ma grupę `Protected`, kłódkę i nie trafia do serii.
3. Pierwszy pasujący wpis z `entries` ustala grupę; brak dopasowania = „Pozostałe”.
4. Powód blokady (gdy pasuje kilka): chroniona, inne konto, niepewny użytkownik, brak bezpiecznego polecenia.

## Źródła rozpoznania

- Lista chroniona i punkt wyjścia sugestii: projekt Etapu 4a (debloat), pkt 6.2.
- **Pakiety Microsoftu:** odczyt świeżej VM 26H2 (Home i Pro, build 26300.9457, 2026-10-21), sekcja „Wyniki VM 26H2 (partia 2)”
  w `docs/zrodla/2026-10-20-etap4a-zrodla.md`. Wszystkie nazwy z VM są potwierdzone odczytem `Get-AppxPackage`.
  Poza VM zostały tylko `Microsoft.MicrosoftOfficeHub` i `Microsoft.Getstarted` (nazwy z MS Learn, archiwum forum
  „Which AppXPackage can be removed without causing system problems?”; brak na świeżym 26H2).
- **Producenci (Lenovo, ASUS, Acer, HP, Dell):** nie było sprzętu OEM, więc nazwy pochodzą ze stron producentów i forów (adresy w polu
  `source` każdego wpisu). **Żaden wpis producenta nie jest sugerowany**, więc pomyłka w nazwie nie usunie niczego; w najgorszym razie
  aplikacja nie dostanie ostrzeżenia. Nazwa pakietu Lenovo Vantage (`E046963F.LenovoCompanion`) nie została potwierdzona w sieci
  (zostaje jako wpis niesugerowany z ostrzeżeniem, bo brak wpisu byłby gorszy niż jego pomyłka).
- **Trial:** wzorce McAfee i Norton zawężone do produktów konsumenckich. McAfee Agent / Endpoint / Drive Encryption / Data Loss Prevention
  i Trellix są na liście **chronionych** (mogą być oprogramowaniem firmowym). Norton Family i Utilities nie pasują do żadnego wzorca.
- Wzorce chronionych sterowników po wydawcy (`Intel*`, `NVIDIA*` itd.) są szerokie celowo: lepiej nie pozwolić usunąć niż pozwolić.
- **Zależności uruchomieniowe Win32 (R21):** Visual C++ Redistributable, .NET Runtime / Windows Desktop Runtime, ASP.NET Core Runtime
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
| `https://www.mcafee.com/support/s/article/000002766?language=pl` | 2026-10-21 | artykuł McAfee o narzędziu usuwania (MCPR); pobranie strony: timeout, curl: 403 (blokada botów) – adres podał i sprawdził ręcznie Dawid. Przypisany do LiveSafe, Total Protection, AntiVirus Plus, Internet Security i Personal Security |

### Nie dopisane, bo nie dało się potwierdzić

- Trial Adobe, Office trial, Spotify, Dropbox, ExpressVPN, LastPass, Amazon: brak ustalonych nazw wpisów Uninstall; część z nich ludzie kupują
  i używają, więc bez nazwy z prawdziwego sprzętu wpisu nie ma. Booking.com jest (wzorzec po nazwie, źródło: Which? i borncity.com); jeśli na laptopie
  istnieje tylko skrót w menu Start, a nie wpis Uninstall, program go nie zobaczy.
- Acer Quick Access, Acer Jumpstart, Dell SupportAssist w wersji Sklep, `AD2F1837.myHP`: nazwy pakietów bez wiarygodnego źródła.
- LinkedIn (`7EE7776C.LinkedInforWindows`) i stary Teams osobisty (`MicrosoftTeams`): nazwy nie potwierdzone, wpisy usunięte ze szkicu.

## Decyzje na granicy

| Pozycja | Decyzja | Dlaczego |
|---|---|---|
| Uzyskaj pomoc (`Microsoft.GetHelp`) | Pozostałe (nie sugerowane) | Aplikacja kontaktu z pomocą; nie jest reklamą, a research proponował „sugerowany” jako odpowiednik Porad |
| Microsoft Bing (`Microsoft.BingSearch`) | Pozostałe | Brak w specu; niejednoznaczny |
| Dev Home (`Microsoft.Windows.DevHome`) | Pozostałe | Narzędzie dla programistów, może być używane |
| Family (`MicrosoftCorporationII.MicrosoftFamily`) | Pozostałe | Kontrola rodzicielska, może być w użyciu |
| Sticky Notes (`Microsoft.MicrosoftStickyNotes`) | **Chronione** | Aplikacja użytkowa (decyzja projektowa) |
| Xbox TCUI, Xbox Identity Provider, Game Speech Window | Pozostałe | Zależności Xbox, mogą być potrzebne grom i Game Bar (decyzja projektowa) |
| Game Bar (`Microsoft.XboxGamingOverlay`) | Pozostałe, jawny wpis | Decyzja Dawida 2026-10-21 |
| Łącze z telefonem, Urządzenia przenośne, Windows Web Experience Pack | Pozostałe | Bez wpisu; Widżety obsługuje partia 1 |
| Ulepszenia zgodności, kodeki AVC/MPEG-2/VP9/RAW/WebMedia, Zakupy w Sklepie | Chronione | Składniki systemu i kodeki, jak reszta kodeków ze specu |
| Edge jako pakiet (`Microsoft.MicrosoftEdge.Stable`) | Chronione | Na VM pakiet istnieje jako osobna pozycja obok wpisu Win32 Edge |
| Podłączanie pulpitu zdalnego | Chronione (po polsku i po angielsku) | Wpis Uninstall `mstsc` ma nazwę tylko w `DisplayName_Localized`; wzorzec zależy od języka systemu |
| HP Support Assistant, Dell SupportAssist | **Pozostałe** (szkic miał „sugerowane”) | Mogą aktualizować sterowniki; brak sprzętu OEM do sprawdzenia |
| MyASUS, Armoury Crate, MyDell, Dell Optimizer | Pozostałe + ostrzeżenie o baterii i Fn | Sterują baterią i wydajnością |
| Dell Digital Delivery | Pozostałe + ostrzeżenie | Może trzymać instalatory kupionego oprogramowania |
| McAfee WebAdvisor, Security Scan Plus | Sugerowane | Bez niezależnego źródła; decyzja Dawida 2026-10-21 po przeczytaniu listy |
| Konsumenckie McAfee i Norton | Sugerowane + ostrzeżenie „jeśli to płatny antywirus, nie usuwaj” | Spec 6.2 mówi: trial sugerowany; program nie odróżni próby od płatnej licencji |
| McAfee Agent, Endpoint, Drive Encryption, DLP, Trellix | **Chronione** | Mogą być oprogramowaniem firmowym |
| McAfee LiveSafe, Total Protection, AntiVirus Plus, Internet Security | Sugerowane; w `source` oficjalne strony mcafee.com | Spec 6.2 (trial McAfee); wzorce to nazwy produktów, nie całe „McAfee*” |
| McAfee Personal Security | Sugerowane + ostrzeżenie | Nie znaleziono oficjalnej strony McAfee dla tej nazwy (R1); sugerowany decyzją Dawida 2026-10-21 |
| WildTangent, HP Games by WildTangent | Sugerowane; źródło „spec 6.2 (decyzja Dawida)” | Brak oficjalnej strony producenta w sprawdzonych źródłach; wzorce `WildTangent*` i `HP Games by WildTangent*` (R1: bez `*WildTangent*`) |
| Booking.com | Sugerowane; źródło „spec 6.2 (decyzja Dawida)” | Brak oficjalnej strony wpisu; nazwa z Which? |
| Microsoft 365 (Office Hub), Porady | Sugerowane; źródło MS Learn (adres w `source`) | Nazw nie ma na świeżym 26H2, tylko w archiwalnej liście z Windows 10 |
| Microsoft Visual C++ Redistributable (`Microsoft Visual C++ * Redistributable*`) | **Chronione** (R21) | Wiele programów i gier ich wymaga; po usunięciu przestaną się uruchamiać |
| .NET Runtime, Windows Desktop Runtime (`Microsoft .NET*Runtime*`, `Microsoft Windows Desktop Runtime*`) | **Chronione** (R21) | Bez nich programy .NET się nie uruchomią |
| ASP.NET Core Runtime / Shared Framework (`Microsoft ASP.NET Core*Runtime*`, `…*Shared Framework*`) | **Chronione** (R21) | Bez nich programy i usługi ASP.NET Core się nie uruchomią |
| Microsoft Update Health Tools | **Chronione** (R21) | Składnik usługi Windows Update (KB4023057) |
| Microsoft GameInput (`Microsoft GameInput*`, program MSI) | **Chronione** (R21) | Obsługa kontrolerów w grach; gry instalują go z powrotem |
