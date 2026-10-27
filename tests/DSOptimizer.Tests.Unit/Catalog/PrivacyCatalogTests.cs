using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DSOptimizer.Catalog;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Model;
using DSOptimizer.Tests.Unit.Support;
using Xunit;

namespace DSOptimizer.Tests.Unit.Catalog;

/// <summary>
/// Pozycje kategorii privacy (Etap 4a, partia 1): wartości z docs/zrodla/2026-10-20-etap4a-zrodla.md,
/// sekcja „Wyniki VM 26H2 (partia 1)”, podsekcja „Pozycje do katalogu”. Kolejne taski dopisują wiersze do <see cref="Expected"/>
/// i <see cref="Dropped"/>.
/// </summary>
public class PrivacyCatalogTests
{
    /// <summary>Oczekiwana pozycja katalogu: id, sugestia, zakres, ryzyko, restart komputera i wpisy rejestru.</summary>
    public sealed record Item(string Id, bool Suggested, TweakScope Scope, RiskLevel Risk, bool RequiresReboot, params ExpectedEntry[] Entries);

    public sealed record ExpectedEntry(string Path, string Name, RegValue Value, RegValue? Original);

    /// <summary>Oczekiwana usługa pozycji katalogu (Task 7).</summary>
    public sealed record ExpectedService(string Name, ServiceStartType StartType, bool Stop, ServiceStartType? Original);

    private const string DataCollection = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection";

    private const string Cdm = @"HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";

