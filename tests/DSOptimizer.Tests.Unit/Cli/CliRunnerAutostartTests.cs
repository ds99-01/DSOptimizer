using System;
using System.Collections.Generic;
using System.Linq;
using DSOptimizer.Core.Autostart;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Cli;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Journal;
using DSOptimizer.Core.Model;
using DSOptimizer.Tests.Unit.Autostart;
using DSOptimizer.Tests.Unit.Engine;
using DSOptimizer.Tests.Unit.Fakes;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DSOptimizer.Tests.Unit.Cli;

/// <summary>
/// <c>--list-autostart</c>/<c>--set-autostart</c>/<c>--undo-autostart</c> na fałszywym rejestrze, journalu i czytniku: kody wyjścia, pola
/// wierszy w JSON i to, że odmowa niczego nie zapisuje.
/// </summary>
public class CliRunnerAutostartTests
{
    private const string UserSid = "S-1-5-21-1-2-3-1001";
    private const string AdminSid = "S-1-5-21-1-2-3-500";
    private const string SecurityHealthPath = @"C:\Windows\System32\SecurityHealthSystray.exe";

    private const string ProtectedEntry = """
        { "id": "windows.security-health",
          "match": { "file": "SecurityHealthSystray.exe", "folders": ["%SystemRoot%\\System32\\"], "publishers": ["Microsoft Corporation"] },
          "verdict": "protected", "summary": "Ikona Zabezpieczeń Windows.", "source": ["https://learn.microsoft.com/windows/security"],
          "confirmed": true }
        """;

    private static readonly AutostartLocations Locations = AutostartLocations.ForUser(UserSid);

    private static readonly string OneDriveId = AutostartIds.For(AutostartSource.RunUser, "OneDrive");

    private static readonly string SecurityHealthId = AutostartIds.For(AutostartSource.RunMachine, "SecurityHealth");

    private static string OneDriveKey => Locations.StartupApprovedKey(AutostartSource.RunUser);

    [Fact]
    public void ListAutostart_RowFields()
    {
        var f = new Fixture();
        f.Warnings.Add("Pominięto plik pomiaru Windows x.etl: błędny format.");

        var report = f.Run(CliCommand.ListAutostart);

        Assert.Equal(CliExitCode.Success, report.Result);
        Assert.Equal(0, report.ExitCode);
        Assert.Equal(CliCommand.ListAutostart, report.Command);
        Assert.Null(report.TweakId);
        Assert.Null(report.Outcome);
        Assert.Contains("Pominięto plik pomiaru Windows x.etl: błędny format.", report.Warnings);
        var context = Assert.Single(f.Reader.Contexts);
        Assert.Equal(UserSid, context.UserSid);
        Assert.Equal(UserSid, context.ProcessSid);
        Assert.Empty(f.Engine.Registry.WriteLog);
        Assert.Equal(0, f.Engine.Journal.SaveCount);

        var json = JObject.Parse(CliReportSerializer.Serialize(report));
        var rows = json["autostartRows"]!.Children<JObject>().ToList();
        Assert.Equal(2, rows.Count);
        var oneDrive = rows[0];
        Assert.Equal(
            new[] { "id", "source", "name", "publisher", "scope", "state", "missingFile", "verdict", "canToggle", "cpuMs", "diskBytes" },
            oneDrive.Properties().Select(p => p.Name));
        Assert.Equal(OneDriveId, (string)oneDrive["id"]!);
        Assert.Equal("RunUser", (string)oneDrive["source"]!);
        Assert.Equal("Microsoft OneDrive", (string)oneDrive["name"]!);
        Assert.Equal("Microsoft Corporation", (string)oneDrive["publisher"]!);
        Assert.Equal("User", (string)oneDrive["scope"]!);
        Assert.Equal("Enabled", (string)oneDrive["state"]!);
        Assert.False((bool)oneDrive["missingFile"]!);
        Assert.Equal(JTokenType.Null, oneDrive["verdict"]!.Type);
        Assert.True((bool)oneDrive["canToggle"]!);
        Assert.Equal(1534L, (long)oneDrive["cpuMs"]!);
        Assert.Equal(3002368L, (long)oneDrive["diskBytes"]!);

        var security = rows[1];
        Assert.Equal(SecurityHealthId, (string)security["id"]!);
        Assert.Equal("RunMachine", (string)security["source"]!);
        Assert.Equal("Machine", (string)security["scope"]!);
        Assert.Equal("Protected", (string)security["verdict"]!);
        Assert.False((bool)security["canToggle"]!);
        Assert.Equal(JTokenType.Null, security["cpuMs"]!.Type);
        Assert.Equal(JTokenType.Null, security["diskBytes"]!.Type);
    }

