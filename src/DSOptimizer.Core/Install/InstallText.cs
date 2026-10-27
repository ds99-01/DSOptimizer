using System;
using System.Globalization;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Engine;

namespace DSOptimizer.Core.Install;

/// <summary>Teksty strony „Instalacja” i serii instalacji.</summary>
public static class InstallText
{
    /// <summary>Nazwa kategorii w UI.</summary>
    public static string CategoryName(InstallCategory category) => category switch
    {
        InstallCategory.Browsers => "Przeglądarki",
        InstallCategory.Messengers => "Komunikatory",
        InstallCategory.Multimedia => "Multimedia",
        InstallCategory.Documents => "Dokumenty",
        InstallCategory.Tools => "Narzędzia",
        InstallCategory.RemoteHelp => "Zdalna pomoc",
        InstallCategory.Games => "Gry",
        InstallCategory.Runtimes => "Środowiska uruchomieniowe",
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Nieznana kategoria listy instalacji."),
    };

    // --- stan winget (WingetAvailability) ---

    /// <summary>Niepewny zalogowany użytkownik = odmowa (decyzja planu), ten sam tekst co na stronie „Aplikacje”.</summary>
    public const string UserUncertain = AppsText.UserUncertain;

    public const string NotRegistered =
        "winget (Instalator aplikacji) nie jest zarejestrowany dla tego konta. Użyj „Naprawy → Napraw winget”.";

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
    {
        var text = "Instalacja działa tylko na koncie zalogowanego użytkownika: uruchom DSOptimizer na koncie " + accountName;
        if (processAccountName is not null && processAccountName.StartsWith(AdministratorProtectionPrefix, StringComparison.OrdinalIgnoreCase))
        {
            text += " (Administrator Protection: program podniesiony działa na ukrytym koncie " + processAccountName + ")";
        }

        return text + ".";
    }

    public static string WingetReady(string version) => "winget działa (wersja " + version + ").";

    public static string WingetNotWorking(string detail) => "winget nie działa (" + detail + "). Użyj „Naprawy → Napraw winget”.";

    public static string VersionExitCode(int exitCode) => "winget --version: kod " + WingetExitCodes.Hex(exitCode);

    public const string VersionWithoutNumber = "winget --version: kod 0, ale w wyjściu brak numeru wersji";

    public static string VersionTimedOut(TimeSpan limit)
        => "winget --version nie odpowiedział w " + ((int)limit.TotalSeconds).ToString(CultureInfo.InvariantCulture) + " s";

    public static string VersionStartFailed(string message) => "nie udało się uruchomić winget: " + message;

    public static string ReadFailed(string message) => "Nie udało się odczytać listy zainstalowanych aplikacji: " + message;

    // --- wynik pozycji serii ---

    public const string Installed = "Zainstalowano i sprawdzono.";

    public const string InstalledUnverified = "Zainstalowano (zgłoszone przez winget).";

    public const string AlreadyInstalledBeforeRun = "Była już zainstalowana – nic nie uruchomiono.";

    public const string AlreadyInstalledByWinget = "Była już zainstalowana (zgłoszone przez winget).";

    public const string NotVisibleAfterSuccess = "winget zgłosił sukces, ale programu nie widać na liście zainstalowanych";

    public const string JournalFailed = "Nie udało się zapisać journala – instalacja nie została uruchomiona.";

    /// <summary>Pozostałe pozycje, gdy pierwsza uruchomiona skończyła się brakiem sieci (spec 5.1).</summary>
    public const string SkippedNoNetwork = "Nie wykonano – brak połączenia z internetem albo ze źródłem winget";

    /// <summary>Pozostałe pozycje po limicie czasu (spec 5.4): winget i instalator działają dalej.</summary>
    public const string SkippedStillRunning = "Nie wykonano – poprzednia instalacja nadal trwa";

    public const string StageDownloading = "Pobieranie";

    public const string StageInstalling = "Instalowanie";

    /// <summary>Etap po wyjściu winget, gdy programu nie widać jeszcze na liście zainstalowanych (odczyt zwrotny do 60 s, R16).</summary>
    public const string StageVerifying = "Sprawdzanie instalacji";

    public static string StartFailed(string message) => "Nie wykonano – nie udało się uruchomić winget: " + message;

    public static string LogPathFailed(string message) => "Nie wykonano – nie udało się przygotować pliku logu winget: " + message;

