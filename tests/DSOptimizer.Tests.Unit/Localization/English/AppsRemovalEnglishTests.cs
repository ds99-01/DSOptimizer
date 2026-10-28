using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Localization;
using DSOptimizer.Tests.Unit.Engine;
using DSOptimizer.Tests.Unit.Fakes;
using Xunit;

namespace DSOptimizer.Tests.Unit.Localization.English;

/// <summary>Strona „Aplikacje” i usuwanie aplikacji (Core/Apps) po angielsku: całe zdania, liczebniki, powody blokady, wyniki serii.</summary>
public class AppsRemovalEnglishTests
{
    private const string UserSid = "S-1-5-21-1000";
    private const string AdminSid = "S-1-5-21-500";
    private const string OtherSid = "S-1-5-21-2000";
    private const string System32 = @"C:\Windows\System32";
    private const string UninstallRoot = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\";
    private const string Match = "\"id\": \"a\", \"match\": { \"packageName\": \"P\" }";

    private const string CatalogJson = """
        {
          "entries": [
            { "id": "copilot", "match": { "packageName": "Microsoft.Copilot" }, "group": "microsoft", "suggested": true,
              "summary": "AI assistant.", "source": "test" },
            { "id": "mcafee", "match": { "displayName": "McAfee*" }, "group": "trial", "suggested": true,
              "summary": "Antivirus trial.", "source": "test" }
          ],
          "protected": [
            { "id": "calc", "match": { "packageName": "Microsoft.WindowsCalculator" }, "reason": "Calculator." }
          ]
        }
        """;

    /// <summary>Odczyt na niby: obecne Id; po próbie usunięcia odpowiedź może być wymuszona (np. tylko w obrazie systemu).</summary>
    private sealed class Reader : IInstalledAppsReader
    {
        public List<InstalledApp> Apps { get; } = new();

        public HashSet<string> Present { get; } = new(StringComparer.Ordinal);

        public AppPresence? Forced { get; set; }

        public InstalledAppsReading Read(string userSid) => new(Apps.ToList(), Array.Empty<string>());

        public AppPresence ReadPresence(InstalledApp app, string userSid, AppPresenceScope scope)
            => Forced ?? (Present.Contains(app.Id) ? AppPresence.Present : AppPresence.Absent);
    }

    /// <summary>Usuwanie na niby: <see cref="Removes"/> = aplikacja znika po próbie; kod deinstalatora i wynik skryptu AppX do ustawienia.</summary>
    private sealed class Remover : IAppRemover
    {
        private readonly Reader _reader;

        public Remover(Reader reader) => _reader = reader;

        public bool Removes { get; set; } = true;

        public int? ExitCode { get; set; } = 0;

        public ScriptResult StoreResult { get; set; } = new() { ExitCode = 0 };

        public Action? OnAttempt { get; set; }

        public ScriptResult RemoveStoreApp(AppxInfo appx, string userSid, StoreRemovalScope scope, bool deprovision, CancellationToken ct)
        {
            OnAttempt?.Invoke();
            Effect("appx:" + appx.PackageFamilyName);
            return StoreResult;
        }

        public UninstallerRun RunUninstaller(UninstallPlan plan, TimeSpan timeout, CancellationToken ct)
        {
            OnAttempt?.Invoke();
            Effect(_reader.Apps.First(app => app.Win32 is { } win32 && win32.UninstallString!.IndexOf(plan.FileName, StringComparison.OrdinalIgnoreCase) >= 0).Id);
            return new UninstallerRun(ExitCode, false);
        }

        private void Effect(string id)
        {
            if (Removes)
            {
                _reader.Present.Remove(id);
            }
        }
    }

    private sealed class Rig
    {
        public Rig(string processSid = UserSid)
        {
            Engine.User.Sid = UserSid;
            Names.Names[UserSid] = "Dawid";
            Names.Names[AdminSid] = "Administrator";
            Remover = new Remover(Reader);
            var sleeper = new FakeSleeper(Engine.Clock);
            Removal = new AppRemovalEngine(Reader, Remover, Engine.Journal, Engine.RestorePoints, Engine.User, processSid, Engine.Log, Engine.Clock, sleeper.Sleep);
            var gui = new GuiSession(new LoadedCatalog(new[] { TestTweaks.Scripted() }, "sha256:test"), Engine.CreateServices());
            Apps = new AppsSession(gui, Reader, Removal, AppsBloatCatalog.Load(CatalogJson), Protection, Names, Engine.User, processSid, _ => true, System32, Engine.Clock);
        }