    /// <summary>Pozycje, które weszły do katalogu (jeden wiersz = jedna pozycja).</summary>
    public static readonly Item[] Expected =
    {
        new("privacy.widgets", true, TweakScope.Machine, RiskLevel.Safe, false,
            new ExpectedEntry(@"HKLM\SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", RegValue.FromDWord(0), RegValue.Absent)),
        new("privacy.search-web-results", true, TweakScope.User, RiskLevel.Safe, false,
            new ExpectedEntry(@"HKCU\Software\Microsoft\Windows\CurrentVersion\SearchSettings", "IsGlobalWebSearchProviderToggleEnabled", RegValue.FromDWord(0), RegValue.Absent)),
        new("privacy.settings-suggestions", true, TweakScope.User, RiskLevel.Safe, false,
            new ExpectedEntry(Cdm, "SubscribedContent-338393Enabled", RegValue.FromDWord(0), RegValue.Absent),
            new ExpectedEntry(Cdm, "SubscribedContent-353694Enabled", RegValue.FromDWord(0), RegValue.Absent),
            new ExpectedEntry(Cdm, "SubscribedContent-353696Enabled", RegValue.FromDWord(0), RegValue.Absent)),
        new("privacy.windows-tips", true, TweakScope.User, RiskLevel.Safe, false,
            new ExpectedEntry(Cdm, "SubscribedContent-338389Enabled", RegValue.FromDWord(0), RegValue.FromDWord(1))),
        new("privacy.settings-account-notifications", true, TweakScope.User, RiskLevel.Safe, false,
            new ExpectedEntry(@"HKCU\Software\Microsoft\Windows\CurrentVersion\SystemSettings\AccountNotifications", "EnableAccountNotifications", RegValue.FromDWord(0), RegValue.Absent)),
        new("privacy.start-recommendations", true, TweakScope.User, RiskLevel.Safe, false,
            new ExpectedEntry(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Start_IrisRecommendations", RegValue.FromDWord(0), RegValue.Absent)),
        new("privacy.start-recommended-section", false, TweakScope.User, RiskLevel.Safe, false,
            new ExpectedEntry(@"HKCU\Software\Policies\Microsoft\Windows\Explorer", "HideRecommendedSection", RegValue.FromDWord(1), RegValue.Absent)),
        new("privacy.advertising-id", true, TweakScope.User, RiskLevel.Safe, false,
            new ExpectedEntry(@"HKCU\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", RegValue.FromDWord(0), RegValue.FromDWord(0)),
            new ExpectedEntry(@"HKCU\Software\Microsoft\Windows\CurrentVersion\CPSS\Store\AdvertisingInfo", "Value", RegValue.FromDWord(0), RegValue.FromDWord(0))),
        new("privacy.tailored-experiences", true, TweakScope.User, RiskLevel.Safe, false,
            new ExpectedEntry(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Privacy", "PersonalizedOffersEnabled", RegValue.FromDWord(0), RegValue.Absent)),
        new("privacy.spotlight", false, TweakScope.User, RiskLevel.Caution, false,
            new ExpectedEntry(@"HKCU\Software\Policies\Microsoft\Windows\CloudContent", "DisableWindowsSpotlightFeatures", RegValue.FromDWord(1), RegValue.Absent)),
        new("privacy.diagnostic-data-required", true, TweakScope.Machine, RiskLevel.Caution, false,
            new ExpectedEntry(DataCollection, "AllowTelemetry", RegValue.FromDWord(1), RegValue.Absent)),
        new("privacy.feedback-notifications", true, TweakScope.Machine, RiskLevel.Safe, false,
            new ExpectedEntry(DataCollection, "DoNotShowFeedbackNotifications", RegValue.FromDWord(1), RegValue.Absent)),
        new("privacy.diagtrack", true, TweakScope.Machine, RiskLevel.Caution, false),
    };

    /// <summary>Usługi pozycji (id -> oczekiwane wpisy <c>services[]</c>); pozycje spoza tabeli nie mają usług.</summary>
    public static readonly Dictionary<string, ExpectedService[]> ExpectedServices = new()
    {
        ["privacy.diagtrack"] = new[] { new ExpectedService("DiagTrack", ServiceStartType.Disabled, true, ServiceStartType.Automatic) },
    };

    /// <summary>Kandydaci, którzy wypadli: id dokumentu z werdyktem „nie wchodzi” i fraza, która ma w nim być.</summary>
    public static readonly (string DocId, string Phrase)[] Dropped =
    {
        ("privacy.copilot-policy", "partia 2"),
        ("privacy.recall", "nie dowiedziono (VM bez NPU, funkcja niedostępna)"),
        ("privacy.click-to-do", "nie dowiedziono (VM bez NPU, funkcja niedostępna)"),
        ("privacy.widgets-taskbar-da", "UCPD"),
        ("privacy.search-connected-web", "ConnectedSearchUseWeb"),
        ("privacy.search-disable-web-search", "DisableWebSearch"),
        ("privacy.search-highlights", "EnableDynamicContentInWSB"),
        ("privacy.consumer-features", "DisableWindowsConsumerFeatures"),
        ("privacy.spotlight-settings", "DisableWindowsSpotlightOnSettings"),
        ("privacy.spotlight-third-party", "DisableThirdPartySuggestions"),
        ("privacy.soft-landing", "DisableSoftLanding"),
        ("privacy.welcome-experience", "brak tych ustawień w 26H2"),
        ("privacy.recommended-personalized-sites", "HideRecommendedPersonalizedSites"),
        ("privacy.limit-diagnostic-log-collection", "LimitDiagnosticLogCollection"),
        ("privacy.limit-dump-collection", "LimitDumpCollection"),
    };

    public static IEnumerable<object[]> ExpectedIds() => Expected.Select(e => new object[] { e.Id });

    private static List<TweakDefinition> Privacy()
        => CatalogLoader.Load(BuiltInCatalog.Sources).Definitions.Where(d => d.Category == "privacy").ToList();

    private static TweakDefinition Get(string id) => Privacy().Single(d => d.Id == id);

    private static string Doc(string id)
        => File.ReadAllText(RepoRoot.Combine("docs", "tweaks", id + ".md"), Encoding.UTF8);

    [Fact]
    public void PrivacyEntries_AreExactlyTheExpectedOnes()
    {
        Assert.Equal(
            Expected.Select(e => e.Id).OrderBy(i => i, StringComparer.Ordinal),
            Privacy().Select(d => d.Id).OrderBy(i => i, StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(ExpectedIds))]
    public void Entry_MatchesResearchSpecification(string id)
    {
        var expected = Expected.Single(e => e.Id == id);
        var def = Get(id);
        Assert.Equal(TweakKind.Tweak, def.Kind);
        Assert.Equal(expected.Suggested, def.Suggested);
        Assert.Equal(expected.Scope, def.Scope);
        Assert.Equal(expected.Risk, def.Risk);
        Assert.Equal(expected.RequiresReboot, def.RequiresReboot);
        Assert.Null(def.Editions);
        Assert.Null(def.Script);
        var expectedServices = ExpectedServices.TryGetValue(id, out var services) ? services : Array.Empty<ExpectedService>();
        Assert.Equal(
            expectedServices.Select(x => (x.Name, x.StartType, x.Stop, x.Original)),
            def.Services.Select(x => (x.Name, x.StartType, x.Stop, x.Original)));
        Assert.Equal(
            expected.Entries.Select(e => (e.Path, e.Name, e.Value, e.Original)),
            def.Registry.Select(e => (e.Path, e.Name, e.Value, e.Original)));
    }

    [Theory]
    [MemberData(nameof(ExpectedIds))]
    public void Entry_HasSummaryAndBenefitWithinLimits(string id)
    {
        var def = Get(id);
        Assert.False(string.IsNullOrWhiteSpace(def.Summary));
        Assert.True(def.Summary.Length <= 160, $"{id}: summary ma {def.Summary.Length} znaków");
        Assert.False(string.IsNullOrWhiteSpace(def.Benefit));
        Assert.True(def.Benefit!.Length <= CatalogValidator.BenefitMaxLength, $"{id}: benefit ma {def.Benefit.Length} znaków");
        Assert.Empty(CatalogValidator.Validate(new[] { def }));
    }

    [Theory]
    [MemberData(nameof(ExpectedIds))]
    public void Entry_DocExistsWithObservationSection(string id)
    {
        var doc = Doc(id);
        Assert.Contains("## Obserwacja skutku", doc, StringComparison.Ordinal);
        Assert.Contains("2026-10-21", doc, StringComparison.Ordinal);
        Assert.Contains("**Home**", doc, StringComparison.Ordinal);
        Assert.Contains("**Pro**", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryPrivacyItem_HasDocWithObservationSection()
    {
        // Egzekwowanie reguły Etapu 4a (R3) także dla pozycji dopisanych później niż lista Expected.
        foreach (var def in Privacy())
        {
            Assert.Contains("## Obserwacja skutku", Doc(def.Id), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SuggestedItems_MatchResearchList()
    {
        var suggested = Privacy().Where(d => d.Suggested).Select(d => d.Id).OrderBy(i => i, StringComparer.Ordinal);
        Assert.Equal(Expected.Where(e => e.Suggested).Select(e => e.Id).OrderBy(i => i, StringComparer.Ordinal), suggested);
    }

    [Fact]
    public void Widgets_WarnsAboutExplorerRestartAndPointsToAction()
    {
        var def = Get("privacy.widgets");
        var text = string.Join(" ", def.Warnings);
        // Spec opisów 2026-10-26: akcja wskazana nazwą kategorii, bez identyfikatora w tekście uwagi.
        Assert.Contains("restarcie Eksploratora w „Narzędziach”", text, StringComparison.Ordinal);
        Assert.Contains("przycisk", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Win+W", def.Description, StringComparison.Ordinal);
        Assert.Contains("AllowNewsAndInterests", def.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchWebResults_AdvisesRelogonAndDoesNotClaimExplorerRestartSuffices()
        => AssertRelogonAdvice(Get("privacy.search-web-results"));

    [Fact]
    public void SearchWebResults_DocDescribesWorkingAlternatives_NotAsCatalogItems()
    {
        var doc = Doc("privacy.search-web-results");
        Assert.Contains("DisableSearchBoxSuggestions", doc, StringComparison.Ordinal);
        Assert.Contains("BingSearchEnabled", doc, StringComparison.Ordinal);
        Assert.Contains("WebSearchProviders", doc, StringComparison.Ordinal);
        var names = CatalogLoader.Load(BuiltInCatalog.Sources).Definitions.SelectMany(d => d.Registry).Select(e => e.Name).ToList();
        Assert.DoesNotContain("DisableSearchBoxSuggestions", names);
        Assert.DoesNotContain("BingSearchEnabled", names);
    }

    [Fact]
    public void DroppedCandidates_AreNotInCatalogButDocumentedWithVerdict()
    {
        var definitions = CatalogLoader.Load(BuiltInCatalog.Sources).Definitions;
        var registryNames = definitions.SelectMany(d => d.Registry).Select(e => e.Name).ToList();
        foreach (var name in new[] { "TurnOffWindowsCopilot", "AllowRecallEnablement", "DisableAIDataAnalysis", "DisableClickToDo", "TaskbarDa", "ConnectedSearchUseWeb", "DisableWebSearch", "EnableDynamicContentInWSB", "DisableWindowsConsumerFeatures","DisableWindowsSpotlightOnSettings", "DisableThirdPartySuggestions", "DisableSoftLanding", "HideRecommendedPersonalizedSites", "DisabledByGroupPolicy", "DisableTailoredExperiencesWithDiagnosticData", "LimitDiagnosticLogCollection", "LimitDumpCollection" })
        {
            Assert.DoesNotContain(name, registryNames);
        }

        var ids = definitions.Select(d => d.Id).ToList();
        foreach (var (docId, phrase) in Dropped)
        {
            Assert.DoesNotContain(docId, ids);
            var doc = Doc(docId);
            Assert.Contains("nie wchodzi:", doc, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(phrase, doc, StringComparison.Ordinal);
            Assert.Contains("## Obserwacja skutku", doc, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("privacy.start-recommendations")]
    [InlineData("privacy.start-recommended-section")]
    public void StartItems_AdviseRelogonAndDoNotClaimExplorerRestartSuffices(string id)
        => AssertRelogonAdvice(Get(id));

    private static void AssertRelogonAdvice(TweakDefinition def)
    {
        var warnings = string.Join(" ", def.Warnings);
        Assert.Contains("po ponownym zalogowaniu", warnings, StringComparison.Ordinal);
        // Spec opisów 2026-10-26: bez dopisku o sprawdzaniu – niepewność zostaje w „może nie wystarczyć”, dowód w docs.
        Assert.Contains("sam restart Eksploratora może nie wystarczyć", warnings, StringComparison.Ordinal);
        var all = def.Summary + " " + def.Benefit + " " + def.Description;
        Assert.DoesNotContain("po restarcie Eksploratora", all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("po ponownym uruchomieniu Eksploratora", all, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StartRecommendedSection_WarnsAboutRecentFilesAndIsNotSuggested()
    {
        var def = Get("privacy.start-recommended-section");
        var text = def.Description + " " + string.Join(" ", def.Warnings);
        Assert.Contains("ostatnio otwierane pliki", text, StringComparison.OrdinalIgnoreCase);
        Assert.False(def.Suggested);
    }

    [Fact]
    public void Widgets_BenefitDoesNotMentionAds()
    {
        Assert.DoesNotContain("reklam", Get("privacy.widgets").Benefit!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("privacy.settings-suggestions")]
    [InlineData("privacy.windows-tips")]
    [InlineData("privacy.settings-account-notifications")]
    [InlineData("privacy.start-recommendations")]
    [InlineData("privacy.advertising-id")]
    [InlineData("privacy.tailored-experiences")]
    public void SettingsToggleOnlyEvidence_IsStatedInObservation(string id)
    {
        Assert.Contains("dowód: stan przełącznika w Ustawieniach, treści nie obserwowano", Doc(id), StringComparison.Ordinal);
    }

    [Fact]
    public void AdvertisingId_WritesOnlyTwoValues_DocNotesIdRemovalAndPolicyAlternative()
    {
        var doc = Doc("privacy.advertising-id");
        Assert.Contains(@"AdvertisingInfo\Id", doc, StringComparison.Ordinal);
        Assert.Contains("DisabledByGroupPolicy", doc, StringComparison.Ordinal);
        var def = Get("privacy.advertising-id");
        Assert.DoesNotContain(def.Registry, e => e.Name == "Id");
        Assert.Equal(2, def.Registry.Count);
    }

    [Fact]
    public void TailoredExperiences_DocDescribesPolicyAlternative()
    {
        Assert.Contains("DisableTailoredExperiencesWithDiagnosticData", Doc("privacy.tailored-experiences"), StringComparison.Ordinal);
    }

    [Fact]
    public void Spotlight_IsNotSuggestedAndWarnsThatUndoDoesNotRestoreDailyPictures()
    {
        // Decyzja Dawida 2026-10-21 (po partii 1): pozycja niesugerowana z ostrzeżeniem, bo cofnięcie nie przywraca tła Spotlight.
        var def = Get("privacy.spotlight");
        Assert.False(def.Suggested);
        var warnings = string.Join(" ", def.Warnings);
        Assert.Contains("nie przywraca zdjęć dnia", warnings, StringComparison.Ordinal);
        Assert.Contains("Personalizacja > Tło", warnings, StringComparison.Ordinal);
        var doc = Doc(def.Id);
        Assert.Contains("niesugerowan", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("po cofnięciu tło zostaje „Obraz”", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void RecallDoc_CoversBothValuesAndAvoidsOverclaiming()
    {
        var doc = Doc("privacy.recall");
        Assert.Contains("AllowRecallEnablement", doc, StringComparison.Ordinal);
        Assert.Contains("DisableAIDataAnalysis", doc, StringComparison.Ordinal);
        Assert.DoesNotContain("nie działa", doc, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CopilotDoc_StatesAppRemovalIsPartTwo()
    {
        var doc = Doc("privacy.copilot-policy");
        Assert.Contains("Microsoft.Copilot", doc, StringComparison.Ordinal);
        Assert.Contains("partia 2", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticDataRequired_UsesLimitingWording_NotTelemetryOffPromise()
    {
        var def = Get("privacy.diagnostic-data-required");
        Assert.Contains("ogranicz dane diagnostyczne do wymaganych", def.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ogranicz dane diagnostyczne do wymaganych", def.Description, StringComparison.OrdinalIgnoreCase);
        foreach (var text in new[] { def.Title, def.Summary, def.Benefit!, def.Description, Doc(def.Id) })
        {
            Assert.DoesNotContain("wyłącz telemetri", text, StringComparison.OrdinalIgnoreCase);
        }

        // Spec opisów 2026-10-26 (przegląd T3, R5): komunikat „zarządza organizacja” to uwaga, nie zysk.
        Assert.Contains("zarządza organizacja", string.Join(" ", def.Warnings), StringComparison.Ordinal);
        var doc = Doc(def.Id);
        Assert.Contains("AllowTelemetry=0", doc, StringComparison.Ordinal);
        Assert.Contains("Enterprise", doc, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("privacy.diagnostic-data-required")]
    [InlineData("privacy.feedback-notifications")]
    public void DataCollectionPolicies_DocsDescribePreExistingKey(string id)
    {
        var doc = Doc(id);
        Assert.Contains("KeyPresentAfterUndo", doc, StringComparison.Ordinal);
        Assert.Contains(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void FeedbackNotifications_StatesOnlySettingsStateWasObserved()
    {
        Assert.Contains("samych powiadomień nie obserwowano", Doc("privacy.feedback-notifications"), StringComparison.Ordinal);
    }

    [Fact]
    public void DiagTrack_WarnsAboutWindowsUpdate_DocKeepsObservation_AndHasNoRegistryOrScript()
    {
        var def = Get("privacy.diagtrack");
        Assert.Empty(def.Registry);
        Assert.Null(def.Script);
        var warnings = string.Join(" ", def.Warnings);
        Assert.Contains("Windows Update", warnings, StringComparison.Ordinal);
        // Spec opisów 2026-10-26: wynik obserwacji Windows Update tylko w dokumentacji, nie w programie.
        var doc = Doc(def.Id);
        Assert.Contains("ResultCode=2", doc, StringComparison.Ordinal);
        Assert.Contains("4 aktualizacje", doc, StringComparison.Ordinal);
        Assert.Contains("R14", doc, StringComparison.Ordinal);
        Assert.Contains("brak dokumentacji MS", doc, StringComparison.OrdinalIgnoreCase);
    }
}
