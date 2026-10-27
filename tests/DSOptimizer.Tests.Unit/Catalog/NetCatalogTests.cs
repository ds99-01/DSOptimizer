using System;
using System.IO;
using System.Linq;
using System.Text;
using DSOptimizer.Catalog;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Model;
using DSOptimizer.Tests.Unit.Engine;
using DSOptimizer.Tests.Unit.Support;
using Xunit;

namespace DSOptimizer.Tests.Unit.Catalog;

/// <summary>Pozycje kategorii net (partia C3): wartości z docs/zrodla/2026-10-18-partia-c3-zrodla.md i z odczytu VM 26H2.</summary>
public class NetCatalogTests
{
    private const string Tcpip6Key = @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters";
    private const string Value = "DisabledComponents";

    private static TweakDefinition Get(string id)
        => CatalogLoader.Load(BuiltInCatalog.Sources).Definitions.Single(d => d.Id == id);

    private static string Doc(string id)
        => File.ReadAllText(Path.Combine(RepoRoot.Combine("docs", "tweaks"), id + ".md"), Encoding.UTF8);

    [Fact]
    public void Conflicts_InBuiltInCatalog_ReferenceExistingIds_AndAreSymmetric()
    {
        var definitions = CatalogLoader.Load(BuiltInCatalog.Sources).Definitions;
        var byId = definitions.ToDictionary(d => d.Id);
        foreach (var definition in definitions)
        {
            foreach (var other in definition.Conflicts)
            {
                Assert.True(byId.ContainsKey(other), $"{definition.Id}: conflicts wskazuje nieistniejące id '{other}'.");
                Assert.True(byId[other].Conflicts.Contains(definition.Id), $"{definition.Id} <-> {other}: konflikt nie jest symetryczny.");
            }
        }
    }

    [Fact]
    public void Ipv6Entries_ArePresentInNetCategory()
    {
        var ids = CatalogLoader.Load(BuiltInCatalog.Sources).Definitions.Where(d => d.Category == "net").Select(d => d.Id).ToList();
        Assert.Contains("net.ipv6-prefer-ipv4", ids);
        Assert.Contains("net.ipv6-disable", ids);
    }

    [Fact]
    public void Ipv6Disable_HasLegacyAlias_AndPreferIpv4HasNone()
    {
        var definitions = CatalogLoader.Load(BuiltInCatalog.Sources).Definitions;
        Assert.Equal(new[] { "net.ipv6-disable" }, definitions.Where(d => d.Aliases.Contains("net_ipv6")).Select(d => d.Id));
        Assert.Empty(Get("net.ipv6-prefer-ipv4").Aliases);
    }