        public EngineFixture Engine { get; } = new();

        public Reader Reader { get; } = new();

        public Remover Remover { get; }

        public FakeSystemProtection Protection { get; } = new();

        public FakeAccountNames Names { get; } = new();

        public AppRemovalEngine Removal { get; }

        public AppsSession Apps { get; }

        /// <summary>Wiersze listy (aplikacje obecne na komputerze).</summary>
        public IReadOnlyList<AppRow> Load(params InstalledApp[] apps)
        {
            Reader.Apps.AddRange(apps);
            foreach (var app in apps)
            {
                Reader.Present.Add(app.Id);
            }

            return Apps.Load().Rows;
        }

        public string[] RemoveLines(IReadOnlyList<AppRow> rows, AppRemovalOptions? options = null)
            => Apps.Remove(rows, options ?? new AppRemovalOptions()).Lines.Select(line => line.Text).ToArray();
    }

    private static InstalledApp Store(string package, string name) => new(
        $"appx:{package}_8wekyb3d8bbwe",
        AppKind.Store,
        name,
        "CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US",
        "1.0.0.0",
        AppScope.User,
        UserSid,
        new AppxInfo($"{package}_1.0.0.0_x64__8wekyb3d8bbwe", $"{package}_8wekyb3d8bbwe", package, false, false, false, "Store", true),
        null);

