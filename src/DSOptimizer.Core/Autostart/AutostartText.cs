using System;
using System.Collections.Generic;
using System.Linq;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Localization;

namespace DSOptimizer.Core.Autostart;

/// <summary>Teksty modułu Autostart (w bieżącym języku, <see cref="L"/>).</summary>
public static class AutostartText
{
    /// <summary>Nazwa wiersza dla wartości Run o pustej nazwie.</summary>
    public static string NoName => L.T("(bez nazwy)");

    public static string NoMeasurement => L.T("Brak pomiaru Windows – w kolumnie pomiaru będzie „nie zmierzono”.");

    public static string EnvironmentUnreadable(string key, string error) => L.F("Nie udało się odczytać zmiennych konta z {0}: {1}", key, error);

    public static string SourceUnreadable(string where, string error) => L.F("Nie udało się odczytać {0}: {1}", where, error);

    public static string ValueSkipped(string key, string name) => L.F("Pominięto wartość {1} w {0}: nieobsługiwany typ.", key, name);

    public static string MeasurementFileSkipped(string file) => L.F("Pominięto plik pomiaru Windows {0}: błędny format.", file);

    public static string StoreUnreadable(string error) => L.F("Nie udało się odczytać aplikacji ze Sklepu: {0}", error);

    /// <summary>Opis pozycji spoza bazy.</summary>
    public static string NotAssessed => L.T("Bez oceny – brak tej pozycji w bazie programu.");

    /// <summary>Opis pozycji bez pliku (spoza bazy).</summary>
    public static string MissingFileSummary => L.T("Plik z polecenia nie istnieje, więc ta pozycja niczego nie uruchamia. Wyłączenie niczego nie psuje.");

    /// <summary>Podpowiedź kłódki pozycji chronionej.</summary>
    public static string ProtectedTooltip => L.T("Chroniona – DSOptimizer jej nie wyłącza");

    /// <summary>Wynik wyłączenia potwierdzony świeżym odczytem.</summary>
    public static string Disabled => L.T("Wyłączona – zadziała od następnego logowania.");

    /// <summary>Wynik włączenia potwierdzony świeżym odczytem.</summary>
    public static string Enabled => L.T("Włączona – zadziała od następnego logowania.");

    public static string AlreadyDisabled => L.T("Była już wyłączona – nic nie zmieniono.");

    public static string AlreadyEnabled => L.T("Była już włączona – nic nie zmieniono.");

    /// <summary>Przełączenie w drugą stronę cofnęło aktywny wpis tej pozycji (bez nowego wpisu).</summary>
    public static string UndoneByToggle => L.T("Cofnięto wcześniejszą zmianę z Historii zmian – zadziała od następnego logowania.");

    /// <summary>Cofnięcie wpisu z Historii zmian potwierdzone świeżym odczytem.</summary>
    public static string UndoneFromHistory => L.T("Przywrócono stan sprzed zmiany – zadziała od następnego logowania.");

    /// <summary>Cofnięcie pozycji bez aktywnego wpisu autostartu (także wpis nieznany albo już cofnięty).</summary>
    public static string NoActiveEntry => L.T("Brak aktywnej zmiany tej pozycji w Historii zmian.");

    public static string NotConfirmed => L.T("Odczyt po zapisie nie potwierdził zmiany – przywrócono poprzedni stan.");

    public static string RollbackIncomplete => L.T("Odczyt po zapisie nie potwierdził zmiany, a poprzedniego stanu nie udało się przywrócić.");

    /// <summary>Zdarzenie wpisu, którego zmianę cofnięto poza programem (np. Menedżerem zadań).</summary>
    public static string ChangedOutside => L.T("Zmieniono poza programem.");

    public static string JournalFailed(string error) => L.F("Nie udało się zapisać journala – nic nie zmieniono: {0}", error);

    /// <summary>Świeży odczyt przed zapisem pokazał format tylko do odczytu albo zapis od zera bez dowodu.</summary>
    public static string FormatChanged => L.T("Zapis stanu zmienił się od odczytu listy – nic nie zmieniono. Odśwież listę.");