    [Fact]
    public void ListAutostart_OtherCommands_NoAutostartRowsInJson()
    {
        var f = new Fixture();

        var json = JObject.Parse(CliReportSerializer.Serialize(f.Run(CliCommand.UndoAutostart, OneDriveId)));

        Assert.Null(json["autostartRows"]);
    }

    [Fact]
    public void SetAutostart_Disable_AppliedExit0()
    {
        var f = new Fixture();

        var report = f.Run(CliCommand.SetAutostart, OneDriveId.ToUpperInvariant(), AutostartState.Disabled);

        Assert.Equal(TweakOutcome.Applied, report.Outcome);
        Assert.Equal(CliExitCode.Success, report.Result);
        Assert.Equal(0, report.ExitCode);
        Assert.Equal(OneDriveId, report.TweakId);
        Assert.Equal(AutostartText.Disabled, report.Message);
        Assert.Null(report.AutostartRows);
        Assert.Equal(AutostartState.Disabled, AutostartSwitchReader.Read(f.Engine.Registry, AutostartSwitchKind.StartupApproved, OneDriveKey, "OneDrive", false).State);
        var entry = Assert.Single(f.Engine.Journal.Current.Entries);
        Assert.Equal(JournalEntry.AutostartKind, entry.Kind);
        Assert.Equal(OneDriveId, entry.TweakId);
        Assert.Equal(JournalEntryStatus.Active, entry.Status);
    }

    [Fact]
    public void SetAutostart_Protected_RefusedExit1_NothingWritten()
    {
        var f = new Fixture();

        var report = f.Run(CliCommand.SetAutostart, SecurityHealthId, AutostartState.Disabled);

        Assert.Equal(TweakOutcome.Refused, report.Outcome);
        Assert.Equal(CliExitCode.OperationFailed, report.Result);
        Assert.Equal(1, report.ExitCode);
        Assert.Equal(SecurityHealthId, report.TweakId);
        Assert.Equal(AutostartText.ProtectedTooltip, report.Message);
        Assert.Empty(f.Engine.Registry.WriteLog);
        Assert.Equal(0, f.Engine.Journal.SaveCount);
    }

    [Fact]
    public void SetAutostart_UnknownId_Exit1()
    {
        var f = new Fixture();

        var report = f.Run(CliCommand.SetAutostart, "run:user:NieMa", AutostartState.Disabled);

        Assert.Equal(CliExitCode.OperationFailed, report.Result);
        Assert.Equal(1, report.ExitCode);
        Assert.Null(report.Outcome);
        Assert.Equal("run:user:NieMa", report.TweakId);
        Assert.Equal(new[] { "Nie ma pozycji 'run:user:NieMa' na liście autostartu." }, report.Errors);
        Assert.Empty(f.Engine.Registry.WriteLog);
        Assert.Equal(0, f.Engine.Journal.SaveCount);
    }

    [Fact]
    public void SetAutostart_SameState_AlreadyAppliedExit0()
    {
        var f = new Fixture();

        var report = f.Run(CliCommand.SetAutostart, OneDriveId, AutostartState.Enabled);

        Assert.Equal(TweakOutcome.AlreadyApplied, report.Outcome);
        Assert.Equal(0, report.ExitCode);
        Assert.Equal(AutostartText.AlreadyEnabled, report.Message);
        Assert.Empty(f.Engine.Registry.WriteLog);
        Assert.Empty(f.Engine.Journal.Current.Entries);
    }