    private static InstalledApp Program(string key, string name, string? uninstall, AppScope scope = AppScope.Machine, string? owner = null, string? quiet = null) => new(
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

    private static InstalledApp McAfee() => Program("McAfee", "McAfee LiveSafe", "\"C:\\Program Files\\McAfee\\uninstall.exe\"");

    private static string Fact(SeriesConfirmation confirmation, string name) => confirmation.Facts.Single(fact => fact.Name == name).Value;

    [Fact]
    public void AppsPage_ListReadAt_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var at = new DateTime(2026, 10, 21, 14, 5, 30);
            Assert.Equal("List read: 14:05, 1 app", AppsPageText.ReadSummary(at, 1));
            Assert.Equal("List read: 14:05, 2 apps", AppsPageText.ReadSummary(at, 2));
            Assert.Equal("List read: 14:05, 21 apps", AppsPageText.ReadSummary(at, 21));
            Assert.Equal("The list hasn't been read yet", AppsPageText.NotRead);
            Assert.Equal("Reading the list", AppsPageText.Reading);
        }
    }

    [Fact]
    public void AppsPage_GroupCounts_English()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("Suggested for removal", AppsPageText.GroupName(AppGroup.Suggested));
            Assert.Equal("Other", AppsPageText.GroupName(AppGroup.Other));
            Assert.Equal("Protected", AppsPageText.GroupName(AppGroup.Protected));
            Assert.Equal("Suggested for removal (trial version)", AppsPageText.GroupDetail(AppGroup.Suggested, "trial"));
            Assert.Equal("Suggested for removal (Microsoft)", AppsPageText.GroupDetail(AppGroup.Suggested, "microsoft"));
            Assert.Equal("Other (computer manufacturer)", AppsPageText.GroupDetail(AppGroup.Other, "producent"));

            Assert.Equal("Double-click a row or checkbox to select it for removal.", AppsPageText.SelectionBarTitle(0));
            Assert.Equal("1 app selected for removal", AppsPageText.SelectionBarTitle(1));
            Assert.Equal("2 apps selected for removal", AppsPageText.SelectionBarTitle(2));
            Assert.Equal("22 apps selected for removal", AppsPageText.SelectionBarTitle(22));

            Assert.Equal("1 app will finish removal after the computer restarts.", AppsPageText.RemovalRebootText(1));
            Assert.Equal("2 apps will finish removal after the computer restarts.", AppsPageText.RemovalRebootText(2));

            Assert.Equal("Removal: 1 of 2 removed.", AppsPageText.BatchSummary(1, 2));
        }
    }

    [Fact]
    public void AppsPage_Labels_English()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("Apps", AppsPageText.PageTitle);
            Assert.Equal("Search by name or publisher", AppsPageText.SearchPlaceholder);
            Assert.Equal("Refresh list", AppsPageText.RefreshButton);
            Assert.Equal("Protected – DSOptimizer doesn't remove it", AppsPageText.ProtectedTooltip);
            Assert.Equal("Selected for removal", AppsPageText.SelectedLine);
            Assert.Equal("Not selected – double-click the row to add it", AppsPageText.NotSelectedLine);
            Assert.Equal("Technical details", AppsPageText.TechnicalHeader);
            Assert.Equal("No apps in this group.", AppsPageText.EmptyGroup);
            Assert.Equal("No app in this group matches the search.", AppsPageText.EmptySearch);
            Assert.Equal("All accounts", AppsPageText.AllAccounts);
            Assert.Equal("unknown", AppsPageText.UnknownAccount);
            Assert.Equal("unknown", AppsPageText.UnknownValue);
            Assert.Equal("unknown", AppsPageText.UnknownPublisher);
            Assert.Equal("Not possible", AppsPageText.Irreversible);
            Assert.Equal("Store", AppsPageText.KindShort(AppKind.Store));
            Assert.Equal("Program", AppsPageText.KindShort(AppKind.Program));
            Assert.Equal("Store app", AppsPageText.KindLong(AppKind.Store));
            Assert.Equal("Program", AppsPageText.KindLong(AppKind.Program));
            Assert.Equal("Store app, Microsoft Corporation", AppsPageText.Summary(AppKind.Store, "Microsoft Corporation"));
        }
    }

    [Fact]
    public void AppsPage_SelectionBarDetails_WholeSentencesEnglish()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal(
                "The program will create a restore point before removing programs. Removal can't be undone.",
                AppsPageText.SelectionBarDetails(hasProgram: true, restorePointSetting: false));
            Assert.Equal(
                "The program will try to create a restore point before removing. Removal can't be undone.",
                AppsPageText.SelectionBarDetails(hasProgram: false, restorePointSetting: true));
            Assert.Equal("No restore point. Removal can't be undone.", AppsPageText.SelectionBarDetails(hasProgram: false, restorePointSetting: false));
        }
    }

    [Fact]
    public void AppsPage_StoreRemovalScope_WholeSentencesEnglish()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal(
                "For account Dawid and from the system image, so it doesn't come back on a new account.",
                AppsPageText.StoreRemoval("Dawid", fromImage: true, allUsers: false));
            Assert.Equal("For account Dawid.", AppsPageText.StoreRemoval(" Dawid ", fromImage: false, allUsers: false));
            Assert.Equal(
                "For the signed-in account and from the system image, so it doesn't come back on a new account.",
                AppsPageText.StoreRemoval(null, fromImage: true, allUsers: false));
            Assert.Equal("For the signed-in account.", AppsPageText.StoreRemoval(null, fromImage: false, allUsers: false));
            Assert.Equal("For all accounts on this computer and from the system image.", AppsPageText.StoreRemoval("Dawid", fromImage: false, allUsers: true));
        }
    }

    [Fact]
    public void AppsPage_ProgramRemoval_English()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal(
                "Manufacturer's uninstaller; its window will appear on the screen, and the program will wait for it to finish.",
                AppsPageText.ProgramRemoval(UninstallMode.Interactive));
            Assert.Equal("Manufacturer's silent uninstaller, no window; the program will wait for it to finish.", AppsPageText.ProgramRemoval(UninstallMode.Quiet));
            Assert.Equal("Windows Installer will remove it without a window; the program will wait for it to finish.", AppsPageText.ProgramRemoval(UninstallMode.Msi));
            Assert.Equal("Manufacturer's uninstaller; its window will appear on the screen", AppsPageText.ProgramRemovalBrief(UninstallMode.Interactive));
            Assert.Equal("Manufacturer's silent uninstaller, no window", AppsPageText.ProgramRemovalBrief(UninstallMode.Quiet));
            Assert.Equal("Windows Installer, no window", AppsPageText.ProgramRemovalBrief(UninstallMode.Msi));
        }
    }

    [Fact]
    public void AppsPage_OutcomeNamesAndFailureDetail_English()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("Removed", AppsPageText.OutcomeName(TweakOutcome.Removed));
            Assert.Equal("Not confirmed", AppsPageText.OutcomeName(TweakOutcome.NotConfirmed));
            Assert.Equal("Skipped", AppsPageText.OutcomeName(TweakOutcome.Skipped));
            Assert.Equal("Refused", AppsPageText.OutcomeName(TweakOutcome.Refused));
            Assert.Equal("Failed", AppsPageText.OutcomeName(TweakOutcome.Failed));
            Assert.Equal("Failed", AppsPageText.FailedStatus);
            Assert.Equal("The app is still on the computer.", AppsPageText.FailureDetail(AppsText.StillPresent));
            Assert.Equal("Unexpected error: x", AppsPageText.FailureDetail("Unexpected error: x"));
        }
    }

    [Fact]
    public void Confirmation_FactsWarningsAndItems_English()
    {
        var rig = new Rig();
        var rows = rig.Load(Copilot(), McAfee());
        rig.Protection.Enabled = false;

        using (L.Use(UiLanguage.English))
        {
            var confirmation = rig.Apps.ConfirmationFor(rows, restorePointSetting: false);

            Assert.Equal("Remove selected", confirmation.Title);
            Assert.Equal("2 apps", confirmation.Subtitle);
            Assert.Equal("Remove (2)", confirmation.PrimaryText);
            Assert.Equal("No – System Protection is turned off", Fact(confirmation, "Restore point"));
            Assert.Equal("Only if the uninstaller asks for it", Fact(confirmation, "Restart"));
            Assert.Equal("Not possible", Fact(confirmation, "Undo"));
            Assert.Equal(
                new[]
                {
                    "System Protection is turned off, so a restore point won't be created. Before the first removal, the program will ask whether to continue without it.",
                    "You can reinstall Store apps from Microsoft Store; programs can't be restored from DSOptimizer.",
                },
                confirmation.Warnings);
            Assert.Equal("Store app, Microsoft Corporation", confirmation.Items[0].Summary);
            Assert.Equal("For account Dawid and from the system image, so it doesn't come back on a new account.", Assert.Single(confirmation.Items[0].Notes));
            Assert.Equal("Program, McAfee, LLC", confirmation.Items[1].Summary);
        }
    }

    [Fact]
    public void Confirmation_SingleAppAndRestorePointStates_English()
    {
        var rig = new Rig();
        var store = rig.Load(Copilot());
        var program = new[] { rig.Load(McAfee()).Single(row => row.App.Kind == AppKind.Program) };

        using (L.Use(UiLanguage.English))
        {
            var one = rig.Apps.ConfirmationFor(store, restorePointSetting: true);
            Assert.Equal("1 app", one.Subtitle);
            Assert.Equal("Remove (1)", one.PrimaryText);
            Assert.Equal("Yes (as set in Settings)", Fact(one, "Restore point"));
            Assert.Equal("Not needed", Fact(one, "Restart"));
            Assert.Equal("No – turned off in Settings", Fact(rig.Apps.ConfirmationFor(store, restorePointSetting: false), "Restore point"));
            Assert.Equal("Yes (required for programs)", Fact(rig.Apps.ConfirmationFor(program, restorePointSetting: false), "Restore point"));

            rig.Protection.Enabled = null;
            var unknown = rig.Apps.ConfirmationFor(program, restorePointSetting: false);
            Assert.Equal("Yes, if possible – couldn't check System Protection", Fact(unknown, "Restore point"));
            Assert.Equal(
                "Couldn't check whether System Protection is turned on. If a restore point isn't created, the program will ask before the first removal whether to continue without it.",
                unknown.Warnings[0]);

            rig.Protection.Enabled = false;
            Assert.Equal(
                "System Protection is turned off, so a restore point won't be created.",
                rig.Apps.ConfirmationFor(store, restorePointSetting: true).Warnings[0]);
        }
    }

    [Fact]
    public void Confirmation_RunsAsOtherAccount_WholeSentenceEnglish()
    {
        var rig = new Rig(processSid: AdminSid);
        var rows = rig.Load(Copilot());

        using (L.Use(UiLanguage.English))
        {
            var confirmation = rig.Apps.ConfirmationFor(rows, restorePointSetting: false);

            Assert.Equal(
                "DSOptimizer is running on account Administrator, not the signed-in account (Dawid): Store apps will be removed for all accounts on this computer and from the system image.",
                confirmation.Warnings[0]);
            Assert.Equal("For all accounts on this computer and from the system image.", Assert.Single(confirmation.Items[0].Notes));
        }
    }

    [Fact]
    public void Removability_Reasons_English()
    {
        var reader = new Reader();
        var apps = new[]
        {
            Store("Microsoft.WindowsCalculator", "Calculator"),
            Program("Known", "Known", "\"C:\\K\\u.exe\"", AppScope.User, OtherSid),
            Program("Nameless", "Nameless", "\"C:\\N\\u.exe\"", AppScope.User, "S-1-5-21-3000"),
            Program("NoCommand", "No command", "cmd.exe /c del x"),
            Program("Fine", "Fine", "\"C:\\F\\u.exe\""),
        };
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [OtherSid] = "Jan" };
        var catalog = AppsBloatCatalog.Load(CatalogJson);

        using (L.Use(UiLanguage.English))
        {
            var rows = AppClassifier.Classify(apps, catalog, UserSid, userIsCertain: true, UserSid, sid => names.TryGetValue(sid, out var n) ? n : null, _ => true, System32);
            var uncertain = AppClassifier.Classify(new[] { Copilot() }, catalog, UserSid, userIsCertain: false, UserSid, _ => null, _ => true, System32);

            var reasons = rows.Concat(uncertain).ToDictionary(row => row.Removability == AppRemovability.OtherAccount ? row.App.Name : row.Removability.ToString(), row => row.BlockedReason);
            // Protected: powód z apps-bloat.json (dane listy, Etap 3); strona pokazuje przy nim ProtectedTooltip.
            Assert.Equal("Calculator.", reasons[nameof(AppRemovability.Protected)]);
            Assert.Equal("Protected – DSOptimizer doesn't remove it", AppsPageText.ProtectedTooltip);
            Assert.Equal("Run DSOptimizer on account Jan to remove it.", reasons["Known"]);
            Assert.Equal("Run DSOptimizer on the account that installed it to remove it.", reasons["Nameless"]);
            Assert.Equal("Couldn't determine the signed-in user.", reasons[nameof(AppRemovability.UserUncertain)]);
            Assert.Equal("The uninstaller can't be run safely.", reasons[nameof(AppRemovability.NoSafeCommand)]);
            Assert.Null(reasons[nameof(AppRemovability.Removable)]);
            Assert.Equal(
                Enum.GetValues(typeof(AppRemovability)).Cast<AppRemovability>().OrderBy(value => value),
                rows.Concat(uncertain).Select(row => row.Removability).Distinct().OrderBy(value => value));
        }
    }

    [Fact]
    public void Removal_AlreadyGone_English()
    {
        var rig = new Rig();
        var rows = rig.Load(McAfee());
        rig.Reader.Present.Clear();

        using (L.Use(UiLanguage.English))
        {
            var lines = rig.RemoveLines(rows);

            Assert.Equal("It was no longer on the computer.", AppsText.AlreadyGone);
            Assert.Equal(new[] { "McAfee LiveSafe: Removed – It was no longer on the computer.", "Removal: 1 of 1 removed." }, lines);
        }
    }

    [Fact]
    public void Removal_ImageOnly_English()
    {
        var rig = new Rig();
        var rows = rig.Load(Copilot());
        rig.Remover.StoreResult = new ScriptResult { ExitCode = 1, Errors = new[] { "0x80070005: access denied." } };
        rig.Remover.OnAttempt = () => rig.Reader.Forced = AppPresence.ImageOnly;

        using (L.Use(UiLanguage.English))
        {
            var lines = rig.RemoveLines(rows);

            Assert.Equal(
                new[]
                {
                    "Copilot: Failed – Removed from the account, but it's still in the system image (it will come back on a new account). Removal error: 0x80070005: access denied.",
                    "Removal: 0 of 1 removed.",
                },
                lines);
        }
    }

    [Fact]
    public void Removal_Deferred_English()
    {
        var rig = new Rig();
        var rows = rig.Load(McAfee());
        rig.Remover.ExitCode = 3010;
        rig.Remover.Removes = false;

        using (L.Use(UiLanguage.English))
        {
            var lines = rig.RemoveLines(rows);

            Assert.Equal(
                new[]
                {
                    "McAfee LiveSafe: Not confirmed – The uninstaller will finish the removal after the computer restarts.",
                    "Restart required so the uninstaller can finish the removal.",
                    "Removal: 0 of 1 removed.",
                },
                lines);
            Assert.Equal("1 app will finish removal after the computer restarts.", AppsPageText.RemovalRebootText(1));
        }
    }

    [Fact]
    public void Removal_GoneWithExitCode_English()
    {
        var rig = new Rig();
        var rows = rig.Load(McAfee());
        rig.Remover.ExitCode = 5;

        using (L.Use(UiLanguage.English))
        {
            var lines = rig.RemoveLines(rows);

            Assert.Equal(new[] { "McAfee LiveSafe: Removed", "The uninstaller ended with code 5.", "Removal: 1 of 1 removed." }, lines);
        }
    }

    [Fact]
    public void Removal_StillPresent_WholeSentenceEnglish()
    {
        var rig = new Rig();
        var rows = rig.Load(Program("Tool", "Tool", "\"C:\\Tool\\un.exe\"", quiet: "\"C:\\Tool\\un.exe\" /S"));
        rig.Remover.ExitCode = 1603;
        rig.Remover.Removes = false;

        using (L.Use(UiLanguage.English))
        {
            var result = Assert.Single(rig.Removal.Remove(rows, new AppRemovalOptions(), CancellationToken.None).Results);

            Assert.Equal("Failed – the app is still on the computer. The uninstaller ended with code 1603.", result.Message);
            Assert.Equal("The app is still on the computer. The uninstaller ended with code 1603.", AppsPageText.FailureDetail(result.Message!));
        }
    }

    [Fact]
    public void Removal_NotConfirmedMessages_English()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("Not confirmed – the uninstaller is still running.", AppsText.NotConfirmed);
            Assert.Equal("The uninstaller may not have finished – check the list after refreshing it.", AppsText.InteractiveMayNotHaveFinished);
            Assert.Equal("The uninstaller may be finishing in the background – check the list after refreshing it.", AppsText.QuietMayFinishInBackground);
            Assert.Equal("Couldn't check whether the app is gone.", AppsText.PresenceUnknown);
            Assert.Equal("Not confirmed – verification was interrupted, check the list after refreshing it.", AppsText.VerificationInterrupted);
            Assert.Equal("Not confirmed – removal of the Store app was interrupted.", AppsText.StoreRemovalInterrupted(null));
            Assert.Equal("Not confirmed – removal of the Store app was interrupted: time limit exceeded.", AppsText.StoreRemovalInterrupted(" time limit exceeded. "));
            Assert.Equal("Removed for all accounts on this computer.", AppsText.RemovedForAllUsers);
            Assert.Equal("If anything is left behind, use the manufacturer's tool: https://example.invalid/x", AppsText.CleanupHint("https://example.invalid/x"));
            Assert.Equal("Removal error: boom", AppsText.RemovalError("boom"));
            Assert.Equal("Couldn't read Store apps. x", AppsText.AppxReadFailed("x"));
            Assert.Equal("Couldn't read programs from the registry. x", AppsText.RegistryReadFailed("x"));
            Assert.Equal("Skipped unreadable program entries in the registry: 3.", AppsText.RegistryKeysSkipped(3));
        }
    }

    [Fact]
    public void Removal_RestorePointQuestion_English()
    {
        var rig = new Rig();
        var rows = rig.Load(McAfee(), Copilot());
        rig.Engine.RestorePoints.Next = new RestorePointResult(RestorePointStatus.ProtectionDisabled, "System Protection is off.");

        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("Continue without a restore point?", AppsText.ContinueWithoutRestorePoint);
            Assert.Equal(
                "Nothing has been removed yet. Without a restore point, you can't bring back a removed program with System Restore either.",
                AppsPageText.ContinueWithoutRestorePointBody);
            Assert.Equal("\"No\" ends the batch without removing anything.", AppsPageText.ContinueWithoutRestorePointNote);
            Assert.Equal("Yes, continue", AppsPageText.ContinueButton);
            Assert.Equal("No", AppsPageText.DeclineButton);
            Assert.Equal("DSOptimizer – before removing apps", AppRemovalEngine.RestorePointDescription);

            var batch = rig.Removal.Remove(rows, new AppRemovalOptions(), CancellationToken.None);

            Assert.Equal("DSOptimizer – before removing apps", Assert.Single(rig.Engine.RestorePoints.Descriptions));
            Assert.Equal(
                "The restore point wasn't created (ProtectionDisabled): System Protection is off. Batch interrupted before the first removal – no restore point.",
                batch.AbortReason);
            Assert.All(batch.Results, result => Assert.Equal("Batch interrupted before it started.", result.Message));
        }
    }

    [Fact]
    public void Removal_Refusals_English()
    {
        var rig = new Rig();
        var protectedRows = rig.Load(Store("Microsoft.WindowsCalculator", "Calculator"));

        using (L.Use(UiLanguage.English))
        {
            Assert.Equal(new[] { "A protected or blocked app can't be removed – nothing was changed." }, rig.RemoveLines(protectedRows));
        }
    }

    [Fact]
    public void Catalog_ValidationMessages_English()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("apps-bloat: (file), field json: invalid JSON – x", Message("{", "x"));
            Assert.Equal("apps-bloat: (file), field entries: missing array \"entries\"", Message("""{ "protected": [] }"""));
            Assert.Equal("apps-bloat: (entry 1), field entries: the element must be an object", Message("""{ "entries": [ 1 ], "protected": [] }"""));
            Assert.Equal("apps-bloat: (entry 1), field id: id is required", Message("""{ "entries": [ { "id": " " } ], "protected": [] }"""));
            Assert.Equal("apps-bloat: a, field match: an object with a match rule is required", Message(Entry("\"id\": \"a\"")));
            Assert.Equal("apps-bloat: a, field match: specify packageName (AppX) or displayName/publisher (Win32)", Message(Entry("\"id\": \"a\", \"match\": {}")));
            Assert.Equal(
                "apps-bloat: a, field match: packageName (AppX) can't be combined with displayName/publisher (Win32)",
                Message(Entry("\"id\": \"a\", \"match\": { \"packageName\": \"P\", \"publisher\": \"X\" }")));
            Assert.Equal("apps-bloat: a, field packageName: the value must be text", Message(Entry("\"id\": \"a\", \"match\": { \"packageName\": 1 }")));
            Assert.Equal("apps-bloat: a, field group: the field is required and can't be empty", Message(Entry(Match)));
            Assert.Equal("apps-bloat: a, field group: allowed: microsoft, producent, trial", Message(Entry(Match + ", \"group\": \"x\"")));
            Assert.Equal("apps-bloat: a, field suggested: a true/false value is required", Message(Entry(Match + ", \"group\": \"trial\"")));
            Assert.Equal(
                "apps-bloat: a, field summary: at most 160 characters",
                Message(Entry(Match + ", \"group\": \"trial\", \"suggested\": true, \"summary\": \"" + new string('x', 161) + "\"")));
            Assert.Equal(
                "apps-bloat: a, field cleanupUrl: the address must be an absolute https URL",
                Message(Entry(Match + ", \"group\": \"trial\", \"suggested\": true, \"summary\": \"s\", \"source\": \"t\", \"cleanupUrl\": \"http://x\"")));
            Assert.Equal(
                "apps-bloat: A, field id: id appears more than once",
                Message("""{ "entries": [], "protected": [ { "id": "a", "match": { "packageName": "P" }, "reason": "r" }, { "id": "A", "match": { "packageName": "Q" }, "reason": "r" } ] }"""));
        }
    }

    [Fact]
    public void AppxResults_FormatErrors_English()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.StartsWith("The AppX read result isn't valid JSON: ", Parse("{"), StringComparison.Ordinal);
            Assert.Equal("The AppX read result contains an element that isn't an object.", Parse("[1]"));
            Assert.Equal("The AppX read result is neither an array nor an object.", Parse("1"));
            Assert.Equal("The AppX read result is missing field PackageFullName.", Parse("""{ "Name": "N" }"""));
            Assert.StartsWith("Invalid AppX presence read result: ", Assert.Throws<FormatException>(() => AppxScript.ParsePresence("{")).Message, StringComparison.Ordinal);
            Assert.Equal(
                "Field Provisioned is missing from the AppX presence read result.",
                Assert.Throws<FormatException>(() => AppxScript.ParsePresence("""{ "Installed": false }""")).Message);
        }

        static string Parse(string json) => Assert.Throws<FormatException>(() => AppxPackages.Parse(json, UserSid, (_, raw) => raw)).Message;
    }

    private static string Entry(string body) => "{ \"entries\": [ { " + body + " } ], \"protected\": [] }";

    private static string Message(string json, string? jsonError = null)
    {
        var message = Assert.Throws<AppsCatalogException>(() => AppsBloatCatalog.Load(json)).Message;
        return jsonError is null ? message : message.Substring(0, message.IndexOf(" – ", StringComparison.Ordinal) + 3) + jsonError;
    }
}
