using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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

    /// <summary>Edycje pozycji ograniczonych do części edycji Windows (id, lista). Pusty, dopóki żadna pozycja nie ma editions.</summary>
    public static readonly Dictionary<string, EditionFamily[]> ExpectedEditions = new();

    /// <summary>Data obserwacji w dokumencie pozycji, gdy inna niż domyślna (id, yyyy-MM-dd).</summary>
    public static readonly Dictionary<string, string> ObservedOn = new()
    {
        ["privacy.explorer-sync-provider-notifications"] = "2026-11-07",
        ["privacy.lockscreen-spotlight"] = "2026-11-07",
        ["privacy.start-account-notifications"] = "2026-11-07",
        ["privacy.toast-suggested"] = "2026-11-07",
        ["privacy.toast-backup-reminder"] = "2026-11-07",
        ["privacy.edge-shopping-recommendations"] = "2026-11-07",
        ["privacy.edge-newtab-content"] = "2026-11-07",
        ["privacy.start-track-progs"] = "2026-11-07",
        ["privacy.online-speech"] = "2026-11-07",
        ["privacy.inking-typing-improve"] = "2026-11-07",
        ["privacy.edge-diagnostic-data"] = "2026-11-07",
        ["privacy.edge-copilot"] = "2026-11-07",
        ["privacy.notepad-ai"] = "2026-11-07",
    };

    private const string DefaultObservedOn = "2026-10-21";

    private const string Cdm = @"HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";

    private const string ExplorerAdvanced = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    private const string NotificationSettings = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";

    private const string EdgePolicies = @"HKLM\SOFTWARE\Policies\Microsoft\Edge";

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
        new("privacy.explorer-sync-provider-notifications", true, TweakScope.User, RiskLevel.Safe, false,
            new ExpectedEntry(ExplorerAdvanced, "ShowSyncProviderNotifications", RegValue.FromDWord(0), RegValue.Absent)),
        new("privacy.lockscreen-spotlight", false, TweakScope.User, RiskLevel.Caution, false,
            new ExpectedEntry(Cdm, "SubscribedContent-338387Enabled", RegValue.FromDWord(0), RegValue.Absent)),
        new("privacy.start-account-notifications", true, TweakScope.User, RiskLevel.Safe, false,
            new ExpectedEntry(ExplorerAdvanced, "Start_AccountNotifications", RegValue.FromDWord(0), RegValue.Absent)),
        new("privacy.toast-suggested", true, TweakScope.User, RiskLevel.Safe, false,
            new ExpectedEntry(NotificationSettings + @"\Windows.SystemToast.Suggested", "Enabled", RegValue.FromDWord(0), RegValue.Absent)),
        new("privacy.toast-backup-reminder", false, TweakScope.User, RiskLevel.Safe, false,
            new ExpectedEntry(NotificationSettings + @"\Windows.SystemToast.BackupReminder", "Enabled", RegValue.FromDWord(0), RegValue.Absent)),
        new("privacy.edge-shopping-recommendations", false, TweakScope.Machine, RiskLevel.Safe, false,
            new ExpectedEntry(EdgePolicies, "EdgeShoppingAssistantEnabled", RegValue.FromDWord(0), RegValue.Absent),
            new ExpectedEntry(EdgePolicies, "ShowRecommendationsEnabled", RegValue.FromDWord(0), RegValue.Absent),
            new ExpectedEntry(EdgePolicies, "SpotlightExperiencesAndRecommendationsEnabled", RegValue.FromDWord(0), RegValue.Absent)),
        new("privacy.edge-newtab-content", false, TweakScope.Machine, RiskLevel.Safe, false,
            new ExpectedEntry(EdgePolicies, "NewTabPageContentEnabled", RegValue.FromDWord(0), RegValue.Absent),
            new ExpectedEntry(EdgePolicies, "NewTabPageHideDefaultTopSites", RegValue.FromDWord(1), RegValue.Absent)),
        new("privacy.start-track-progs", true, TweakScope.User, RiskLevel.Caution, false,
            new ExpectedEntry(ExplorerAdvanced, "Start_TrackProgs", RegValue.FromDWord(0), RegValue.Absent)),
        new("privacy.online-speech", false, TweakScope.User, RiskLevel.Caution, false,
            new ExpectedEntry(@"HKCU\Software\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy", "HasAccepted", RegValue.FromDWord(0), RegValue.Absent)),
        new("privacy.inking-typing-improve", false, TweakScope.User, RiskLevel.Caution, false,
            new ExpectedEntry(@"HKCU\Software\Microsoft\InputPersonalization", "RestrictImplicitInkCollection", RegValue.FromDWord(1), RegValue.FromDWord(0)),
            new ExpectedEntry(@"HKCU\Software\Microsoft\InputPersonalization", "RestrictImplicitTextCollection", RegValue.FromDWord(1), RegValue.FromDWord(0)),
            new ExpectedEntry(@"HKCU\Software\Microsoft\InputPersonalization\TrainedDataStore", "HarvestContacts", RegValue.FromDWord(0), RegValue.FromDWord(1)),
            new ExpectedEntry(@"HKCU\Software\Microsoft\Personalization\Settings", "AcceptedPrivacyPolicy", RegValue.FromDWord(0), RegValue.FromDWord(1))),
        new("privacy.edge-diagnostic-data", false, TweakScope.Machine, RiskLevel.Caution, false,
            new ExpectedEntry(EdgePolicies, "DiagnosticData", RegValue.FromDWord(0), RegValue.Absent),
            new ExpectedEntry(EdgePolicies, "PersonalizationReportingEnabled", RegValue.FromDWord(0), RegValue.Absent),
            new ExpectedEntry(EdgePolicies, "UserFeedbackAllowed", RegValue.FromDWord(0), RegValue.Absent)),
        new("privacy.edge-copilot", false, TweakScope.Machine, RiskLevel.Caution, false,
            new ExpectedEntry(EdgePolicies, "CopilotPageContext", RegValue.FromDWord(0), RegValue.Absent),
            new ExpectedEntry(EdgePolicies, "EdgeEntraCopilotPageContext", RegValue.FromDWord(0), RegValue.Absent),
            new ExpectedEntry(EdgePolicies, "Microsoft365CopilotChatIconEnabled", RegValue.FromDWord(0), RegValue.Absent),
            new ExpectedEntry(EdgePolicies, "NewTabPageBingChatEnabled", RegValue.FromDWord(0), RegValue.Absent),
            new ExpectedEntry(EdgePolicies, "EdgeHistoryAISearchEnabled", RegValue.FromDWord(0), RegValue.Absent),
            new ExpectedEntry(EdgePolicies, "ComposeInlineEnabled", RegValue.FromDWord(0), RegValue.Absent)),
        new("privacy.notepad-ai", false, TweakScope.Machine, RiskLevel.Caution, false,
            new ExpectedEntry(@"HKLM\SOFTWARE\Policies\WindowsNotepad", "DisableAIFeatures", RegValue.FromDWord(1), RegValue.Absent)),
    };

    /// <summary>Usługi pozycji (id -> oczekiwane wpisy <c>services[]</c>); pozycje spoza tabeli nie mają usług.</summary>
    public static readonly Dictionary<string, ExpectedService[]> ExpectedServices = new()
    {
        ["privacy.diagtrack"] = new[] { new ExpectedService("DiagTrack", ServiceStartType.Disabled, true, ServiceStartType.Automatic) },
    };

    /// <summary>Kandydaci, którzy wypadli: id dokumentu z werdyktem „nie wchodzi” i fraza, która ma w nim być.</summary>
    public static readonly (string DocId, string Phrase)[] Dropped =
    {
        ("privacy.copilot-policy", "kategoria Aplikacje"),
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
        ("privacy.settings-365-ads", "DisableConsumerAccountStateContent"),
        ("privacy.edge-prompts-default-browser", "DefaultBrowserSettingEnabled"),
        ("privacy.activity-history", "EnableActivityFeed"),
        ("privacy.paint-ai", "DisableCocreator"),
        ("privacy.copilot-taskbar-button", "ShowCopilotButton"),
        ("privacy.ai-service-fabric", "WSAIFabricSvc"),
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

    [Fact]
    public void Groups_AsDesigned()
    {
        var expected = new Dictionary<string, string>
        {
            ["privacy.widgets"] = "start",
            ["privacy.search-web-results"] = "start",
            ["privacy.start-recommendations"] = "start",
            ["privacy.start-recommended-section"] = "start",
            ["privacy.settings-suggestions"] = "ads",
            ["privacy.windows-tips"] = "ads",
            ["privacy.settings-account-notifications"] = "ads",
            ["privacy.advertising-id"] = "ads",
            ["privacy.tailored-experiences"] = "ads",
            ["privacy.spotlight"] = "ads",
            ["privacy.diagnostic-data-required"] = "telemetry",
            ["privacy.feedback-notifications"] = "telemetry",
            ["privacy.diagtrack"] = "telemetry",
            ["privacy.explorer-sync-provider-notifications"] = "ads",
            ["privacy.lockscreen-spotlight"] = "ads",
            ["privacy.start-account-notifications"] = "ads",
            ["privacy.toast-suggested"] = "ads",
            ["privacy.toast-backup-reminder"] = "ads",
            ["privacy.edge-shopping-recommendations"] = "ads",
            ["privacy.edge-newtab-content"] = "ads",
            ["privacy.start-track-progs"] = "telemetry",
            ["privacy.online-speech"] = "telemetry",
            ["privacy.inking-typing-improve"] = "telemetry",
            ["privacy.edge-diagnostic-data"] = "telemetry",
            ["privacy.edge-copilot"] = "ai",
            ["privacy.notepad-ai"] = "ai",
        };

        Assert.Equal(Expected.Length, expected.Count);
        Assert.Equal(
            expected.OrderBy(p => p.Key, StringComparer.Ordinal),
            Privacy().ToDictionary(d => d.Id, d => d.Group!).OrderBy(p => p.Key, StringComparer.Ordinal));
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
        if (ExpectedEditions.TryGetValue(id, out var editions))
        {
            Assert.Equal(editions, def.Editions);
        }
        else
        {
            Assert.Null(def.Editions);
        }

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
        Assert.Contains(ObservedOn.TryGetValue(id, out var observed) ? observed : DefaultObservedOn, doc, StringComparison.Ordinal);
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
    public void PrivacyBenefits_PromiseNoSpeedOrResources()
    {
        var forbidden = new Regex(@"(?i)szybc|przyspiesz|lżej|lżejsz|\bRAM\b|pamię|wydajn");
        foreach (var def in Privacy())
        {
            Assert.False(forbidden.IsMatch(def.Benefit ?? string.Empty), $"{def.Id}: benefit obiecuje szybkość lub zasoby");
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
        Assert.Contains("restarcie Eksploratora w „Naprawach i narzędziach”", text, StringComparison.Ordinal);
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
        foreach (var name in new[] { "TurnOffWindowsCopilot", "AllowRecallEnablement", "DisableAIDataAnalysis", "DisableClickToDo", "TaskbarDa", "ConnectedSearchUseWeb", "DisableWebSearch", "EnableDynamicContentInWSB", "DisableWindowsConsumerFeatures","DisableWindowsSpotlightOnSettings", "DisableThirdPartySuggestions", "DisableSoftLanding", "HideRecommendedPersonalizedSites", "DisabledByGroupPolicy", "DisableTailoredExperiencesWithDiagnosticData", "LimitDiagnosticLogCollection", "LimitDumpCollection", "DisableConsumerAccountStateContent", "DefaultBrowserSettingEnabled", "DefaultBrowserSettingsCampaignEnabled", "EnableActivityFeed", "PublishUserActivities", "UploadUserActivities", "DisableCocreator", "DisableGenerativeFill", "DisableImageCreator", "ShowCopilotButton" })
        {
            Assert.DoesNotContain(name, registryNames);
        }

        // Usługa WSAIFabricSvc (C9) nie wchodzi: żadna pozycja nie zmienia jej typu uruchomienia.
        Assert.DoesNotContain(definitions.SelectMany(d => d.Services), s => string.Equals(s.Name, "WSAIFabricSvc", StringComparison.OrdinalIgnoreCase));

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
    [InlineData("privacy.start-account-notifications")]
    [InlineData("privacy.toast-suggested")]
    [InlineData("privacy.toast-backup-reminder")]
    [InlineData("privacy.start-track-progs")]
    [InlineData("privacy.online-speech")]
    [InlineData("privacy.inking-typing-improve")]
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
        // Decyzja autora 2026-10-21 (po partii 1): pozycja niesugerowana z ostrzeżeniem, bo cofnięcie nie przywraca tła Spotlight.
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
    public void LockscreenSpotlight_WarnsAboutLockScreen_DocExplainsDroppedValueAndDesktopItem()
    {
        // Pozycja zmienia tylko Spotlight na ekranie blokady (jedna wartość); druga wartość ze źródeł nie dała skutku.
        var def = Get("privacy.lockscreen-spotlight");
        Assert.False(def.Suggested);
        Assert.Contains("ekranie blokady", string.Join(" ", def.Warnings), StringComparison.Ordinal);
        Assert.DoesNotContain(def.Registry, e => e.Name == "RotatingLockScreenOverlayEnabled");
        var doc = Doc(def.Id);
        Assert.Contains("RotatingLockScreenOverlayEnabled", doc, StringComparison.Ordinal);
        Assert.Contains("privacy.spotlight", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void ToastBackupReminder_IsNotSuggestedAndWarnsThatRemindersStop()
    {
        // Przypomnienie o kopii zapasowej to coś użytecznego, co przestaje działać: pozycja niesugerowana z uwagą.
        var def = Get("privacy.toast-backup-reminder");
        Assert.False(def.Suggested);
        Assert.Contains("kopii zapasowej", string.Join(" ", def.Warnings), StringComparison.Ordinal);
        Assert.Contains("OneDrive", def.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void StartTrackProgs_WarnsThatMostUsedAppsFieldBecomesInactive()
    {
        // Po zapisie pole „Pokaż najczęściej używane aplikacje” w ustawieniach Start jest nieaktywne: uwaga, ryzyko caution.
        var def = Get("privacy.start-track-progs");
        Assert.Contains("Pokaż najczęściej używane aplikacje", string.Join(" ", def.Warnings), StringComparison.Ordinal);
        Assert.Contains("Show most used apps", Doc(def.Id), StringComparison.Ordinal);
    }

    [Fact]
    public void OnlineSpeech_DictationEffectIsStated()
    {
        // Utraty dyktowania (Win+H) nie obserwowano: docs mówią o tym wprost, a uwagi tego nie obiecują.
        var def = Get("privacy.online-speech");
        var doc = Doc(def.Id);
        // Zdanie o dyktowaniu (Win+H) samo mówi „nie sprawdzono”, a nie dowolne inne miejsce w pliku.
        Assert.Contains(
            doc.Split('\n'),
            line => line.Contains("Win+H")
                && line.IndexOf("dyktowa", StringComparison.OrdinalIgnoreCase) >= 0
                && line.IndexOf("nie sprawdzono", StringComparison.OrdinalIgnoreCase) >= 0);
        Assert.DoesNotContain("Win+H", string.Join(" ", def.Warnings), StringComparison.Ordinal);
        // Dowód odwrotny: na świeżym Windows przełącznik jest już wyłączony, więc detekcja daje NotApplied, a zastosowanie nic nie zmienia na ekranie.
        Assert.False(def.Suggested);
        Assert.Contains("dowód odwrotny", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NotApplied", doc, StringComparison.Ordinal);
        Assert.Contains("już wyłączony", def.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void InkingTypingImprove_WarnsAboutDictionary_DocExplainsDroppedTipcValue()
    {
        // Windows pisze pod przełącznikiem, że wyłączenie czyści słownik niestandardowy: uwaga, ryzyko caution.
        // Wpis TIPC\Enabled ze źródeł nie wchodzi (przełącznik go nie zapisuje); docs to wyjaśniają.
        // Słownik może zniknąć, a jego powrotu po cofnięciu nie sprawdzono: pozycja niesugerowana.
        var def = Get("privacy.inking-typing-improve");
        Assert.False(def.Suggested);
        Assert.Contains("słownik", string.Join(" ", def.Warnings), StringComparison.Ordinal);
        Assert.DoesNotContain(def.Registry, e => e.Path.EndsWith(@"\TIPC", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(@"TIPC\Enabled", Doc(def.Id), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("privacy.edge-shopping-recommendations")]
    [InlineData("privacy.edge-newtab-content")]
    [InlineData("privacy.edge-diagnostic-data")]
    [InlineData("privacy.edge-copilot")]
    public void EdgeItems_ManagedWarning_NotSuggested_DocCitesPolicyPages(string id)
    {
        // Polityki Edge: przeglądarka pokazuje ustawienia jako zarządzane przez organizację, więc pozycja jest niesugerowana z uwagą,
        // a dokument cytuje stronę Microsoftu każdej polityki i stan z edge://policy.
        var def = Get(id);
        Assert.False(def.Suggested);
        var warnings = string.Join(" ", def.Warnings);
        Assert.Contains("Edge", warnings, StringComparison.Ordinal);
        Assert.Contains("organizacj", warnings, StringComparison.Ordinal);
        // Klucz Edge jest wspólny: cofnięcie zostawia wartości innych pozycji Edge (i wtedy sam klucz).
        Assert.Contains("wartości innych pozycji Edge w tym kluczu zostają", def.Description, StringComparison.Ordinal);
        var doc = Doc(id);
        Assert.Contains("edge://policy", doc, StringComparison.Ordinal);
        foreach (var entry in def.Registry)
        {
            Assert.Contains(entry.Name, doc, StringComparison.Ordinal);
            Assert.Contains("learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/" + entry.Name.ToLowerInvariant(), doc, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EdgeDiagnosticData_WarnsThatRequiredDataIsOffAndNotRecommended()
    {
        // DiagnosticData = 0 wyłącza też wymagane dane diagnostyczne Edge, czego Microsoft nie zaleca: uwaga i opis mówią to wprost.
        var def = Get("privacy.edge-diagnostic-data");
        var warnings = string.Join(" ", def.Warnings);
        Assert.Contains("wymagane", warnings, StringComparison.Ordinal);
        Assert.Contains("Microsoft nie zaleca", warnings, StringComparison.Ordinal);
        Assert.Contains("wymagane", def.Description, StringComparison.Ordinal);
        Assert.Contains("Not recommended", Doc(def.Id), StringComparison.Ordinal);
    }

    [Fact]
    public void EdgeCopilot_SidebarWarningMatchesRegistry()
    {
        // HubsSidebarEnabled chowa cały pasek boczny Edge. Z nim w rejestrze uwaga ma to mówić; bez niego docs wyjaśniają, czemu go nie ma.
        var def = Get("privacy.edge-copilot");
        if (def.Registry.Any(e => e.Name == "HubsSidebarEnabled"))
        {
            Assert.Contains("pasek boczny", string.Join(" ", def.Warnings), StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("HubsSidebarEnabled", Doc(def.Id), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void NotepadAi_DocConfirmsNotepadStarts()
    {
        // Wartość wyłącza funkcje AI Notatnika: docs potwierdzają, że Notatnik po zmianie startuje, i wskazują źródło (dokument MS albo test).
        var doc = Doc("privacy.notepad-ai");
        Assert.Contains("po zmianie Notatnik się uruchamia", doc, StringComparison.Ordinal);
        Assert.True(
            doc.Contains("https://learn.microsoft.com/en-us/windows/client-management/manage-notepad")
                || doc.Contains("brak dokumentacji MS, dowód: test na VM"),
            "privacy.notepad-ai: docs bez adresu dokumentu MS i bez zdania o braku dokumentacji");
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
    public void CopilotDoc_StatesAppRemovalIsSeparate()
    {
        var doc = Doc("privacy.copilot-policy");
        Assert.Contains("Microsoft.Copilot", doc, StringComparison.Ordinal);
        Assert.Contains("kategoria Aplikacje", doc, StringComparison.Ordinal);
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
        Assert.Contains("brak dokumentacji MS", doc, StringComparison.OrdinalIgnoreCase);
    }
}