    [Fact]
    public void SetAutostart_EnableAfterDisable_UndoneExit0_OneJournalEntry()
    {
        var f = new Fixture();
        Assert.Equal(0, f.Run(CliCommand.SetAutostart, OneDriveId, AutostartState.Disabled).ExitCode);

        var report = f.Run(CliCommand.SetAutostart, OneDriveId, AutostartState.Enabled);

        Assert.Equal(TweakOutcome.Undone, report.Outcome);
        Assert.Equal(0, report.ExitCode);
        Assert.Equal(AutostartText.UndoneByToggle, report.Message);
        Assert.Equal(RegValue.Absent, f.Engine.Registry.Get(OneDriveKey, "OneDrive"));
        var entry = Assert.Single(f.Engine.Journal.Current.Entries);
        Assert.Equal(JournalEntryStatus.Undone, entry.Status);
    }

    [Fact]
    public void UndoAutostart_Active_UndoneExit0()
    {
        var f = new Fixture();
        f.Run(CliCommand.SetAutostart, OneDriveId, AutostartState.Disabled);

        var report = f.Run(CliCommand.UndoAutostart, OneDriveId);

        Assert.Equal(TweakOutcome.Undone, report.Outcome);
        Assert.Equal(CliExitCode.Success, report.Result);
        Assert.Equal(0, report.ExitCode);
        Assert.Equal(OneDriveId, report.TweakId);
        Assert.Equal(AutostartText.UndoneFromHistory, report.Message);
        Assert.Equal(RegValue.Absent, f.Engine.Registry.Get(OneDriveKey, "OneDrive"));
        Assert.Equal(JournalEntryStatus.Undone, Assert.Single(f.Engine.Journal.Current.Entries).Status);
    }

    [Fact]
    public void UndoAutostart_NoActive_Exit1()
    {
        var f = new Fixture();

        var report = f.Run(CliCommand.UndoAutostart, OneDriveId);

        Assert.Equal(TweakOutcome.Refused, report.Outcome);
        Assert.Equal(CliExitCode.OperationFailed, report.Result);
        Assert.Equal(1, report.ExitCode);
        Assert.Equal(AutostartText.NoActiveEntry, report.Message);
        Assert.Empty(f.Engine.Registry.WriteLog);
    }

    // Cofnięcie, które nie przywróciło stanu: kod 4, a raport mówi, czego nie przywrócono (pole notRestored).
    [Fact]
    public void UndoAutostart_RestoreFails_Exit4_ReportsNotRestored()
    {
        var f = new Fixture();
        f.Run(CliCommand.SetAutostart, OneDriveId, AutostartState.Disabled);
        f.Engine.Registry.FailWrite(OneDriveKey, "OneDrive");

        var report = f.Run(CliCommand.UndoAutostart, OneDriveId);

        Assert.Equal(TweakOutcome.UndoIncomplete, report.Outcome);
        Assert.Equal(CliExitCode.Incomplete, report.Result);
        Assert.Equal(4, report.ExitCode);
        Assert.Equal(new[] { OneDriveKey + "\\OneDrive = " + RegValue.Absent.ToDisplayString() }, report.NotRestored);
    }

    // Program jako administrator przy zalogowanym koncie: kontekst z konta zalogowanego, gałąź konta sprawdzana w HKU tego konta.
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void ListAutostart_AdminProcess_ContextFromLoggedOnUser(bool hiveLoaded, bool canToggle)
    {
        var f = new Fixture(processSid: AdminSid);
        if (hiveLoaded)
        {
            f.Engine.Registry.CreateKey(Locations.UserRoot);
        }

        var report = f.Run(CliCommand.ListAutostart);

        var context = Assert.Single(f.Reader.Contexts);
        Assert.Equal(UserSid, context.UserSid);
        Assert.Equal(AdminSid, context.ProcessSid);
        Assert.Equal(hiveLoaded, context.UserHiveLoaded);
        Assert.Equal(canToggle, report.AutostartRows!.Single(r => r.Id == OneDriveId).CanToggle);
    }