    public static string StillRunningInBackground(TimeSpan limit)
        => "Nie potwierdzono – instalacja nadal trwa w tle (program przestał czekać po "
            + Math.Max(1, (int)Math.Ceiling(limit.TotalMinutes)).ToString(CultureInfo.InvariantCulture) + " min).";

    public static string NotVisibleReadFailed(string message)
        => "winget zgłosił sukces, ale nie udało się odczytać listy zainstalowanych: " + message;

    /// <summary>Porażka z kodem winget: opis ze spec 5.3, kod szesnastkowo (nieznany kod ma go już w opisie) i ścieżka logu.</summary>
    public static string FailedWithCode(int exitCode, string? logPath)
    {
        var text = WingetExitCodes.Describe(exitCode);
        if (WingetExitCodes.Classify(exitCode) is not (WingetVerdict.Unknown or WingetVerdict.Cancelled))
        {
            text += " Kod " + WingetExitCodes.Hex(exitCode) + ".";
        }

        return WithLog(text, logPath);
    }

    /// <summary>
    /// Ruling R21: nieznany kod ≠ 0, ale program pozycji listy jest widoczny na liście zainstalowanych. Tekst mówi tylko o kodzie
    /// i o tym, co widać – bez zgadywania przyczyny.
    /// </summary>
    public static string FailedButVisible(int exitCode, string? logPath)
        => WithLog(
            NotConfirmedStatus + " – winget zakończył się kodem " + WingetExitCodes.Hex(exitCode)
                + ", ale program jest widoczny na liście zainstalowanych; sprawdź log winget.",
            logPath);

    /// <summary>Tekst z dopisaną ścieżką logu winget (gdy jest).</summary>
    public static string WithLog(string text, string? logPath)
        => logPath is null ? text : text.TrimEnd() + " Log winget: " + logPath;

