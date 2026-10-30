using System;
using System.Collections.Generic;
using System.Globalization;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Localization;

namespace DSOptimizer.Core.Install;

/// <summary>Teksty strony „Instalacja” i serii instalacji.</summary>
public static class InstallText
{
    /// <summary>Nazwa kategorii w UI.</summary>
    public static string CategoryName(InstallCategory category) => category switch
    {
        InstallCategory.Browsers => L.T("Przeglądarki"),
        InstallCategory.Messengers => L.T("Komunikatory"),
        InstallCategory.Multimedia => L.T("Multimedia"),
        InstallCategory.Documents => L.T("Dokumenty"),
        InstallCategory.Tools => L.T("Narzędzia"),
        InstallCategory.RemoteHelp => L.T("Zdalna pomoc"),
        InstallCategory.Games => L.T("Gry"),
        InstallCategory.Runtimes => L.T("Środowiska uruchomieniowe"),
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Nieznana kategoria listy instalacji."),
    };

    // --- stan winget (WingetAvailability) ---

    /// <summary>Niepewny zalogowany użytkownik = odmowa (decyzja planu), ten sam tekst co na stronie „Aplikacje”.</summary>
    public static string UserUncertain => AppsText.UserUncertain;

    public static string NotRegistered
        => L.T("winget (Instalator aplikacji) nie jest zarejestrowany dla tego konta. Użyj „Naprawy → Napraw winget”.");

    /// <summary>Prefiks nazwy ukrytego konta, na którym działa podniesiony proces przy Administrator Protection (pomiar VM 4c: <c>ADMIN_dso</c>).</summary>
    private const string AdministratorProtectionPrefix = "ADMIN_";

    /// <summary>
    /// Odmowa: proces działa na innym koncie niż zalogowane (spec 2, ten sam tekst co <c>repair.winget</c>). Ruling R17: gdy nazwa
    /// konta procesu zaczyna się od <c>ADMIN_</c> (Administrator Protection, KB5120998 – ukryte konto z innym SID), tekst mówi
    /// o tej przyczynie. Ten tekst idzie do strony „Instalacja” i do CLI (<see cref="WingetAvailability"/>).
    /// </summary>
    /// <param name="accountName">Nazwa zalogowanego użytkownika (albo jego SID).</param>
    /// <param name="processAccountName">Nazwa konta procesu; <c>null</c> = nieznana (bez dopisku).</param>
    public static string OtherAccount(string accountName, string? processAccountName = null)
        => processAccountName is not null && processAccountName.StartsWith(AdministratorProtectionPrefix, StringComparison.OrdinalIgnoreCase)
            ? L.F("Instalacja działa tylko na koncie zalogowanego użytkownika: uruchom DSOptimizer na koncie {0} (Administrator Protection: program podniesiony działa na ukrytym koncie {1}).",
                accountName,
                processAccountName)
            : L.F("Instalacja działa tylko na koncie zalogowanego użytkownika: uruchom DSOptimizer na koncie {0}.", accountName);

    public static string WingetReady(string version) => L.F("winget działa (wersja {0}).", version);

    public static string WingetNotWorking(string detail) => L.F("winget nie działa ({0}). Użyj „Naprawy → Napraw winget”.", detail);

    public static string VersionExitCode(int exitCode) => L.F("winget --version: kod {0}", WingetExitCodes.Hex(exitCode));

    public static string VersionWithoutNumber => L.T("winget --version: kod 0, ale w wyjściu brak numeru wersji");

    public static string VersionTimedOut(TimeSpan limit)
        => L.F("winget --version nie odpowiedział w {0} s", ((int)limit.TotalSeconds).ToString(CultureInfo.InvariantCulture));

    public static string VersionStartFailed(string message) => L.F("nie udało się uruchomić winget: {0}", message);

    public static string ReadFailed(string message) => L.F("Nie udało się odczytać listy zainstalowanych aplikacji: {0}", message);

    // --- wynik pozycji serii ---

    public static string Installed => L.T("Zainstalowano i sprawdzono.");

    public static string InstalledUnverified => L.T("Zainstalowano (zgłoszone przez winget).");

    public static string AlreadyInstalledBeforeRun => L.T("Była już zainstalowana – nic nie uruchomiono.");