    [Fact]
    public void SetAutostart_ContextSeam_UsedInsteadOfDefault()
    {
        var f = new Fixture();
        f.Context = AutostartContext.Create(f.Engine.User, UserSid, f.Engine.Registry) with { UserCertain = false };

        var report = f.Run(CliCommand.SetAutostart, OneDriveId, AutostartState.Disabled);

        Assert.Equal(TweakOutcome.Refused, report.Outcome);
        Assert.Equal("Nie udało się ustalić zalogowanego użytkownika.", report.Message);
        Assert.Same(f.Context, Assert.Single(f.Reader.Contexts));
        Assert.Empty(f.Engine.Registry.WriteLog);
    }

    [Fact]
    public void Autostart_WithoutServices_Throws()
    {
        var f = new Fixture();

        Assert.Throws<ArgumentNullException>(() => CliRunner.Run(
            new CliOptions { Command = CliCommand.ListAutostart, TweakId = string.Empty, OutPath = @"C:\o.json" },
            Catalog(), f.Engine.CreateServices(), Array.Empty<string>(), null, null, null));
    }

    private static CliCatalogResult Catalog()
        => new(new LoadedCatalog(Array.Empty<TweakDefinition>(), "sha256:abc"), CliCatalog.BuiltInSource, Array.Empty<string>());

    private sealed class Fixture
    {
        private static readonly AutostartKnownCatalog KnownCatalog = AutostartKnownCatalog.Load(AutostartKnownCatalogTests.Wrap(ProtectedEntry));

        private readonly string _processSid;

        public Fixture(string processSid = UserSid)
        {
            _processSid = processSid;
            Engine.User.Sid = UserSid;
        }

        public EngineFixture Engine { get; } = new();

        public FakeAutostartReader Reader { get; } = new();

        public List<string> Warnings { get; } = new();

        public AutostartContext? Context { get; set; }

        public CliReport Run(CliCommand command, string id = "", AutostartState? target = null)
        {
            Reader.Reading = Reading();
            return CliRunner.Run(
                new CliOptions { Command = command, TweakId = id, AutostartTarget = target, OutPath = @"C:\o.json" },
                Catalog(),
                Engine.CreateServices(),
                Array.Empty<string>(),
                null,
                null,
                new CliAutostartServices { Reader = Reader, Catalog = KnownCatalog, ProcessSid = _processSid, Context = Context });
        }

        /// <summary>Odczyt jak ze skanera: przełączniki czytane z fałszywego rejestru przy każdym uruchomieniu.</summary>
        private AutostartReading Reading()
        {
            var measuredAt = new DateTimeOffset(2026, 11, 2, 8, 20, 5, TimeSpan.Zero);
            var oneDrive = new AutostartEntry
            {
                Id = OneDriveId,
                Source = AutostartSource.RunUser,
                EntryName = "OneDrive",
                Name = "Microsoft OneDrive",
                Location = Locations.RunKey(AutostartSource.RunUser),
                FileStatus = AutostartFileStatus.Exists,
                Switch = AutostartSwitchReader.Read(Engine.Registry, AutostartSwitchKind.StartupApproved, OneDriveKey, "OneDrive", enabledInManifest: false),
                Publisher = "Microsoft Corporation",
                FilePath = @"C:\Users\dso\AppData\Local\Microsoft\OneDrive\OneDrive.exe",
                Measurement = new StartupMeasurement(1_534_999, 3_002_368, measuredAt, false),
            };
            var security = new AutostartEntry
            {
                Id = SecurityHealthId,
                Source = AutostartSource.RunMachine,
                EntryName = "SecurityHealth",
                Name = "Windows Security notification icon",
                Location = Locations.RunKey(AutostartSource.RunMachine),
                FileStatus = AutostartFileStatus.Exists,
                Switch = AutostartSwitchReader.Read(
                    Engine.Registry, AutostartSwitchKind.StartupApproved, Locations.StartupApprovedKey(AutostartSource.RunMachine), "SecurityHealth", enabledInManifest: false),
                Publisher = "Microsoft Corporation",
                FilePath = SecurityHealthPath,
            };
            return new AutostartReading(
                new[] { security, oneDrive },
                UserEnvironment.FromVariables(new Dictionary<string, string> { ["SystemRoot"] = @"C:\Windows" }),
                measuredAt,
                Warnings.ToList());
        }
    }
}
