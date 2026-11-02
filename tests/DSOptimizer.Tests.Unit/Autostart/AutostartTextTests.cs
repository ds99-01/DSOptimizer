using System;
using System.Linq;
using DSOptimizer.Core.Autostart;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Localization;
using Xunit;

namespace DSOptimizer.Tests.Unit.Autostart;

public class AutostartTextTests
{
    [Fact]
    public void Blocked_NoneNull_EachBlockerText()
    {
        Assert.Null(AutostartText.Blocked(AutostartBlocker.None));
        Assert.Equal("Chroniona – DSOptimizer jej nie wyłącza", AutostartText.Blocked(AutostartBlocker.Protected));
        Assert.Equal(AutostartText.ProtectedTooltip, AutostartText.Blocked(AutostartBlocker.Protected));
        Assert.Equal("Nie udało się ustalić zalogowanego użytkownika.", AutostartText.Blocked(AutostartBlocker.UserUncertain));
        Assert.Equal(
            "Ustawienia tego konta są niedostępne – uruchom DSOptimizer na zalogowanym koncie.",
            AutostartText.Blocked(AutostartBlocker.UserHiveMissing));
        Assert.Equal("Nie udało się odczytać stanu – DSOptimizer tej pozycji nie zmienia.", AutostartText.Blocked(AutostartBlocker.Unreadable));
        Assert.Equal("Nieznany zapis stanu – DSOptimizer tej pozycji nie zmienia.", AutostartText.Blocked(AutostartBlocker.UnknownFormat));
        Assert.Equal(
            "Stan ustawiony zasadami albo nieznany – DSOptimizer tej pozycji nie zmienia.",
            AutostartText.Blocked(AutostartBlocker.TaskStateNotSupported));
        Assert.Equal(
            "Tej pozycji nikt jeszcze nie przełączał – DSOptimizer jej nie zmienia, wyłączysz ją Menedżerem zadań.",
            AutostartText.Blocked(AutostartBlocker.WriteFromZeroNotMeasured));
        Assert.Equal("Wpis bez nazwy – DSOptimizer go nie zmienia.", AutostartText.Blocked(AutostartBlocker.NoName));
        Assert.Throws<ArgumentOutOfRangeException>(() => AutostartText.Blocked((AutostartBlocker)99));
    }

    [Fact]
    public void Blocked_EveryBlockerTranslated()
    {
        using (L.Use(UiLanguage.English))
        {
            var texts = Enum.GetValues(typeof(AutostartBlocker)).Cast<AutostartBlocker>().Where(b => b != AutostartBlocker.None)
                .Select(AutostartText.Blocked).ToList();

            Assert.Equal(texts.Count, texts.Distinct(StringComparer.Ordinal).Count());
            Assert.Equal("Protected – DSOptimizer doesn't disable it", AutostartText.Blocked(AutostartBlocker.Protected));
        }
    }

    [Fact]
    public void Scope_ThreeTexts()
    {
        Assert.Equal("to konto", AutostartText.Scope(AutostartScope.User));
        Assert.Equal("wszystkie konta", AutostartText.Scope(AutostartScope.Machine));
        Assert.Equal("Sklep", AutostartText.Scope(AutostartScope.Store));
        Assert.Throws<ArgumentOutOfRangeException>(() => AutostartText.Scope((AutostartScope)99));
    }

    [Fact]
    public void EngineTexts_Verbatim()
    {
        Assert.Equal("Wyłączona – zadziała od następnego logowania.", AutostartText.Disabled);
        Assert.Equal("Włączona – zadziała od następnego logowania.", AutostartText.Enabled);
        Assert.Equal("Była już wyłączona – nic nie zmieniono.", AutostartText.AlreadyDisabled);
        Assert.Equal("Była już włączona – nic nie zmieniono.", AutostartText.AlreadyEnabled);
        Assert.Equal("Cofnięto wcześniejszą zmianę z Historii zmian – zadziała od następnego logowania.", AutostartText.UndoneByToggle);
        Assert.Equal("Odczyt po zapisie nie potwierdził zmiany – przywrócono poprzedni stan.", AutostartText.NotConfirmed);
        Assert.Equal("Odczyt po zapisie nie potwierdził zmiany, a poprzedniego stanu nie udało się przywrócić.", AutostartText.RollbackIncomplete);
        Assert.Equal("Zmieniono poza programem.", AutostartText.ChangedOutside);
        Assert.Equal("Nie udało się zapisać journala – nic nie zmieniono: dysk", AutostartText.JournalFailed("dysk"));
        Assert.Equal("Zapis stanu zmienił się od odczytu listy – nic nie zmieniono. Odśwież listę.", AutostartText.FormatChanged);
        Assert.Equal("Przywrócono stan sprzed zmiany – zadziała od następnego logowania.", AutostartText.UndoneFromHistory);
        Assert.Equal("Brak aktywnej zmiany tej pozycji w Historii zmian.", AutostartText.NoActiveEntry);
    }

