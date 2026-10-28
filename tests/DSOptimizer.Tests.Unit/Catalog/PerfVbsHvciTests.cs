using System;
using System.IO;
using System.Linq;
using System.Text;
using DSOptimizer.Catalog;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Journal;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Model;
using DSOptimizer.Tests.Unit.Engine;
using DSOptimizer.Tests.Unit.Fakes;
using DSOptimizer.Tests.Unit.Support;
using Xunit;

namespace DSOptimizer.Tests.Unit.Catalog;

/// <summary>perf.vbs-hvci (partia C2): wartości z docs/zrodla/2026-10-18-partia-c2-zrodla.md i z odczytu VM 26H2.</summary>
public class PerfVbsHvciTests
{
    private const string HvciKey = @"HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity";

    private const string NoLock = "$null";

    private static TweakDefinition Get()
        => CatalogLoader.Load(BuiltInCatalog.Sources).Definitions.Single(d => d.Id == "perf.vbs-hvci");

    // Mocki PowerShell: helpery CIM i rejestru. Skrypt produkcyjny pomija własne definicje, gdy te nazwy już istnieją.
    private static string VbsMocks(string running, string locked, string? enabled = null)
        => string.Join(
            "\r\n",
            $"function Get-DsoSecurityServicesRunning {{ {running} }}",
            $"function Get-DsoHvciLocked {{ {locked} }}",
            $"function Get-DsoHvciEnabled {{ {enabled ?? EnabledZero} }}");

    private static string Locked(string kind, uint value) => $"[pscustomobject]@{{ Kind = '{kind}'; Value = [uint32]{value} }}";

    // Mock Get-DsoHvciEnabled: wartość Enabled w rejestrze ($null = brak). Domyślnie DWord 0, czyli konfiguracja "wyłączone".
    private static readonly string EnabledZero = Locked("DWord", 0);

    // ---- katalog ----

