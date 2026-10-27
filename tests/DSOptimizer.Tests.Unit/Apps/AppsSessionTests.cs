using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Gui;
using DSOptimizer.Tests.Unit.Engine;
using DSOptimizer.Tests.Unit.Fakes;
using Xunit;

namespace DSOptimizer.Tests.Unit.Apps;

public class AppsSessionTests
{
    private const string UserSid = "S-1-5-21-1000";
    private const string AdminSid = "S-1-5-21-500";
    private const string OtherSid = "S-1-5-21-2000";
    private const string System32 = @"C:\Windows\System32";
    private const string UninstallRoot = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\";
    private const string MicrosoftDn = "CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US";

    private const string CatalogJson = """
        {
          "entries": [
            { "id": "copilot", "match": { "packageName": "Microsoft.Copilot" }, "group": "microsoft", "suggested": true,
              "summary": "Asystent AI.", "source": "test" },
            { "id": "spotify", "match": { "packageName": "SpotifyAB.SpotifyMusic" }, "group": "producent", "suggested": true,
              "summary": "Muzyka.", "source": "test" },
            { "id": "mcafee", "match": { "displayName": "McAfee*" }, "group": "trial", "suggested": true,
              "summary": "Trial antywirusa.", "warning": "Zostaw, jeśli to jedyny antywirus.", "source": "test" }
          ],
          "protected": [
            { "id": "calc", "match": { "packageName": "Microsoft.WindowsCalculator" }, "reason": "Kalkulator." }
          ]
        }
        """;

    private sealed class Fixture
    {
        public Fixture(string processSid = UserSid)
        {
            Engine.User.Sid = UserSid;
            Names.Names[UserSid] = "Dawid";
            Names.Names[AdminSid] = "Administrator";
            Names.Names[OtherSid] = "Jan";
            Gui = new GuiSession(new LoadedCatalog(new[] { TestTweaks.Scripted() }, "sha256:test"), Engine.CreateServices());
            Remover = new FakeAppRemover(Reader);
            var sleeper = new FakeSleeper(Engine.Clock);
            var removal = new AppRemovalEngine(
                Reader, Remover, Engine.Journal, Engine.RestorePoints, Engine.User, processSid, Engine.Log, Engine.Clock, sleeper.Sleep);
            Apps = new AppsSession(
                Gui, Reader, removal, AppsBloatCatalog.Load(CatalogJson), Protection, Names, Engine.User, processSid, _ => true, System32, Engine.Clock);
        }

        public EngineFixture Engine { get; } = new();

        public FakeInstalledAppsReader Reader { get; } = new();

        public FakeAppRemover Remover { get; }

        public FakeSystemProtection Protection { get; } = new();

        public FakeAccountNames Names { get; } = new();

        public GuiSession Gui { get; }

        public AppsSession Apps { get; }

        public IReadOnlyList<AppRow> Load(params InstalledApp[] apps)
        {
            Reader.Apps.AddRange(apps);
            return Apps.Load().Rows;
        }
    }

    private static InstalledApp Store(string package, string name, string? publisher = MicrosoftDn) => new(
        $"appx:{package}_8wekyb3d8bbwe",
        AppKind.Store,
        name,
        publisher,
        "1.0.0.0",
        AppScope.User,
        UserSid,
        new AppxInfo($"{package}_1.0.0.0_x64__8wekyb3d8bbwe", $"{package}_8wekyb3d8bbwe", package, false, false, false, "Store", true),
        null);

    private static InstalledApp Program(string key, string name, string? uninstall, string? quiet = null, AppScope scope = AppScope.Machine, string? owner = null) => new(
        "win32:" + UninstallRoot + key,
        AppKind.Program,
        name,
        "McAfee, LLC",
        "16.0",
        scope,
        owner,
        null,
        new Win32Info(UninstallRoot + key, uninstall, quiet, false, null, null, false));

    private static InstalledApp Copilot() => Store("Microsoft.Copilot", "Copilot");

    private static InstalledApp Spotify() => Store("SpotifyAB.SpotifyMusic", "Spotify", "CN=Spotify AB, O=Spotify AB, C=SE");

    private static InstalledApp Calculator() => Store("Microsoft.WindowsCalculator", "Kalkulator");