    public static string AlreadyInstalledByWinget => L.T("Była już zainstalowana (zgłoszone przez winget).");

    public static string NotVisibleAfterSuccess => L.T("winget zgłosił sukces, ale programu nie widać na liście zainstalowanych");

    public static string JournalFailed => L.T("Nie udało się zapisać journala – instalacja nie została uruchomiona.");

    /// <summary>Pozostałe pozycje, gdy pierwsza uruchomiona skończyła się brakiem sieci (spec 5.1).</summary>
    public static string SkippedNoNetwork => L.T("Nie wykonano – brak połączenia z internetem albo ze źródłem winget");

    /// <summary>Pozostałe pozycje po limicie czasu (spec 5.4): winget i instalator działają dalej.</summary>
    public static string SkippedStillRunning => L.T("Nie wykonano – poprzednia instalacja nadal trwa");

    /// <summary>Etap pokazywany w oknie serii; rozpoznanie wyjścia winget to <see cref="WingetOutput.StageOf"/> (wzorce angielskie winget).</summary>
    public static string StageDownloading => L.T("Pobieranie");

    public static string StageInstalling => L.T("Instalowanie");

    /// <summary>Etap po wyjściu winget, gdy programu nie widać jeszcze na liście zainstalowanych (odczyt zwrotny do 60 s, R16).</summary>
    public static string StageVerifying => L.T("Sprawdzanie instalacji");

    public static string StartFailed(string message) => L.F("Nie wykonano – nie udało się uruchomić winget: {0}", message);

    public static string LogPathFailed(string message) => L.F("Nie wykonano – nie udało się przygotować pliku logu winget: {0}", message);

    public static string StillRunningInBackground(TimeSpan limit)
        => L.F("Nie potwierdzono – instalacja nadal trwa w tle (program przestał czekać po {0} min).",
            Math.Max(1, (int)Math.Ceiling(limit.TotalMinutes)).ToString(CultureInfo.InvariantCulture));

    public static string NotVisibleReadFailed(string message)
        => L.F("winget zgłosił sukces, ale nie udało się odczytać listy zainstalowanych: {0}", message);

    /// <summary>Porażka z kodem winget: opis ze spec 5.3, kod szesnastkowo (nieznany kod ma go już w opisie) i ścieżka logu.</summary>
    public static string FailedWithCode(int exitCode, string? logPath)
    {
        var text = WingetExitCodes.Describe(exitCode);
        if (WingetExitCodes.Classify(exitCode) is not (WingetVerdict.Unknown or WingetVerdict.Cancelled))
        {
            text += " " + L.F("Kod {0}.", WingetExitCodes.Hex(exitCode));
        }

        return WithLog(text, logPath);
    }

    /// <summary>
    /// Ruling R21: nieznany kod ≠ 0, ale program pozycji listy jest widoczny na liście zainstalowanych. Tekst mówi tylko o kodzie
    /// i o tym, co widać – bez zgadywania przyczyny.
    /// </summary>
    public static string FailedButVisible(int exitCode, string? logPath)
        => WithLog(
            L.F("Nie potwierdzono – winget zakończył się kodem {0}, ale program jest widoczny na liście zainstalowanych; sprawdź log winget.",
                WingetExitCodes.Hex(exitCode)),
            logPath);

    /// <summary>Znacznik ścieżki logu winget w tekście wyniku (<see cref="WithLog"/>, <see cref="WithoutLog"/>).</summary>
    private static string WingetLogMarker => L.T(" Log winget: ");

    /// <summary>Znacznik ścieżki logu MSI w tekście wyniku (<see cref="WithMsiLog"/>, <see cref="WithoutLog"/>).</summary>
    private static string MsiLogMarker => L.T(" Log MSI: ");

    /// <summary>Tekst z dopisaną ścieżką logu winget (gdy jest).</summary>
    public static string WithLog(string text, string? logPath)
        => logPath is null ? text : text.TrimEnd() + WingetLogMarker + logPath;

