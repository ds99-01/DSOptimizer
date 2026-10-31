using System;
using System.Collections.Generic;
using System.Linq;
using DSOptimizer.Catalog;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Localization;
using DSOptimizer.Tests.Unit.Catalog;
using Xunit;

namespace DSOptimizer.Tests.Unit.Apps;

public class AppsBloatCatalogTests
{
    private const string GoodEntry = """
        { "id": "ms.news", "match": { "packageName": "Microsoft.BingNews" }, "group": "microsoft", "suggested": true,
          "summary": "Wiadomości.", "source": "spec 6.2" }
        """;

    // Adresy sprawdzone pobraniem strony 2026-10-21 (zob. docs/apps/README.md). Nowy adres wymaga sprawdzenia i wpisu w README.
    private static readonly string[] VerifiedCleanupUrls =
    {
        "https://support.norton.com/sp/en/us/home/current/solutions/v60392881",

        // McAfee blokuje pobranie strony (timeout) i curl (403); adres podał i sprawdził ręcznie autor 2026-10-21.
        "https://www.mcafee.com/support/s/article/000002766?language=pl",
    };

    private static string Wrap(string entries, string prot = "")
        => "{ \"entries\": [" + entries + "], \"protected\": [" + prot + "] }";

    [Fact]
    public void BuiltInFile_LoadsAndHasUniqueIds()
    {
        var catalog = AppsBloatCatalog.Load(BuiltInCatalog.AppsBloatSource.Json);

        Assert.NotEmpty(catalog.Entries);
        Assert.NotEmpty(catalog.Protected);
        var ids = catalog.Entries.Select(e => e.Id).Concat(catalog.Protected.Select(p => p.Id)).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(catalog.Entries, e => Assert.False(string.IsNullOrWhiteSpace(e.Source)));
        Assert.All(catalog.Entries.Where(e => e.CleanupUrl is not null), e => Assert.Contains(e.CleanupUrl!, VerifiedCleanupUrls));
    }

    [Fact]
    public void BuiltInFile_ProtectsCalculatorAndStoreAndEdge()
    {
        var catalog = AppsBloatCatalog.Load(BuiltInCatalog.AppsBloatSource.Json);

        Assert.Contains(catalog.Protected, p => p.Match.PackageName == "Microsoft.WindowsCalculator");
        Assert.Contains(catalog.Protected, p => p.Match.PackageName == "Microsoft.WindowsStore");
        Assert.Contains(catalog.Protected, p => p.Match.DisplayName is not null && p.Match.DisplayName.StartsWith("Microsoft Edge", StringComparison.Ordinal));
    }

    [Fact]
    public void BuiltInFile_ProtectedPackagesStayProtected()
    {
        var catalog = AppsBloatCatalog.Load(BuiltInCatalog.AppsBloatSource.Json);
        var apps = catalog.Protected
            .Where(p => p.Match.PackageName is not null && !p.Match.PackageName.Contains('*'))
            .Select(p => new InstalledApp(
                "appx:" + p.Match.PackageName, AppKind.Store, p.Match.PackageName!, null, null, AppScope.User, "S-1",
                new AppxInfo("full", "fam", p.Match.PackageName!, false, false, false, "Store", false), null))
            .ToList();

        var rows = AppClassifier.Classify(apps, catalog, "S-1", true, "S-1", _ => null, _ => true, @"C:\Windows\System32");

        Assert.Equal(apps.Count, rows.Count);
        Assert.All(rows, r => Assert.Equal(AppGroup.Protected, r.Group));
    }

    [Fact]
    public void Load_ReadsAllFields()
    {
        var catalog = AppsBloatCatalog.Load(Wrap(
            """
            { "id": "a", "match": { "displayName": "Lenovo Vantage*", "publisher": "Lenovo*" }, "group": "producent", "suggested": false,
              "summary": "S", "warning": "W", "source": "Z", "cleanupUrl": "https://example.com/x" }
            """,
            """{ "id": "p", "match": { "packageName": "Microsoft.Paint" }, "reason": "Paint" }"""));

        var entry = Assert.Single(catalog.Entries);
        Assert.Equal("Lenovo Vantage*", entry.Match.DisplayName);
        Assert.Equal("Lenovo*", entry.Match.Publisher);
        Assert.Null(entry.Match.PackageName);
        Assert.Equal("producent", entry.Group);
        Assert.False(entry.Suggested);
        Assert.Equal("W", entry.Warning);
        Assert.Equal("https://example.com/x", entry.CleanupUrl);
        var prot = Assert.Single(catalog.Protected);
        Assert.Equal("Paint", prot.Reason);
    }