    private static InstalledApp McAfee() => Program("McAfee", "McAfee LiveSafe", "\"C:\\Program Files\\McAfee\\uninstall.exe\"");

    private static InstalledApp QuietTool() => Program("Tool", "Narzędzie", "\"C:\\Tool\\un.exe\"", "\"C:\\Tool\\un.exe\" /S");

    private static AppRow Row(IReadOnlyList<AppRow> rows, string name) => rows.Single(row => row.App.Name == name);

    private static string Fact(SeriesConfirmation confirmation, string name) => confirmation.Facts.Single(fact => fact.Name == name).Value;

    [Fact]
    public void Load_ClassifiesForLoggedInUser_AndPassesWarnings()
    {
        var f = new Fixture();
        f.Reader.Warnings.Add("Nie udało się odczytać aplikacji ze Sklepu. test");
        f.Reader.Apps.AddRange(new[] { Copilot(), Calculator(), McAfee(), Program("Spotify", "Spotify", "\"C:\\S\\u.exe\"", scope: AppScope.User, owner: OtherSid) });

        var page = f.Apps.Load();

        Assert.Equal(new[] { UserSid }, f.Reader.ReadSids);
        Assert.Equal(new[] { "Nie udało się odczytać aplikacji ze Sklepu. test" }, page.Warnings);
        Assert.Equal(AppGroup.Suggested, Row(page.Rows, "Copilot").Group);
        Assert.Equal(AppRemovability.Protected, Row(page.Rows, "Kalkulator").Removability);
        Assert.Equal(AppGroup.Suggested, Row(page.Rows, "McAfee LiveSafe").Group);
        var foreign = Row(page.Rows, "Spotify");
        Assert.Equal(AppRemovability.OtherAccount, foreign.Removability);
        Assert.Equal("Uruchom DSOptimizer na koncie Jan, żeby to usunąć.", foreign.BlockedReason);
    }

    [Fact]
    public void ConfirmationFor_Header_CountsAndOperation()
    {
        var f = new Fixture();
        var rows = f.Load(Copilot(), McAfee());

        var confirmation = f.Apps.ConfirmationFor(rows, restorePointSetting: false);

        Assert.Equal(GuiOperation.RemoveApps, confirmation.Operation);
        Assert.Equal("Usuń zaznaczone", confirmation.Title);
        Assert.Equal("2 aplikacje", confirmation.Subtitle);
        Assert.Equal("Usuń (2)", confirmation.PrimaryText);
        Assert.Equal(new[] { "Copilot", "McAfee LiveSafe" }, confirmation.Items.Select(item => item.Title));
        Assert.Equal("Niemożliwe", Fact(confirmation, "Cofanie"));
        Assert.Equal(AppsText.IrreversibleWarning, confirmation.Warnings[confirmation.Warnings.Count - 1]);
    }

    [Fact]
    public void ConfirmationFor_ProgramWithProtectionOn_RestorePointRequired()
    {
        var f = new Fixture();
        var rows = f.Load(Copilot(), McAfee());

        var confirmation = f.Apps.ConfirmationFor(rows, restorePointSetting: false);

        Assert.True(confirmation.CreatesRestorePoint);
        Assert.True(confirmation.RestorePointRequired);
        Assert.Equal("Tak (wymagany przy programach)", Fact(confirmation, "Punkt przywracania"));
        Assert.Equal("Tylko jeśli zgłosi go deinstalator", Fact(confirmation, "Restart"));
        Assert.Equal(new[] { AppsText.IrreversibleWarning }, confirmation.Warnings);
    }