    /// <summary>
    /// Tekst bez dopisku ze ścieżką logu (<c>" Log winget: …"</c> albo <c>" Log MSI: …"</c>, pierwszy znaleziony) – powód porażki w journalu.
    /// Znacznik rozpoznawany w każdym języku interfejsu: tekst zapisany przez wcześniejsze uruchomienie bywa w innym języku.
    /// </summary>
    public static string WithoutLog(string text)
    {
        var cut = -1;
        foreach (var marker in L.AllLanguages(() => WingetLogMarker, () => MsiLogMarker))
        {
            var at = text.IndexOf(marker, StringComparison.Ordinal);
            if (at >= 0 && (cut < 0 || at < cut))
            {
                cut = at;
            }
        }

        return cut < 0 ? text : text.Substring(0, cut);
    }

    /// <summary>Komunikat zdarzenia journala <c>install-app</c>: <c>winget: &lt;id&gt;[, wersja &lt;v&gt;][, log: &lt;ścieżka&gt;]</c>.</summary>
    public static string JournalMessage(string wingetId, string? version, string? logPath)
        => "winget: " + wingetId + JournalDetails(version, logPath);

    // --- pozycja z GitHuba (RustDesk, spec RustDesk 3–4; teksty spoza specu = decyzja planu) ---

    /// <summary>Etap pobrania wydania z GitHuba (procent z rozmiaru z API).</summary>
    public static string StageDownloadingGitHub => L.T("Pobieranie (z GitHuba)");

    /// <summary>Etap sprawdzenia skrótu SHA256 i podpisu wydawcy.</summary>
    public static string StageCheckingSignature => L.T("Sprawdzanie podpisu");

    /// <summary>Brak sieci przy API albo pobraniu (spec 3.7); na pierwszej uruchomionej pozycji kończy serię.</summary>
    public static string GitHubNoConnection => L.T("Nie wykonano – brak połączenia z GitHubem");

    /// <summary>
    /// Limit <c>RustDeskSource.DownloadTimeout</c> (10 min) przy pobieraniu pliku – wolne albo zerwane łącze (M2 final review); seria i tak
    /// się kończy jak przy braku sieci (decyzja planu 4). Limit przy API (15 s) zostaje <see cref="GitHubNoConnection"/>.
    /// </summary>
    public static string GitHubDownloadTimedOut => L.T("Nie wykonano – pobieranie z GitHuba przekroczyło limit 10 min (wolne lub zerwane łącze)");

    /// <summary>API odpowiedziało 403/429 (spec 3.7); seria idzie dalej.</summary>
    public static string GitHubRateLimited => L.T("Nie wykonano – GitHub chwilowo odmawia (limit zapytań), spróbuj za godzinę");

    public static string GitHubHttpError(int status)
        => L.F("Nie wykonano – GitHub odpowiedział kodem {0}", status.ToString(CultureInfo.InvariantCulture));

    /// <summary>Wydanie bez assetu <c>rustdesk-&lt;tag&gt;-x86_64.msi</c> (spec 3.1, dosłownie).</summary>
    public static string GitHubNoMsi => L.T("Nie wykonano – na GitHubie RustDesk nie ma instalatora MSI dla tej wersji");

    public static string ReleaseRefused(string reason)
        => L.F("Nie wykonano – wydanie RustDesk na GitHubie nie przeszło sprawdzenia ({0})", reason);

    /// <summary>Porażka sprawdzenia pobranego pliku (spec 3.3): plik usunięty, nic nie uruchomione.</summary>
    public static string DownloadCheckFailed(string reason)
        => L.F("Nie wykonano – pobrany plik nie przeszedł sprawdzenia ({0})", reason);

    /// <summary>Błąd zapisu pobieranego pliku (pełny dysk, plik istnieje) – porażka pozycji, nie brak sieci (ruling, Task 3).</summary>
    public static string DownloadWriteFailed(string message)
        => L.F("Nie wykonano – nie udało się zapisać pobranego pliku ({0})", message);

    /// <summary>Powód <see cref="ReleaseRefused"/>: <c>browser_download_url</c> poza <c>RustDeskSource.IsAllowedDownloadUrl</c>.</summary>
    public static string ReasonForeignDownloadUrl => L.T("adres pobrania spoza GitHuba RustDesk");

    public static string ReasonRedirect(string? host)
        => string.IsNullOrEmpty(host) ? L.T("przekierowanie na nieznany serwer") : L.F("przekierowanie na nieznany serwer {0}", host);