    /// <summary>Tekst bez dopisku ze ścieżką logu (<c>" Log winget: …"</c> albo <c>" Log MSI: …"</c>, pierwszy znaleziony) – powód porażki w journalu.</summary>
    public static string WithoutLog(string text)
    {
        var cut = -1;
        foreach (var marker in new[] { " Log winget: ", " Log MSI: " })
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
        => "winget: " + wingetId
            + (version is null ? string.Empty : ", wersja " + version)
            + (logPath is null ? string.Empty : ", log: " + logPath);

    // --- pozycja z GitHuba (RustDesk, spec RustDesk 3–4; teksty spoza specu = decyzja planu) ---

    /// <summary>Etap pobrania wydania z GitHuba (procent z rozmiaru z API).</summary>
    public const string StageDownloadingGitHub = "Pobieranie (z GitHuba)";

    /// <summary>Etap sprawdzenia skrótu SHA256 i podpisu wydawcy.</summary>
    public const string StageCheckingSignature = "Sprawdzanie podpisu";

    /// <summary>Brak sieci przy API albo pobraniu (spec 3.7); na pierwszej uruchomionej pozycji kończy serię.</summary>
    public const string GitHubNoConnection = "Nie wykonano – brak połączenia z GitHubem";

    /// <summary>
    /// Limit <c>RustDeskSource.DownloadTimeout</c> (10 min) przy pobieraniu pliku – wolne albo zerwane łącze (M2 final review); seria i tak
    /// się kończy jak przy braku sieci (decyzja planu 4). Limit przy API (15 s) zostaje <see cref="GitHubNoConnection"/>.
    /// </summary>
    public const string GitHubDownloadTimedOut = "Nie wykonano – pobieranie z GitHuba przekroczyło limit 10 min (wolne lub zerwane łącze)";

    /// <summary>API odpowiedziało 403/429 (spec 3.7); seria idzie dalej.</summary>
    public const string GitHubRateLimited = "Nie wykonano – GitHub chwilowo odmawia (limit zapytań), spróbuj za godzinę";

    public static string GitHubHttpError(int status)
        => "Nie wykonano – GitHub odpowiedział kodem " + status.ToString(CultureInfo.InvariantCulture);

    /// <summary>Wydanie bez assetu <c>rustdesk-&lt;tag&gt;-x86_64.msi</c> (spec 3.1, dosłownie).</summary>
    public const string GitHubNoMsi = "Nie wykonano – na GitHubie RustDesk nie ma instalatora MSI dla tej wersji";

    public static string ReleaseRefused(string reason)
        => "Nie wykonano – wydanie RustDesk na GitHubie nie przeszło sprawdzenia (" + reason + ")";

    /// <summary>Porażka sprawdzenia pobranego pliku (spec 3.3): plik usunięty, nic nie uruchomione.</summary>
    public static string DownloadCheckFailed(string reason)
        => "Nie wykonano – pobrany plik nie przeszedł sprawdzenia (" + reason + ")";

    /// <summary>Błąd zapisu pobieranego pliku (pełny dysk, plik istnieje) – porażka pozycji, nie brak sieci (ruling, Task 3).</summary>
    public static string DownloadWriteFailed(string message)
        => "Nie wykonano – nie udało się zapisać pobranego pliku (" + message + ")";

    /// <summary>Powód <see cref="ReleaseRefused"/>: <c>browser_download_url</c> poza <c>RustDeskSource.IsAllowedDownloadUrl</c>.</summary>
    public const string ReasonForeignDownloadUrl = "adres pobrania spoza GitHuba RustDesk";

    public static string ReasonRedirect(string? host) => "przekierowanie na nieznany serwer" + (string.IsNullOrEmpty(host) ? string.Empty : " " + host);

    public const string ReasonSizeMismatch = "rozmiar inny niż w GitHubie";

    public const string ReasonHashMismatch = "skrót SHA256 inny niż w GitHubie";

    public const string ReasonNotSigned = "brak podpisu wydawcy";

    public static string ReasonInvalidSignature(string? detail)
        => "podpis nieważny" + (string.IsNullOrEmpty(detail) ? string.Empty : " (" + detail + ")");

    public static string ReasonOtherPublisher(string? subject)
        => "podpis innego wydawcy: " + (string.IsNullOrEmpty(subject) ? "(brak podmiotu)" : subject);

    /// <summary>msiexec zakończył się kodem innym niż 0/3010/1641 (spec 3.4), z logiem MSI.</summary>
    public static string MsiFailed(int code, string? logPath)
        => WithMsiLog("Nie wykonano – instalator MSI zakończył się kodem " + code.ToString(CultureInfo.InvariantCulture) + ".", logPath);

    /// <summary>Nie udało się przygotować ścieżki logu MSI (odpowiednik <see cref="LogPathFailed"/> dla winget) – msiexec nie startuje.</summary>
    public static string MsiLogPathFailed(string message) => "Nie wykonano – nie udało się przygotować pliku logu MSI: " + message;

    public static string MsiStartFailed(string message) => "Nie wykonano – nie udało się uruchomić instalatora MSI: " + message;

    public const string MsiNotVisibleAfterSuccess = "instalator MSI zgłosił sukces, ale programu nie widać na liście zainstalowanych";

    public static string MsiNotVisibleReadFailed(string message)
        => "instalator MSI zgłosił sukces, ale nie udało się odczytać listy zainstalowanych: " + message;

    /// <summary>Tekst z dopisaną ścieżką logu MSI (gdy jest).</summary>
    public static string WithMsiLog(string text, string? logPath)
        => logPath is null ? text : text.TrimEnd() + " Log MSI: " + logPath;

    /// <summary>Pozostałe pozycje, gdy pierwsza uruchomiona pozycja z GitHuba nie miała połączenia.</summary>
    public const string SkippedNoInternet = "Nie wykonano – brak połączenia z internetem";

    /// <summary>Pozycja z GitHuba w serii bez usług GitHuba (np. CLI bez składania pobierania).</summary>
    public const string GitHubUnavailable = "Nie wykonano – instalacja z GitHuba nie jest dostępna w tym uruchomieniu";

    /// <summary>Ostrzeżenie wyniku, gdy pobranego pliku nie udało się usunąć.</summary>
    public static string DownloadNotDeleted(string path) => "Nie udało się usunąć pobranego pliku " + path;

    /// <summary>Komunikat zdarzenia journala <c>install-app</c> pozycji z GitHuba: <c>github: &lt;repo&gt;[, wersja &lt;tag&gt;][, log: &lt;ścieżka&gt;]</c>.</summary>
    public static string GitHubJournalMessage(string repository, string? version, string? logPath)
        => "github: " + repository
            + (version is null ? string.Empty : ", wersja " + version)
            + (logPath is null ? string.Empty : ", log: " + logPath);

    // --- strona „Instalacja” ---

    /// <summary>Pozycja menu i tytuł strony (spec 6).</summary>
    public const string PageTitle = "Instalacja";

    public const string RefreshButton = "Odśwież";

    public const string Reading = "Odczyt listy w toku";

    public const string NotRead = "Lista nie została jeszcze odczytana";

    /// <summary>Podtytuł, gdy winget nie działa (brak App Installera, <c>--version</c> nie działa, błąd odczytu listy).</summary>
    public const string PageUnavailable = "Źródło: winget (Microsoft) · niedostępne";

    public const string SearchPlaceholder = "Szukaj aplikacji, np. VLC";

    /// <summary>Podpowiedź pola, gdy winget nie działa: filtr listy działa, wyszukiwanie w winget nie.</summary>
    public const string SearchPlaceholderListOnly = "Szukaj aplikacji na liście, np. VLC";

    public const string SearchButton = "Szukaj też w winget";

    public const string SelectedGroupName = "Zaznaczone";

    public const string SelectedEmpty = "Nic nie jest zaznaczone. Zaznacz aplikacje na liście albo w wynikach winget.";

    public static string CatalogSection(int count) => "Z listy (" + count.ToString(CultureInfo.InvariantCulture) + ")";

    public static string WingetSection(int count) => "W winget (" + count.ToString(CultureInfo.InvariantCulture) + ")";

    public const string WingetSectionNote = "Pakiety spoza listy DSOptimizera – pobierane z oficjalnego źródła winget (Microsoft), bez wcześniejszego sprawdzenia przez autora programu.";

    public const string NoCatalogMatch = "Brak na liście. Naciśnij Enter, żeby poszukać w winget.";

    public const string NoCatalogMatchWingetDown = "Brak na liście. Winget nie działa – wyszukiwanie w winget niedostępne.";

    /// <summary>Brak dopasowań przy odmowie konta (inne konto, niepewny użytkownik): winget działa, tylko nie na tym koncie (Ruling R4).</summary>
    public const string NoCatalogMatchPlain = "Brak na liście.";

    /// <summary>
    /// CLI: <c>winget:</c> z aliasem pozycji z GitHuba (<see cref="GitHubSources.WingetAliases"/>) to nie druga droga instalacji
    /// (spec 11.2, Ruling R5) – błędne argumenty z podpowiedzią właściwego id.
    /// </summary>
    public static string CliGitHubAlias(string title, string listId) => title + " instaluje się z listy: użyj " + listId + ".";

    public static string OnListNote(string title) => title + " jest na liście – wyżej";

    public static string OnListNoteGitHub(string title) => title + " jest na liście – wyżej (instalowany z GitHuba)";

    // --- walidacja zapytania (WingetArguments.TryValidateQuery) ---

    public const string QueryEmpty = "Wpisz, czego szukać.";

    public static string QueryTooLong(int max) => "Zapytanie może mieć najwyżej " + max.ToString(CultureInfo.InvariantCulture) + " znaków.";

    public const string QueryInvalidChars = "Zapytanie nie może zawierać cudzysłowu ani znaków sterujących.";

    /// <summary>M6 final review: zapytanie od „-” winget mógłby wziąć za opcję (obejście stałego <c>--source winget</c>).</summary>
    public const string QueryLeadingDash = "Zapytanie nie może zaczynać się od „-”.";

    public const string Searching = "Szukanie w winget";

    /// <summary>Grupa wyników wyszukiwania na końcu listy kategorii (spec 6, wariant A).</summary>
    public const string SearchGroupName = "Wyniki wyszukiwania";

    public const string InstalledStatus = "Zainstalowana";

    public const string WingetProblemTitle = "Winget nie działa na tym komputerze";

    /// <summary>Przycisk przy <see cref="WingetState.NotRegistered"/>/<see cref="WingetState.NotWorking"/> (spec 2): kategoria „Naprawy”, pozycja <c>repair.winget</c>.</summary>
    public const string RepairWingetButton = "Przejdź do: Napraw winget";

    /// <summary>„Zatrzymaj” okna serii instalacji (spec 5.2): bieżąca instalacja zawsze się kończy.</summary>
    public const string StopText = "Zatrzymaj po tej aplikacji";

    public const string StoppingText = "Zatrzymam po bieżącej aplikacji…";

    public const string StopNote = "Bieżąca instalacja zawsze się kończy.";

    /// <summary>
    /// Podtytuł strony po odczycie: źródło, liczba pozycji listy i zainstalowanych. <paramref name="withGitHub"/> = lista ma pozycję
    /// z GitHuba (RustDesk) – wtedy oba źródła (M6 review Tasku 5).
    /// </summary>
    public static string PageSummary(int total, int installed, bool withGitHub)
        => (withGitHub ? "Źródła: winget (Microsoft) i GitHub (RustDesk) · " : "Źródło: winget (Microsoft) · ") + Counts(total, installed);

    /// <summary>
    /// Podtytuł, gdy winget nie działa, ale lista ma pozycję z GitHuba do zaznaczenia (decyzja planu 1): GitHub działa, winget nie
    /// (M6 review Tasku 5). Bez pozycji z GitHuba i przy błędzie odczytu listy – <see cref="PageUnavailable"/>.
    /// </summary>
    public static string PageWingetUnavailable(int total, int installed)
        => "Źródła: GitHub (RustDesk) · winget (Microsoft) niedostępny · " + Counts(total, installed);

    private static string Counts(int total, int installed)
    {
        if (total < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(total), total, null);
        }

        if (installed < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(installed), installed, null);
        }