    public static IEnumerable<object?[]> InvalidFiles()
    {
        // json, oczekiwane id w komunikacie, oczekiwane pole
        yield return new object?[] { Wrap(GoodEntry + "," + GoodEntry), "ms.news", "id" };
        yield return new object?[] { Wrap(GoodEntry, """{ "id": "ms.news", "match": { "packageName": "X" }, "reason": "r" }"""), "ms.news", "id" };
        yield return new object?[] { Wrap(GoodEntry.Replace("\"microsoft\"", "\"inne\"")), "ms.news", "group" };
        yield return new object?[] { Wrap(GoodEntry.Replace("\"packageName\": \"Microsoft.BingNews\"", "\"packageName\": \"A\", \"displayName\": \"B\"")), "ms.news", "match" };
        yield return new object?[] { Wrap(GoodEntry.Replace("\"packageName\": \"Microsoft.BingNews\"", "\"packageName\": \"A\", \"publisher\": \"B\"")), "ms.news", "match" };
        yield return new object?[] { Wrap(GoodEntry.Replace("{ \"packageName\": \"Microsoft.BingNews\" }", "{ }")), "ms.news", "match" };
        yield return new object?[] { Wrap(GoodEntry.Replace("\"match\": { \"packageName\": \"Microsoft.BingNews\" }, ", "")), "ms.news", "match" };
        yield return new object?[] { Wrap(GoodEntry.Replace("\"Wiadomości.\"", "\"" + new string('x', 161) + "\"")), "ms.news", "summary" };
        yield return new object?[] { Wrap(GoodEntry.Replace("\"summary\": \"Wiadomości.\", ", "")), "ms.news", "summary" };
        yield return new object?[] { Wrap(GoodEntry.Replace("\"source\": \"spec 6.2\"", "\"source\": \"\"")), "ms.news", "source" };
        yield return new object?[] { Wrap(GoodEntry.Replace("\"suggested\": true,", "")), "ms.news", "suggested" };
        yield return new object?[] { Wrap(GoodEntry.Replace("\"source\"", "\"cleanupUrl\": \"http://example.com\", \"source\"")), "ms.news", "cleanupUrl" };
        yield return new object?[] { Wrap(GoodEntry.Replace("\"source\"", "\"cleanupUrl\": \"nie url\", \"source\"")), "ms.news", "cleanupUrl" };
        yield return new object?[] { Wrap(GoodEntry.Replace("\"id\": \"ms.news\"", "\"id\": \"\"")), "(wpis 1)", "id" };
        yield return new object?[] { Wrap("", """{ "id": "p", "match": { "packageName": "X" } }"""), "p", "reason" };
        yield return new object?[] { Wrap("", """{ "id": "p", "match": { }, "reason": "r" }"""), "p", "match" };
        yield return new object?[] { Wrap("", """{ "id": "p", "match": { "packageName": "X", "displayName": "Y" }, "reason": "r" }"""), "p", "match" };
        yield return new object?[] { Wrap(GoodEntry.Replace("\"Wiadomości.\"", "123")), "ms.news", "summary" };
        yield return new object?[] { Wrap(GoodEntry.Replace("\"id\": \"ms.news\"", "\"id\": 5")), "(wpis 1)", "id" };
        yield return new object?[] { Wrap(GoodEntry.Replace("\"Microsoft.BingNews\"", "[\"a\"]")), "ms.news", "packageName" };
        yield return new object?[] { "[1]", "(plik)", "json" };
        yield return new object?[] { "{ \"protected\": [] }", "(plik)", "entries" };
        yield return new object?[] { "{ \"entries\": [], \"protected\": [] ", "(plik)", "json" };
        yield return new object?[] { "{ \"entries\": [ 5 ], \"protected\": [] }", "(wpis 1)", "entries" };
    }

