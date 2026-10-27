using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Cli;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Model;
using DSOptimizer.Tests.Unit.Engine;
using DSOptimizer.Tests.Unit.Fakes;
using Xunit;

namespace DSOptimizer.Tests.Unit.Cli;

public class CliRunnerAppsTests
{
    private const string UserSid = "S-1-5-21-1000";
    private const string AdminSid = "S-1-5-21-500";
    private const string UninstallRoot = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\";

    private const string CatalogJson = """
        {
          "entries": [
            { "id": "ms.news", "match": { "packageName": "Microsoft.BingNews" }, "group": "microsoft", "suggested": true,
              "summary": "Wiadomości.", "source": "test" }
          ],
          "protected": [
            { "id": "prot.calc", "match": { "packageName": "Microsoft.WindowsCalculator" }, "reason": "Kalkulator: podstawowe narzędzie systemu." }
          ]
        }
        """;

    private static readonly InstalledApp News = Store("Microsoft.BingNews", "Wiadomości");
    private static readonly InstalledApp Calculator = Store("Microsoft.WindowsCalculator", "Kalkulator");
    private static readonly InstalledApp Framework = Store("Microsoft.VCLibs.140.00", "VCLibs", framework: true);
    private static readonly InstalledApp Quiet = Program("DsoTestQuiet");

    /// <summary>Lista aplikacji i obecność: <see cref="Present"/> to Id aplikacji, które są w systemie.</summary>
    private sealed class FakeAppsReader : IInstalledAppsReader
    {
        public List<InstalledApp> Apps { get; } = new();

        public List<string> Warnings { get; } = new();

        public HashSet<string> Present { get; } = new(StringComparer.Ordinal);

        public List<string> ReadSids { get; } = new();

        public InstalledAppsReading Read(string userSid)
        {
            ReadSids.Add(userSid);
            return new InstalledAppsReading(Apps.ToList(), Warnings.ToList());
        }

        public AppPresence ReadPresence(InstalledApp app, string userSid, AppPresenceScope scope)
            => Present.Contains(app.Id) ? AppPresence.Present : AppPresence.Absent;
    }

    private sealed class FakeAppRemover : IAppRemover
    {
        private readonly FakeAppsReader _reader;

        public FakeAppRemover(FakeAppsReader reader) => _reader = reader;

        public List<(AppxInfo Appx, StoreRemovalScope Scope, bool Deprovision)> StoreCalls { get; } = new();

        public List<UninstallPlan> UninstallerCalls { get; } = new();

        public int ExitCode { get; set; }

        public int Attempts => StoreCalls.Count + UninstallerCalls.Count;

        public ScriptResult RemoveStoreApp(AppxInfo appx, string userSid, StoreRemovalScope scope, bool deprovision, CancellationToken ct)
        {
            StoreCalls.Add((appx, scope, deprovision));
            _reader.Present.Remove("appx:" + appx.PackageFamilyName);
            return new ScriptResult { ExitCode = 0 };
        }

        public UninstallerRun RunUninstaller(UninstallPlan plan, TimeSpan timeout, CancellationToken ct)
        {
            UninstallerCalls.Add(plan);
            _reader.Present.RemoveWhere(id => id.StartsWith("win32:", StringComparison.Ordinal));
            return new UninstallerRun(ExitCode, false);
        }
    }

    private sealed class Fixture
    {
        public Fixture(string processSid = UserSid)
        {
            Remover = new FakeAppRemover(Reader);
            Engine.User.Sid = UserSid;
            Apps = new CliAppServices
            {
                Reader = Reader,
                Remover = Remover,
                Catalog = AppsBloatCatalog.Load(CatalogJson),
                ProcessSid = processSid,
                AccountName = sid => sid == UserSid ? "dso" : null,
                FileExists = _ => true,
                System32 = @"C:\Windows\System32",
                Sleep = new FakeSleeper(Engine.Clock).Sleep,
            };
        }

        public EngineFixture Engine { get; } = new();