    public static string ReasonSizeMismatch => L.T("rozmiar inny niż w GitHubie");

    public static string ReasonHashMismatch => L.T("skrót SHA256 inny niż w GitHubie");

    public static string ReasonNotSigned => L.T("brak podpisu wydawcy");

    public static string ReasonInvalidSignature(string? detail)
        => string.IsNullOrEmpty(detail) ? L.T("podpis nieważny") : L.F("podpis nieważny ({0})", detail);

    public static string ReasonOtherPublisher(string? subject)
        => string.IsNullOrEmpty(subject) ? L.T("podpis innego wydawcy: (brak podmiotu)") : L.F("podpis innego wydawcy: {0}", subject);

    /// <summary>msiexec zakończył się kodem innym niż 0/3010/1641 (spec 3.4), z logiem MSI.</summary>
    public static string MsiFailed(int code, string? logPath)
        => WithMsiLog(L.F("Nie wykonano – instalator MSI zakończył się kodem {0}.", code.ToString(CultureInfo.InvariantCulture)), logPath);

    /// <summary>Nie udało się przygotować ścieżki logu MSI (odpowiednik <see cref="LogPathFailed"/> dla winget) – msiexec nie startuje.</summary>
    public static string MsiLogPathFailed(string message) => L.F("Nie wykonano – nie udało się przygotować pliku logu MSI: {0}", message);

    public static string MsiStartFailed(string message) => L.F("Nie wykonano – nie udało się uruchomić instalatora MSI: {0}", message);

    public static string MsiNotVisibleAfterSuccess => L.T("instalator MSI zgłosił sukces, ale programu nie widać na liście zainstalowanych");

    public static string MsiNotVisibleReadFailed(string message)
        => L.F("instalator MSI zgłosił sukces, ale nie udało się odczytać listy zainstalowanych: {0}", message);

    /// <summary>Tekst z dopisaną ścieżką logu MSI (gdy jest).</summary>
    public static string WithMsiLog(string text, string? logPath)
        => logPath is null ? text : text.TrimEnd() + MsiLogMarker + logPath;

    /// <summary>Pozostałe pozycje, gdy pierwsza uruchomiona pozycja z GitHuba nie miała połączenia.</summary>
    public static string SkippedNoInternet => L.T("Nie wykonano – brak połączenia z internetem");

    /// <summary>Pozycja z GitHuba w serii bez usług GitHuba (np. CLI bez składania pobierania).</summary>
    public static string GitHubUnavailable => L.T("Nie wykonano – instalacja z GitHuba nie jest dostępna w tym uruchomieniu");

    /// <summary>Ostrzeżenie wyniku, gdy pobranego pliku nie udało się usunąć.</summary>
    public static string DownloadNotDeleted(string path) => L.F("Nie udało się usunąć pobranego pliku {0}", path);

    /// <summary>Komunikat zdarzenia journala <c>install-app</c> pozycji z GitHuba: <c>github: &lt;repo&gt;[, wersja &lt;tag&gt;][, log: &lt;ścieżka&gt;]</c>.</summary>
    public static string GitHubJournalMessage(string repository, string? version, string? logPath)
        => "github: " + repository + JournalDetails(version, logPath);

    // --- strona „Instalacja” ---

    /// <summary>Pozycja menu i tytuł strony (spec 6).</summary>
    public static string PageTitle => L.T("Instalacja");

    public static string RefreshButton => L.T("Odśwież");

    public static string Reading => L.T("Odczyt listy w toku");

    public static string NotRead => L.T("Lista nie została jeszcze odczytana");

    /// <summary>Podtytuł, gdy winget nie działa (brak App Installera, <c>--version</c> nie działa, błąd odczytu listy).</summary>
    public static string PageUnavailable => L.T("Źródło: winget (Microsoft) · niedostępne");

    public static string SearchPlaceholder => L.T("Szukaj aplikacji, np. VLC");

    /// <summary>Podpowiedź pola, gdy winget nie działa: filtr listy działa, wyszukiwanie w winget nie.</summary>
    public static string SearchPlaceholderListOnly => L.T("Szukaj aplikacji na liście, np. VLC");

    public static string SearchButton => L.T("Szukaj też w winget");