    [Theory]
    [MemberData(nameof(InvalidFiles))]
    public void Load_InvalidFile_ThrowsWithIdAndField(string json, string expectedId, string expectedField)
    {
        var ex = Assert.Throws<AppsCatalogException>(() => AppsBloatCatalog.Load(json));

        Assert.Equal(expectedId, ex.EntryId);
        Assert.Equal(expectedField, ex.Field);
        Assert.Contains(expectedId, ex.Message, StringComparison.Ordinal);
        Assert.Contains(expectedField, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_ProtectedOnlyFile_Works()
    {
        var catalog = AppsBloatCatalog.Load(Wrap("", """{ "id": "p", "match": { "packageName": "X" }, "reason": "r" }"""));

        Assert.Empty(catalog.Entries);
        Assert.Single(catalog.Protected);
    }

    // Pakiety widoczne na świeżej VM 26H2 (Home i Pro, odczyt 2026-10-21) oznaczone w research jako chronione.
    public static readonly string[] VmProtectedPackages =
    {
        "Microsoft.ApplicationCompatibilityEnhancements", "Microsoft.AV1VideoExtension", "Microsoft.AVCEncoderVideoExtension",
        "Microsoft.HEIFImageExtension", "Microsoft.HEVCVideoExtension", "Microsoft.MicrosoftEdge.Stable", "Microsoft.MicrosoftStickyNotes",
        "Microsoft.MPEG2VideoExtension", "Microsoft.Paint", "Microsoft.RawImageExtension", "Microsoft.ScreenSketch",
        "Microsoft.StorePurchaseApp", "Microsoft.VP9VideoExtensions", "Microsoft.WebMediaExtensions", "Microsoft.WebpImageExtension",
        "Microsoft.Windows.Photos", "Microsoft.WindowsAlarms", "Microsoft.WindowsCalculator", "Microsoft.WindowsCamera",
        "Microsoft.WindowsNotepad", "Microsoft.WindowsSoundRecorder", "Microsoft.WindowsStore", "Microsoft.WindowsTerminal",
        "Microsoft.ZuneMusic",
    };

    // Pakiety z VM oznaczone w research jako sugerowane i nadal sugerowane (Copilot tylko na Home).
    public static readonly string[] VmSuggestedPackages =
    {
        "Clipchamp.Clipchamp", "Microsoft.BingNews", "Microsoft.BingWeather", "Microsoft.Copilot", "Microsoft.GamingApp",
        "Microsoft.MicrosoftSolitaireCollection", "Microsoft.PowerAutomateDesktop", "Microsoft.Todos", "Microsoft.WindowsFeedbackHub",
    };

    // Pakiety z VM, które mają zostać w grupie „Pozostałe” (decyzje autora i ostrożność; zob. docs/apps/README.md).
    public static readonly string[] VmOtherPackages =
    {
        "Microsoft.XboxGamingOverlay", "Microsoft.Xbox.TCUI", "Microsoft.XboxIdentityProvider", "Microsoft.XboxSpeechToTextOverlay",
        "Microsoft.OutlookForWindows", "MicrosoftCorporationII.QuickAssist", "MSTeams", "Microsoft.GetHelp", "Microsoft.BingSearch",
        "Microsoft.Windows.DevHome", "MicrosoftCorporationII.MicrosoftFamily", "Microsoft.YourPhone", "MicrosoftWindows.CrossDevice",
        "MicrosoftWindows.Client.WebExperience",
    };

    private const string BatteryWarning = "Może sterować oszczędzaniem baterii i klawiszami Fn; po usunięciu ta funkcja może zniknąć.";

    private static AppsBloatCatalog Builtin() => AppsBloatCatalog.Load(BuiltInCatalog.AppsBloatSource.Json);

    private static InstalledApp Store(string package)
        => new("appx:" + package, AppKind.Store, package, "CN=Microsoft Corporation", null, AppScope.User, "S-1",
            new AppxInfo("full", "fam", package, false, false, false, "Store", false), null);

    private static InstalledApp Program(string name, string? publisher = null)
        => new("win32:" + name, AppKind.Program, name, publisher, null, AppScope.Machine, null, null,
            new Win32Info(@"HKLM\Software\x\" + name, @"C:\Program Files\x\unins.exe", null, false, null, null, false));

    private static AppRow ClassifyOne(InstalledApp app)
        => Assert.Single(AppClassifier.Classify(new[] { app }, Builtin(), "S-1", true, "S-1", _ => null, _ => true, @"C:\Windows\System32"));

    [Fact]
    public void Vm_ProtectedPackages_ClassifyAsProtected()
    {
        foreach (var package in VmProtectedPackages)
        {
            Assert.True(ClassifyOne(Store(package)).Group == AppGroup.Protected, package);
        }
    }

    [Fact]
    public void Vm_NoSuggestedEntryMatchesAnyProtectedPackage()
    {
        var catalog = Builtin();
        var protectedApps = VmProtectedPackages.Select(Store)
            .Concat(catalog.Protected.Where(p => p.Match.PackageName is not null && !p.Match.PackageName.Contains('*')).Select(p => Store(p.Match.PackageName!)))
            .ToList();

        foreach (var app in protectedApps)
        {
            Assert.DoesNotContain(catalog.Entries.Where(e => e.Suggested), e => e.Match.Matches(app));
        }
    }

    [Fact]
    public void Vm_SuggestedPackages_ClassifyAsSuggested()
    {
        foreach (var package in VmSuggestedPackages)
        {
            Assert.True(ClassifyOne(Store(package)).Group == AppGroup.Suggested, package);
        }
    }

    [Fact]
    public void Vm_OtherPackages_StayOther()
    {
        foreach (var package in VmOtherPackages)
        {
            Assert.True(ClassifyOne(Store(package)).Group == AppGroup.Other, package);
        }
    }

    [Fact]
    public void Vm_EveryPackageHasExactlyOneDecision()
    {
        var all = VmProtectedPackages.Concat(VmSuggestedPackages).Concat(VmOtherPackages).ToList();
        Assert.Equal(all.Count, all.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void DecisionsOfAuthor_OneDriveOutlookQuickAssistTeamsGameBar_NotSuggested()
    {
        Assert.Equal(AppGroup.Other, ClassifyOne(Program("Microsoft OneDrive")).Group);
        Assert.Equal(AppGroup.Other, ClassifyOne(Store("Microsoft.OutlookForWindows")).Group);
        Assert.Equal(AppGroup.Other, ClassifyOne(Store("MicrosoftCorporationII.QuickAssist")).Group);
        Assert.Equal(AppGroup.Other, ClassifyOne(Store("Microsoft.XboxGamingOverlay")).Group);
        var teams = ClassifyOne(Store("MSTeams"));
        Assert.Equal(AppGroup.Other, teams.Group);
        Assert.False(string.IsNullOrWhiteSpace(teams.Warning));
    }

    public static IEnumerable<object[]> BatteryControllingApps()
    {
        yield return new object[] { "E046963F.LenovoCompanion", true };
        yield return new object[] { "Lenovo Vantage Service", false };
        yield return new object[] { "B9ECED6F.ASUSPCAssistant", true };
        yield return new object[] { "B9ECED6F.ArmouryCrate", true };
        yield return new object[] { "MyASUS", false };
        yield return new object[] { "MyDell", false };
        yield return new object[] { "Dell Optimizer Service", false };
    }

    [Theory]
    [MemberData(nameof(BatteryControllingApps))]
    public void BatteryAndFnApps_HaveLiteralWarning_AndAreNotSuggested(string name, bool store)
    {
        var row = ClassifyOne(store ? Store(name) : Program(name));

        Assert.Equal(AppGroup.Other, row.Group);
        Assert.Equal(BatteryWarning, row.Warning);
    }

    [Theory]
    [InlineData("McAfee Agent")]
    [InlineData("McAfee Endpoint Security Platform")]
    [InlineData("McAfee Drive Encryption")]
    [InlineData("Trellix Endpoint Security")]
    public void CorporateMcAfeeAndTrellix_AreProtected(string name)
    {
        Assert.Equal(AppGroup.Protected, ClassifyOne(Program(name)).Group);
    }

    // R21: zależności uruchomieniowe Win32 – nazwy wpisów Uninstall widocznych na liście (bez SystemComponent), odczyt hosta Windows 11
    // 2026-10-21 (Microsoft GameInput to MSI, nie pakiet AppX) oraz nazwy instalatorów .NET z Microsoft Learn.
    [Theory]
    [InlineData("Microsoft Visual C++ 2005 Redistributable")]
    [InlineData("Microsoft Visual C++ 2010  x64 Redistributable - 10.0.40219")]
    [InlineData("Microsoft Visual C++ 2013 Redistributable (x86) - 12.0.40664")]
    [InlineData("Microsoft Visual C++ v14 Redistributable (x64) - 14.51.36247")]
    [InlineData("Microsoft Visual C++ 2015-2022 Redistributable (x64) - 14.40.33810")]
    [InlineData("Microsoft Windows Desktop Runtime - 8.0.22 (x64)")]
    [InlineData("Microsoft Windows Desktop Runtime 10.0.9 (x64)")]
    [InlineData("Microsoft .NET Runtime - 8.0.21 (x64)")]
    [InlineData("Microsoft ASP.NET Core 8.0.11 - Shared Framework (x64)")]
    [InlineData("Microsoft ASP.NET Core Runtime - 10.0.13 (x64)")]
    [InlineData("Microsoft Update Health Tools")]
    [InlineData("Microsoft GameInput")]
    public void RuntimeDependencies_AreProtected(string name)
    {
        var row = ClassifyOne(Program(name, "Microsoft Corporation"));

        Assert.Equal(AppGroup.Protected, row.Group);
        Assert.Equal(AppRemovability.Protected, row.Removability);
        Assert.False(row.CanRemove);
    }

    [Theory]
    [InlineData("Microsoft .NET SDK 10.0.402 (x64)")]
    [InlineData("Microsoft ASP.NET Core Targeting Pack - 10.0.13 (x64)")]
    public void DeveloperToolsOfDotNet_AreNotProtectedByRuntimeRules(string name)
    {
        Assert.NotEqual(AppGroup.Protected, ClassifyOne(Program(name, "Microsoft Corporation")).Group);
    }

    [Theory]
    [InlineData("McAfee LiveSafe")]
    [InlineData("McAfee Total Protection")]
    [InlineData("Norton 360 Deluxe")]
    [InlineData("Norton Security")]
    [InlineData("HP Games by WildTangent")]
    [InlineData("WildTangent Games App")]
    [InlineData("Booking.com")]
    [InlineData("McAfee Personal Security")]
    [InlineData("McAfee WebAdvisor")]
    [InlineData("McAfee Security Scan Plus")]
    public void ConsumerTrials_AreSuggested_InTrialGroup(string name)
    {
        var row = ClassifyOne(Program(name));

        Assert.Equal(AppGroup.Suggested, row.Group);
        Assert.Equal("trial", row.BloatGroup);
    }

    [Theory]
    [InlineData("Norton Family")]
    [InlineData("Norton Utilities")]
    [InlineData("McAfee Unknown Thing")]
    public void OtherNortonAndMcAfeeProducts_AreNotSuggested(string name)
    {
        Assert.NotEqual(AppGroup.Suggested, ClassifyOne(Program(name)).Group);
    }

    [Fact]
    public void Norton_HasVerifiedCleanupUrl()
    {
        Assert.Equal("https://support.norton.com/sp/en/us/home/current/solutions/v60392881", ClassifyOne(Program("Norton 360 Deluxe")).CleanupUrl);
    }

    [Theory]
    [InlineData("McAfee LiveSafe")]
    [InlineData("McAfee Total Protection")]
    [InlineData("McAfee AntiVirus Plus")]
    [InlineData("McAfee Internet Security")]
    [InlineData("McAfee Personal Security")]
    public void McAfeeAntivirus_HasCleanupUrl(string name)
    {
        Assert.Equal("https://www.mcafee.com/support/s/article/000002766?language=pl", ClassifyOne(Program(name)).CleanupUrl);
    }

    [Fact]
    public void SuggestedEntries_HaveVerifiableSource()
    {
        var catalog = Builtin();

        Assert.All(catalog.Entries.Where(e => e.Suggested), e =>
        {
            Assert.DoesNotContain("z pamięci", e.Source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("powszechnie znana", e.Source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("do potwierdzenia", e.Source, StringComparison.OrdinalIgnoreCase);
            Assert.True(
                e.Source.IndexOf("https://", StringComparison.Ordinal) >= 0 || e.Source.IndexOf("spec 6.2 (decyzja autora)", StringComparison.Ordinal) >= 0
                    || e.Source.StartsWith("Odczyt VM 26H2", StringComparison.Ordinal),
                e.Id + ": źródło wpisu sugerowanego musi mieć adres https://, znacznik „spec 6.2 (decyzja autora)” albo odczyt VM 26H2");
        });
    }

    [Fact]
    public void NoSuggestedProgramPatternMatchesProtectedOrUserPrograms()
    {
        var catalog = Builtin();
        var programs = new List<InstalledApp>
        {
            // Wpisy Uninstall z odczytu VM 26H2 (2026-10-21) uznane za chronione albo użytkowe.
            Program("Microsoft Edge", "Microsoft Corporation"),
            Program("Microsoft Edge WebView2 Runtime", "Microsoft Corporation"),
            Program("Podłączanie pulpitu zdalnego", "Microsoft Corporation"),
            Program("Remote Desktop Connection", "Microsoft Corporation"),
            Program("Microsoft OneDrive", "Microsoft Corporation"),
        };
        foreach (var guard in catalog.Protected.Where(p => p.Match.PackageName is null))
        {
            programs.Add(Program(
                (guard.Match.DisplayName ?? "Program").Replace("*", " Test"),
                guard.Match.Publisher?.Replace("*", " Corp")));
        }

        foreach (var app in programs)
        {
            Assert.DoesNotContain(catalog.Entries.Where(e => e.Suggested), e => e.Match.Matches(app));
        }
    }

    [Fact]
    public void Entries_HaveNoPatternAsBroadAsAWholeVendor()
    {
        var catalog = Builtin();

        Assert.DoesNotContain(catalog.Entries, e => e.Match.DisplayName is "McAfee*" or "Norton*");
    }

    [Fact]
    public void Load_NullJson_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => AppsBloatCatalog.Load(null!));
    }

    private const string WarnedEntry = """
        { "id": "ms.mail", "match": { "packageName": "Microsoft.Mail" }, "group": "microsoft", "suggested": false,
          "summary": "Poczta.", "warning": "Stracisz pocztę.", "source": "spec 6.2" }
        """;

    private const string ProtectedCalc = """
        { "id": "ms.calc", "match": { "packageName": "Microsoft.WindowsCalculator" }, "reason": "Część systemu." }
        """;

    [Fact]
    public void Load_English_TranslatesSummaryWarningReason()
    {
        var table = CatalogTranslationTests.DataTable(
            ("Wiadomości.", "News."),
            ("Poczta.", "Mail."),
            ("Stracisz pocztę.", "You lose your mail."),
            ("Część systemu.", "Part of the system."));
        using (L.UseTable(table))
        using (L.Use(UiLanguage.English))
        {
            var catalog = AppsBloatCatalog.Load(Wrap(GoodEntry + "," + WarnedEntry, ProtectedCalc));

            Assert.Equal("News.", catalog.Entries[0].Summary);
            Assert.Null(catalog.Entries[0].Warning);
            Assert.Equal("Mail.", catalog.Entries[1].Summary);
            Assert.Equal("You lose your mail.", catalog.Entries[1].Warning);
            Assert.Equal("Part of the system.", Assert.Single(catalog.Protected).Reason);
            Assert.Equal("spec 6.2", catalog.Entries[1].Source);
            Assert.Equal("ms.mail", catalog.Entries[1].Id);
        }
    }

    [Fact]
    public void Load_Polish_KeepsOriginalTexts()
    {
        var table = CatalogTranslationTests.DataTable(("Wiadomości.", "News."), ("Część systemu.", "Part of the system."));
        using (L.UseTable(table))
        using (L.Use(UiLanguage.Polish))
        {
            var catalog = AppsBloatCatalog.Load(Wrap(GoodEntry, ProtectedCalc));

            Assert.Equal("Wiadomości.", catalog.Entries[0].Summary);
            Assert.Equal("Część systemu.", Assert.Single(catalog.Protected).Reason);
        }
    }

    [Fact]
    public void Load_EnglishTooLongTranslation_DoesNotFailLoad()
    {
        var longEnglish = new string('e', 200);
        using (L.UseTable(CatalogTranslationTests.DataTable(("Wiadomości.", longEnglish))))
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal(longEnglish, AppsBloatCatalog.Load(Wrap(GoodEntry)).Entries[0].Summary);
        }
    }
}