    [Fact]
    public void VbsHvci_IsExperimentalNarrowEnabledZeroWithRemoveEntryOriginalAndGuardScript()
    {
        var def = Get();
        Assert.Equal(RiskLevel.Experimental, def.Risk);
        Assert.Equal(TweakScope.Machine, def.Scope);
        Assert.True(def.RequiresReboot);
        Assert.Contains("cpu_vbs", def.Aliases);
        Assert.Empty(def.Services);

        // Wariant A z research 4: tylko Scenarios\HypervisorEnforcedCodeIntegrity\Enabled = 0, original z VM = brak wartości.
        var entry = Assert.Single(def.Registry);
        Assert.Equal(HvciKey, entry.Path);
        Assert.Equal("Enabled", entry.Name);
        Assert.Equal(RegValue.FromDWord(0), entry.Value);
        Assert.Equal(RegValue.Absent, entry.Original);
        Assert.False(entry.DetectOnly);

        var script = Assert.IsType<ScriptSet>(def.Script);
        Assert.All(new[] { script.Detect, script.Capture, script.Apply, script.Undo }, Assert.NotNull);
        Assert.Contains("Win32_DeviceGuard", script.Detect, StringComparison.Ordinal);

        // Nie dotyka VBS jako całości, Credential Guard ani WasEnabledBy; HVCIEnabled nie jest sygnałem detekcji.
        var all = string.Join("\n", script.Detect, script.Capture, script.Apply, script.Undo, entry.Path, entry.Name);
        Assert.DoesNotContain("EnableVirtualizationBasedSecurity", all, StringComparison.Ordinal);
        Assert.DoesNotContain("LsaCfgFlags", all, StringComparison.Ordinal);
        Assert.DoesNotContain("WasEnabledBy", all, StringComparison.Ordinal);
        Assert.DoesNotContain("HVCIEnabled", all, StringComparison.Ordinal);
        Assert.DoesNotContain("HKCU", all, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VbsHvci_WarningsComeFromMicrosoftSources_NoUnsourcedClaims()
    {
        var def = Get();
        var text = def.Description + " " + string.Join(" ", def.Warnings);
        Assert.Contains("jądra", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("czystych instalacjach", text, StringComparison.Ordinal);
        // Termin rozszerzenia (doniesienia prasowe, nie Microsoft) tylko w dokumencie – zasada 3 spec opisów 2026-10-26.
        Assert.DoesNotContain("października 2026", text, StringComparison.Ordinal);
        Assert.Contains("od października 2026", File.ReadAllText(Path.Combine(RepoRoot.Combine("docs", "tweaks"), "perf.vbs-hvci.md"), Encoding.UTF8), StringComparison.Ordinal);
        Assert.Contains("Windows Security", text, StringComparison.Ordinal);
        Assert.Contains("UEFI", text, StringComparison.Ordinal);
        Assert.Contains("Secure Boot", text, StringComparison.Ordinal);
        Assert.Contains("restart", text, StringComparison.OrdinalIgnoreCase);
        foreach (var banned in new[] { "Hyper-V", "Docker", "WSL", "%", "przyspiesz" })
        {
            Assert.DoesNotContain(banned, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Docs_StateTheCoexistenceDecisionAndTheVmLimitation()
    {
        var doc = File.ReadAllText(Path.Combine(RepoRoot.Combine("docs", "tweaks"), "perf.vbs-hvci.md"), Encoding.UTF8);
        Assert.Contains("realny stan nie testowany na VM", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("VirtualizationBasedSecurityStatus", doc, StringComparison.Ordinal);
        Assert.Contains("StateAggregator", doc, StringComparison.Ordinal);
        Assert.Contains("PendingReboot", doc, StringComparison.Ordinal);
        Assert.Contains("Win32_DeviceGuard", doc, StringComparison.Ordinal);
    }

    // ---- skrypty (harness z bezpiecznikami; helpery CIM i rejestru zamockowane) ----

    [Theory]
    [InlineData("@(0)", "Applied")] // Enabled = 0 i nic nie działa (po restarcie)
    [InlineData("@(2)", "NotApplied")]
    [InlineData("@(1, 2)", "NotApplied")] // tablica: sprawdzamy zawieranie 2, nie równość
    [InlineData("@(1, 3, 5)", "Applied")]
    public void Detect_ReportsRunningStateFromSecurityServicesRunning(string running, string expected)
    {
        var run = PowerShellHarness.Run(Get().Script!.Detect!, VbsMocks(running, NoLock), string.Empty);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(expected, run.LastLine);
    }

    [Fact]
    public void Detect_WithoutCimMock_HitsGuardAndNeverTouchesHostCim()
    {
        // Odczyt Enabled zamockowany (bez tego skrypt czytałby rejestr hosta); bez mocka CIM trafia w bezpiecznik harnessu.
        var run = PowerShellHarness.Run(Get().Script!.Detect!, $"function Get-DsoHvciEnabled {{ {EnabledZero} }}", string.Empty);
        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Bezpiecznik harnessu", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Detect_WhenCimFails_IsNotAnAnswer()
    {
        // Brak odpowiedzi CIM nie może wyglądać na Applied.
        var run = PowerShellHarness.Run(Get().Script!.Detect!, VbsMocks("throw 'CIM niedostępny'", NoLock), string.Empty);
        Assert.NotEqual(0, run.ExitCode);
        Assert.NotEqual("Applied", run.LastLine);
    }

    [Theory]
    [InlineData("$null")]
    [InlineData("@()")]
    [InlineData(", @()")] // pusta tablica z CIM (helper produkcyjny zwracał ją jako "nic nie działa")
    public void Detect_WhenCimReturnsNothing_IsNotAnAnswer(string running)
    {
        // Get-CimInstance bez wyjątku, ale bez instancji / bez SecurityServicesRunning: brak odpowiedzi to nie "memory integrity nie działa".
        var run = PowerShellHarness.Run(Get().Script!.Detect!, VbsMocks(running, NoLock), string.Empty);
        Assert.NotEqual(0, run.ExitCode);
        Assert.NotEqual("Applied", run.LastLine);
    }

    [Fact]
    public void Detect_WhenEnabledIsMissing_IsNotAppliedAndCimIsNeverAsked()
    {
        // E3-P2: bez wpisu Enabled = 0 nic nie zastosowano; stan usługi nie ma znaczenia, więc CIM nie jest wołany (mock rzuca).
        var run = PowerShellHarness.Run(Get().Script!.Detect!, VbsMocks("throw 'CIM wołany mimo braku Enabled'", NoLock, "$null"), string.Empty);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("NotApplied", run.LastLine);
    }

    [Theory]
    [InlineData("DWord", 1u)] // memory integrity włączone
    [InlineData("DWord", 2u)]
    [InlineData("String", 0u)] // zły typ
    public void Detect_WhenEnabledIsNotDwordZero_IsNotAppliedAndCimIsNeverAsked(string kind, uint value)
    {
        var run = PowerShellHarness.Run(Get().Script!.Detect!, VbsMocks("throw 'CIM wołany mimo Enabled różnego od 0'", NoLock, Locked(kind, value)), string.Empty);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("NotApplied", run.LastLine);
    }

    [Fact]
    public void Detect_WhenEnabledIsZeroAndMemoryIntegrityStillRuns_IsNotApplied()
    {
        var run = PowerShellHarness.Run(Get().Script!.Detect!, VbsMocks("@(2)", NoLock, EnabledZero), string.Empty);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("NotApplied", run.LastLine);
    }

    [Fact]
    public void Detect_WhenEnabledIsZeroAndMemoryIntegrityNotRunning_IsApplied()
    {
        var run = PowerShellHarness.Run(Get().Script!.Detect!, VbsMocks("@(0)", NoLock, EnabledZero), string.Empty);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("Applied", run.LastLine);
    }

    [Fact]
    public void Detect_WhenEnabledIsZeroAndCimGivesNoAnswer_Throws()
    {
        var run = PowerShellHarness.Run(Get().Script!.Detect!, VbsMocks("$null", NoLock, EnabledZero), string.Empty);
        Assert.NotEqual(0, run.ExitCode);
        Assert.NotEqual("Applied", run.LastLine);
    }

    [Fact]
    public void Capture_WhenLockedKeyAbsent_ReturnsUnlocked()
    {
        var run = PowerShellHarness.Run(Get().Script!.Capture!, VbsMocks("@(0)", NoLock), string.Empty);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("Unlocked", run.LastLine);
    }

    [Fact]
    public void Capture_WhenLockedIsZero_ReturnsUnlocked()
    {
        var run = PowerShellHarness.Run(Get().Script!.Capture!, VbsMocks("@(2)", Locked("DWord", 0)), string.Empty);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("Unlocked", run.LastLine);
    }

    [Fact]
    public void Capture_WhenUefiLocked_RefusesWithUefiMessage()
    {
        // Review Focus 3: Locked = 1 -> odmowa przed jakąkolwiek zmianą, z komunikatem o blokadzie UEFI.
        var run = PowerShellHarness.Run(Get().Script!.Capture!, VbsMocks("@(2)", Locked("DWord", 1)), string.Empty);
        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("UEFI", run.Error, StringComparison.Ordinal);
        Assert.Contains("Secure Boot", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("Unlocked", string.Join("\n", run.Output), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("DWord", 2u)]
    [InlineData("String", 0u)]
    public void Capture_WhenLockedValueIsUnexpected_RefusesInsteadOfGuessing(string kind, uint value)
    {
        var run = PowerShellHarness.Run(Get().Script!.Capture!, VbsMocks("@(2)", Locked(kind, value)), string.Empty);
        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Locked", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void VbsHvci_English_Message()
    {
        // Komunikat z wartościami ({0}: {1}) po angielsku z tabeli catalog-perf.json.
        ScriptRun run;
        using (L.Use(UiLanguage.English))
        {
            run = PowerShellHarness.Run(Get().Script!.Capture!, VbsMocks("@(2)", Locked("DWord", 2)), string.Empty);
        }

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains(
            @"The Locked value in Scenarios\HypervisorEnforcedCodeIntegrity has an unexpected type or value (DWord: 2) – it's unknown whether there's a UEFI lock; nothing was changed.",
            run.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public void VbsHvci_Polish_MessageUnchanged()
    {
        var run = PowerShellHarness.Run(Get().Script!.Capture!, VbsMocks("@(2)", Locked("DWord", 2)), string.Empty);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains(
            @"Wartość Locked w Scenarios\HypervisorEnforcedCodeIntegrity ma nieoczekiwany typ lub wartość (DWord: 2) – nie wiadomo, czy jest blokada UEFI; nic nie zmieniono.",
            run.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_IsSingleLineNoOp_RefusalLivesOnlyInCapture()
    {
        // Skrypt apply biegnie PO zapisie rejestru (OperationFactory), więc odmowa tam byłaby rollbackiem, nie odmową przed zmianą.
        var apply = Get().Script!.Apply!;
        Assert.DoesNotContain("Assert-DsoHvciNotLocked", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("Get-DsoHvciLocked", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("przed zmianą", apply, StringComparison.Ordinal);
        var run = PowerShellHarness.Run(apply, VbsMocks("@(2)", Locked("DWord", 1)), string.Empty);
        Assert.Equal(0, run.ExitCode);
    }

    [Fact]
    public void ApplyAndUndo_WhenNotLocked_AreHarmlessGuards()
    {
        var script = Get().Script!;
        Assert.Equal(0, PowerShellHarness.Run(script.Apply!, VbsMocks("@(2)", NoLock), string.Empty).ExitCode);
        Assert.Equal(0, PowerShellHarness.Run(script.Undo!, VbsMocks("@(2)", NoLock), "Unlocked").ExitCode);
    }

    [Fact]
    public void Undo_WithEmptySnapshot_FailsAndChangesNothing()
    {
        // Brak wpisu w journalu: nie wiadomo, czy memory integrity było włączone, więc skrypt undo odmawia (C-1).
        var run = PowerShellHarness.Run(Get().Script!.Undo!, VbsMocks("@(2)", NoLock), string.Empty);
        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("journal", run.Error, StringComparison.Ordinal);
        Assert.Contains("nic nie zmieniono", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Warnings_StateThatUndoWorksOnlyAfterApplyByThisTool()
    {
        var text = string.Join(" ", Get().Warnings);
        Assert.Contains("tylko po zmianie wykonanej w DSOptimizerze", text, StringComparison.Ordinal);
    }

    // ---- silnik na fake'ach (Review Focus 3, 4; reguła A1) ----

    private static void WireScripts(EngineFixture f, TweakDefinition def, Func<bool> memoryIntegrityRunning, bool uefiLocked = false)
    {
        var script = def.Script!;
        // Model skryptu detect (E3-P2): bez Enabled = DWord 0 -> NotApplied, bez pytania o usługę; z Enabled = 0 -> wg SecurityServicesRunning.
        f.Scripts.On(script.Detect!, _ => FakeScriptRunner.Result(
            0,
            !RegValue.FromDWord(0).Equals(f.Registry.Get(HvciKey, "Enabled")) || memoryIntegrityRunning() ? "NotApplied" : "Applied"));
        f.Scripts.On(script.Capture!, _ => uefiLocked ? FakeScriptRunner.Result(1) : FakeScriptRunner.Result(0, "Unlocked"));
        f.Scripts.On(script.Apply!, 0, "ok");
        // Model skryptu undo z katalogu: pusty snapshot (ścieżka bez journala) kończy się błędem, snapshot z journala przechodzi.
        f.Scripts.On(script.Undo!, request => string.IsNullOrEmpty(request.Snapshot) ? FakeScriptRunner.Result(1) : FakeScriptRunner.Result(0, "ok"));
    }

    [Fact]
    public void Engine_OnFreshSystemWhereMemoryIntegrityIsNotRunning_AppliesAndUndoRemovesCreatedKey()
    {
        var f = new EngineFixture();
        var def = Get();
        WireScripts(f, def, () => false);
        var engine = f.CreateEngine();

        Assert.Equal(DetectionState.NotApplied, engine.Detect(def)); // konfiguracja nieustawiona, nic nie zastosowano

        var applied = engine.Apply(def);
        Assert.Equal(TweakOutcome.Applied, applied.Outcome);
        Assert.True(applied.RequiresReboot);
        Assert.Equal(RegValue.FromDWord(0), f.Registry.Get(HvciKey, "Enabled"));

        var undone = engine.Undo(def);
        Assert.Equal(TweakOutcome.Undone, undone.Outcome);
        Assert.Equal(RegValue.Absent, f.Registry.Get(HvciKey, "Enabled"));
        Assert.False(f.Registry.HasKey(HvciKey)); // A1: klucz utworzony przez apply znika po undo
        Assert.Equal(DetectionState.NotApplied, engine.Detect(def)); // po undo znów niezastosowane, GUI nie ma czego "cofać"
    }

    [Fact]
    public void Engine_FullCycle_FreshApplyRestartUndo_EndsNotApplied()
    {
        // Świeży system -> apply przed restartem (HVCI działa) -> restart -> undo; stan po każdym kroku wg tabeli E3-P2.
        var f = new EngineFixture();
        var def = Get();
        var running = true; // HVCI działa na tym sprzęcie od początku
        WireScripts(f, def, () => running);
        var engine = f.CreateEngine();

        Assert.Equal(DetectionState.NotApplied, engine.Detect(def)); // działa, brak konfiguracji

        var applied = engine.Apply(def);
        Assert.Equal(TweakOutcome.PendingReboot, applied.Outcome);
        Assert.Equal(DetectionState.Partial, engine.Detect(def)); // konfiguracja 0, usługa jeszcze działa

        running = false; // restart
        Assert.Equal(DetectionState.Applied, engine.Detect(def));

        var undone = engine.Undo(def);
        Assert.Equal(TweakOutcome.Undone, undone.Outcome);
        Assert.Equal(DetectionState.NotApplied, engine.Detect(def)); // także zanim minie restart cofnięcia

        running = true; // po restarcie memory integrity wraca (jeśli było włączone)
        Assert.Equal(DetectionState.NotApplied, engine.Detect(def));
    }

    [Fact]
    public void Engine_WhenConfigIsOffButMemoryIntegrityStillRunning_IsPendingRebootThenAppliedAfterRestart()
    {
        // Review Focus 4: Enabled = 0 zapisane, SecurityServicesRunning dalej zawiera 2 -> PendingReboot (nie Applied, nie błąd).
        var f = new EngineFixture();
        var def = Get();
        var running = true;
        WireScripts(f, def, () => running);
        f.Registry.Set(HvciKey, "Enabled", RegValue.FromDWord(1));
        var engine = f.CreateEngine();

        var applied = engine.Apply(def);
        Assert.Equal(TweakOutcome.PendingReboot, applied.Outcome);
        Assert.True(applied.RequiresReboot);
        Assert.Equal(DetectionState.Partial, applied.FinalState);
        Assert.Equal(RegValue.FromDWord(0), f.Registry.Get(HvciKey, "Enabled"));
        Assert.Equal(JournalEntryStatus.PendingReboot, Assert.Single(f.Journal.Current.Entries).Status);

        Assert.Equal(DetectionState.Partial, engine.Detect(def)); // przed restartem nadal oczekuje

        running = false; // po restarcie memory integrity nie działa
        Assert.Equal(DetectionState.Applied, engine.Detect(def));
        Assert.Equal(JournalEntryStatus.Active, Assert.Single(f.Journal.Current.Entries).Status);

        // Undo wraca do Enabled = 1 ze snapshotu.
        Assert.Equal(TweakOutcome.Undone, engine.Undo(def).Outcome);
        Assert.Equal(RegValue.FromDWord(1), f.Registry.Get(HvciKey, "Enabled"));
    }

    [Theory]
    [InlineData(false, DetectionState.NotApplied)] // brak konfiguracji, nie działa: nic nie zastosowano
    [InlineData(true, DetectionState.NotApplied)] // brak konfiguracji, działa
    public void Engine_DetectWithoutConfiguration_IsNeverApplied(bool memoryIntegrityRunning, DetectionState expected)
    {
        var f = new EngineFixture();
        var def = Get();
        WireScripts(f, def, () => memoryIntegrityRunning);

        Assert.Equal(expected, f.CreateEngine().Detect(def));
    }

    [Fact]
    public void Engine_DetectWithConfigOffAndNotRunning_IsApplied()
    {
        var f = new EngineFixture();
        var def = Get();
        WireScripts(f, def, () => false);
        f.Registry.Set(HvciKey, "Enabled", RegValue.FromDWord(0));

        Assert.Equal(DetectionState.Applied, f.CreateEngine().Detect(def));
    }

    [Theory]
    [InlineData(1u)] // memory integrity włączone przez Windows albo właściciela
    [InlineData(0u)] // wyłączone przez właściciela w Windows Security
    public void Engine_UndoWithoutJournal_RefusesAndLeavesEnabledUntouched(uint enabled)
    {
        var f = new EngineFixture();
        var def = Get();
        WireScripts(f, def, () => enabled == 1);
        f.Registry.Set(HvciKey, "Enabled", RegValue.FromDWord(enabled));
        var writesBefore = f.Registry.WriteLog.Count;

        var result = f.CreateEngine().Undo(def);

        Assert.False(result.IsSuccess);
        Assert.NotEqual(TweakOutcome.UndoneFromDefaults, result.Outcome);
        Assert.Equal(RegValue.FromDWord(enabled), f.Registry.Get(HvciKey, "Enabled"));
        Assert.Equal(writesBefore, f.Registry.WriteLog.Count);
        Assert.Empty(f.Journal.Current.Entries);
    }

    [Fact]
    public void Engine_WhenUefiLocked_ApplyRefusesBeforeAnyChangeAndIsNotSuccess()
    {
        // Review Focus 3: capture rzuca przy Locked = 1 -> silnik przerywa przed zapisem rejestru i journala.
        var f = new EngineFixture();
        var def = Get();
        WireScripts(f, def, () => true, uefiLocked: true);

        var result = f.CreateEngine().Apply(def);

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.False(result.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
        Assert.Empty(f.Registry.WriteLog);
        Assert.False(f.Registry.HasKey(HvciKey));
        Assert.Empty(f.Journal.Current.Entries);
    }
}