    [Fact]
    public void ThrottlingIndex_IsNotPorted_NoCatalogEntryNorAlias()
    {
        var definitions = CatalogLoader.Load(BuiltInCatalog.Sources).Definitions;
        Assert.DoesNotContain(definitions, d => d.Id == "net.throttling-index");
        Assert.DoesNotContain(definitions, d => d.Aliases.Contains("net_throttle"));
        var doc = Doc("net.throttling-index");
        Assert.Contains("nie portowane", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NetworkThrottlingIndex", doc, StringComparison.Ordinal);
        Assert.Contains("SystemResponsiveness", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void PreferIpv4_SetsDwordTwentyWithAbsentOriginal_CautionAndReboot()
    {
        var def = Get("net.ipv6-prefer-ipv4");
        Assert.Equal(RiskLevel.Caution, def.Risk);
        Assert.Equal(TweakScope.Machine, def.Scope);
        Assert.True(def.RequiresReboot);
        Assert.Empty(def.Services);
        var entry = Assert.Single(def.Registry);
        Assert.Equal(Tcpip6Key, entry.Path);
        Assert.Equal(Value, entry.Name);
        Assert.Equal(RegValue.FromDWord(0x20), entry.Value);
        Assert.Equal(RegValue.Absent, entry.Original); // VM 26H2 Home i Pro: DisabledComponents nie istnieje
        Assert.Contains("net.ipv6-disable", def.Conflicts);
    }

    [Fact]
    public void Ipv6Disable_SetsDwordFFWithAbsentOriginal_ExperimentalAndReboot()
    {
        var def = Get("net.ipv6-disable");
        Assert.Equal(RiskLevel.Experimental, def.Risk);
        Assert.Equal(TweakScope.Machine, def.Scope);
        Assert.True(def.RequiresReboot);
        Assert.Empty(def.Services);
        var entry = Assert.Single(def.Registry);
        Assert.Equal(Tcpip6Key, entry.Path);
        Assert.Equal(Value, entry.Name);
        Assert.Equal(RegValue.FromDWord(0xFF), entry.Value); // nie 0xFFFFFFFF (MS: 5 s opóźnienia startu)
        Assert.Equal(RegValue.Absent, entry.Original);
        Assert.Contains("net.ipv6-prefer-ipv4", def.Conflicts);
    }

    [Fact]
    public void Ipv6Warnings_AreMsFactsOnly()
    {
        var prefer = Get("net.ipv6-prefer-ipv4");
        var disable = Get("net.ipv6-disable");
        foreach (var def in new[] { prefer, disable })
        {
            Assert.NotEmpty(def.Warnings);
            var text = def.Description + " " + string.Join(" ", def.Warnings);
            Assert.Contains("restarc", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("przyspiesz", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DNS", text, StringComparison.Ordinal);
            Assert.DoesNotContain("%", text, StringComparison.Ordinal);
        }

        // Uwagi (widoczne od razu): odradzenie przez Microsoft i usługa, która przestaje działać.
        var disableText = string.Join(" ", disable.Warnings);
        Assert.Contains("nie zaleca", disableText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Routing and Remote Access", disableText, StringComparison.Ordinal);
        // Szczegóły techniczne (spec opisów 2026-10-26): ::1, 0xFFFFFFFF i zalecana alternatywa nazwana tytułem, nie identyfikatorem.
        Assert.Contains("::1", disable.Description, StringComparison.Ordinal);
        Assert.Contains("0xFFFFFFFF", disable.Description, StringComparison.Ordinal);
        Assert.Contains("Prefer IPv4", disable.Description, StringComparison.Ordinal);
        Assert.Contains(prefer.Title, disable.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Docs_OfIpv6Entries_DescribeUndoOrderAndVmDefaults()
    {
        foreach (var id in new[] { "net.ipv6-prefer-ipv4", "net.ipv6-disable" })
        {
            var doc = Doc(id);
            Assert.Contains("najpierw", doc, StringComparison.Ordinal);
            Assert.Contains("poza narzędziem", doc, StringComparison.Ordinal);
            Assert.Contains("DisabledComponents", doc, StringComparison.Ordinal);
            Assert.Contains("brak", doc, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PreferIpv4_Detection_IsEquality_OtherBitCombinationsAreNotApplied()
    {
        // Silnik nie ma masek: 0x21 (bit Tunnel + PreferIpv4) to NotApplied, apply nadpisuje całość, snapshot zachowuje 0x21.
        var f = new EngineFixture();
        f.Registry.Set(Tcpip6Key, Value, RegValue.FromDWord(0x21));
        var engine = f.CreateEngine();
        var def = Get("net.ipv6-prefer-ipv4");

        Assert.Equal(DetectionState.NotApplied, engine.Detect(def));
        Assert.Equal(TweakOutcome.Applied, engine.Apply(def).Outcome);
        Assert.Equal(RegValue.FromDWord(0x20), f.Registry.Get(Tcpip6Key, Value));
        Assert.Equal(TweakOutcome.Undone, engine.Undo(def).Outcome);
        Assert.Equal(RegValue.FromDWord(0x21), f.Registry.Get(Tcpip6Key, Value));
    }

    [Theory]
    [InlineData("net.ipv6-prefer-ipv4", 0x20u)]
    [InlineData("net.ipv6-disable", 0xFFu)]
    public void Ipv6_ApplyUndo_WhenValueAbsent_RestoresAbsenceNotZero(string id, uint expected)
    {
        var f = new EngineFixture();
        var engine = f.CreateEngine();
        var def = Get(id);

        var applied = engine.Apply(def);
        Assert.Equal(TweakOutcome.Applied, applied.Outcome);
        Assert.True(applied.RequiresReboot);
        Assert.Equal(RegValue.FromDWord(expected), f.Registry.Get(Tcpip6Key, Value));

        Assert.Equal(TweakOutcome.Undone, engine.Undo(def).Outcome);
        Assert.Equal(RegValue.Absent, f.Registry.Get(Tcpip6Key, Value));
    }

    [Fact]
    public void Ipv6Disable_ValueFFFFFFFF_IsNotApplied()
    {
        var f = new EngineFixture();
        f.Registry.Set(Tcpip6Key, Value, RegValue.FromDWord(0xFFFFFFFFu));
        Assert.Equal(DetectionState.NotApplied, f.CreateEngine().Detect(Get("net.ipv6-disable")));
    }

    // ---- Konflikt obu pozycji (ta sama wartość rejestru): silnik odmawia Apply, gdy konfliktująca pozycja jest aktywna (R5) ----

    [Theory]
    [InlineData("net.ipv6-prefer-ipv4", "net.ipv6-disable", 0x20u)]
    [InlineData("net.ipv6-disable", "net.ipv6-prefer-ipv4", 0xFFu)]
    public void Conflict_ApplyIsRefused_WhileOtherEntryIsActive_WithoutAnyWrite(string firstId, string secondId, uint firstValue)
    {
        var f = new EngineFixture();
        var engine = f.CreateEngine();
        Assert.Equal(TweakOutcome.Applied, engine.Apply(Get(firstId)).Outcome);
        var journalBefore = f.Journal.SaveCount;
        var writesBefore = f.Registry.WriteLog.ToList();

        var result = engine.Apply(Get(secondId));

        Assert.Equal(TweakOutcome.Refused, result.Outcome);
        Assert.Contains(firstId, result.Message, StringComparison.Ordinal);
        Assert.Contains("najpierw ją cofnij", result.Message, StringComparison.Ordinal);
        Assert.Equal(journalBefore, f.Journal.SaveCount);
        Assert.Equal(writesBefore, f.Registry.WriteLog);
        Assert.Equal(RegValue.FromDWord(firstValue), f.Registry.Get(Tcpip6Key, Value));
        Assert.Single(f.Journal.Current.Entries);
    }

    [Theory]
    [InlineData("net.ipv6-prefer-ipv4", "net.ipv6-disable", 0xFFu)]
    [InlineData("net.ipv6-disable", "net.ipv6-prefer-ipv4", 0x20u)]
    public void Conflict_AfterUndoOfOtherEntry_ApplySucceeds_AndUndoRestoresAbsence(string firstId, string secondId, uint secondValue)
    {
        var f = new EngineFixture();
        var engine = f.CreateEngine();
        Assert.Equal(TweakOutcome.Applied, engine.Apply(Get(firstId)).Outcome);
        Assert.Equal(TweakOutcome.Undone, engine.Undo(Get(firstId)).Outcome);

        Assert.Equal(TweakOutcome.Applied, engine.Apply(Get(secondId)).Outcome);
        Assert.Equal(RegValue.FromDWord(secondValue), f.Registry.Get(Tcpip6Key, Value));
        Assert.Equal(TweakOutcome.Undone, engine.Undo(Get(secondId)).Outcome);
        Assert.Equal(RegValue.Absent, f.Registry.Get(Tcpip6Key, Value));
    }

    [Fact]
    public void Conflict_UnknownIdInConflicts_HasNoEffect()
    {
        var f = new EngineFixture();
        var engine = f.CreateEngine();
        var def = Get("net.ipv6-disable") with { Conflicts = new[] { "net.nie-istnieje" } };

        Assert.Equal(TweakOutcome.Applied, engine.Apply(def).Outcome);
        Assert.Equal(RegValue.FromDWord(0xFF), f.Registry.Get(Tcpip6Key, Value));
    }

    [Fact]
    public void Conflict_ValueSetOutsideTheTool_IsNotDetectedAsConflict()
    {
        // Ograniczenie opisane w docs: konflikt dotyczy tylko wpisów journala, nie ręcznie ustawionej wartości.
        var f = new EngineFixture();
        f.Registry.Set(Tcpip6Key, Value, RegValue.FromDWord(0x20));
        var engine = f.CreateEngine();

        Assert.Equal(TweakOutcome.Applied, engine.Apply(Get("net.ipv6-disable")).Outcome);
        Assert.Equal(TweakOutcome.Undone, engine.Undo(Get("net.ipv6-disable")).Outcome);
        Assert.Equal(RegValue.FromDWord(0x20), f.Registry.Get(Tcpip6Key, Value));
    }

    [Fact]
    public void Conflict_WhenJournalCannotBeRead_ApplyFailsClosed_WithoutAnyWrite()
    {
        var f = new EngineFixture();
        f.Journal.FailLoad = true;

        var result = f.CreateEngine().Apply(Get("net.ipv6-disable"));

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Contains("zmiana nie została wykonana", result.Message, StringComparison.Ordinal);
        Assert.Empty(f.Registry.WriteLog);
        Assert.Equal(RegValue.Absent, f.Registry.Get(Tcpip6Key, Value));
        Assert.Equal(0, f.Journal.SaveCount);
    }

    [Fact]
    public void Conflict_WhenJournalLoadThrowsUnexpectedly_ApplyFailsClosed_AndLogsFailure()
    {
        var f = new EngineFixture();
        f.Journal.LoadException = new InvalidOperationException("boom");

        var result = f.CreateEngine().Apply(Get("net.ipv6-disable"));

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Empty(f.Registry.WriteLog);
        Assert.Contains(f.Log.Lines, l => l.Contains("sprawdzaniu konfliktów"));
    }

    [Fact]
    public void Conflict_LoadWarning_ReturnedOnlyByFirstLoad_StillReachesResultAndLog_WithSingleLoad()
    {
        // Prawdziwy JsonFileJournalStore odkłada uszkodzony plik przy pierwszym Load, kolejny Load nie ma już ostrzeżenia.
        var f = new EngineFixture();
        f.Journal.LoadWarning = "Journal był uszkodzony – utworzono nowy.";
        f.Journal.WarnOnlyOnFirstLoad = true;

        var result = f.CreateEngine().Apply(Get("net.ipv6-disable"));

        Assert.Equal(TweakOutcome.Applied, result.Outcome);
        Assert.Contains("Journal był uszkodzony – utworzono nowy.", result.Warnings);
        Assert.Contains(f.Log.Lines, l => l.Contains("Journal był uszkodzony"));
        Assert.Equal(1, f.Journal.LoadCount);
    }

    [Fact]
    public void Conflict_LoadWarning_IsAlsoReportedOnRefusal()
    {
        var f = new EngineFixture();
        var engine = f.CreateEngine();
        Assert.Equal(TweakOutcome.Applied, engine.Apply(Get("net.ipv6-prefer-ipv4")).Outcome);
        f.Journal.LoadWarning = "ostrzeżenie journala";

        var result = engine.Apply(Get("net.ipv6-disable"));

        Assert.Equal(TweakOutcome.Refused, result.Outcome);
        Assert.Contains("ostrzeżenie journala", result.Warnings);
    }

    [Theory]
    [InlineData("net.ipv6-prefer-ipv4", "net.ipv6-disable", 0xFFu)]
    [InlineData("net.ipv6-disable", "net.ipv6-prefer-ipv4", 0x20u)]
    public void Conflict_UndoWithoutOwnJournalEntry_IsRefused_WhileConflictingEntryIsActive(string undoId, string activeId, uint activeValue)
    {
        var f = new EngineFixture();
        var engine = f.CreateEngine();
        Assert.Equal(TweakOutcome.Applied, engine.Apply(Get(activeId)).Outcome);
        var writes = f.Registry.WriteLog.ToList();
        var saves = f.Journal.SaveCount;

        var result = engine.Undo(Get(undoId));

        Assert.Equal(TweakOutcome.Refused, result.Outcome);
        Assert.Contains(activeId, result.Message, StringComparison.Ordinal);
        Assert.Equal(writes, f.Registry.WriteLog);
        Assert.Equal(saves, f.Journal.SaveCount);
        Assert.Equal(RegValue.FromDWord(activeValue), f.Registry.Get(Tcpip6Key, Value));
        Assert.Equal(DetectionState.Applied, engine.Detect(Get(activeId)));
    }

    [Fact]
    public void Conflict_UndoWithoutOwnJournalEntry_WhenNoConflictActive_StillUsesDefaults()
    {
        var f = new EngineFixture();
        f.Registry.Set(Tcpip6Key, Value, RegValue.FromDWord(0x20));

        var result = f.CreateEngine().Undo(Get("net.ipv6-prefer-ipv4"));

        Assert.Equal(TweakOutcome.UndoneFromDefaults, result.Outcome);
        Assert.Equal(RegValue.Absent, f.Registry.Get(Tcpip6Key, Value));
    }
}