    [Fact]
    public void ConfirmationFor_ProgramWithProtectionOff_SaysSoAndWarnsAboutQuestion()
    {
        var f = new Fixture();
        f.Protection.Enabled = false;
        var rows = f.Load(McAfee());

        var confirmation = f.Apps.ConfirmationFor(rows, restorePointSetting: true);

        Assert.True(confirmation.CreatesRestorePoint);
        Assert.Equal("Nie – ochrona systemu wyłączona", Fact(confirmation, "Punkt przywracania"));
        Assert.Equal(
            new[]
            {
                "Ochrona systemu jest wyłączona, więc punkt przywracania nie powstanie. Przed pierwszym usunięciem program zapyta, czy kontynuować bez niego.",
                AppsText.IrreversibleWarning,
            },
            confirmation.Warnings);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConfirmationFor_ProtectionUnknown_SaysItCouldNotBeChecked(bool throws)
    {
        var f = new Fixture();
        f.Protection.Enabled = null;
        if (throws)
        {
            f.Protection.Throw = new InvalidOperationException("WMI");
        }

        var rows = f.Load(McAfee());

        var confirmation = f.Apps.ConfirmationFor(rows, restorePointSetting: false);

        Assert.Equal("Tak, jeśli się uda – nie udało się sprawdzić ochrony systemu", Fact(confirmation, "Punkt przywracania"));
        Assert.Equal(
            "Nie udało się sprawdzić, czy ochrona systemu jest włączona. Jeśli punkt przywracania nie powstanie, przed pierwszym usunięciem program zapyta, czy kontynuować bez niego.",
            confirmation.Warnings[0]);
    }

    [Fact]
    public void ConfirmationFor_StoreOnly_SettingOn_FollowsSetting()
    {
        var f = new Fixture();
        var rows = f.Load(Copilot());

        var confirmation = f.Apps.ConfirmationFor(rows, restorePointSetting: true);

        Assert.True(confirmation.CreatesRestorePoint);
        Assert.False(confirmation.RestorePointRequired);
        Assert.Equal("Tak (według Ustawień)", Fact(confirmation, "Punkt przywracania"));
        Assert.Equal("Niepotrzebny", Fact(confirmation, "Restart"));
    }

    [Fact]
    public void ConfirmationFor_StoreOnly_SettingOff_NoRestorePointAndNoProtectionRead()
    {
        var f = new Fixture();
        var rows = f.Load(Copilot());

        var confirmation = f.Apps.ConfirmationFor(rows, restorePointSetting: false);

        Assert.False(confirmation.CreatesRestorePoint);
        Assert.Equal("Nie – wyłączony w Ustawieniach", Fact(confirmation, "Punkt przywracania"));
        Assert.Equal(0, f.Protection.Calls);
    }

    [Fact]
    public void ConfirmationFor_StoreOnly_ProtectionOff_WarnsWithoutQuestion()
    {
        var f = new Fixture();
        f.Protection.Enabled = false;
        var rows = f.Load(Copilot());

        var confirmation = f.Apps.ConfirmationFor(rows, restorePointSetting: true);

        Assert.Equal("Nie – ochrona systemu wyłączona", Fact(confirmation, "Punkt przywracania"));
        Assert.Equal("Ochrona systemu jest wyłączona, więc punkt przywracania nie powstanie.", confirmation.Warnings[0]);
    }

    [Fact]
    public void ConfirmationFor_SameAccount_StoreNotesNameUserAndImageOnlyForMicrosoft()
    {
        var f = new Fixture();
        var rows = f.Load(Copilot(), Spotify());

        var confirmation = f.Apps.ConfirmationFor(rows, restorePointSetting: false);

        var copilot = confirmation.Items.Single(item => item.Title == "Copilot");
        Assert.Equal("Aplikacja ze Sklepu, Microsoft Corporation", copilot.Summary);
        Assert.Equal(new[] { "Dla konta Dawid i z obrazu systemu, żeby nie wróciła na nowym koncie." }, copilot.Notes);
        var spotify = confirmation.Items.Single(item => item.Title == "Spotify");
        Assert.Equal("Aplikacja ze Sklepu, Spotify AB", spotify.Summary);
        Assert.Equal(new[] { "Dla konta Dawid." }, spotify.Notes);
        Assert.DoesNotContain(confirmation.Warnings, warning => warning.StartsWith("DSOptimizer działa na koncie", StringComparison.Ordinal));
    }

    [Fact]
    public void ConfirmationFor_ProcessAccountDiffers_WithStore_WarnsAllUsersR8()
    {
        var f = new Fixture(processSid: AdminSid);
        var rows = f.Load(Copilot(), Spotify());

        var confirmation = f.Apps.ConfirmationFor(rows, restorePointSetting: false);

        Assert.Contains(
            "DSOptimizer działa na koncie Administrator, innym niż zalogowane (Dawid): aplikacje ze Sklepu zostaną usunięte dla wszystkich kont na tym komputerze i z obrazu systemu.",
            confirmation.Warnings);
        Assert.All(confirmation.Items, item => Assert.Equal(new[] { "Dla wszystkich kont na tym komputerze i z obrazu systemu." }, item.Notes));
    }

    [Fact]
    public void ConfirmationFor_ProcessAccountDiffers_ProgramsOnly_NoR8Warning()
    {
        var f = new Fixture(processSid: AdminSid);
        var rows = f.Load(McAfee());

        var confirmation = f.Apps.ConfirmationFor(rows, restorePointSetting: false);

        Assert.DoesNotContain(confirmation.Warnings, warning => warning.StartsWith("DSOptimizer działa na koncie", StringComparison.Ordinal));
    }

    [Fact]
    public void ConfirmationFor_UnknownAccountNames_FallBackToSids()
    {
        var f = new Fixture(processSid: AdminSid);
        f.Names.Names.Clear();
        var rows = f.Load(Copilot());

        var confirmation = f.Apps.ConfirmationFor(rows, restorePointSetting: false);

        Assert.Contains(
            $"DSOptimizer działa na koncie {AdminSid}, innym niż zalogowane ({UserSid}): aplikacje ze Sklepu zostaną usunięte dla wszystkich kont na tym komputerze i z obrazu systemu.",
            confirmation.Warnings);
    }

    [Fact]
    public void ConfirmationFor_ProgramNotes_DescribeUninstallMode_AndCarryCatalogWarning()
    {
        var f = new Fixture();
        var rows = f.Load(McAfee(), QuietTool());

        var confirmation = f.Apps.ConfirmationFor(rows, restorePointSetting: false);

        var mcafee = confirmation.Items.Single(item => item.Title == "McAfee LiveSafe");
        Assert.Equal("Program, McAfee, LLC", mcafee.Summary);
        Assert.Equal(new[] { "Deinstalator producenta; jego okno pojawi się na ekranie, program poczeka na koniec." }, mcafee.Notes);
        Assert.Equal(new[] { "Zostaw, jeśli to jedyny antywirus." }, mcafee.Warnings);
        var quiet = confirmation.Items.Single(item => item.Title == "Narzędzie");
        Assert.Equal(new[] { "Cichy deinstalator producenta, bez okna; program poczeka na koniec." }, quiet.Notes);
    }

    [Fact]
    public void ConfirmationFor_ProtectedOrBlockedRow_Throws()
    {
        var f = new Fixture();
        var rows = f.Load(Copilot(), Calculator(), Program("Spotify", "Spotify", "\"C:\\S\\u.exe\"", scope: AppScope.User, owner: OtherSid));

        Assert.Throws<ArgumentException>(() => f.Apps.ConfirmationFor(new[] { Row(rows, "Copilot"), Row(rows, "Kalkulator") }, false));
        Assert.Throws<ArgumentException>(() => f.Apps.ConfirmationFor(new[] { Row(rows, "Spotify") }, false));
        Assert.Throws<ArgumentException>(() => f.Apps.ConfirmationFor(Array.Empty<AppRow>(), false));
        var disguised = Row(rows, "Kalkulator") with { Removability = AppRemovability.Removable };
        Assert.Throws<ArgumentException>(() => f.Apps.ConfirmationFor(new[] { disguised }, false));
    }

    [Fact]
    public void Remove_ProtectedRow_RefusedWithoutAnyAttempt()
    {
        var f = new Fixture();
        var rows = f.Load(Copilot(), Calculator());
        var disguised = Row(rows, "Kalkulator") with { Removability = AppRemovability.Removable };

        var report = f.Apps.Remove(new[] { Row(rows, "Copilot"), disguised }, new AppRemovalOptions());

        Assert.Null(report.Batch);
        Assert.Equal(GuiOperation.RemoveApps, report.Operation);
        var line = Assert.Single(report.Lines);
        Assert.Equal(GuiLogLevel.Warning, line.Level);
        Assert.Equal("Chronionej ani zablokowanej aplikacji nie da się usunąć – nic nie zmieniono.", line.Text);
        Assert.Equal(0, f.Remover.Attempts);
        Assert.Empty(f.Engine.RestorePoints.Descriptions);
    }

    [Fact]
    public async Task Remove_WhileTweakSeriesHoldsSessionLock_Refused()
    {
        var f = new Fixture();
        var rows = f.Load(Copilot());
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = Task.Run(() => f.Gui.TryRunExclusive(
            () =>
            {
                entered.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(30)));
                return 1;
            },
            out _));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(30)));

        var report = f.Apps.Remove(rows, new AppRemovalOptions());

        release.Set();
        Assert.True(await holder);
        Assert.Null(report.Batch);
        var line = Assert.Single(report.Lines);
        Assert.Equal("Trwa inna operacja – nic nie zmieniono.", line.Text);
        Assert.Equal(0, f.Remover.Attempts);
    }

    [Fact]
    public async Task TweakSeries_WhileRemovalRuns_Refused()
    {
        var f = new Fixture();
        var rows = f.Load(Copilot());
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        f.Remover.OnAttempt = () =>
        {
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(30)));
        };

        var removal = Task.Run(() => f.Apps.Remove(rows, new AppRemovalOptions()));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(30)));
        var batch = f.Gui.RunBatch(new[] { "test.script" }, GuiOperation.Apply, confirmed: true);
        release.Set();
        var report = await removal;

        Assert.Equal("Trwa inna operacja – nic nie zmieniono.", Assert.Single(batch.Lines).Text);
        Assert.Equal(TweakOutcome.Removed, Assert.Single(report.Batch!.Results).Outcome);
    }

    [Fact]
    public void Remove_Success_ReportsLinesBatchAndSucceededIds()
    {
        var f = new Fixture();
        var rows = f.Load(Copilot(), QuietTool(), McAfee());
        f.Remover.Keep.Add(QuietTool().Id);
        f.Remover.Keep.Add(McAfee().Id);
        f.Remover.ExitCode = 1603;
        var steps = new ListProgress<BatchStepEvent>();

        var report = f.Apps.Remove(rows, new AppRemovalOptions { Steps = steps });

        Assert.Equal(GuiOperation.RemoveApps, report.Operation);
        Assert.NotNull(report.Batch);
        Assert.Equal(new[] { Copilot().Id }, report.SucceededIds);
        Assert.Empty(report.Refreshed);
        Assert.Contains(report.Lines, line => line.Level == GuiLogLevel.Info && line.Text == "Copilot: Usunięto");
        Assert.Contains(report.Lines, line => line.Level == GuiLogLevel.Error && line.Text == "Narzędzie: Nie udało się – Aplikacja nadal jest na komputerze. " + AppsText.UninstallerExitCode(1603));
        Assert.Contains(report.Lines, line => line.Level == GuiLogLevel.Warning
            && line.Text == "McAfee LiveSafe: Nie potwierdzono – " + AppsText.InteractiveMayNotHaveFinished);
        Assert.Equal("Usuwanie: 1 z 3 usuniętych.", report.Lines[report.Lines.Count - 1].Text);
        Assert.Equal(new[] { "DSOptimizer – przed usunięciem aplikacji" }, f.Engine.RestorePoints.Descriptions);
        Assert.NotEmpty(steps.Items);
    }

    [Fact]
    public void Remove_RestorePointFailed_AnswerNo_AbortsBeforeAnyAttempt()
    {
        var f = new Fixture();
        f.Engine.RestorePoints.Next = new RestorePointResult(RestorePointStatus.ProtectionDisabled, "Ochrona systemu na dysku C: jest wyłączona.");
        var rows = f.Load(McAfee());
        RestorePointResult? asked = null;

        var report = f.Apps.Remove(rows, new AppRemovalOptions { ContinueWithoutRestorePoint = point => { asked = point; return false; } });

        Assert.Equal(RestorePointStatus.ProtectionDisabled, asked!.Status);
        Assert.True(report.Batch!.Aborted);
        Assert.Equal(0, f.Remover.Attempts);
        Assert.Contains(report.Lines, line => line.Level == GuiLogLevel.Error && line.Text.Contains(AppsText.AbortedWithoutRestorePoint));
    }
}
