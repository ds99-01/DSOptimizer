using System;
using System.IO;
using System.Linq;
using System.Text;
using DSOptimizer.Catalog;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Model;
using DSOptimizer.Tests.Unit.Engine;
using DSOptimizer.Tests.Unit.Fakes;
using DSOptimizer.Tests.Unit.Support;
using Xunit;

namespace DSOptimizer.Tests.Unit.Catalog;

/// <summary>
/// net.smb-guest-access i net.smb-signing-client-off (partia C3, Task 4): wartości z odczytu VM 26H2 (Home i Pro, 2026-10-18),
/// zdania ostrzeżeń dosłownie z MS Learn (docs/zrodla/2026-10-18-partia-c3-zrodla.md, sekcja 4).
/// Skrypty detect idą przez harness wyłącznie z mockiem helpera Get-DsoSmbClientSetting; nic nie dotyka sieci ani rejestru hosta.
/// </summary>
public class NetSmbClientTests
{
    private const string GuestId = "net.smb-guest-access";
    private const string SigningId = "net.smb-signing-client-off";
    private const string ParametersKey = @"HKLM\SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters";
    private const string GuestValue = "AllowInsecureGuestAuth";
    private const string SigningValue = "RequireSecuritySignature";

    private static TweakDefinition Get(string id)
        => CatalogLoader.Load(BuiltInCatalog.Sources).Definitions.Single(d => d.Id == id);

    private static string Doc(string id)
        => File.ReadAllText(Path.Combine(RepoRoot.Combine("docs", "tweaks"), id + ".md"), Encoding.UTF8);

    private static string AllText(TweakDefinition def) => def.Description + " " + string.Join(" ", def.Warnings);

    // ---- katalog ----

    [Fact]
    public void Entries_ArePresentInNetCategory_WithAliasOnlyOnGuestAccess()
    {
        var definitions = CatalogLoader.Load(BuiltInCatalog.Sources).Definitions;
        Assert.Contains(definitions, d => d.Id == GuestId && d.Category == "net");
        Assert.Contains(definitions, d => d.Id == SigningId && d.Category == "net");
        Assert.Equal(new[] { GuestId }, definitions.Where(d => d.Aliases.Contains("lan_godmode")).Select(d => d.Id));
        Assert.Empty(Get(SigningId).Aliases);
    }

    [Theory]
    [InlineData(GuestId, GuestValue, 1u)]
    [InlineData(SigningId, SigningValue, 0u)]
    public void Entry_WritesOneDwordInLanmanWorkstationParameters_OriginalIsRemoveEntry_NoReboot_NoConflicts(string id, string name, uint value)
    {
        var def = Get(id);
        Assert.Equal(RiskLevel.Caution, def.Risk);
        Assert.Equal(TweakScope.Machine, def.Scope);
        Assert.False(def.RequiresReboot); // VM 26H2: Get-SmbClientConfiguration zmienia się od razu, MS: podpisywanie bez restartu
        Assert.Empty(def.Services);
        Assert.Empty(def.Conflicts); // brak wspólnej wartości rejestru; net.smb-harden to akcja, nie tweak

        var entry = Assert.Single(def.Registry);
        Assert.Equal(ParametersKey, entry.Path);
        Assert.Equal(name, entry.Name);
        Assert.Equal(RegValue.FromDWord(value), entry.Value);
        Assert.Equal(RegValue.Absent, entry.Original); // VM Home i Pro: wartość nie istnieje na świeżym systemie
        Assert.False(entry.DetectOnly);
    }