    public static string SelectedGroupName => L.T("Zaznaczone");

    public static string SelectedEmpty => L.T("Nic nie jest zaznaczone. Zaznacz aplikacje na liście albo w wynikach winget.");

    public static string CatalogSection(int count) => L.F("Z listy ({0})", count.ToString(CultureInfo.InvariantCulture));

    public static string WingetSection(int count) => L.F("W winget ({0})", count.ToString(CultureInfo.InvariantCulture));

    public static string WingetSectionNote
        => L.T("Pakiety spoza listy DSOptimizera – pobierane z oficjalnego źródła winget (Microsoft), bez wcześniejszego sprawdzenia przez autora programu.");

    public static string NoCatalogMatch => L.T("Brak na liście. Naciśnij Enter, żeby poszukać w winget.");

    public static string NoCatalogMatchWingetDown => L.T("Brak na liście. winget nie działa – wyszukiwanie w winget niedostępne.");

    /// <summary>Brak dopasowań przy odmowie konta (inne konto, niepewny użytkownik): winget działa, tylko nie na tym koncie (Ruling R4).</summary>
    public static string NoCatalogMatchPlain => L.T("Brak na liście.");

    /// <summary>
    /// CLI: <c>winget:</c> z aliasem pozycji z GitHuba (<see cref="GitHubSources.WingetAliases"/>) to nie druga droga instalacji
    /// (spec 11.2, Ruling R5) – błędne argumenty z podpowiedzią właściwego id.
    /// </summary>
    public static string CliGitHubAlias(string title, string listId) => L.F("{0} instaluje się z listy: użyj {1}.", title, listId);

    public static string OnListNote(string title) => L.F("{0} jest na liście – wyżej", title);

    public static string OnListNoteGitHub(string title) => L.F("{0} jest na liście – wyżej (instalowany z GitHuba)", title);

    // --- walidacja zapytania (WingetArguments.TryValidateQuery) ---

    public static string QueryEmpty => L.T("Wpisz, czego szukać.");

    public static string QueryTooLong(int max) => L.F("Zapytanie może mieć najwyżej {0} znaków.", max.ToString(CultureInfo.InvariantCulture));

    public static string QueryInvalidChars => L.T("Zapytanie nie może zawierać cudzysłowu ani znaków sterujących.");

    /// <summary>M6 final review: zapytanie od „-” winget mógłby wziąć za opcję (obejście stałego <c>--source winget</c>).</summary>
    public static string QueryLeadingDash => L.T("Zapytanie nie może zaczynać się od „-”.");

    public static string Searching => L.T("Szukanie w winget");

    /// <summary>Grupa wyników wyszukiwania na końcu listy kategorii (spec 6, wariant A).</summary>
    public static string SearchGroupName => L.T("Wyniki wyszukiwania");

    public static string InstalledStatus => L.T("Zainstalowana");

    public static string WingetProblemTitle => L.T("winget nie działa na tym komputerze");

    /// <summary>Przycisk przy <see cref="WingetState.NotRegistered"/>/<see cref="WingetState.NotWorking"/> (spec 2): kategoria „Naprawy”, pozycja <c>repair.winget</c>.</summary>
    public static string RepairWingetButton => L.T("Przejdź do: Napraw winget");

    /// <summary>„Zatrzymaj” okna serii instalacji (spec 5.2): bieżąca instalacja zawsze się kończy.</summary>
    public static string StopText => L.T("Zatrzymaj po tej aplikacji");

    public static string StoppingText => L.T("Zatrzymam po bieżącej aplikacji…");

    public static string StopNote => L.T("Bieżąca instalacja zawsze się kończy.");

    /// <summary>
    /// Podtytuł strony po odczycie: źródło, liczba pozycji listy i zainstalowanych. <paramref name="withGitHub"/> = lista ma pozycję
    /// z GitHuba (RustDesk) – wtedy oba źródła (M6 review Tasku 5).
    /// </summary>
    public static string PageSummary(int total, int installed, bool withGitHub)
    {
        RequireCounts(total, installed);
        return withGitHub
            ? L.F("Źródła: winget (Microsoft) i GitHub (RustDesk) · {0} · zainstalowane: {1}", AppCount(total), installed)
            : L.F("Źródło: winget (Microsoft) · {0} · zainstalowane: {1}", AppCount(total), installed);
    }

