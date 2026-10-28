using System;
using System.Collections.Generic;
using System.Linq;
using DSOptimizer.Catalog;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Localization;
using Xunit;

namespace DSOptimizer.Tests.Unit.Apps;

public class AppClassifierTests
{
    private const string UserSid = "S-1-5-21-1000";
    private const string OtherSid = "S-1-5-21-2000";
    private const string System32 = @"C:\Windows\System32";
    private const string UninstallRoot = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\";

    private const string CatalogJson = """
        {
          "entries": [
            { "id": "ms.all", "match": { "packageName": "Microsoft.*" }, "group": "microsoft", "suggested": true,
              "summary": "Wszystko Microsoftu.", "source": "test" },
            { "id": "ms.news", "match": { "packageName": "Microsoft.BingNews" }, "group": "microsoft", "suggested": false,
              "summary": "Wiadomości.", "source": "test" },
            { "id": "lenovo", "match": { "displayName": "Lenovo Vantage*" }, "group": "producent", "suggested": false,
              "summary": "Asystent Lenovo.", "warning": "Może sterować baterią.", "source": "test" },
            { "id": "realtek.junk", "match": { "publisher": "Realtek*" }, "group": "producent", "suggested": true,
              "summary": "Dodatki Realtek.", "source": "test" },
            { "id": "mcafee", "match": { "displayName": "*mcafee*" }, "group": "trial", "suggested": true,
              "summary": "Trial antywirusa.", "source": "test" }
          ],
          "protected": [
            { "id": "prot.calc", "match": { "packageName": "Microsoft.WindowsCalculator" }, "reason": "Kalkulator." },
            { "id": "prot.realtek", "match": { "displayName": "Realtek Audio*" }, "reason": "Sterownik dźwięku." }
          ]
        }
        """;

    private static readonly AppsBloatCatalog Catalog = AppsBloatCatalog.Load(CatalogJson);

    private static InstalledApp Store(string package, string name = "App", string? sig = "Store", bool framework = false, bool resource = false, bool nonRemovable = false)
        => new(
            "appx:" + package + "_fam", AppKind.Store, name, "Microsoft Corporation", "1.0", AppScope.User, UserSid,
            new AppxInfo(package + "_1.0_x64__fam", package + "_fam", package, framework, resource, nonRemovable, sig, false), null);

    private static InstalledApp Program(
        string name,
        string? publisher = "Vendor",
        AppScope scope = AppScope.Machine,
        string? owner = null,
        string? uninstall = "\"C:\\Program Files\\V\\un.exe\"",
        string? quiet = null,
        bool systemComponent = false,
        string? parent = null,
        string? release = null)
    {
        var key = UninstallRoot + name;
        return new InstalledApp(
            "win32:" + key, AppKind.Program, name, publisher, "1.0", scope, owner,
            null, new Win32Info(key, uninstall, quiet, false, parent, release, systemComponent));
    }

    private static IReadOnlyList<AppRow> Classify(
        IReadOnlyList<InstalledApp> apps,
        bool userIsCertain = true,
        string processSid = UserSid,
        Func<string, string?>? accountName = null,
        Func<string, bool>? fileExists = null)
        => AppClassifier.Classify(apps, Catalog, UserSid, userIsCertain, processSid, accountName ?? (_ => null), fileExists ?? (_ => true), System32);

    private static AppRow One(InstalledApp app, bool userIsCertain = true, string processSid = UserSid, Func<string, string?>? accountName = null, Func<string, bool>? fileExists = null)
        => Assert.Single(Classify(new[] { app }, userIsCertain, processSid, accountName, fileExists));

    [Theory]
    [InlineData("framework")]
    [InlineData("resource")]
    [InlineData("nonRemovable")]
    [InlineData("systemSignature")]
    public void Hidden_Store(string reason)
    {
        var app = reason switch
        {
            "framework" => Store("Microsoft.VCLibs", framework: true),
            "resource" => Store("Microsoft.Foo", resource: true),
            "nonRemovable" => Store("Microsoft.Foo", nonRemovable: true),
            _ => Store("Microsoft.Foo", sig: "System"),
        };

        Assert.True(AppClassifier.IsHidden(app));
        Assert.Empty(Classify(new[] { app }));
    }