    [Theory]
    [InlineData(GuestId, "EnableInsecureGuestLogons")]
    [InlineData(SigningId, "RequireSecuritySignature")]
    public void Script_IsDetectOnly_ReadsBooleanThroughMockedHelper_NeverWrites(string id, string property)
    {
        var script = Assert.IsType<ScriptSet>(Get(id).Script);
        Assert.Null(script.Capture);
        Assert.Null(script.Apply);
        Assert.Null(script.Undo);
        var detect = Assert.IsType<string>(script.Detect);
        Assert.Contains($"Get-DsoSmbClientSetting -Name '{property}'", detect, StringComparison.Ordinal);
        Assert.DoesNotContain("Set-Smb", detect, StringComparison.Ordinal);
        Assert.DoesNotContain("HKCU", detect, StringComparison.OrdinalIgnoreCase);
        // Get-SmbClientConfiguration występuje wyłącznie w definicji helpera (jedno wywołanie, tylko odczyt).
        Assert.Equal(1, detect.Split(new[] { "Get-SmbClientConfiguration -ErrorAction Stop" }, StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("Format-List", detect, StringComparison.Ordinal);
    }

    [Fact]
    public void GuestWarnings_AreVerbatimFromMicrosoft_AndPromiseNothingAboutLan()
    {
        var def = Get(GuestId);
        var text = AllText(def);
        // Cytaty Microsoftu przeniesione z programu do dokumentu pozycji (spec opisów 2026-10-26, zasada 3).
        var doc = Doc(GuestId);
        Assert.Contains("enabling guest logons can pose a security threat", doc, StringComparison.Ordinal);
        Assert.Contains("spoofed server", doc, StringComparison.Ordinal);
        Assert.Contains("ransomware", doc, StringComparison.Ordinal);
        Assert.Contains("AitM", doc, StringComparison.Ordinal);
        Assert.Contains("We recommend that you don't enable insecure guest logons.", doc, StringComparison.Ordinal);
        Assert.Contains("Guest logons don't support standard security features such as SMB signing and SMB encryption", doc, StringComparison.Ordinal);
        Assert.Contains("Requiring SMB signing also disables guest access to shares", doc, StringComparison.Ordinal);
        // W programie: ostrzeżenie Microsoftu po polsku w uwagach, druga potrzebna pozycja nazwana tytułem, stan częściowy i cofnięcie bez zapisu.
        var warnings = string.Join(" ", def.Warnings);
        Assert.Contains("Microsoft odradza", warnings, StringComparison.Ordinal);
        Assert.Contains(Get(SigningId).Title, warnings, StringComparison.Ordinal);
        Assert.Contains("24H2", text, StringComparison.Ordinal);
        Assert.Contains("nie ma zapisu sprzed zmiany", text, StringComparison.Ordinal);
        Assert.Contains("częściowy", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Komputery widoczne", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("przyspiesz", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("%", text, StringComparison.Ordinal);
    }

    [Fact]
    public void SigningWarnings_AreVerbatimFromMicrosoft_AndScopeIsClientOnly()
    {
        var def = Get(SigningId);
        var text = AllText(def);
        // Cytaty Microsoftu przeniesione z programu do dokumentu pozycji (spec opisów 2026-10-26, zasada 3).
        var doc = Doc(SigningId);
        Assert.Contains("We don't recommend disabling SMB signing as a workaround for third-party servers.", doc, StringComparison.Ordinal);
        Assert.Contains("We also don't recommend trying to sign with guest accounts.", doc, StringComparison.Ordinal);
        Assert.Contains("Requiring SMB signing also disables guest access to shares", doc, StringComparison.Ordinal);
        Assert.Contains("Microsoft odradza", string.Join(" ", def.Warnings), StringComparison.Ordinal);
        Assert.Contains(Get(GuestId).Title, text, StringComparison.Ordinal);
        Assert.Contains("klient", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("serwer", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("nie ma zapisu sprzed zmiany", text, StringComparison.Ordinal);
        Assert.DoesNotContain("przyspiesz", text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(GuestId)]
    [InlineData(SigningId)]
    public void Docs_StateVmDefaultsUndoWithoutJournalAndWhatWasNotTested(string id)
    {
        var doc = Doc(id);
        Assert.Contains("brak wartości", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Get-SmbClientConfiguration", doc, StringComparison.Ordinal);
        Assert.Contains("Set-SmbClientConfiguration", doc, StringComparison.Ordinal);
        Assert.Contains("bez wpisu w journalu", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("wartość domyślna", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Partial", doc, StringComparison.Ordinal);
        Assert.Contains("nie testowane na VM", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("brak sieci", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("learn.microsoft.com", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void GuestDoc_ExplainsHomeVersusProDefaults_AndSigningRequirementOn24H2()
    {
        var doc = Doc(GuestId);
        Assert.Contains("Home", doc, StringComparison.Ordinal);
        Assert.Contains("Pro", doc, StringComparison.Ordinal);
        Assert.Contains("EnableInsecureGuestLogons", doc, StringComparison.Ordinal);
        Assert.Contains("net.smb-signing-client-off", doc, StringComparison.Ordinal);
        Assert.Contains("Requiring SMB signing also disables guest access", doc, StringComparison.Ordinal);
    }

    // ---- skrypt detect w harnessie (helper zamockowany) ----

    private static string Mock(string body) => $"function Get-DsoSmbClientSetting {{ param($Name) {body} }}";

    [Theory]
    [InlineData(GuestId, "$true", "Applied")]
    [InlineData(GuestId, "$false", "NotApplied")]
    [InlineData(SigningId, "$false", "Applied")]
    [InlineData(SigningId, "$true", "NotApplied")]
    public void Detect_MapsBooleanToState(string id, string mocked, string expected)
    {
        var run = PowerShellHarness.Run(Get(id).Script!.Detect!, Mock(mocked), string.Empty);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(expected, run.LastLine);
    }

    [Theory]
    [InlineData(GuestId)]
    [InlineData(SigningId)]
    public void Detect_AsksTheHelperForTheRightProperty(string id)
    {
        var expectedName = id == GuestId ? "EnableInsecureGuestLogons" : "RequireSecuritySignature";
        var run = PowerShellHarness.Run(Get(id).Script!.Detect!, Mock("if ($Name -eq '" + expectedName + "') { $true } else { $null }"), string.Empty);
        Assert.Equal(0, run.ExitCode);
        Assert.Contains(run.LastLine, new[] { "Applied", "NotApplied" });
    }

    [Theory]
    [InlineData(GuestId, "throw 'Get-SmbClientConfiguration niedostępny'")]
    [InlineData(GuestId, "$null")]
    [InlineData(GuestId, "'True'")] // tekst nie jest wartością logiczną
    [InlineData(GuestId, "1")]
    [InlineData(SigningId, "throw 'Get-SmbClientConfiguration niedostępny'")]
    [InlineData(SigningId, "$null")]
    [InlineData(SigningId, "'False'")]
    [InlineData(SigningId, "0")]
    public void Detect_WhenCmdletFailsOrAnswersNotBoolean_IsNotAnAnswer(string id, string mocked)
    {
        var run = PowerShellHarness.Run(Get(id).Script!.Detect!, Mock(mocked), string.Empty);
        Assert.NotEqual(0, run.ExitCode);
        Assert.NotEqual("Applied", run.LastLine);
        Assert.NotEqual("NotApplied", run.LastLine);
    }

    [Theory]
    [InlineData(GuestId)]
    [InlineData(SigningId)]
    public void Detect_WithoutMock_HitsGuard_AndNeverTouchesHostNetworkStack(string id)
    {
        var run = PowerShellHarness.Run(Get(id).Script!.Detect!, mocks: string.Empty, snapshot: string.Empty);
        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Bezpiecznik harnessu", run.Error, StringComparison.Ordinal);
    }

    // ---- silnik na fake'ach: rejestr + odczyt cmdletu w jednej agregacji ----

    /// <summary>Model Get-SmbClientConfiguration: jawna wartość z rejestru wygrywa, brak wartości = domyślna edycji (odczyt VM).</summary>
    private sealed class SmbModel
    {
        public SmbModel(EngineFixture fixture, string valueName, bool editionDefault)
        {
            Fixture = fixture;
            ValueName = valueName;
            EditionDefault = editionDefault;
        }

        public EngineFixture Fixture { get; }

        public string ValueName { get; }

        public bool EditionDefault { get; }

        /// <summary>Polityka (GPO) przesłaniająca rejestr: null = brak polityki.</summary>
        public bool? PolicyOverride { get; set; }

        public bool CmdletFails { get; set; }

        public bool Effective()
        {
            if (PolicyOverride is { } forced)
            {
                return forced;
            }

            var current = Fixture.Registry.Get(ParametersKey, ValueName);
            return current.Exists ? (uint)current.Data! != 0 : EditionDefault;
        }
    }

    private static SmbModel Wire(EngineFixture f, TweakDefinition def, bool editionDefault)
    {
        var model = new SmbModel(f, def.Registry[0].Name, editionDefault);
        f.Scripts.On(def.Script!.Detect!, _ =>
        {
            if (model.CmdletFails)
            {
                return FakeScriptRunner.Result(1);
            }

            // Guest: Applied, gdy EnableInsecureGuestLogons = True. Signing: Applied, gdy RequireSecuritySignature = False.
            var applied = def.Id == GuestId ? model.Effective() : !model.Effective();
            return FakeScriptRunner.Result(0, applied ? "Applied" : "NotApplied");
        });
        return model;
    }

    [Theory]
    [InlineData(true, DetectionState.Partial)] // Home: klient już zezwala na gościa, ale konfiguracja nie jest zapisana jawnie
    [InlineData(false, DetectionState.NotApplied)] // Pro
    public void Guest_OnFreshSystem_DetectDependsOnEditionDefault(bool homeDefaultGuestAllowed, DetectionState expected)
    {
        var f = new EngineFixture();
        var def = Get(GuestId);
        Wire(f, def, homeDefaultGuestAllowed);

        Assert.Equal(expected, f.CreateEngine().Detect(def));
    }

    [Fact]
    public void Signing_OnFreshSystem_IsNotApplied()
    {
        // Odczyt VM: RequireSecuritySignature = True na Home i Pro (domyślnie), wartości w rejestrze brak: ten sam wynik na obu edycjach.
        var f = new EngineFixture();
        var def = Get(SigningId);
        Wire(f, def, editionDefault: true);

        Assert.Equal(DetectionState.NotApplied, f.CreateEngine().Detect(def));
    }

    [Theory]
    [InlineData(GuestId, true, GuestValue, 1u)]
    [InlineData(GuestId, false, GuestValue, 1u)]
    [InlineData(SigningId, true, SigningValue, 0u)]
    public void ApplyThenUndo_WhenValueAbsent_EndsInAppliedThenRestoresAbsence(string id, bool editionDefault, string name, uint applied)
    {
        var f = new EngineFixture();
        var def = Get(id);
        var model = Wire(f, def, editionDefault);
        f.Registry.CreateKey(ParametersKey); // VM Home i Pro: klucz Parameters istnieje na świeżym systemie
        var engine = f.CreateEngine();
        var before = engine.Detect(def);

        var result = engine.Apply(def);
        Assert.Equal(TweakOutcome.Applied, result.Outcome);
        Assert.False(result.RequiresReboot);
        Assert.Equal(DetectionState.Applied, result.FinalState);
        Assert.Equal(RegValue.FromDWord(applied), f.Registry.Get(ParametersKey, name));

        var undone = engine.Undo(def);
        Assert.Equal(TweakOutcome.Undone, undone.Outcome);
        Assert.Equal(RegValue.Absent, f.Registry.Get(ParametersKey, name)); // R4: brak wartości wraca jako brak
        Assert.Equal(before, engine.Detect(def)); // odczyt cmdletu wraca do stanu sprzed apply
        Assert.Equal(editionDefault, model.Effective()); // wartość usunięta: obowiązuje domyślna edycji
        Assert.True(f.Registry.HasKey(ParametersKey)); // klucz Parameters istniał przed apply i zostaje
    }

    [Fact]
    public void Undo_WithJournal_RestoresExplicitValueSetBeforeApply()
    {
        var f = new EngineFixture();
        var def = Get(GuestId);
        Wire(f, def, editionDefault: false);
        f.Registry.Set(ParametersKey, GuestValue, RegValue.FromDWord(0)); // np. po wcześniejszym Set-SmbClientConfiguration $false
        var engine = f.CreateEngine();

        Assert.Equal(TweakOutcome.Applied, engine.Apply(def).Outcome);
        Assert.Equal(TweakOutcome.Undone, engine.Undo(def).Outcome);
        Assert.Equal(RegValue.FromDWord(0), f.Registry.Get(ParametersKey, GuestValue));
    }

    [Theory]
    [InlineData(GuestId, GuestValue, 1u, true)]
    [InlineData(GuestId, GuestValue, 1u, false)]
    [InlineData(SigningId, SigningValue, 0u, true)]
    public void UndoWithoutJournal_RemovesTheValue_SoEditionDefaultReturns_NeverLowersProtectionBelowDefault(string id, string name, uint present, bool editionDefault)
    {
        var f = new EngineFixture();
        var def = Get(id);
        var model = Wire(f, def, editionDefault);
        f.Registry.Set(ParametersKey, name, RegValue.FromDWord(present));

        var result = f.CreateEngine().Undo(def);

        Assert.Equal(TweakOutcome.UndoneFromDefaults, result.Outcome);
        Assert.Equal(RegValue.Absent, f.Registry.Get(ParametersKey, name));
        Assert.Equal(editionDefault, model.Effective()); // po usunięciu wartości obowiązuje domyślna edycji (Home/Pro)
        Assert.Empty(f.Journal.Current.Entries);
    }

    [Theory]
    [InlineData(GuestId, GuestValue, 1u, false)] // zapis 1, a polityka dalej blokuje gościa
    [InlineData(SigningId, SigningValue, 0u, true)] // zapis 0, a polityka dalej wymaga podpisywania
    public void PolicyOverridingTheRegistry_IsPartial_NeverApplied(string id, string name, uint written, bool forcedByPolicy)
    {
        var f = new EngineFixture();
        var def = Get(id);
        var model = Wire(f, def, editionDefault: id == SigningId);
        model.PolicyOverride = forcedByPolicy;
        f.Registry.Set(ParametersKey, name, RegValue.FromDWord(written));

        Assert.Equal(DetectionState.Partial, f.CreateEngine().Detect(def));
    }

    [Theory]
    [InlineData(GuestId, GuestValue, false)]
    [InlineData(SigningId, SigningValue, true)]
    public void Apply_WhenPolicyOverrides_IsNotReportedAsSuccess_AndRegistryIsRolledBack(string id, string name, bool forcedByPolicy)
    {
        var f = new EngineFixture();
        var def = Get(id);
        var model = Wire(f, def, editionDefault: id == SigningId);
        model.PolicyOverride = forcedByPolicy;

        var result = f.CreateEngine().Apply(def);

        Assert.False(result.IsSuccess);
        Assert.NotEqual(TweakOutcome.Applied, result.Outcome);
        Assert.Equal(RegValue.Absent, f.Registry.Get(ParametersKey, name));
    }

    [Theory]
    [InlineData(GuestId, GuestValue, 1u)]
    [InlineData(SigningId, SigningValue, 0u)]
    public void Detect_WhenCmdletFails_IsUnknown_NeverApplied(string id, string name, uint written)
    {
        var f = new EngineFixture();
        var def = Get(id);
        var model = Wire(f, def, editionDefault: true);
        f.Registry.Set(ParametersKey, name, RegValue.FromDWord(written));
        model.CmdletFails = true;

        Assert.Equal(DetectionState.Unknown, f.CreateEngine().Detect(def));
    }

    [Fact]
    public void GuestAndSigning_AreIndependent_BothCanBeAppliedAndUndoneInAnyOrder()
    {
        var f = new EngineFixture();
        var guest = Get(GuestId);
        var signing = Get(SigningId);
        Wire(f, guest, editionDefault: false);
        Wire(f, signing, editionDefault: true);
        var engine = f.CreateEngine();

        Assert.Equal(TweakOutcome.Applied, engine.Apply(guest).Outcome);
        Assert.Equal(TweakOutcome.Applied, engine.Apply(signing).Outcome);
        Assert.Equal(TweakOutcome.Undone, engine.Undo(guest).Outcome);
        Assert.Equal(DetectionState.Applied, engine.Detect(signing));
        Assert.Equal(TweakOutcome.Undone, engine.Undo(signing).Outcome);
        Assert.Equal(RegValue.Absent, f.Registry.Get(ParametersKey, GuestValue));
        Assert.Equal(RegValue.Absent, f.Registry.Get(ParametersKey, SigningValue));
    }
}