    /// <summary>
    /// Podtytuł, gdy winget nie działa, ale lista ma pozycję z GitHuba do zaznaczenia (decyzja planu 1): GitHub działa, winget nie
    /// (M6 review Tasku 5). Bez pozycji z GitHuba i przy błędzie odczytu listy – <see cref="PageUnavailable"/>.
    /// </summary>
    public static string PageWingetUnavailable(int total, int installed)
    {
        RequireCounts(total, installed);
        return L.F("Źródła: GitHub (RustDesk) · winget (Microsoft) niedostępny · {0} · zainstalowane: {1}", AppCount(total), installed);
    }

    private static void RequireCounts(int total, int installed)
    {
        if (total < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(total), total, null);
        }

        if (installed < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(installed), installed, null);
        }
    }

    /// <summary>„N aplikacja/aplikacje/aplikacji” (podtytuł strony i okna potwierdzenia).</summary>
    internal static string AppCount(int count) => L.P("{0} aplikacja", "{0} aplikacje", "{0} aplikacji", count);

    /// <summary>Pierwsza linia paska serii; przy 0 podpowiedź, jak zacząć.</summary>
    public static string SelectionBarTitle(int selected)
    {
        if (selected < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(selected), selected, null);
        }

        return selected == 0
            ? L.T("Kliknij dwukrotnie wiersz albo pole wyboru, żeby dodać do instalacji.")
            : L.P("Zaznaczono {0} aplikację", "Zaznaczono {0} aplikacje", "Zaznaczono {0} aplikacji", selected);
    }

    /// <summary>
    /// Druga linia paska serii: z ilu grup (kategorie i wyniki wyszukiwania) i skąd pobierane – winget, GitHub RustDesk albo oba;
    /// pusta bez zaznaczenia. Zaznaczenie bez żadnego źródła = <see cref="ArgumentException"/>. Polskie „kategorii” jest to samo
    /// dla 1 i 5, więc 1 ma osobne zdania (angielskie „category”/„categories”).
    /// </summary>
    public static string SelectionBarDetails(int groups, bool winget, bool gitHub)
    {
        if (groups < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(groups), groups, null);
        }

        if (groups == 0)
        {
            return string.Empty;
        }

        if (!winget && !gitHub)
        {
            throw new ArgumentException("Zaznaczone pozycje muszą mieć źródło (winget albo GitHub).", nameof(winget));
        }

        if (groups == 1)
        {
            return winget && gitHub
                ? L.T("Z 1 kategorii, pobierane z winget (Microsoft) i z GitHuba RustDesk. Wymaga internetu.")
                : winget
                    ? L.T("Z 1 kategorii, pobierane z winget (Microsoft). Wymaga internetu.")
                    : L.T("Z 1 kategorii, pobierane z GitHuba RustDesk. Wymaga internetu.");
        }

        var count = groups.ToString(CultureInfo.InvariantCulture);
        return winget && gitHub
            ? L.F("Z {0} kategorii, pobierane z winget (Microsoft) i z GitHuba RustDesk. Wymaga internetu.", count)
            : winget
                ? L.F("Z {0} kategorii, pobierane z winget (Microsoft). Wymaga internetu.", count)
                : L.F("Z {0} kategorii, pobierane z GitHuba RustDesk. Wymaga internetu.", count);
    }

    /// <summary>Pierwsza pozycja listy kategorii (D1, Ruling P3): cała lista pogrupowana nagłówkami kategorii.</summary>
    public static string AllCategoriesName => L.T("Wszystkie");

    /// <summary>Źródło wiersza pozycji z GitHuba drobnym drukiem (zamiast identyfikatora winget).</summary>
    public static string GitHubLabel(string repository) => "GitHub: " + repository;

    /// <summary>Dopisek komunikatu „winget nie działa” nad listą, gdy lista ma pozycję z GitHuba (decyzja planu 1).</summary>
    public static string GitHubStillAvailable => L.T("RustDesk (z GitHuba) można zainstalować także bez winget.");

    /// <summary>Link w komunikacie „winget nie działa” (M5): kategoria pozycji z GitHuba i jej wiersz na liście.</summary>
    public static string ShowRustDeskLink => L.T("Pokaż RustDesk");

    /// <summary>Przycisk serii (pasek strony i okno potwierdzenia).</summary>
    public static string InstallButton(int count) => L.F("Zainstaluj ({0})", count.ToString(CultureInfo.InvariantCulture));

    /// <summary>Notatka wiersza wyszukiwania z Id obciętym „…”, pustym albo przesuniętym znakiem szerokim (spec 7, R8).</summary>
    public static string TruncatedIdNote => L.T("nazwa pakietu skrócona – zawęź wyszukiwanie");

    public static string SearchBusy => L.T("Trwa inna operacja – spróbuj za chwilę.");

    public static string InstallBusy => L.T("Trwa inna operacja – nic nie zmieniono.");

    public static string SearchNoResults => L.T("Nic nie znaleziono w winget.");

    public static string SearchNoNetwork => L.T("Brak połączenia z internetem albo ze źródłem winget.");

    /// <summary>winget coś znalazł, ale wszystko to pozycje listy (pokazane raz, w swojej kategorii).</summary>
    public static string SearchAllOnList => L.T("Znalezione aplikacje są na liście – wyżej.");

    public static string SearchFailed(int exitCode) => L.F("Wyszukiwanie nie powiodło się (kod {0}).", WingetExitCodes.Hex(exitCode));

    public static string SearchTimedOut(TimeSpan limit)
        => L.F("winget nie odpowiedział w {0} min.", Math.Max(1, (int)Math.Ceiling(limit.TotalMinutes)).ToString(CultureInfo.InvariantCulture));

    public static string SearchStartFailed(string message) => L.F("Nie udało się uruchomić winget: {0}", message);

    /// <summary>Do serii trafiła pozycja z Id, którego nie da się podać w <c>--id --exact</c> (strażnik pola wyboru).</summary>
    public static string InvalidTargetRefusal => L.T("Pozycji ze skróconą nazwą pakietu nie da się zainstalować – nic nie zmieniono.");

    // --- okno potwierdzenia i seria ---

    public static string ConfirmTitle => L.T("Zainstaluj zaznaczone");

    /// <summary>Ostrzeżenie serii, dosłownie ze spec 5.4.</summary>
    public static string SeriesWarning
        => L.T("Aplikacje zostaną pobrane z oficjalnego źródła winget (Microsoft) i zainstalowane bez okien instalatorów. Wymaga internetu. Instalacja może potrwać kilka–kilkanaście minut.");

    /// <summary>
    /// Ostrzeżenie serii z pozycją z GitHuba (decyzja planu, Task 5; skrócone rulingiem M4): GitHub i PURSLANE podają już
    /// fakt „Źródło” i ostrzeżenie pozycji RustDesk (spec 4), a „Wymaga internetu.” mówi w serii mieszanej <see cref="SeriesWarning"/>.
    /// </summary>
    public static string SeriesWarningGitHub
        => L.T("RustDesk zostanie pobrany i zainstalowany bez okna instalatora dopiero po sprawdzeniu skrótu SHA256 i podpisu wydawcy.");

    /// <summary>Ostrzeżenie serii samego RustDeska (bez pozycji winget): <see cref="SeriesWarningGitHub"/> + „Wymaga internetu.”.</summary>
    public static string SeriesWarningGitHubOnly
        => L.T("RustDesk zostanie pobrany i zainstalowany bez okna instalatora dopiero po sprawdzeniu skrótu SHA256 i podpisu wydawcy. Wymaga internetu.");

    public static string SourceFact => L.T("Źródło");

    public const string SourceValue = "winget (Microsoft)";

    /// <summary>Fakt „Źródło” serii z pozycją z GitHuba (seria mieszana: oba połączone „; ”).</summary>
    public static string GitHubSourceValue => L.T("GitHub RustDesk (rustdesk/rustdesk), podpis PURSLANE");

    /// <summary>Fakt techniczny pozycji z GitHuba w oknie potwierdzenia (zamiast <see cref="WingetIdFact"/>).</summary>
    public static string GitHubRepositoryFact => L.T("Repozytorium GitHub");

    public static string RestorePointFact => L.T("Punkt przywracania");

    public static string RestorePointValue => L.T("nie – instalację cofa się usunięciem na stronie „Aplikacje”");

    public static string RestartFact => L.T("Restart");

    public static string RestartValue => L.T("tylko gdy instalator go zgłosi");

    public static string WingetIdFact => L.T("Identyfikator winget");

    /// <summary>Opis pozycji spoza listy w oknie potwierdzenia.</summary>
    public static string SearchItemSummary => L.T("Wynik wyszukiwania w winget (spoza listy).");

    public static string NotDone => L.T("Nie wykonano");

    public static string NotConfirmedStatus => L.T("Nie potwierdzono");

    /// <summary>Status pozycji w oknie serii i w dzienniku.</summary>
    public static string OutcomeName(TweakOutcome outcome) => outcome switch
    {
        TweakOutcome.Installed => L.T("Zainstalowano"),
        TweakOutcome.AlreadyInstalled => L.T("Już zainstalowane"),
        TweakOutcome.InstalledUnverified => L.T("Zainstalowano (zgłoszone przez winget)"),
        TweakOutcome.NotConfirmed => NotConfirmedStatus,
        _ => NotDone,
    };

    /// <summary>
    /// Linia dziennika pozycji zatrzymanej przed startem (<see cref="TweakEngine.StoppedMessage"/>): jedno zdanie zamiast sklejki
    /// „Tytuł: Nie wykonano – Pominięte – zatrzymano.”, która po angielsku powtarzałaby status.
    /// </summary>
    public static string StoppedLine(string title) => L.F("{0}: Nie wykonano – Pominięte – zatrzymano.", title);

    /// <summary>
    /// Szczegół wyniku pod statusem: komunikat bez powtórzonego „Nie wykonano – ”/„Nie potwierdzono – ” (status już to mówi);
    /// standardowy komunikat sukcesu nic nie dodaje, więc <c>null</c>.
    /// </summary>
    /// <param name="result">Wynik pozycji serii instalacji.</param>
    /// <param name="capitalize">
    /// <c>true</c> (okno serii: szczegół w osobnej linii) = pierwsza litera po zdjętym prefiksie wielka; <c>false</c> (linia dziennika
    /// „Tytuł: Status – szczegół”, M8) = tekst jak w komunikacie, bo po półpauzie w środku zdania zaczyna się małą literą.
    /// </param>
    public static string? ResultDetail(TweakResult result, bool capitalize = true)
    {
        if (result is null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        var message = result.Message;
        if (string.IsNullOrWhiteSpace(message)
            || (result.Outcome == TweakOutcome.Installed && string.Equals(message, Installed, StringComparison.Ordinal))
            || (result.Outcome == TweakOutcome.InstalledUnverified && string.Equals(message, InstalledUnverified, StringComparison.Ordinal)))
        {
            return null;
        }

        foreach (var status in new[] { NotDone, NotConfirmedStatus })
        {
            var prefix = status + " – ";
            if (message!.StartsWith(prefix, StringComparison.Ordinal) && message.Length > prefix.Length)
            {
                var rest = message.Substring(prefix.Length);
                return capitalize ? char.ToUpper(rest[0], L.Culture) + rest.Substring(1) : rest;
            }
        }

        return message;
    }

    /// <summary>Blok restartu w wyniku serii instalacji.</summary>
    public static string RebootText(int count)
        => L.P("{0} aplikacja potrzebuje ponownego uruchomienia komputera.", "{0} aplikacje potrzebują ponownego uruchomienia komputera.", "{0} aplikacji potrzebuje ponownego uruchomienia komputera.", count);

    /// <summary>Podsumowanie serii: „N z M zainstalowane”.</summary>
    public static string BatchSummary(int installed, int total)
        => L.F("{0} z {1} zainstalowane", installed.ToString(CultureInfo.InvariantCulture), total.ToString(CultureInfo.InvariantCulture));

    /// <summary>Dopisek komunikatu journala: <c>, wersja &lt;v&gt;</c> i <c>, log: &lt;ścieżka&gt;</c> (każdy tylko, gdy jest).</summary>
    private static string JournalDetails(string? version, string? logPath)
        => (version is null ? string.Empty : L.F(", wersja {0}", version))
            + (logPath is null ? string.Empty : ", log: " + logPath);
}