    [Fact]
    public void EngineTexts_Translated()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("Disabled – takes effect at the next sign-in.", AutostartText.Disabled);
            Assert.Equal("Couldn't save the journal – nothing was changed: disk", AutostartText.JournalFailed("disk"));
        }
    }

    [Theory]
    [InlineData(4_800_000L, "4,8 s", "4.8 s")]
    [InlineData(200_000L, "0,2 s", "0.2 s")]
    [InlineData(49_999L, "< 0,1 s", "< 0.1 s")]
    [InlineData(50_000L, "0,1 s", "0.1 s")]
    [InlineData(0L, "< 0,1 s", "< 0.1 s")]
    [InlineData(1_049_999L, "1,0 s", "1.0 s")]
    [InlineData(12_350_000L, "12,4 s", "12.4 s")]
    public void CpuSeconds(long us, string pl, string en)
    {
        Assert.Equal(pl, AutostartText.CpuSeconds(us));
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal(en, AutostartText.CpuSeconds(us));
        }
    }

    [Fact]
    public void Subtitle_PluralAndDatePattern()
    {
        var local = new DateTime(2026, 11, 2, 8, 14, 0, DateTimeKind.Unspecified);
        var at = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)).ToUniversalTime();

        Assert.Equal("1 włączona · pomiar Windows 02.11, 08:14", AutostartText.Subtitle(1, at));
        Assert.Equal("3 włączone · pomiar Windows 02.11, 08:14", AutostartText.Subtitle(3, at));
        Assert.Equal("11 włączonych · pomiar Windows 02.11, 08:14", AutostartText.Subtitle(11, at));
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("1 enabled · Windows measurement 02/11/2026 08:14", AutostartText.Subtitle(1, at));
            Assert.Equal("11 enabled · Windows measurement 02/11/2026 08:14", AutostartText.Subtitle(11, at));
        }
    }

    [Fact]
    public void Subtitle_NoMeasurement()
    {
        Assert.Equal("0 włączonych · brak pomiaru Windows", AutostartText.Subtitle(0, null));
        Assert.Equal("22 włączone · brak pomiaru Windows", AutostartText.Subtitle(22, null));
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("5 enabled · no Windows measurement", AutostartText.Subtitle(5, null));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => AutostartText.Subtitle(-1, null));
    }

    [Fact]
    public void CopyNameText_SkipsEmpty()
    {
        var full = Row(publisher: "Spotify AB", filePath: @"C:\Users\Jan\AppData\Roaming\Spotify\Spotify.exe");
        var bare = Row(publisher: " ", filePath: null);

        Assert.Equal("Spotify\r\nSpotify AB\r\nC:\\Users\\Jan\\AppData\\Roaming\\Spotify\\Spotify.exe", AutostartText.CopyNameText(full));
        Assert.Equal("Spotify", AutostartText.CopyNameText(bare));
        Assert.Throws<ArgumentNullException>(() => AutostartText.CopyNameText(null!));
    }

    [Fact]
    public void SourceLine_HostsOrMeasurement()
    {
        var hosts = new[] { "https://support.spotify.com/a", "https://www.spotify.com/b", "https://SUPPORT.spotify.com/c", "docs/zrodla/2026-11-02-autostart-pomiar.md" };
        var measurement = new[] { "docs/zrodla/2026-11-02-autostart-pomiar.md" };

        Assert.Equal("Źródło: support.spotify.com, www.spotify.com", AutostartText.SourceLine(hosts));
        Assert.Equal("Źródło: pomiar DSOptimizera", AutostartText.SourceLine(measurement));
        Assert.Null(AutostartText.SourceLine(Array.Empty<string>()));
        Assert.Null(AutostartText.SourceLine(new[] { "http://example.com/x", "nie-adres" }));
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("Source: support.spotify.com, www.spotify.com", AutostartText.SourceLine(hosts));
            Assert.Equal("Source: DSOptimizer measurement", AutostartText.SourceLine(measurement));
        }

        Assert.Throws<ArgumentNullException>(() => AutostartText.SourceLine(null!));
    }

    [Fact]
    public void PageTexts_Verbatim()
    {
        Assert.Equal("Autostart", AutostartText.PageTitle);
        Assert.Equal("Włączone", AutostartText.GroupName(AutostartGroup.Enabled));
        Assert.Equal("Wyłączone", AutostartText.GroupName(AutostartGroup.Disabled));
        Assert.Equal("Bez pliku", AutostartText.GroupName(AutostartGroup.MissingFile));
        Assert.Throws<ArgumentOutOfRangeException>(() => AutostartText.GroupName((AutostartGroup)99));
        Assert.Equal("Odczyt listy w toku", AutostartText.Reading);
        Assert.Equal("Lista nie została jeszcze odczytana", AutostartText.NotRead);
        Assert.Equal("Nie udało się odczytać listy autostartu.", AutostartText.ReadFailed);
        Assert.Equal("Pozycje innych kont nie są pokazywane.", AutostartText.OtherAccountsNote);
        Assert.Equal("Szukaj po nazwie, wydawcy albo poleceniu", AutostartText.SearchPlaceholder);
        Assert.Equal("nie zmierzono", AutostartText.NotMeasured);
        Assert.Equal("plik nie istnieje", AutostartText.FileMissing);
        Assert.Equal("Niski (progi Windows)", AutostartText.Impact(StartupImpactLevel.Low));
        Assert.Equal("Średni (progi Windows)", AutostartText.Impact(StartupImpactLevel.Medium));
        Assert.Equal("Wysoki (progi Windows)", AutostartText.Impact(StartupImpactLevel.High));
        Assert.Throws<ArgumentOutOfRangeException>(() => AutostartText.Impact((StartupImpactLevel)99));
        Assert.Equal("To czas procesora zużyty przy starcie, nie czas rozruchu komputera.", AutostartText.CpuNote);
        Assert.Equal("Tego programu nie ma w ostatnim pomiarze rozruchu Windows.", AutostartText.NotInMeasurement);
        Assert.Equal("Plik nie istnieje, więc Windows nie miał czego zmierzyć.", AutostartText.FileMissingNote);
        Assert.Equal(" (ostatni start)", AutostartText.LastStart);
        Assert.Equal("Kliknij dwukrotnie wiersz albo pole wyboru, żeby zaznaczyć.", AutostartText.SelectionBarTitle(0));
        Assert.Equal("Zaznaczono 1 pozycję", AutostartText.SelectionBarTitle(1));
        Assert.Equal("Zaznaczono 3 pozycje", AutostartText.SelectionBarTitle(3));
        Assert.Equal("Zaznaczono 5 pozycji", AutostartText.SelectionBarTitle(5));
        Assert.Equal("Od następnego logowania. Cofniesz w Historii zmian.", AutostartText.SelectionBarDetails);
        Assert.Equal("Zaznaczona", AutostartText.SelectedLine);
        Assert.Equal("Wyłącz (3)", AutostartText.DisableButton(3));
        Assert.Equal("Włącz (0)", AutostartText.EnableButton(0));
    }

    [Fact]
    public void PageTexts_Translated()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("Startup apps", AutostartText.PageTitle);
            Assert.Equal("File missing", AutostartText.GroupName(AutostartGroup.MissingFile));
            Assert.Equal("Reading the list", AutostartText.Reading);
            Assert.Equal("Search by name, publisher or command", AutostartText.SearchPlaceholder);
            Assert.Equal("High (Windows thresholds)", AutostartText.Impact(StartupImpactLevel.High));
            Assert.Equal(" (last start)", AutostartText.LastStart);
            Assert.Equal("3 items selected", AutostartText.SelectionBarTitle(3));
            Assert.Equal("Disable (3)", AutostartText.DisableButton(3));
            Assert.Equal("Enable (2)", AutostartText.EnableButton(2));
            Assert.Equal("Disabled 28/10/2026", AutostartText.State(Row(state: AutostartState.Disabled, disabledAt: DisabledAt)));
        }
    }

    [Fact]
    public void State_FourTexts()
    {
        Assert.Equal("Włączona", AutostartText.State(Row()));
        Assert.Equal("Wyłączona 28.10.2026", AutostartText.State(Row(state: AutostartState.Disabled, disabledAt: DisabledAt)));
        Assert.Equal("Wyłączona", AutostartText.State(Row(state: AutostartState.Disabled)));
        Assert.Equal("Stan nieznany", AutostartText.State(Row(state: AutostartState.Unknown)));
        Assert.Throws<ArgumentNullException>(() => AutostartText.State(null!));
    }

    [Fact]
    public void RowNote_DisabledMissingAndBlocker()
    {
        Assert.Null(AutostartText.RowNote(Row()));
        Assert.Null(AutostartText.RowNote(Row(blocker: AutostartBlocker.Protected)));
        Assert.Equal("Wyłączona 28.10.2026", AutostartText.RowNote(Row(state: AutostartState.Disabled, disabledAt: DisabledAt)));
        Assert.Equal("Włączona, ale pliku nie ma – nic nie startuje", AutostartText.RowNote(Row(file: AutostartFileStatus.Missing)));
        Assert.Equal("Wyłączona", AutostartText.RowNote(Row(state: AutostartState.Disabled, file: AutostartFileStatus.Missing)));
        Assert.Equal(
            "Nie udało się ustalić zalogowanego użytkownika.",
            AutostartText.RowNote(Row(state: AutostartState.Disabled, blocker: AutostartBlocker.UserUncertain)));
        Assert.Throws<ArgumentNullException>(() => AutostartText.RowNote(null!));
    }

    [Fact]
    public void RowNote_MissingFileWithBlocker_ShowsBoth()
    {
        Assert.Equal(
            "Włączona, ale pliku nie ma – nic nie startuje. Nie udało się ustalić zalogowanego użytkownika.",
            AutostartText.RowNote(Row(file: AutostartFileStatus.Missing, blocker: AutostartBlocker.UserUncertain)));
        Assert.Equal(
            "Wyłączona 28.10.2026. Nieznany zapis stanu – DSOptimizer tej pozycji nie zmienia.",
            AutostartText.RowNote(Row(state: AutostartState.Disabled, disabledAt: DisabledAt, file: AutostartFileStatus.Missing, blocker: AutostartBlocker.UnknownFormat)));
        // Kłódka ma własną podpowiedź – bez powodu w linii.
        Assert.Equal(
            "Włączona, ale pliku nie ma – nic nie startuje",
            AutostartText.RowNote(Row(file: AutostartFileStatus.Missing, blocker: AutostartBlocker.Protected)));
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal(
                "Enabled, but the file is gone – nothing starts. Unknown state format – DSOptimizer doesn't change this item.",
                AutostartText.RowNote(Row(file: AutostartFileStatus.Missing, blocker: AutostartBlocker.UnknownFormat)));
        }
    }

    [Fact]
    public void Verdict_FourTexts()
    {
        Assert.Equal("Można wyłączyć", AutostartText.Verdict(Row(known: Known(AutostartVerdict.CanDisable))));
        Assert.Equal("Chroniona", AutostartText.Verdict(Row(known: Known(AutostartVerdict.Protected), blocker: AutostartBlocker.Protected, file: AutostartFileStatus.Missing)));
        Assert.Equal("Bez oceny", AutostartText.Verdict(Row()));
        Assert.Equal("Sugerowana – plik nie istnieje", AutostartText.Verdict(Row(file: AutostartFileStatus.Missing)));
        // Bez pliku, ale wyłączona – nie jest sugerowana, więc bez „Sugerowana”.
        Assert.Equal("Bez oceny", AutostartText.Verdict(Row(state: AutostartState.Disabled, file: AutostartFileStatus.Missing)));
        Assert.Throws<ArgumentNullException>(() => AutostartText.Verdict(null!));
    }

    [Fact]
    public void Location_PerSource()
    {
        Assert.Equal(
            @"Rejestr: HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run, wartość „Spotify”",
            AutostartText.Location(Entry(AutostartSource.RunUser, Locations.RunKey(AutostartSource.RunUser), "Spotify")));
        Assert.Equal(
            @"Rejestr: HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Run, wartość „SecurityHealth”",
            AutostartText.Location(Entry(AutostartSource.RunMachine, Locations.RunKey(AutostartSource.RunMachine), "SecurityHealth")));
        Assert.Equal(
            @"Rejestr: HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run, wartość „Adobe ARM”",
            AutostartText.Location(Entry(AutostartSource.Run32, Locations.RunKey(AutostartSource.Run32), "Adobe ARM")));
        Assert.Equal(
            @"Rejestr: HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run, wartość „(bez nazwy)”",
            AutostartText.Location(Entry(AutostartSource.RunUser, Locations.RunKey(AutostartSource.RunUser), string.Empty)));
        var folder = @"C:\Users\Jan\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup";
        Assert.Equal(folder, AutostartText.Location(Entry(AutostartSource.FolderUser, folder)));
        Assert.Equal(
            "Aplikacja ze Sklepu: Microsoft.WindowsTerminal_8wekyb3d8bbwe",
            AutostartText.Location(Entry(AutostartSource.StartupTask, "Microsoft.WindowsTerminal_8wekyb3d8bbwe")));
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal(
                @"Registry: HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run, value ""Spotify""",
                AutostartText.Location(Entry(AutostartSource.RunUser, Locations.RunKey(AutostartSource.RunUser), "Spotify")));
            Assert.Equal(
                @"Registry: HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run, value ""(no name)""",
                AutostartText.Location(Entry(AutostartSource.RunUser, Locations.RunKey(AutostartSource.RunUser), string.Empty)));
            Assert.Equal("Store app: Fam_1", AutostartText.Location(Entry(AutostartSource.StartupTask, "Fam_1")));
        }

        Assert.Throws<ArgumentNullException>(() => AutostartText.Location(null!));
    }

    [Fact]
    public void Account_UserAndMachine()
    {
        Assert.Equal("Jan (zalogowane konto)", AutostartText.Account(AutostartScope.User, "Jan"));
        Assert.Equal("Jan (zalogowane konto)", AutostartText.Account(AutostartScope.Store, "Jan"));
        Assert.Equal("Wszystkie konta na tym komputerze", AutostartText.Account(AutostartScope.Machine, "Jan"));
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("Jan (signed-in account)", AutostartText.Account(AutostartScope.User, "Jan"));
            Assert.Equal("All accounts on this PC", AutostartText.Account(AutostartScope.Machine, "Jan"));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => AutostartText.Account((AutostartScope)99, "Jan"));
    }

    [Fact]
    public void ResultStatus_PerOutcomeAndDirection()
    {
        Assert.Equal("Wyłączono i sprawdzono", AutostartText.ResultStatus(TweakOutcome.Applied, AutostartState.Disabled));
        Assert.Equal("Włączono i sprawdzono", AutostartText.ResultStatus(TweakOutcome.Applied, AutostartState.Enabled));
        Assert.Equal("Włączono i sprawdzono", AutostartText.ResultStatus(TweakOutcome.Undone, AutostartState.Enabled));
        Assert.Equal("Bez zmian", AutostartText.ResultStatus(TweakOutcome.AlreadyApplied, AutostartState.Disabled));
        Assert.Equal("Nie wykonano", AutostartText.ResultStatus(TweakOutcome.FailedRolledBack, AutostartState.Disabled));
        Assert.Equal("Nie wykonano", AutostartText.ResultStatus(TweakOutcome.FailedRollbackIncomplete, AutostartState.Disabled));
        Assert.Equal("Nie wykonano", AutostartText.ResultStatus(TweakOutcome.Failed, AutostartState.Enabled));
        Assert.Equal("Odmowa", AutostartText.ResultStatus(TweakOutcome.Refused, AutostartState.Enabled));
        Assert.Equal("Pominięto", AutostartText.ResultStatus(TweakOutcome.Skipped, AutostartState.Enabled));
        Assert.True(AutostartText.CountsAsChanged(TweakOutcome.Applied));
        Assert.True(AutostartText.CountsAsChanged(TweakOutcome.Undone));
        Assert.False(AutostartText.CountsAsChanged(TweakOutcome.AlreadyApplied));
        Assert.False(AutostartText.CountsAsChanged(TweakOutcome.FailedRolledBack));
        Assert.Equal("Wyłączono 1 z 2.", AutostartText.Summary(AutostartState.Disabled, 1, 2));
        Assert.Equal("Włączono 0 z 1.", AutostartText.Summary(AutostartState.Enabled, 0, 1));
        Assert.Equal("Nie udało się w pełni przywrócić stanu sprzed zmiany: a; b", AutostartText.UndoIncomplete(new[] { "a", "b" }));
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("Disabled 1 of 2.", AutostartText.Summary(AutostartState.Disabled, 1, 2));
            Assert.Equal("No change", AutostartText.ResultStatus(TweakOutcome.AlreadyApplied, AutostartState.Disabled));
        }
    }

    [Fact]
    public void ConfirmationTexts_Verbatim()
    {
        Assert.Equal("Wyłącz w autostarcie", AutostartText.ConfirmTitle(AutostartState.Disabled));
        Assert.Equal("Włącz w autostarcie", AutostartText.ConfirmTitle(AutostartState.Enabled));
        Assert.Equal("Program zostaje zainstalowany, tylko przestaje startować sam.", AutostartText.ItemNote(AutostartState.Disabled));
        Assert.Equal("Program znowu będzie startował sam po zalogowaniu.", AutostartText.ItemNote(AutostartState.Enabled));
        Assert.Equal("Dotyczy wszystkich kont na tym komputerze.", AutostartText.AllAccountsWarning);
        Assert.Equal("Cofnij zmianę w autostarcie", AutostartText.UndoConfirmTitle);
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("Disable at startup", AutostartText.ConfirmTitle(AutostartState.Disabled));
            Assert.Equal("Applies to all accounts on this PC.", AutostartText.AllAccountsWarning);
        }
    }

    private static readonly AutostartLocations Locations = AutostartLocations.ForUser("S-1-5-21-1-2-3-1001");

    private static readonly DateTimeOffset DisabledAt = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    private static AutostartKnownEntry Known(AutostartVerdict verdict)
        => new("test", new AutostartMatchRule("a.exe", new[] { @"%ProgramFiles%\*" }, new[] { "*" }, null, null), verdict, "Opis.", new[] { "https://example.com/" }, true);

    private static AutostartEntry Entry(AutostartSource source, string location, string entryName = "x") => new()
    {
        Id = "test:" + source + ":" + entryName,
        Source = source,
        EntryName = entryName,
        Name = "x",
        Location = location,
        FileStatus = AutostartFileStatus.Exists,
        Switch = new AutostartSwitch(AutostartSwitchKind.StartupApproved, "k", "x", AutostartSwitchFormat.Absent, AutostartState.Enabled, null, null),
        PackageFamily = source == AutostartSource.StartupTask ? location : null,
    };

    private static AutostartRow Row(
        AutostartState state = AutostartState.Enabled,
        DateTimeOffset? disabledAt = null,
        AutostartFileStatus file = AutostartFileStatus.Exists,
        AutostartBlocker blocker = AutostartBlocker.None,
        AutostartKnownEntry? known = null,
        string? publisher = null,
        string? filePath = null)
        => new(
            new AutostartEntry
            {
                Id = "run:user:Spotify",
                Source = AutostartSource.RunUser,
                EntryName = "Spotify",
                Name = "Spotify",
                Location = "loc",
                FileStatus = file,
                Switch = new AutostartSwitch(
                    AutostartSwitchKind.StartupApproved, "k", "Spotify", state == AutostartState.Unknown ? AutostartSwitchFormat.UnknownFormat : AutostartSwitchFormat.Valid, state, disabledAt, null),
                Publisher = publisher,
                FilePath = filePath,
            },
            known,
            blocker);

    [Fact]
    public void RowTexts_FromRender()
    {
        Assert.Equal("Bez oceny – brak tej pozycji w bazie programu.", AutostartText.NotAssessed);
        Assert.Equal(
            "Plik z polecenia nie istnieje, więc ta pozycja niczego nie uruchamia. Wyłączenie niczego nie psuje.",
            AutostartText.MissingFileSummary);
    }
}