    [Fact]
    public void Hidden_StoreSignatureKindIsCaseInsensitive()
    {
        Assert.True(AppClassifier.IsHidden(Store("Microsoft.Foo", sig: "system")));
    }

    [Theory]
    [InlineData("systemComponent")]
    [InlineData("parent")]
    [InlineData("update")]
    [InlineData("hotfix")]
    [InlineData("security")]
    [InlineData("noCommands")]
    [InlineData("blankCommands")]
    public void Hidden_Program(string reason)
    {
        var app = reason switch
        {
            "systemComponent" => Program("A", systemComponent: true),
            "parent" => Program("A", parent: "KB123"),
            "update" => Program("A", release: "Update"),
            "hotfix" => Program("A", release: "Hotfix"),
            "security" => Program("A", release: "Security Update"),
            "noCommands" => Program("A", uninstall: null),
            _ => Program("A", uninstall: " ", quiet: ""),
        };

        Assert.True(AppClassifier.IsHidden(app));
        Assert.Empty(Classify(new[] { app }));
    }

    [Fact]
    public void Hidden_ProgramWithOnlyQuietCommand_IsVisible()
    {
        Assert.False(AppClassifier.IsHidden(Program("A", uninstall: null, quiet: "\"C:\\V\\un.exe\" /S")));
    }

    [Fact]
    public void Visible_ProgramWithOtherReleaseType_IsVisible()
    {
        Assert.False(AppClassifier.IsHidden(Program("A", release: "Application")));
    }

    [Fact]
    public void Protected_WinsOverBloatRule()
    {
        // Kalkulator pasuje też do szerokiej reguły śmieci „Microsoft.*” (suggested).
        var row = One(Store("Microsoft.WindowsCalculator", "Kalkulator"));

        Assert.Equal(AppGroup.Protected, row.Group);
        Assert.Equal(AppRemovability.Protected, row.Removability);
        Assert.Equal("Kalkulator.", row.BlockedReason);
        Assert.Null(row.BloatGroup);
        Assert.Null(row.Plan);
    }

