using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DSOptimizer.App.ViewModels;
using DSOptimizer.Catalog;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Autostart;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Localization;
using DSOptimizer.Tests.Windows.Support;
using DSOptimizer.Windows;
using Xunit;

namespace DSOptimizer.Tests.Windows;

/// <summary>
/// Strona „Autostart” na sesji z atrapą czytnika (<see cref="FakeAutostartReader"/>): prawdziwy autostart hosta nie jest czytany, a seria
/// „Wyłącz/Włącz” to atrapa (lista wywołań), więc silnik niczego nie zapisuje. Klucze przełączników wierszy leżą w piaskownicy rejestru
/// testów; rejestr Windows służy sesji tylko do sprawdzenia, czy gałąź konta istnieje.
/// </summary>
[Trait("Category", "Windows")]
public sealed class AutostartViewModelTests : IDisposable
{
    private const string Sid = "S-1-5-21-1-2-3-1001";
    private const string ProgramFiles = @"C:\Program Files";

    private const string KnownJson = """
        {
          "entries": [
            {
              "id": "test-spotify",
              "match": { "file": "Spotify.exe", "folders": ["%ProgramFiles%\\Spotify\\"], "publishers": ["Spotify AB"] },
              "verdict": "can-disable",
              "summary": "Odtwarzacz muzyki (test).",
              "source": ["docs/zrodla/2026-11-02-autostart-programy.md", "https://support.spotify.com/test"],
              "confirmed": true
            },
            {
              "id": "test-realtek",
              "match": { "file": "Realtek.exe", "folders": ["%ProgramFiles%\\Realtek\\"], "publishers": ["Realtek"] },
              "verdict": "protected",
              "summary": "Dźwięk (test).",
              "source": ["docs/zrodla/2026-11-02-autostart-programy.md", "https://www.realtek.com/test"],
              "confirmed": true
            }
          ]
        }
        """;

    private static readonly DateTimeOffset MeasuredAt = LocalTime(2026, 11, 2, 8, 14);

    private readonly TempDirectory _temp = new();
    private readonly RegistrySandbox _sandbox = new();
    private readonly List<(GuiLogLevel Level, string Text)> _log = new();
    private readonly List<(IReadOnlyList<AutostartRow> Rows, AutostartState Target)> _runs = new();
    private readonly FakeClipboard _clipboard = new();

    public AutostartViewModelTests()
    {
        Reader = new FakeAutostartReader(Reading(Entries()));
        Vm = Create(Reader);
    }

    private FakeAutostartReader Reader { get; }

    private AutostartViewModel Vm { get; }

    public void Dispose()
    {
        _sandbox.Dispose();
        _temp.Dispose();
    }

    [Fact]
    public void Refresh_GroupCountsAndOrder()
    {
        Refresh(Vm);

        Assert.Equal(AutostartGroup.Enabled, Vm.SelectedGroup);
        Assert.Equal(new[] { 5, 1, 1 }, Vm.Groups.Select(group => group.Count).ToArray());
        Assert.Equal(new[] { "Włączone", "Wyłączone", "Bez pliku" }, Vm.Groups.Select(group => group.Name).ToArray());
        Assert.Equal("Włączone, 5", Vm.Groups[0].AutomationName);
        Assert.True(Vm.Groups[0].IsSelected);
        // Zmierzone malejąco po czasie procesora, potem niezmierzone.
        Assert.Equal(new[] { "Alpha", "Spotify", "Realtek", "Beta", "Gamma" }, Names(Vm));
        Assert.Equal("7", Vm.CounterText);
        Assert.Equal("7 pozycji", Vm.CounterDescription);
        Assert.Same(Vm.VisibleRows[0], Vm.SelectedRow);

        Vm.SelectGroupCommand.Execute(Vm.Groups[1]);

        Assert.Equal(AutostartGroup.Disabled, Vm.SelectedGroup);
        Assert.Equal(new[] { "Delta" }, Names(Vm));
        Assert.True(Vm.Groups[1].IsSelected);
        Assert.False(Vm.Groups[0].IsSelected);
    }