    /// <summary>Powód „tylko do odczytu”; null, gdy pozycję można przełączać.</summary>
    public static string? Blocked(AutostartBlocker blocker) => blocker switch
    {
        AutostartBlocker.None => null,
        AutostartBlocker.Protected => ProtectedTooltip,
        AutostartBlocker.UserUncertain => L.T("Nie udało się ustalić zalogowanego użytkownika."),
        AutostartBlocker.UserHiveMissing => L.T("Ustawienia tego konta są niedostępne – uruchom DSOptimizer na zalogowanym koncie."),
        AutostartBlocker.Unreadable => L.T("Nie udało się odczytać stanu – DSOptimizer tej pozycji nie zmienia."),
        AutostartBlocker.UnknownFormat => L.T("Nieznany zapis stanu – DSOptimizer tej pozycji nie zmienia."),
        AutostartBlocker.TaskStateNotSupported => L.T("Stan ustawiony zasadami albo nieznany – DSOptimizer tej pozycji nie zmienia."),
        AutostartBlocker.WriteFromZeroNotMeasured => L.T("Tej pozycji nikt jeszcze nie przełączał – DSOptimizer jej nie zmienia, przełączysz ją Menedżerem zadań."),
        AutostartBlocker.NoName => L.T("Wpis bez nazwy – DSOptimizer go nie zmienia."),
        _ => throw new ArgumentOutOfRangeException(nameof(blocker), blocker, null),
    };