        public FakeAppsReader Reader { get; } = new();

        public FakeAppRemover Remover { get; }

        public CliAppServices Apps { get; }

        public Fixture With(params InstalledApp[] apps)
        {
            foreach (var app in apps)
            {
                Reader.Apps.Add(app);
                Reader.Present.Add(app.Id);
            }

            return this;
        }

        public CliReport Run(CliCommand command, string id = "", RestorePointPolicy policy = RestorePointPolicy.Required)
            => CliRunner.Run(
                new CliOptions { Command = command, TweakId = id, OutPath = @"C:\o.json", RestorePointPolicy = policy },
                new CliCatalogResult(new LoadedCatalog(Array.Empty<TweakDefinition>(), "sha256:abc"), CliCatalog.BuiltInSource, Array.Empty<string>()),
                Engine.CreateServices(),
                Array.Empty<string>(),
                Apps);
    }

    private static InstalledApp Store(string package, string name, bool framework = false)
        => new(
            "appx:" + package + "_8wekyb3d8bbwe", AppKind.Store, name, "Microsoft Corporation", "1.0", AppScope.User, UserSid,
            new AppxInfo(package + "_1.0_x64__8wekyb3d8bbwe", package + "_8wekyb3d8bbwe", package, framework, false, false, "Store", true), null);