    [Fact]
    public void ProtectedReason_English()
    {
        // Wbudowana lista wczytana po angielsku: powód „Chronione” z tabeli catalog-apps-bloat.json (AppX i Win32).
        AppsBloatCatalog catalog;
        using (L.Use(UiLanguage.English))
        {
            catalog = AppsBloatCatalog.Load(BuiltInCatalog.AppsBloatSource.Json);
        }

        var rows = AppClassifier.Classify(
            new[] { Store("Microsoft.WindowsCalculator", "Calculator"), Program("Remote Desktop Connection", "Microsoft Corporation") },
            catalog,
            UserSid,
            true,
            UserSid,
            _ => null,
            _ => true,
            System32);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Equal(AppGroup.Protected, row.Group));
        Assert.Equal("Calculator: a basic system tool.", rows[0].BlockedReason);
        Assert.Equal("Remote Desktop Connection: part of the system.", rows[1].BlockedReason);
    }

    [Fact]
    public void Protected_Win32ByDisplayName_WinsOverPublisherRule()
    {
        var row = One(Program("Realtek Audio Console", publisher: "Realtek Semiconductor Corp."));

        Assert.Equal(AppGroup.Protected, row.Group);
        Assert.Equal(AppRemovability.Protected, row.Removability);
        Assert.Equal("Sterownik dźwięku.", row.BlockedReason);
        Assert.NotNull(row.Plan);
    }

    [Fact]
    public void Protected_WinsOverOtherAccountUncertainAndNoCommand()
    {
        var app = Program("Realtek Audio Console", scope: AppScope.User, owner: OtherSid, uninstall: "x.exe");

        var row = One(app, userIsCertain: false);

        Assert.Equal(AppRemovability.Protected, row.Removability);
    }

    [Fact]
    public void Suggested_WhenEntrySuggested()
    {
        var row = One(Store("Microsoft.Clipchamp", "Clipchamp"));

        Assert.Equal(AppGroup.Suggested, row.Group);
        Assert.Equal("microsoft", row.BloatGroup);
        Assert.Equal("Wszystko Microsoftu.", row.Summary);
        Assert.Null(row.Warning);
        Assert.Equal(AppRemovability.Removable, row.Removability);
        Assert.Null(row.BlockedReason);
        Assert.Null(row.Plan);
    }

    [Fact]
    public void Other_WithWarning_WhenEntryNotSuggested()
    {
        var row = One(Program("Lenovo Vantage", publisher: "Lenovo"));

        Assert.Equal(AppGroup.Other, row.Group);
        Assert.Equal("producent", row.BloatGroup);
        Assert.Equal("Może sterować baterią.", row.Warning);
        Assert.Equal("Asystent Lenovo.", row.Summary);
        Assert.Equal(AppRemovability.Removable, row.Removability);
    }

    [Fact]
    public void FirstMatchingEntry_DecidesGroup()
    {
        // „Microsoft.BingNews” pasuje do ms.all (pierwszy, suggested) i ms.news (drugi, nie).
        var row = One(Store("Microsoft.BingNews", "Wiadomości"));

        Assert.Equal(AppGroup.Suggested, row.Group);
        Assert.Equal("Wszystko Microsoftu.", row.Summary);
    }

    [Fact]
    public void Unmatched_IsOther()
    {
        var row = One(Program("Jakiś program"));

        Assert.Equal(AppGroup.Other, row.Group);
        Assert.Null(row.BloatGroup);
        Assert.Null(row.Summary);
        Assert.Null(row.Warning);
        Assert.Null(row.CleanupUrl);
        Assert.Equal(AppRemovability.Removable, row.Removability);
    }

    [Fact]
    public void Matching_IsCaseInsensitiveWildcard()
    {
        var row = One(Program("Total MCAFEE Security", publisher: "Intel"));

        Assert.Equal(AppGroup.Suggested, row.Group);
        Assert.Equal("trial", row.BloatGroup);
    }

    [Fact]
    public void PackageNameRule_DoesNotMatchWin32_AndDisplayNameRule_DoesNotMatchStore()
    {
        var win32 = One(Program("Microsoft.Clipchamp"));
        var store = One(Store("Some.Package", "Lenovo Vantage"));

        Assert.Equal(AppGroup.Other, win32.Group);
        Assert.Null(win32.BloatGroup);
        Assert.Equal(AppGroup.Other, store.Group);
        Assert.Null(store.BloatGroup);
    }

    [Fact]
    public void PublisherRule_NeedsPublisher()
    {
        var row = One(Program("Realtek thing", publisher: null));

        Assert.Equal(AppGroup.Other, row.Group);
    }

    [Fact]
    public void PerUserProgram_OtherAccount_HasReasonWithName()
    {
        var row = One(Program("Zoom", scope: AppScope.User, owner: OtherSid), accountName: sid => sid == OtherSid ? "Jan" : null);

        Assert.Equal(AppRemovability.OtherAccount, row.Removability);
        Assert.Equal(AppsText.OtherAccount("Jan"), row.BlockedReason);
        Assert.Contains("Jan", row.BlockedReason, StringComparison.Ordinal);
    }

    [Fact]
    public void PerUserProgram_OtherAccount_WithoutName_UsesGenericReason()
    {
        var row = One(Program("Zoom", scope: AppScope.User, owner: OtherSid));

        Assert.Equal(AppRemovability.OtherAccount, row.Removability);
        Assert.Equal(AppsText.OtherAccount(null), row.BlockedReason);
    }

    [Fact]
    public void PerUserProgram_SameAccount_Removable()
    {
        var row = One(Program("Zoom", scope: AppScope.User, owner: UserSid));

        Assert.Equal(AppRemovability.Removable, row.Removability);
        Assert.Null(row.BlockedReason);
        Assert.NotNull(row.Plan);
    }

    [Fact]
    public void PerUserProgram_ProcessAccountDiffersFromLoggedUser_IsOtherAccount()
    {
        // Proces admina (inne konto niż zalogowany): wpis zalogowanego użytkownika nie jest „własnym” kontem procesu.
        var row = One(Program("Zoom", scope: AppScope.User, owner: UserSid), processSid: OtherSid);

        Assert.Equal(AppRemovability.OtherAccount, row.Removability);
    }

    [Fact]
    public void PerUserProgram_OwnedByProcessAccount_IsNotOtherAccountEvenIfSomeoneElseIsLoggedOn()
    {
        // Kryterium obcego konta: SID właściciela wpisu != SID procesu. Deinstalator i tak biegnie na tokenie właściciela.
        var row = One(Program("Zoom", scope: AppScope.User, owner: OtherSid), processSid: OtherSid);

        Assert.Equal(AppRemovability.Removable, row.Removability);
        Assert.Null(row.BlockedReason);
    }

    [Fact]
    public void UserUncertain_StoreAndPerUserNotRemovable_MachineProgramRemovable()
    {
        var rows = Classify(
            new[]
            {
                Store("Some.Package"),
                Program("PerUser", scope: AppScope.User, owner: UserSid),
                Program("Machine"),
            },
            userIsCertain: false);

        Assert.Equal(AppRemovability.UserUncertain, rows[0].Removability);
        Assert.Equal(AppsText.UserUncertain, rows[0].BlockedReason);
        Assert.Equal(AppRemovability.UserUncertain, rows[1].Removability);
        Assert.Equal(AppRemovability.Removable, rows[2].Removability);
    }

    [Fact]
    public void OtherAccount_WinsOverUserUncertain()
    {
        var row = One(Program("Zoom", scope: AppScope.User, owner: OtherSid), userIsCertain: false);

        Assert.Equal(AppRemovability.OtherAccount, row.Removability);
    }

    [Fact]
    public void UserUncertain_WinsOverNoSafeCommand()
    {
        var row = One(Program("Zoom", scope: AppScope.User, owner: UserSid, uninstall: "rundll32.exe x.dll,Y"), userIsCertain: false);

        Assert.Equal(AppRemovability.UserUncertain, row.Removability);
    }

    [Fact]
    public void Program_WithoutSafeCommand_NoSafeCommand()
    {
        var row = One(Program("Dziwny", uninstall: "rundll32.exe x.dll,Y"));

        Assert.Equal(AppRemovability.NoSafeCommand, row.Removability);
        Assert.Equal(AppsText.NoSafeCommand, row.BlockedReason);
        Assert.Null(row.Plan);
    }

    [Fact]
    public void Program_Removable_HasPlan_AndStoreHasNone()
    {
        var program = One(Program("Dobry"));
        var store = One(Store("Some.Package"));

        Assert.NotNull(program.Plan);
        Assert.Equal(@"C:\Program Files\V\un.exe", program.Plan!.FileName);
        Assert.Null(store.Plan);
        Assert.Equal(AppRemovability.Removable, store.Removability);
    }

    [Fact]
    public void Program_FileMissing_NoSafeCommand()
    {
        var row = One(Program("Dobry"), fileExists: _ => false);

        Assert.Equal(AppRemovability.NoSafeCommand, row.Removability);
    }

    [Fact]
    public void Classify_KeepsInputOrder_AndSkipsHidden()
    {
        var rows = Classify(new[] { Program("B"), Store("Microsoft.VCLibs", framework: true), Store("Some.Package", "A") });

        Assert.Equal(new[] { "B", "A" }, rows.Select(r => r.App.Name));
    }
}