    /// <summary>Zakres pozycji w wierszu: konto zalogowanego użytkownika, cały komputer albo aplikacja ze Sklepu.</summary>
    public static string Scope(AutostartScope scope) => scope switch
    {
        AutostartScope.User => L.T("to konto"),
        AutostartScope.Machine => L.T("wszystkie konta"),
        AutostartScope.Store => L.T("Sklep"),
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null),
    };

    // --- Strona „Autostart” ---

    public static string PageTitle => L.T("Autostart");

    public static string GroupName(AutostartGroup group) => group switch
    {
        AutostartGroup.Enabled => L.T("Włączone"),
        AutostartGroup.Disabled => L.T("Wyłączone"),
        AutostartGroup.MissingFile => L.T("Bez pliku"),
        _ => throw new ArgumentOutOfRangeException(nameof(group), group, null),
    };

    /// <summary>
    /// Podtytuł strony: liczba włączonych i data pomiaru Windows w czasie lokalnym. Po polsku format zaakceptowanego renderu
    /// („02.11, 08:14”), po angielsku pełna data programu (<see cref="L.DateTimePattern"/>), bo „02/11” czyta się jak 11 lutego.
    /// </summary>
    public static string Subtitle(int enabled, DateTimeOffset? measuredAt)
    {
        if (enabled < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(enabled), enabled, null);
        }

        var count = L.P("{0} włączona", "{0} włączone", "{0} włączonych", enabled);
        if (measuredAt is not { } at)
        {
            return L.F("{0} · brak pomiaru Windows", count);
        }

        var local = at.ToLocalTime();
        var date = L.Current == UiLanguage.English ? local.ToString(L.DateTimePattern, L.Culture) : local.ToString("dd.MM, HH:mm", L.Culture);
        return L.F("{0} · pomiar Windows {1}", count, date);
    }

    public static string Reading => L.T("Odczyt listy w toku");

    public static string NotRead => L.T("Lista nie została jeszcze odczytana");

    /// <summary>Błąd odczytu strony (limit czasu albo wyjątek czytnika); szczegół jest w dzienniku.</summary>
    public static string ReadFailed => L.T("Nie udało się odczytać listy autostartu.");

    /// <summary>Nazwa odczytu w linii dziennika o limicie czasu.</summary>
    public static string ReadPartName => L.T("lista autostartu");

    /// <summary>Stała linia pod listą (w podtytule się nie mieści).</summary>
    public static string OtherAccountsNote => L.T("Pozycje innych kont nie są pokazywane.");

    public static string SearchPlaceholder => L.T("Szukaj po nazwie, wydawcy albo poleceniu");

    /// <summary>
    /// Czas procesora w wierszu: sekundy z jednym miejscem po przecinku w kulturze języka („4,8 s” / „4.8 s”), zaokrąglone połówką w górę;
    /// poniżej 0,05 s – „&lt; 0,1 s”.
    /// </summary>
    public static string CpuSeconds(long microseconds)
    {
        if (microseconds < 50_000)
        {
            return string.Format(L.Culture, "< {0:0.0} s", 0.1m);
        }

        var tenths = (microseconds + 50_000) / 100_000;
        return string.Format(L.Culture, "{0:0.0} s", tenths / 10m);
    }

    public static string NotMeasured => L.T("nie zmierzono");

    public static string FileMissing => L.T("plik nie istnieje");

    public static string Impact(StartupImpactLevel level) => level switch
    {
        StartupImpactLevel.Low => L.T("Niski (progi Windows)"),
        StartupImpactLevel.Medium => L.T("Średni (progi Windows)"),
        StartupImpactLevel.High => L.T("Wysoki (progi Windows)"),
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, null),
    };

    /// <summary>Dopisek pod pomiarem: liczba to czas procesora, nie czas rozruchu (bez obietnic przyspieszenia).</summary>
    public static string CpuNote => L.T("To czas procesora zużyty przy starcie, nie czas rozruchu komputera.");

    public static string NotInMeasurement => L.T("Tego programu nie ma w ostatnim pomiarze rozruchu Windows.");

    public static string FileMissingNote => L.T("Plik nie istnieje, więc Windows nie miał czego zmierzyć.");

    /// <summary>Dopisek daty pomiaru pozycji wyłączonej (liczby z ostatniego rozruchu, w którym startowała).</summary>
    public static string LastStart => L.T(" (ostatni start)");

    /// <summary>Stan pozycji: „Włączona”, „Wyłączona 02.11.2026” (data wyłączenia, czas lokalny), „Wyłączona”, „Stan nieznany”.</summary>
    public static string State(AutostartRow row)
    {
        if (row is null)
        {
            throw new ArgumentNullException(nameof(row));
        }

        return row.State switch
        {
            AutostartState.Enabled => L.T("Włączona"),
            AutostartState.Disabled => row.Entry.Switch.DisabledAt is { } at
                ? L.F("Wyłączona {0}", at.ToLocalTime().ToString(L.DatePattern, L.Culture))
                : L.T("Wyłączona"),
            _ => L.T("Stan nieznany"),
        };
    }

    /// <summary>
    /// Druga linia wiersza pod wydawcą; null = bez linii. Bez pliku: stan (grupa „Bez pliku” go nie mówi), włączona – że nic nie startuje.
    /// Wiersz bez pola wyboru z blokadą inną niż ochrona (kłódka ma własną podpowiedź) zawsze pokazuje powód – przy braku pliku obok stanu,
    /// jako osobne zdanie. Bez blokady i z plikiem: data wyłączenia albo nic.
    /// </summary>
    public static string? RowNote(AutostartRow row)
    {
        if (row is null)
        {
            throw new ArgumentNullException(nameof(row));
        }

        var reason = row.Blocker is not (AutostartBlocker.None or AutostartBlocker.Protected) ? row.BlockedReason : null;
        if (row.MissingFile)
        {
            var note = row.State == AutostartState.Enabled ? L.T("Włączona, ale pliku nie ma – nic nie startuje") : State(row);
            return reason is null ? note : L.F("{0}. {1}", note, reason);
        }

        if (reason is not null)
        {
            return reason;
        }

        return row.State == AutostartState.Disabled ? State(row) : null;
    }

    /// <summary>Ocena w panelu; „Sugerowana – plik nie istnieje” tylko dla sugerowanej pozycji spoza bazy z pewnym brakiem pliku.</summary>
    public static string Verdict(AutostartRow row)
    {
        if (row is null)
        {
            throw new ArgumentNullException(nameof(row));
        }

        return row.Verdict switch
        {
            AutostartVerdict.Protected => L.T("Chroniona"),
            AutostartVerdict.CanDisable => L.T("Można wyłączyć"),
            _ => row.MissingFile && row.Suggested ? L.T("Sugerowana – plik nie istnieje") : L.T("Bez oceny"),
        };
    }

    /// <summary>
    /// Źródło oceny: „Źródło: ” i domeny adresów https wpisu bazy (bez powtórzeń, w kolejności); wpis tylko z dokumentem pomiaru –
    /// „Źródło: pomiar DSOptimizera”; nic z tego – null.
    /// </summary>
    public static string? SourceLine(IReadOnlyList<string> sources)
    {
        if (sources is null)
        {
            throw new ArgumentNullException(nameof(sources));
        }

        var hosts = new List<string>();
        foreach (var source in sources)
        {
            if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
                && !hosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
            {
                hosts.Add(uri.Host);
            }
        }

        if (hosts.Count > 0)
        {
            return L.F("Źródło: {0}", string.Join(", ", hosts));
        }

        return sources.Any(source => source is not null && source.StartsWith("docs/zrodla/", StringComparison.Ordinal))
            ? L.T("Źródło: pomiar DSOptimizera")
            : null;
    }

    /// <summary>
    /// Miejsce pozycji w panelu: klucz Run w zapisie dla człowieka (gałąź konta jako <c>HKEY_CURRENT_USER</c> zalogowanego, maszyna jako
    /// <c>HKEY_LOCAL_MACHINE</c>), rozwinięty folder Autostart albo rodzina pakietu aplikacji ze Sklepu.
    /// </summary>
    public static string Location(AutostartEntry entry)
    {
        if (entry is null)
        {
            throw new ArgumentNullException(nameof(entry));
        }

        // Nazwa wartości Run nie pojawia się nigdzie indziej w panelu (nazwa wiersza pochodzi z opisu pliku).
        var valueName = string.IsNullOrEmpty(entry.EntryName) ? NoName : entry.EntryName;
        return entry.Source switch
        {
            AutostartSource.RunUser => L.F("Rejestr: {0}, wartość „{1}”", ReadableKey(entry.Location, @"HKU\", "HKEY_CURRENT_USER", skipSegment: true), valueName),
            AutostartSource.RunMachine or AutostartSource.Run32
                => L.F("Rejestr: {0}, wartość „{1}”", ReadableKey(entry.Location, @"HKLM\", "HKEY_LOCAL_MACHINE", skipSegment: false), valueName),
            AutostartSource.StartupTask => L.F("Aplikacja ze Sklepu: {0}", entry.PackageFamily ?? entry.Location),
            _ => entry.Location,
        };
    }

    /// <summary>Konto w panelu: pozycja konta i Sklepu – nazwa zalogowanego konta, pozycja maszyny – wszystkie konta.</summary>
    public static string Account(AutostartScope scope, string accountName) => scope switch
    {
        AutostartScope.User or AutostartScope.Store => L.F("{0} (zalogowane konto)", accountName),
        AutostartScope.Machine => L.T("Wszystkie konta na tym komputerze"),
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null),
    };

    /// <summary>Tekst „Kopiuj nazwę”: nazwa, wydawca i plik, każde w osobnej linii; puste pominięte.</summary>
    public static string CopyNameText(AutostartRow row)
    {
        if (row is null)
        {
            throw new ArgumentNullException(nameof(row));
        }

        var lines = new[] { row.Entry.Name, row.Entry.Publisher, row.Entry.FilePath }.Where(line => !string.IsNullOrWhiteSpace(line));
        return string.Join("\r\n", lines);
    }

    public static string SelectionBarTitle(int selected) => selected <= 0
        ? L.T("Kliknij dwukrotnie wiersz albo pole wyboru, żeby zaznaczyć.")
        : L.P("Zaznaczono {0} pozycję", "Zaznaczono {0} pozycje", "Zaznaczono {0} pozycji", selected);

    public static string SelectionBarDetails => L.T("Od następnego logowania. Cofniesz w Historii zmian.");

    /// <summary>Linia zaznaczenia w panelu szczegółów.</summary>
    public static string SelectedLine => L.T("Zaznaczona");

    public static string DisableButton(int count) => L.F("Wyłącz ({0})", count);

    public static string EnableButton(int count) => L.F("Włącz ({0})", count);

    /// <summary>Linia zaznaczenia w panelu, gdy wiersz można zaznaczyć, a nie jest zaznaczony (ten sam tekst co w Aplikacjach).</summary>
    public static string NotSelectedLine => L.T("Niezaznaczona – kliknij dwukrotnie wiersz, żeby dodać");

    /// <summary>Opis licznika w menu (podpowiedź, czytnik ekranu): „16 pozycji”.</summary>
    public static string CounterDescription(int count) => L.P("{0} pozycja", "{0} pozycje", "{0} pozycji", count);

    /// <summary>Nazwa wyszukiwarki strony dla czytnika ekranu.</summary>
    public static string SearchName => L.T("Szukaj w autostarcie");

    /// <summary>Nazwa przełącznika grup dla czytnika ekranu.</summary>
    public static string GroupsName => L.T("Grupy autostartu");

    public static string EmptyGroup => L.T("Brak pozycji w tej grupie.");

    public static string EmptySearch => L.T("Żadna pozycja w tej grupie nie pasuje do wyszukiwania.");

    // --- Panel szczegółów ---

    public static string CopyNameButton => L.T("Kopiuj nazwę");

    public static string NameCopied => L.T("Skopiowano nazwę do schowka.");

    public static string NameCopyFailed(string error) => L.F("Nie udało się skopiować nazwy do schowka: {0}", error);

    public static string MeasurementHeader => L.T("Pomiar Windows");

    public static string CpuLabel => L.T("Procesor");

    public static string DiskLabel => L.T("Odczyt z dysku");

    public static string ImpactLabel => L.T("Wpływ");

    public static string MeasuredAtLabel => L.T("Pomiar");

    public static string VerdictLabel => L.T("Ocena");

    public static string LocationLabel => L.T("Miejsce");

    public static string UnknownPublisher => L.T("nieznany");

    public static string UnknownAccount => L.T("nieznane");

    /// <summary>Data i godzina pomiaru w czasie lokalnym („02.11.2026, 08:14”), przy pozycji wyłączonej z dopiskiem „ (ostatni start)”.</summary>
    public static string MeasuredAt(StartupMeasurement measurement)
    {
        if (measurement is null)
        {
            throw new ArgumentNullException(nameof(measurement));
        }

        var at = measurement.MeasuredAt.ToLocalTime().ToString(L.DatePattern + ", HH:mm", L.Culture);
        return measurement.FromEarlierStart ? at + LastStart : at;
    }

    /// <summary>Linia „Szczegółów technicznych” z poleceniem pozycji.</summary>
    public static string CommandLine(string command) => L.F("Polecenie: {0}", command);

    /// <summary>Linia „Szczegółów technicznych” z plikiem pozycji; null = pliku nie da się ustalić.</summary>
    public static string FileLine(string? path) => path is null ? L.T("Plik: nie ustalono") : L.F("Plik: {0}", path);

    /// <summary>Komunikat przy starcie okna, gdy wbudowana baza znanych pozycji jest uszkodzona.</summary>
    public static string CatalogInvalid(string reason) => L.F("Wbudowana baza autostartu jest nieprawidłowa:{0}{1}", Environment.NewLine, reason);

    // --- Okno serii i wynik ---

    public static string ConfirmTitle(AutostartState target)
        => target == AutostartState.Disabled ? L.T("Wyłącz w autostarcie") : L.T("Włącz w autostarcie");

    /// <summary>Tytuł okna w trakcie serii.</summary>
    public static string SeriesTitle(AutostartState target)
        => target == AutostartState.Disabled ? L.T("Wyłączanie w autostarcie") : L.T("Włączanie w autostarcie");

    public static string UndoConfirmTitle => L.T("Cofnij zmianę w autostarcie");

    public static string TakesEffectFact => L.T("Działa od");

    public static string TakesEffectValue => L.T("Następnego logowania");

    public static string RestorePointNotNeeded => L.T("Niepotrzebny – zmianę cofniesz w Historii zmian");

    public static string UndoInHistory => L.T("W Historii zmian");

    /// <summary>Uwaga pozycji w oknie potwierdzenia.</summary>
    public static string ItemNote(AutostartState target) => target == AutostartState.Disabled
        ? L.T("Program zostaje zainstalowany, tylko przestaje startować sam.")
        : L.T("Program znowu będzie startował sam po zalogowaniu.");

    /// <summary>
    /// Uwaga pozycji w oknie potwierdzenia z uwzględnieniem pewnego braku pliku: wtedy pozycja niczego nie uruchamia, więc wyłączenie
    /// ma opis pozycji bez pliku, a włączenie mówi, że nic nie wystartuje.
    /// </summary>
    public static string ItemNote(AutostartState target, bool missingFile)
    {
        if (!missingFile)
        {
            return ItemNote(target);
        }

        return target == AutostartState.Disabled
            ? MissingFileSummary
            : L.T("Plik z polecenia nie istnieje, więc po włączeniu nic nie wystartuje.");
    }

    /// <summary>Ostrzeżenie pozycji „wszystkie konta” (HKLM: Run, Run 32-bit, folder wspólny).</summary>
    public static string AllAccountsWarning => L.T("Dotyczy wszystkich kont na tym komputerze.");

    public static string SelectionRefused => L.T("Wśród zaznaczonych jest pozycja, której nie można tak przełączyć – nic nie zmieniono. Odśwież listę.");

    /// <summary>
    /// Status wyniku pozycji (krok okna serii i linia dziennika): sukces nazwany kierunkiem zmiany (także cofnięcie aktywnego wpisu przy
    /// przełączeniu w drugą stronę), „Bez zmian” przy stanie docelowym już na miejscu, „Nie wykonano” przy porażce; reszta – nazwa wyniku.
    /// </summary>
    public static string ResultStatus(TweakOutcome outcome, AutostartState target) => outcome switch
    {
        TweakOutcome.Applied or TweakOutcome.Undone => target == AutostartState.Disabled ? L.T("Wyłączono i sprawdzono") : L.T("Włączono i sprawdzono"),
        TweakOutcome.AlreadyApplied => L.T("Bez zmian"),
        TweakOutcome.Failed or TweakOutcome.FailedRolledBack or TweakOutcome.FailedRollbackIncomplete => L.T("Nie wykonano"),
        _ => GuiText.OutcomeName(outcome),
    };

    /// <summary>Wynik, który naprawdę zmienił stan pozycji (licznik „Wyłączono N z M.”); „było już” się nie liczy.</summary>
    public static bool CountsAsChanged(TweakOutcome outcome) => outcome is TweakOutcome.Applied or TweakOutcome.Undone;

    public static string Summary(AutostartState target, int changed, int total) => target == AutostartState.Disabled
        ? L.F("Wyłączono {0} z {1}.", changed, total)
        : L.F("Włączono {0} z {1}.", changed, total);

    /// <summary>Linia cofnięcia niepełnego (silnik nie daje komunikatu, tylko rekordy, których nie przywrócono).</summary>
    public static string UndoIncomplete(IReadOnlyList<string> notRestored)
        => L.F("Nie udało się w pełni przywrócić stanu sprzed zmiany: {0}", string.Join("; ", notRestored ?? Array.Empty<string>()));

    /// <summary>Klucz rejestru dla człowieka: przedrostek gałęzi zamieniony na pełną nazwę (przy gałęzi konta razem z SID); inny – bez zmian.</summary>
    private static string ReadableKey(string key, string prefix, string root, bool skipSegment)
    {
        if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return key;
        }

        var rest = key.Substring(prefix.Length);
        if (skipSegment)
        {
            var slash = rest.IndexOf('\\');
            rest = slash < 0 ? string.Empty : rest.Substring(slash + 1);
        }

        return rest.Length == 0 ? root : root + @"\" + rest;
    }
}