        return Gui.GuiText.Count(total, "aplikacja", "aplikacje", "aplikacji")
            + " · zainstalowane: " + installed.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Pierwsza linia paska serii; przy 0 podpowiedź, jak zacząć.</summary>
    public static string SelectionBarTitle(int selected)
    {
        if (selected < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(selected), selected, null);
        }

        return selected == 0
            ? "Kliknij dwukrotnie wiersz albo pole wyboru, żeby dodać do instalacji."
            : "Zaznaczono " + Gui.GuiText.Count(selected, "aplikację", "aplikacje", "aplikacji");
    }

    /// <summary>
    /// Druga linia paska serii: z ilu grup (kategorie i wyniki wyszukiwania) i skąd pobierane – winget, GitHub RustDesk albo oba;
    /// pusta bez zaznaczenia. Zaznaczenie bez żadnego źródła = <see cref="ArgumentException"/>.
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

        var source = winget && gitHub ? "z winget (Microsoft) i z GitHuba RustDesk" : winget ? "z winget (Microsoft)" : "z GitHuba RustDesk";
        return "Z " + groups.ToString(CultureInfo.InvariantCulture) + " kategorii, pobierane " + source + ". Wymaga internetu.";
    }

    /// <summary>Pierwsza pozycja listy kategorii (D1, Ruling P3): cała lista pogrupowana nagłówkami kategorii.</summary>
    public const string AllCategoriesName = "Wszystkie";

    /// <summary>Źródło wiersza pozycji z GitHuba drobnym drukiem (zamiast identyfikatora winget).</summary>
    public static string GitHubLabel(string repository) => "GitHub: " + repository;

    /// <summary>Dopisek komunikatu „winget nie działa” nad listą, gdy lista ma pozycję z GitHuba (decyzja planu 1).</summary>
    public const string GitHubStillAvailable = "RustDesk (z GitHuba) można zainstalować także bez winget.";

    /// <summary>Link w komunikacie „winget nie działa” (M5): kategoria pozycji z GitHuba i jej wiersz na liście.</summary>
    public const string ShowRustDeskLink = "Pokaż RustDesk";

    /// <summary>Przycisk serii (pasek strony i okno potwierdzenia).</summary>
    public static string InstallButton(int count) => "Zainstaluj (" + count.ToString(CultureInfo.InvariantCulture) + ")";

    /// <summary>Notatka wiersza wyszukiwania z Id obciętym „…”, pustym albo przesuniętym znakiem szerokim (spec 7, R8).</summary>
    public const string TruncatedIdNote = "nazwa pakietu skrócona – zawęź wyszukiwanie";

    public const string SearchBusy = "Trwa inna operacja – spróbuj za chwilę.";

    public const string InstallBusy = "Trwa inna operacja – nic nie zmieniono.";

    public const string SearchNoResults = "Nic nie znaleziono w winget.";

    public const string SearchNoNetwork = "Brak połączenia z internetem albo ze źródłem winget.";

    /// <summary>winget coś znalazł, ale wszystko to pozycje listy (pokazane raz, w swojej kategorii).</summary>
    public const string SearchAllOnList = "Znalezione aplikacje są na liście – wyżej.";

    public static string SearchFailed(int exitCode) => "Wyszukiwanie nie powiodło się (kod " + WingetExitCodes.Hex(exitCode) + ").";

    public static string SearchTimedOut(TimeSpan limit)
        => "winget nie odpowiedział w " + Math.Max(1, (int)Math.Ceiling(limit.TotalMinutes)).ToString(CultureInfo.InvariantCulture) + " min.";

    public static string SearchStartFailed(string message) => "Nie udało się uruchomić winget: " + message;

    /// <summary>Do serii trafiła pozycja z Id, którego nie da się podać w <c>--id --exact</c> (strażnik pola wyboru).</summary>
    public const string InvalidTargetRefusal = "Pozycji ze skróconą nazwą pakietu nie da się zainstalować – nic nie zmieniono.";

    // --- okno potwierdzenia i seria ---

    public const string ConfirmTitle = "Zainstaluj zaznaczone";

    /// <summary>Ostrzeżenie serii, dosłownie ze spec 5.4.</summary>
    public const string SeriesWarning =
        "Aplikacje zostaną pobrane z oficjalnego źródła winget (Microsoft) i zainstalowane bez okien instalatorów. Wymaga internetu. "
        + "Instalacja może potrwać kilka–kilkanaście minut.";

    /// <summary>
    /// Ostrzeżenie serii z pozycją z GitHuba (decyzja planu, Task 5; skrócone rulingiem M4): GitHub i PURSLANE podają już
    /// fakt „Źródło” i ostrzeżenie pozycji RustDesk (spec 4), a „Wymaga internetu.” mówi w serii mieszanej <see cref="SeriesWarning"/>.
    /// </summary>
    public const string SeriesWarningGitHub =
        "RustDesk zostanie pobrany i zainstalowany bez okna instalatora dopiero po sprawdzeniu skrótu SHA256 i podpisu wydawcy.";

    /// <summary>Ostrzeżenie serii samego RustDeska (bez pozycji winget): <see cref="SeriesWarningGitHub"/> + „Wymaga internetu.”.</summary>
    public const string SeriesWarningGitHubOnly = SeriesWarningGitHub + " Wymaga internetu.";

    public const string SourceFact = "Źródło";

    public const string SourceValue = "winget (Microsoft)";

    /// <summary>Fakt „Źródło” serii z pozycją z GitHuba (seria mieszana: oba połączone „; ”).</summary>
    public const string GitHubSourceValue = "GitHub RustDesk (rustdesk/rustdesk), podpis PURSLANE";

    /// <summary>Fakt techniczny pozycji z GitHuba w oknie potwierdzenia (zamiast <see cref="WingetIdFact"/>).</summary>
    public const string GitHubRepositoryFact = "Repozytorium GitHub";

    public const string RestorePointFact = "Punkt przywracania";

    public const string RestorePointValue = "nie – instalację cofa się usunięciem na stronie „Aplikacje”";

    public const string RestartFact = "Restart";

    public const string RestartValue = "tylko gdy instalator go zgłosi";

    public const string WingetIdFact = "Identyfikator winget";

    /// <summary>Opis pozycji spoza listy w oknie potwierdzenia.</summary>
    public const string SearchItemSummary = "Wynik wyszukiwania w winget (spoza listy).";

    public const string NotDone = "Nie wykonano";

    public const string NotConfirmedStatus = "Nie potwierdzono";

    /// <summary>Status pozycji w oknie serii i w dzienniku.</summary>
    public static string OutcomeName(TweakOutcome outcome) => outcome switch
    {
        TweakOutcome.Installed => "Zainstalowano",
        TweakOutcome.AlreadyInstalled => "Już zainstalowane",
        TweakOutcome.InstalledUnverified => "Zainstalowano (zgłoszone przez winget)",
        TweakOutcome.NotConfirmed => NotConfirmedStatus,
        _ => NotDone,
    };

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
                return capitalize ? char.ToUpper(rest[0], CultureInfo.GetCultureInfo("pl-PL")) + rest.Substring(1) : rest;
            }
        }

        return message;
    }

    /// <summary>Blok restartu w wyniku serii instalacji.</summary>
    public static string RebootText(int count)
        => Gui.GuiText.Count(count, "aplikacja potrzebuje", "aplikacje potrzebują", "aplikacji potrzebuje") + " ponownego uruchomienia komputera.";

    /// <summary>Podsumowanie serii: „N z M zainstalowane”.</summary>
    public static string BatchSummary(int installed, int total)
        => string.Format(CultureInfo.InvariantCulture, "{0} z {1} zainstalowane", installed, total);
}