    [Fact]
    public async Task Refresh_OlderReadDiscarded()
    {
        using var reader = new GatedReader(Reading(new[] { Entry("Old") }), Reading(new[] { Entry("New") }));
        var vm = Create(reader);

        var older = Task.Run(vm.RefreshAsync);
        Assert.True(SpinWait.SpinUntil(() => reader.Calls >= 1, TimeSpan.FromSeconds(30)));
        await Task.Run(vm.RefreshAsync);
        reader.Release.Set();
        await older;

        Assert.Equal(new[] { "New" }, Names(vm));
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task Refresh_OlderRead_WarningsNotLogged()
    {
        using var reader = new GatedReader(
            Reading(new[] { Entry("Old") }, "ostrzeżenie starszego odczytu"),
            Reading(new[] { Entry("New") }, "ostrzeżenie nowszego odczytu"));
        var vm = Create(reader);

        var older = Task.Run(vm.RefreshAsync);
        Assert.True(SpinWait.SpinUntil(() => reader.Calls >= 1, TimeSpan.FromSeconds(30)));
        await Task.Run(vm.RefreshAsync);
        reader.Release.Set();
        await older;

        Assert.Contains(_log, line => line.Text == "ostrzeżenie nowszego odczytu");
        Assert.DoesNotContain(_log, line => line.Text == "ostrzeżenie starszego odczytu");
    }

    [Fact]
    public void Deselect_OnlyGivenIds_RestStaySelected()
    {
        Refresh(Vm);
        Row(Vm, "Alpha").IsSelected = true;
        Row(Vm, "Spotify").IsSelected = true;
        Vm.SelectGroupCommand.Execute(Vm.Groups[1]);
        Row(Vm, "Delta").IsSelected = true;
        Assert.Equal("Wyłącz (2)", Vm.DisableText);

        Vm.Deselect(new[] { AutostartIds.For(AutostartSource.RunUser, "Alpha"), "run:user:nie-ma-takiej" });

        Assert.True(Row(Vm, "Delta").IsSelected);
        Assert.Equal(new[] { "Delta", "Spotify" }, Selected(Vm));
        Assert.Equal("Wyłącz (1)", Vm.DisableText);
        Assert.Equal("Włącz (1)", Vm.EnableText);
        Assert.Equal("Zaznaczono 2 pozycje", Vm.SelectionBarTitle);

        Vm.Deselect(new[] { Row(Vm, "Delta").Id, AutostartIds.For(AutostartSource.RunUser, "Spotify") });

        Assert.Equal("Wyłącz (0)", Vm.DisableText);
        Assert.Equal("Włącz (0)", Vm.EnableText);
        Assert.False(Vm.ClearSelectionCommand.CanExecute(null));
        Assert.Throws<ArgumentNullException>(() => Vm.Deselect(null!));
    }

    [Fact]
    public void Refresh_SelectionKeptById()
    {
        Refresh(Vm);
        Row(Vm, "Alpha").IsSelected = true;
        Vm.SelectedRow = Row(Vm, "Spotify");
        var before = Row(Vm, "Alpha");

        Refresh(Vm);

        Assert.NotSame(before, Row(Vm, "Alpha"));
        Assert.True(Row(Vm, "Alpha").IsSelected);
        Assert.False(Row(Vm, "Beta").IsSelected);
        Assert.Equal(Row(Vm, "Spotify").Id, Vm.SelectedRow!.Id);
        Assert.Equal("Wyłącz (1)", Vm.DisableText);
    }

    [Fact]
    public void Refresh_WarningsLoggedOnce()
    {
        Refresh(Vm);
        Refresh(Vm);

        Assert.Equal(1, _log.Count(line => line.Level == GuiLogLevel.Warning && line.Text == "ostrzeżenie odczytu (test)"));
    }

    [Fact]
    public void SelectSuggested_SelectsSuggestedFromAllGroupsMatchingSearch_NothingElse()
    {
        Refresh(Vm);
        Row(Vm, "Alpha").IsSelected = true;

        Assert.True(Vm.SelectSuggestedCommand.CanExecute(null));
        Vm.SelectSuggestedCommand.Execute(null);

        // Spotify (baza „można wyłączyć”) i Epsilon (pewny brak pliku, grupa „Bez pliku”); ręczne zaznaczenie zostaje.
        Assert.Equal(new[] { "Alpha", "Epsilon", "Spotify" }, Selected(Vm));
        Assert.Equal("Zaznaczono 3 pozycje", Vm.SelectionBarTitle);

        Vm.ClearSelectionCommand.Execute(null);
        Vm.SearchText = "spot";
        Vm.SelectSuggestedCommand.Execute(null);

        Assert.Equal(new[] { "Spotify" }, Selected(Vm));
    }

    [Fact]
    public void DisableEnableCounts_FromSelectedStates()
    {
        Refresh(Vm);
        Row(Vm, "Alpha").IsSelected = true;
        Row(Vm, "Spotify").IsSelected = true;
        Vm.SelectGroupCommand.Execute(Vm.Groups[1]);
        Row(Vm, "Delta").IsSelected = true;

        Assert.Equal("Wyłącz (2)", Vm.DisableText);
        Assert.Equal("Włącz (1)", Vm.EnableText);
        Assert.True(Vm.DisableCommand.CanExecute(null));
        Assert.True(Vm.EnableCommand.CanExecute(null));
        Assert.Equal("Zaznaczono 3 pozycje", Vm.SelectionBarTitle);
        Assert.Equal("Od następnego logowania. Cofniesz w Historii zmian.", Vm.SelectionBarDetails);
    }

    [Fact]
    public void ZeroButtonsDisabled()
    {
        Refresh(Vm);

        Assert.Equal("Wyłącz (0)", Vm.DisableText);
        Assert.Equal("Włącz (0)", Vm.EnableText);
        Assert.False(Vm.DisableCommand.CanExecute(null));
        Assert.False(Vm.EnableCommand.CanExecute(null));
        Assert.False(Vm.ClearSelectionCommand.CanExecute(null));
        Assert.Equal(string.Empty, Vm.SelectionBarDetails);

        Vm.SelectGroupCommand.Execute(Vm.Groups[1]);
        Row(Vm, "Delta").IsSelected = true;

        Assert.False(Vm.DisableCommand.CanExecute(null));
        Assert.True(Vm.EnableCommand.CanExecute(null));

        Vm.IsBusy = true;
        Assert.False(Vm.EnableCommand.CanExecute(null));
        Assert.False(Vm.RefreshCommand.CanExecute(null));
    }

    [Fact]
    public void ProtectedRow_NotSelectable()
    {
        Refresh(Vm);
        var realtek = Row(Vm, "Realtek");

        realtek.IsSelected = true;

        Assert.False(realtek.IsSelectable);
        Assert.True(realtek.IsProtected);
        Assert.False(realtek.IsSelected);
        Assert.Null(realtek.SelectionLineText);
        Assert.False(realtek.IsSuggested);
        Assert.Equal("Chroniona", realtek.VerdictText);
        Assert.Equal(AutostartText.ProtectedTooltip, realtek.AutomationHelpText);
        Assert.Equal(7, Vm.Groups.Sum(group => group.Count));
        Assert.Equal("Wyłącz (0)", Vm.DisableText);
    }

    [Fact]
    public async Task DisableEnable_PassOnlyMatchingRows()
    {
        Refresh(Vm);
        Row(Vm, "Alpha").IsSelected = true;
        Vm.SelectGroupCommand.Execute(Vm.Groups[1]);
        Row(Vm, "Delta").IsSelected = true;
        Vm.SelectGroupCommand.Execute(Vm.Groups[2]);
        Row(Vm, "Epsilon").IsSelected = true;

        await Vm.DisableCommand.ExecuteAsync(null);
        await Vm.EnableCommand.ExecuteAsync(null);

        Assert.Equal(2, _runs.Count);
        Assert.Equal(AutostartState.Disabled, _runs[0].Target);
        Assert.Equal(new[] { "Alpha", "Epsilon" }, _runs[0].Rows.Select(row => row.Entry.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());
        Assert.Equal(AutostartState.Enabled, _runs[1].Target);
        Assert.Equal(new[] { "Delta" }, _runs[1].Rows.Select(row => row.Entry.Name).ToArray());
        Assert.All(_runs.SelectMany(run => run.Rows), row => Assert.True(row.CanToggle));
    }

    [Fact]
    public void Indicator_MaxBar72_MinBar4_UnmeasuredZero()
    {
        Refresh(Vm);

        Assert.Equal(72, Row(Vm, "Alpha").BarWidth);
        Assert.Equal(24, Row(Vm, "Spotify").BarWidth);
        Assert.Equal(4, Row(Vm, "Beta").BarWidth);
        Assert.Equal(0, Row(Vm, "Gamma").BarWidth);
        Assert.False(Row(Vm, "Gamma").IsMeasured);
        // Maksimum z całej listy, nie tylko z widocznej grupy.
        Vm.SelectGroupCommand.Execute(Vm.Groups[1]);
        Assert.Equal(21, Row(Vm, "Delta").BarWidth);
        Vm.SelectGroupCommand.Execute(Vm.Groups[2]);
        Assert.Equal(0, Row(Vm, "Epsilon").BarWidth);
        Assert.False(Row(Vm, "Epsilon").IsMeasured);
    }

    [Fact]
    public void Indicator_Texts()
    {
        Refresh(Vm);
        var alpha = Row(Vm, "Alpha");

        Assert.Equal("4,8 s", alpha.IndicatorText);
        Assert.Equal("< 0,1 s", Row(Vm, "Beta").IndicatorText);
        Assert.Equal("nie zmierzono", Row(Vm, "Gamma").IndicatorText);
        Assert.Equal("4,8 s", alpha.CpuText);
        Assert.Equal("1 MB", alpha.DiskText);
        Assert.Equal("Wysoki (progi Windows)", alpha.ImpactText);
        Assert.Equal("02.11.2026, 08:14", alpha.MeasuredAtText);
        Assert.Equal(AutostartText.CpuNote, alpha.MeasureNote);
        Assert.Equal(AutostartText.NotInMeasurement, Row(Vm, "Gamma").MeasureNote);
        Assert.Equal("Producent · to konto", alpha.PublisherText);
        Assert.Equal("Jan (zalogowane konto)", alpha.AccountText);
        Assert.Equal("Bez oceny – brak tej pozycji w bazie programu.", alpha.Summary);
        Assert.False(alpha.HasSource);
        Assert.Equal("Źródło: support.spotify.com", Row(Vm, "Spotify").SourceText);
        Assert.Equal("Odtwarzacz muzyki (test).", Row(Vm, "Spotify").Summary);
        Assert.True(Row(Vm, "Spotify").IsSuggested);
        Assert.Equal("Niezaznaczona – kliknij dwukrotnie wiersz, żeby dodać", alpha.SelectionLineText);
        alpha.IsSelected = true;
        Assert.Equal("Zaznaczona", alpha.SelectionLineText);
        Assert.Equal("Polecenie: \"C:\\Programs\\Alpha.exe\" --start", alpha.CommandText);
        Assert.Equal(@"Plik: C:\Programs\Alpha.exe", alpha.FileText);
        Assert.Equal("W Historii zmian", alpha.UndoText);
        Assert.Equal("Alpha", alpha.AutomationName);

        Vm.SelectGroupCommand.Execute(Vm.Groups[1]);
        var delta = Row(Vm, "Delta");
        Assert.Equal("02.11.2026, 08:14 (ostatni start)", delta.MeasuredAtText);
        Assert.StartsWith("Wyłączona ", delta.StateText, StringComparison.Ordinal);
        Assert.True(delta.HasRowNote);

        Vm.SelectGroupCommand.Execute(Vm.Groups[2]);
        var epsilon = Row(Vm, "Epsilon");
        Assert.Equal("plik nie istnieje", epsilon.IndicatorText);
        Assert.Equal("nie zmierzono", epsilon.CpuText);
        Assert.Equal(AutostartText.FileMissingNote, epsilon.MeasureNote);
        Assert.Equal("Włączona, ale pliku nie ma – nic nie startuje", epsilon.RowNote);
        Assert.Equal("Sugerowana – plik nie istnieje", epsilon.VerdictText);
    }

    [Fact]
    public void Search_NamePublisherCommand()
    {
        Refresh(Vm);

        Vm.SearchText = "alp";
        Assert.Equal(new[] { "Alpha" }, Names(Vm));

        Vm.SearchText = "spotify ab";
        Assert.Equal(new[] { "Spotify" }, Names(Vm));

        Vm.SearchText = "tray-only";
        Assert.Equal(new[] { "Gamma" }, Names(Vm));
        Assert.Equal(new[] { 1, 0, 0 }, Vm.Groups.Select(group => group.Count).ToArray());

        Vm.SearchText = "nic-takiego";
        Assert.Empty(Vm.VisibleRows);
        Assert.Equal("Żadna pozycja w tej grupie nie pasuje do wyszukiwania.", Vm.EmptyText);

        Vm.ClearSearchCommand.Execute(null);
        Assert.Equal(5, Vm.VisibleRows.Count);
        Assert.Equal(string.Empty, Vm.EmptyText);
    }

    [Fact]
    public void CopyName_ThreeLinesToClipboard()
    {
        Refresh(Vm);
        Vm.SelectedRow = Row(Vm, "Spotify");

        Vm.CopyNameCommand.Execute(null);

        Assert.Equal("Spotify\r\nSpotify AB\r\n" + ProgramFiles + @"\Spotify\Spotify.exe", _clipboard.Text);
        Assert.Contains(_log, line => line.Level == GuiLogLevel.Info && line.Text == "Skopiowano nazwę do schowka.");
    }

    [Fact]
    public void Subtitle_PolishShortDate_EnglishFullDate()
    {
        Assert.Equal("Lista nie została jeszcze odczytana", Vm.Subtitle);

        Refresh(Vm);

        Assert.Equal("5 włączonych · pomiar Windows 02.11, 08:14", Vm.Subtitle);
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("5 enabled · Windows measurement 2 Nov, 08:14", Vm.Subtitle);
        }
    }

    [Fact]
    public void Refresh_ReadFailed_ShowsErrorAndKeepsList()
    {
        Refresh(Vm);
        Reader.Failure = new InvalidOperationException("rejestr (test)");

        Refresh(Vm);

        Assert.Equal(AutostartText.ReadFailed, Vm.Subtitle);
        Assert.Equal(5, Vm.VisibleRows.Count);
    }

    private static DateTimeOffset LocalTime(int year, int month, int day, int hour, int minute)
    {
        var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    private static void Refresh(AutostartViewModel vm) => Task.Run(vm.RefreshAsync).GetAwaiter().GetResult();

    private static string[] Names(AutostartViewModel vm) => vm.VisibleRows.Select(row => row.Name).ToArray();

    private static AutostartRowViewModel Row(AutostartViewModel vm, string name) => vm.VisibleRows.Single(row => row.Name == name);

    private static string[] Selected(AutostartViewModel vm)
    {
        var search = vm.SearchText;
        var group = vm.SelectedGroup;
        vm.SearchText = string.Empty;
        var names = new List<string>();
        foreach (var option in vm.Groups)
        {
            vm.SelectedGroup = option.Value;
            names.AddRange(vm.VisibleRows.Where(row => row.IsSelected).Select(row => row.Name));
        }

        vm.SelectedGroup = group;
        vm.SearchText = search;
        return names.OrderBy(name => name, StringComparer.Ordinal).ToArray();
    }

    private static AutostartReading Reading(IReadOnlyList<AutostartEntry> entries, string warning = "ostrzeżenie odczytu (test)")
        => new(
            entries,
            UserEnvironment.FromVariables(new Dictionary<string, string> { ["ProgramFiles"] = ProgramFiles }),
            MeasuredAt,
            new[] { warning });

    private AutostartViewModel Create(IAutostartReader reader)
    {
        var composition = AppComposition.Create(_temp.AppRoot, null);
        var services = composition.Services;
        var catalog = CatalogLoader.Load(BuiltInCatalog.Sources);
        var gui = new GuiSession(new LoadedCatalog(catalog.Definitions, "sha256:test"), services);
        var user = new FixedUser();
        var session = new AutostartSession(
            gui,
            reader,
            new AutostartEngine(new WindowsRegistry(), services.Journal, composition.Log, services.Clock),
            AutostartKnownCatalog.Load(KnownJson),
            user,
            Sid,
            new WindowsRegistry(),
            composition.Log,
            services.Clock);
        return new AutostartViewModel(
            session,
            new JanNames(),
            (rows, target) =>
            {
                _runs.Add((rows, target));
                return Task.CompletedTask;
            },
            _clipboard,
            (level, text) =>
            {
                lock (_log)
                {
                    _log.Add((level, text));
                }
            },
            services.Clock);
    }

    private AutostartEntry[] Entries() => new[]
    {
        Entry("Alpha", cpu: 4_800_000, command: "\"C:\\Programs\\Alpha.exe\" --start"),
        Entry("Beta", cpu: 40_000),
        Entry("Gamma", command: "\"C:\\Programs\\Gamma.exe\" --tray-only"),
        Entry("Delta", state: AutostartState.Disabled, cpu: 1_400_000),
        Entry("Epsilon", file: AutostartFileStatus.Missing, source: AutostartSource.RunMachine),
        Entry("Spotify", cpu: 1_600_000, publisher: "Spotify AB", path: ProgramFiles + @"\Spotify\Spotify.exe"),
        Entry("Realtek", cpu: 200_000, publisher: "Realtek", path: ProgramFiles + @"\Realtek\Realtek.exe", source: AutostartSource.RunMachine),
    };

    /// <summary>Pozycja Run z przełącznikiem w piaskownicy rejestru testów (odczytany format „zmierzony”, stan podany).</summary>
    private AutostartEntry Entry(
        string name,
        AutostartSource source = AutostartSource.RunUser,
        AutostartState state = AutostartState.Enabled,
        long? cpu = null,
        string publisher = "Producent",
        string? command = null,
        string? path = null,
        AutostartFileStatus file = AutostartFileStatus.Exists)
        => new()
        {
            Id = AutostartIds.For(source, name),
            Source = source,
            EntryName = name,
            Name = name,
            Location = _sandbox.HkcuPath + @"\Run",
            FileStatus = file,
            Switch = new AutostartSwitch(
                AutostartSwitchKind.StartupApproved,
                _sandbox.HkcuPath + @"\StartupApproved\Run",
                name,
                AutostartSwitchFormat.Valid,
                state,
                state == AutostartState.Disabled ? MeasuredAt.AddDays(-5) : null,
                null),
            Publisher = publisher,
            Command = command ?? "\"C:\\Programs\\" + name + ".exe\"",
            FilePath = path ?? @"C:\Programs\" + name + ".exe",
            Measurement = cpu is { } us ? new StartupMeasurement(us, 1_100_000, MeasuredAt, state == AutostartState.Disabled) : null,
        };

    private sealed class FixedUser : IUserContext
    {
        public string Sid => AutostartViewModelTests.Sid;

        public bool IsCertain => true;
    }

    private sealed class JanNames : IAccountNames
    {
        public string? NameOf(string sid) => sid == Sid ? "Jan" : null;
    }

    private sealed class FakeClipboard : IClipboardText
    {
        public string? Text { get; private set; }

        public void SetText(string text) => Text = text;

        public string? GetText() => Text;
    }

    /// <summary>Pierwszy odczyt czeka na <see cref="Release"/> i zwraca starszą listę; każdy następny od razu zwraca nowszą.</summary>
    private sealed class GatedReader : IAutostartReader, IDisposable
    {
        private readonly AutostartReading _older;
        private readonly AutostartReading _newer;
        private int _calls;

        public GatedReader(AutostartReading older, AutostartReading newer)
        {
            _older = older;
            _newer = newer;
        }

        public ManualResetEventSlim Release { get; } = new();

        public int Calls => Volatile.Read(ref _calls);

        public AutostartReading Read(AutostartContext context)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                Release.Wait(TimeSpan.FromSeconds(30));
                return _older;
            }

            return _newer;
        }

        public void Dispose() => Release.Dispose();
    }
}