    private static InstalledApp Program(string key, AppScope scope = AppScope.Machine, string? owner = null)
    {
        var path = (owner is null ? UninstallRoot : @"HKU\" + owner + @"\Software\Microsoft\Windows\CurrentVersion\Uninstall\") + key;
        var command = "\"C:\\DsoTest\\" + key + "\\DsoTestUninstaller.exe\" quiet " + key;
        return new InstalledApp(
            "win32:" + path, AppKind.Program, "DSO Test " + key, "DSOptimizer VM Tests", "1.0", scope, owner,
            null, new Win32Info(path, command, command, false, null, null, false));
    }

    [Fact]
    public void ListApps_ReportsClassifiedVisibleRows_ForLoggedOnUser_WithoutRemoving()
    {
        var f = new Fixture(processSid: AdminSid).With(News, Calculator, Framework, Quiet, Program("DsoTestUser", AppScope.User, UserSid));

        var report = f.Run(CliCommand.ListApps);

        Assert.Equal(CliExitCode.Success, report.Result);
        Assert.Equal(0, report.ExitCode);
        Assert.Equal(CliCommand.ListApps, report.Command);
        Assert.Null(report.TweakId);
        Assert.Null(report.Outcome);
        Assert.Equal(UserSid, Assert.Single(f.Reader.ReadSids));
        Assert.Equal(0, f.Remover.Attempts);
        Assert.Empty(f.Engine.RestorePoints.Descriptions);
        var apps = report.Apps!;
        Assert.Equal(4, apps.Count);
        Assert.DoesNotContain(apps, app => app.Id == Framework.Id);
        var news = apps.Single(app => app.Id == News.Id);
        Assert.Equal(AppKind.Store, news.Kind);
        Assert.Equal("Wiadomości", news.Name);
        Assert.Equal("Microsoft Corporation", news.Publisher);
        Assert.Equal(AppGroup.Suggested, news.Group);
        Assert.Equal(AppRemovability.Removable, news.Removability);
        var calculator = apps.Single(app => app.Id == Calculator.Id);
        Assert.Equal(AppGroup.Protected, calculator.Group);
        Assert.Equal(AppRemovability.Protected, calculator.Removability);
        var quiet = apps.Single(app => app.Id == Quiet.Id);
        Assert.Equal(AppKind.Program, quiet.Kind);
        Assert.Equal(AppGroup.Other, quiet.Group);
        Assert.Equal(AppRemovability.Removable, quiet.Removability);
        Assert.Equal(AppRemovability.OtherAccount, apps.Single(app => app.Name == "DSO Test DsoTestUser").Removability);
    }

    [Fact]
    public void ListApps_ReaderWarnings_LandInReport()
    {
        var f = new Fixture().With(News);
        f.Reader.Warnings.Add("Nie udało się odczytać programów z rejestru. x");

        var report = f.Run(CliCommand.ListApps);

        Assert.Equal(CliExitCode.Success, report.Result);
        Assert.Contains("Nie udało się odczytać programów z rejestru. x", report.Warnings);
    }

    [Fact]
    public void RemoveApp_ProtectedCalculator_RefusedExit1_NothingRuns()
    {
        var f = new Fixture().With(Calculator);

        var report = f.Run(CliCommand.RemoveApp, Calculator.Id, RestorePointPolicy.IfAvailable);

        Assert.Equal(TweakOutcome.Refused, report.Outcome);
        Assert.Equal(CliExitCode.OperationFailed, report.Result);
        Assert.Equal(1, report.ExitCode);
        Assert.Equal(Calculator.Id, report.TweakId);
        Assert.Contains("Kalkulator: podstawowe narzędzie systemu.", report.Message, StringComparison.Ordinal);
        Assert.Equal(0, f.Remover.Attempts);
        Assert.Empty(f.Engine.RestorePoints.Descriptions);
        Assert.Empty(f.Engine.Journal.Current.Entries);
        Assert.Null(report.Apps);
    }

    /// <summary>Obrona w głąb (Review Focus 1): wiersz w grupie Protected z Removability=Removable (np. przerobiony przez `with`) też jest odrzucany.</summary>
    [Fact]
    public void RemoveApp_ProtectedGroupWithRemovableFlag_RefusedExit1_EngineNotCalled()
    {
        var f = new Fixture().With(News);
        var apps = new CliAppServices
        {
            Reader = f.Apps.Reader,
            Remover = f.Apps.Remover,
            Catalog = f.Apps.Catalog,
            ProcessSid = f.Apps.ProcessSid,
            AccountName = f.Apps.AccountName,
            FileExists = f.Apps.FileExists,
            System32 = f.Apps.System32,
            Sleep = f.Apps.Sleep,
            Classify = reading => reading.Apps
                .Select(app => new AppRow(app, AppGroup.Protected, null, null, null, null, AppRemovability.Removable, null, null))
                .ToList(),
        };

        var report = CliRunner.Run(
            new CliOptions { Command = CliCommand.RemoveApp, TweakId = News.Id, OutPath = @"C:\o.json", RestorePointPolicy = RestorePointPolicy.Skip },
            new CliCatalogResult(new LoadedCatalog(Array.Empty<TweakDefinition>(), "sha256:abc"), CliCatalog.BuiltInSource, Array.Empty<string>()),
            f.Engine.CreateServices(),
            Array.Empty<string>(),
            apps);

        Assert.Equal(TweakOutcome.Refused, report.Outcome);
        Assert.Equal(CliExitCode.OperationFailed, report.Result);
        Assert.Equal(1, report.ExitCode);
        Assert.Equal(0, f.Remover.Attempts);
        Assert.Empty(f.Engine.RestorePoints.Descriptions);
        Assert.Empty(f.Engine.Journal.Current.Entries);
        Assert.Contains(News.Id, f.Reader.Present);
    }

    /// <summary>Strażnik CLI to ta sama reguła co pole wyboru (<see cref="AppRow.CanRemove"/>): program bez planu deinstalacji odpada przed silnikiem.</summary>
    [Fact]
    public void RemoveApp_RemovableProgramWithoutPlan_RefusedExit1_EngineNotCalled()
    {
        var f = new Fixture().With(Quiet);
        var apps = new CliAppServices
        {
            Reader = f.Apps.Reader,
            Remover = f.Apps.Remover,
            Catalog = f.Apps.Catalog,
            ProcessSid = f.Apps.ProcessSid,
            AccountName = f.Apps.AccountName,
            FileExists = f.Apps.FileExists,
            System32 = f.Apps.System32,
            Sleep = f.Apps.Sleep,
            Classify = reading => reading.Apps
                .Select(app => new AppRow(app, AppGroup.Other, null, null, null, null, AppRemovability.Removable, null, null))
                .ToList(),
        };

        var report = CliRunner.Run(
            new CliOptions { Command = CliCommand.RemoveApp, TweakId = Quiet.Id, OutPath = @"C:\o.json", RestorePointPolicy = RestorePointPolicy.IfAvailable },
            new CliCatalogResult(new LoadedCatalog(Array.Empty<TweakDefinition>(), "sha256:abc"), CliCatalog.BuiltInSource, Array.Empty<string>()),
            f.Engine.CreateServices(),
            Array.Empty<string>(),
            apps);

        Assert.Equal(TweakOutcome.Refused, report.Outcome);
        Assert.Equal(1, report.ExitCode);
        Assert.Equal(0, f.Remover.Attempts);
        Assert.Empty(f.Engine.RestorePoints.Descriptions);
        Assert.Empty(f.Engine.Journal.Current.Entries);
    }

    [Fact]
    public void ListApps_ClassifySeam_ReplacesBuiltInClassification()
    {
        var f = new Fixture().With(News);
        var apps = new CliAppServices
        {
            Reader = f.Apps.Reader,
            Remover = f.Apps.Remover,
            Catalog = f.Apps.Catalog,
            ProcessSid = f.Apps.ProcessSid,
            AccountName = f.Apps.AccountName,
            FileExists = f.Apps.FileExists,
            System32 = f.Apps.System32,
            Classify = reading => reading.Apps
                .Select(app => new AppRow(app, AppGroup.Other, null, null, null, null, AppRemovability.NoSafeCommand, "x", null))
                .ToList(),
        };

        var report = CliRunner.Run(
            new CliOptions { Command = CliCommand.ListApps, TweakId = string.Empty, OutPath = @"C:\o.json" },
            new CliCatalogResult(new LoadedCatalog(Array.Empty<TweakDefinition>(), "sha256:abc"), CliCatalog.BuiltInSource, Array.Empty<string>()),
            f.Engine.CreateServices(),
            Array.Empty<string>(),
            apps);

        var row = Assert.Single(report.Apps!);
        Assert.Equal(AppGroup.Other, row.Group);
        Assert.Equal(AppRemovability.NoSafeCommand, row.Removability);
    }

    [Fact]
    public void RemoveApp_ProgramOfOtherAccount_RefusedExit1_NothingRuns()
    {
        var user = Program("DsoTestUser", AppScope.User, UserSid);
        var f = new Fixture(processSid: AdminSid).With(user);

        var report = f.Run(CliCommand.RemoveApp, user.Id, RestorePointPolicy.IfAvailable);

        Assert.Equal(TweakOutcome.Refused, report.Outcome);
        Assert.Equal(1, report.ExitCode);
        Assert.Contains("Uruchom DSOptimizer na koncie dso", report.Message, StringComparison.Ordinal);
        Assert.Equal(0, f.Remover.Attempts);
        Assert.Empty(f.Engine.RestorePoints.Descriptions);
    }

    [Fact]
    public void RemoveApp_UnknownOrHiddenId_Exit1_NothingRuns()
    {
        var f = new Fixture().With(Framework);

        var report = f.Run(CliCommand.RemoveApp, Framework.Id);

        Assert.Equal(CliExitCode.OperationFailed, report.Result);
        Assert.Null(report.Outcome);
        Assert.Contains(Framework.Id, Assert.Single(report.Errors), StringComparison.Ordinal);
        Assert.Equal(0, f.Remover.Attempts);
        Assert.Empty(f.Engine.RestorePoints.Descriptions);
    }

    [Fact]
    public void RemoveApp_IdMatchesIgnoringCase()
    {
        var f = new Fixture().With(News);

        var report = f.Run(CliCommand.RemoveApp, News.Id.ToUpperInvariant(), RestorePointPolicy.Skip);

        Assert.Equal(TweakOutcome.Removed, report.Outcome);
        Assert.Equal(News.Id, report.TweakId);
    }

    [Fact]
    public void RemoveApp_StoreAppAsLoggedOnUser_RemovedForUserAndImage()
    {
        var f = new Fixture().With(News);

        var report = f.Run(CliCommand.RemoveApp, News.Id, RestorePointPolicy.Skip);

        Assert.Equal(TweakOutcome.Removed, report.Outcome);
        Assert.Equal(CliExitCode.Success, report.Result);
        Assert.Equal(News.Id, report.TweakId);
        var call = Assert.Single(f.Remover.StoreCalls);
        Assert.Equal(StoreRemovalScope.CurrentUser, call.Scope);
        Assert.True(call.Deprovision);
        Assert.DoesNotContain(AppsText.RemovedForAllUsers, report.Warnings);
        Assert.Null(report.RestorePoint);
        Assert.Empty(f.Engine.RestorePoints.Descriptions);
        Assert.Null(report.Apps);
    }

    [Fact]
    public void RemoveApp_StoreAppFromOtherAccount_AllUsersWithWarning()
    {
        var f = new Fixture(processSid: AdminSid).With(News);

        var report = f.Run(CliCommand.RemoveApp, News.Id, RestorePointPolicy.Skip);

        Assert.Equal(TweakOutcome.Removed, report.Outcome);
        Assert.Equal(StoreRemovalScope.AllUsers, Assert.Single(f.Remover.StoreCalls).Scope);
        Assert.Contains(AppsText.RemovedForAllUsers, report.Warnings);
    }

    /// <summary>R20: `required` (domyślne) znaczy to samo co przy --apply także dla serii samych AppX – bez punktu nic nie usuwamy.</summary>
    [Fact]
    public void RemoveApp_StoreAppRequired_RestorePointFails_AbortedBeforeRemoval_PackageStays()
    {
        var f = new Fixture().With(News);
        f.Engine.RestorePoints.Next = new RestorePointResult(RestorePointStatus.ProtectionDisabled, "Ochrona wyłączona.");

        var report = f.Run(CliCommand.RemoveApp, News.Id);

        Assert.Single(f.Engine.RestorePoints.Descriptions);
        Assert.Equal(RestorePointStatus.ProtectionDisabled, report.RestorePoint!.Status);
        Assert.True(report.Aborted);
        Assert.Equal(TweakOutcome.Skipped, report.Outcome);
        Assert.Equal(CliExitCode.OperationFailed, report.Result);
        Assert.Equal(1, report.ExitCode);
        Assert.Contains(AppsText.AbortedWithoutRestorePoint, report.Message, StringComparison.Ordinal);
        Assert.Equal(0, f.Remover.Attempts);
        Assert.Empty(f.Engine.Journal.Current.Entries);
        Assert.Contains(News.Id, f.Reader.Present);
    }

    [Fact]
    public void RemoveApp_StoreAppRequired_RestorePointCreated_Removed()
    {
        var f = new Fixture().With(News);

        var report = f.Run(CliCommand.RemoveApp, News.Id);

        Assert.Equal(RestorePointStatus.Created, report.RestorePoint!.Status);
        Assert.False(report.Aborted);
        Assert.Equal(TweakOutcome.Removed, report.Outcome);
        Assert.Equal(0, report.ExitCode);
    }

    [Fact]
    public void RemoveApp_StoreAppIfAvailable_RestorePointFails_ContinuesWithWarning()
    {
        var f = new Fixture().With(News);
        f.Engine.RestorePoints.Next = new RestorePointResult(RestorePointStatus.ProtectionDisabled, "Ochrona wyłączona.");

        var report = f.Run(CliCommand.RemoveApp, News.Id, RestorePointPolicy.IfAvailable);

        Assert.Single(f.Engine.RestorePoints.Descriptions);
        Assert.Equal(RestorePointStatus.ProtectionDisabled, report.RestorePoint!.Status);
        Assert.False(report.Aborted);
        Assert.Equal(TweakOutcome.Removed, report.Outcome);
        Assert.Equal(0, report.ExitCode);
        Assert.Contains(report.Warnings, w => w.IndexOf("Punkt przywracania nie powstał", StringComparison.Ordinal) >= 0);
    }

    [Fact]
    public void RemoveApp_ProgramRequired_RestorePointFails_AbortedBeforeRemoval()
    {
        var f = new Fixture().With(Quiet);
        f.Engine.RestorePoints.Next = new RestorePointResult(RestorePointStatus.ProtectionDisabled, "Ochrona wyłączona.");

        var report = f.Run(CliCommand.RemoveApp, Quiet.Id);

        Assert.True(report.Aborted);
        Assert.Equal(TweakOutcome.Skipped, report.Outcome);
        Assert.Equal(CliExitCode.OperationFailed, report.Result);
        Assert.Equal(1, report.ExitCode);
        Assert.Contains(AppsText.AbortedWithoutRestorePoint, report.Message, StringComparison.Ordinal);
        Assert.Equal(RestorePointStatus.ProtectionDisabled, report.RestorePoint!.Status);
        Assert.Equal(0, f.Remover.Attempts);
        Assert.Contains(Quiet.Id, f.Reader.Present);
    }

    [Theory]
    [InlineData(RestorePointPolicy.IfAvailable)]
    [InlineData(RestorePointPolicy.Skip)]
    public void RemoveApp_ProgramIfAvailableOrSkip_RestorePointStillForced_ContinuesWhenItFails(RestorePointPolicy policy)
    {
        var f = new Fixture().With(Quiet);
        f.Engine.RestorePoints.Next = new RestorePointResult(RestorePointStatus.Failed, "Błąd WMI.");

        var report = f.Run(CliCommand.RemoveApp, Quiet.Id, policy);

        Assert.Single(f.Engine.RestorePoints.Descriptions);
        Assert.Equal(RestorePointStatus.Failed, report.RestorePoint!.Status);
        Assert.False(report.Aborted);
        Assert.Equal(TweakOutcome.Removed, report.Outcome);
        Assert.Equal(0, report.ExitCode);
        Assert.Equal(@"C:\DsoTest\DsoTestQuiet\DsoTestUninstaller.exe", Assert.Single(f.Remover.UninstallerCalls).FileName);
    }

    [Fact]
    public void RemoveApp_UninstallerReturns3010_RemovedWithReboot()
    {
        var f = new Fixture().With(Quiet);
        f.Remover.ExitCode = 3010;

        var report = f.Run(CliCommand.RemoveApp, Quiet.Id, RestorePointPolicy.IfAvailable);

        Assert.Equal(TweakOutcome.Removed, report.Outcome);
        Assert.True(report.RequiresReboot);
        Assert.Equal(0, report.ExitCode);
    }

    [Theory]
    [InlineData(CliCommand.ListApps)]
    [InlineData(CliCommand.RemoveApp)]
    public void Run_AppCommandWithoutAppServices_Throws(CliCommand command)
        => Assert.Throws<ArgumentNullException>(() => CliRunner.Run(
            new CliOptions { Command = command, TweakId = "appx:x", OutPath = @"C:\o.json" },
            new CliCatalogResult(new LoadedCatalog(Array.Empty<TweakDefinition>(), "sha256:abc"), CliCatalog.BuiltInSource, Array.Empty<string>()),
            new EngineFixture().CreateServices(),
            Array.Empty<string>()));

    [Fact]
    public void Run_TweakCommand_HasNoAppsInReport()
    {
        var report = CliRunner.Run(
            new CliOptions { Command = CliCommand.Detect, TweakId = "test.two-values", OutPath = @"C:\o.json" },
            new CliCatalogResult(new LoadedCatalog(new[] { TestTweaks.TwoValues() }, "sha256:abc"), CliCatalog.BuiltInSource, Array.Empty<string>()),
            new EngineFixture().CreateServices(),
            Array.Empty<string>());

        Assert.Null(report.Apps);
    }
}
