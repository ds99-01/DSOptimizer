using System;
using System.IO;
using System.Linq;
using System.Text;
using DSOptimizer.Catalog;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Model;
using DSOptimizer.Tests.Unit.Support;
using Xunit;

namespace DSOptimizer.Tests.Unit.Catalog;

/// <summary>Pozycje kategorii disk (partia C1): wartości z docs/zrodla/2026-10-17-partia-c1-zrodla.md i z odczytu VM 26H2.</summary>
public class DiskCatalogTests
{
    private const string FileSystemKey = @"HKLM\SYSTEM\CurrentControlSet\Control\FileSystem";

    private static string Ps(bool value) => value ? "$true" : "$false";

    // Mocki PowerShell: stan hibernacji (SystemS4, HiberFilePresent, HibernateEnabled) i powercfg.exe. Skrypt produkcyjny
    // pomija własne funkcje czytające system, gdy te nazwy już istnieją (haczyk testowy).
    private static string HibernationMocks(bool s4, bool file, string? enabled, int powercfgExit = 0, bool powercfgDisablesFile = true, bool powercfgOnRestoresFile = true)
        => string.Join(
            "\r\n",
            $"$global:S4 = {Ps(s4)}",
            $"$global:File = {Ps(file)}",
            $"$global:H = {enabled ?? "$null"}",
            "function Get-DsoPowerCaps { [pscustomobject]@{ S4 = $global:S4; HiberFilePresent = $global:File } }",
            "function Get-DsoHibernateEnabled { $global:H }",
            "function powercfg.exe {",
            "  Write-Output ('powercfg ' + ($args -join ' '))",
            $"  if ({powercfgExit} -eq 0) {{ if ($args -contains 'off') {{ $global:H = 0; if ({Ps(powercfgDisablesFile)}) {{ $global:File = $false }} }} else {{ $global:H = 1; if ({Ps(powercfgOnRestoresFile)}) {{ $global:File = $true }} }} }}",
            $"  cmd /c exit {powercfgExit}",
            "}");

    private static TweakDefinition Get(string id)
        => CatalogLoader.Load(BuiltInCatalog.Sources).Definitions.Single(d => d.Id == id);

    [Fact]
    public void DiskEntries_ArePresentInDiskCategory()
    {
        var ids = CatalogLoader.Load(BuiltInCatalog.Sources).Definitions.Where(d => d.Category == "disk").Select(d => d.Id).OrderBy(i => i, StringComparer.Ordinal);
        Assert.Equal(new[] { "disk.hibernation", "disk.ntfs-8dot3", "disk.ntfs-last-access", "disk.reserved-storage" }, ids);
    }

    [Theory]
    [InlineData("io_hiber", "disk.hibernation")]
    [InlineData("io_ntfs", "disk.ntfs-8dot3")]
    [InlineData("sec_storage_reserve", "disk.reserved-storage")]
    public void LegacyAlias_PointsToExactlyOneEntry(string alias, string id)
    {
        var owners = CatalogLoader.Load(BuiltInCatalog.Sources).Definitions.Where(d => d.Aliases.Contains(alias)).Select(d => d.Id).ToList();
        Assert.Equal(new[] { id }, owners);
    }

    [Fact]
    public void NtfsLastAccess_IsMachineScriptThatNeedsRestartPerMs()
    {
        var def = Get("disk.ntfs-last-access");
        Assert.Equal(TweakScope.Machine, def.Scope);
        Assert.Equal(RiskLevel.Safe, def.Risk);
        Assert.True(def.RequiresReboot);
        Assert.Empty(def.Aliases);
        // Silnik porównuje w registry[] całe wartości, a tryb to dolne 2 bity: pozycja musi być skryptem z maską.
        Assert.Empty(def.Registry);
        var script = Assert.IsType<ScriptSet>(def.Script);
        Assert.All(new[] { script.Detect, script.Capture, script.Apply, script.Undo }, Assert.NotNull);
        Assert.Contains("-band 3", script.Detect, StringComparison.Ordinal);
        Assert.Contains("2147483649", script.Apply, StringComparison.Ordinal); // 0x80000001 jak `fsutil behavior set disablelastaccess 1`
    }

    [Fact]
    public void Ntfs8dot3_SetsGlobalPolicyToDisabledAndWarnsAboutLegacyInstallers()
    {
        var def = Get("disk.ntfs-8dot3");
        Assert.Equal(TweakScope.Machine, def.Scope);
        Assert.Equal(RiskLevel.Caution, def.Risk);
        Assert.False(def.RequiresReboot);
        var entry = Assert.Single(def.Registry);
        Assert.Equal(FileSystemKey, entry.Path);
        Assert.Equal("NtfsDisable8dot3NameCreation", entry.Name);
        Assert.Equal(RegValue.FromDWord(1), entry.Value);
        Assert.Equal(RegValue.FromDWord(2), entry.Original);
        var warnings = string.Join(" ", def.Warnings);
        Assert.Contains("instalator", warnings, StringComparison.OrdinalIgnoreCase);
        // Spec opisów 2026-10-26 (przegląd T3, R5): „tylko nowe nazwy” to uwaga techniczna – w szczegółach, nie w uwagach.
        Assert.Contains("nowych", def.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Null(def.Script);
    }

    [Fact]
    public void Hibernation_IsExperimentalScriptWithAllStepsAndLaptopWarning()
    {
        var def = Get("disk.hibernation");
        Assert.Equal(TweakScope.Machine, def.Scope);
        Assert.Equal(RiskLevel.Experimental, def.Risk);
        Assert.Empty(def.Registry);
        var script = Assert.IsType<ScriptSet>(def.Script);
        Assert.All(new[] { script.Detect, script.Capture, script.Apply, script.Undo }, Assert.NotNull);
        Assert.Contains("CallNtPowerInformation", script.Detect, StringComparison.Ordinal);
        Assert.DoesNotContain("powercfg", script.Detect!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/hibernate off", script.Apply, StringComparison.Ordinal);
        Assert.Contains("/hibernate on", script.Undo, StringComparison.Ordinal);
        var warnings = string.Join(" ", def.Warnings);
        Assert.Contains("laptop", warnings, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Szybkie uruchamianie", warnings, StringComparison.Ordinal);
    }

    [Fact]
    public void ReservedStorage_IsCautionScriptUsingCmdletsWithoutOnlineSwitch()
    {
        var def = Get("disk.reserved-storage");
        Assert.Equal(TweakScope.Machine, def.Scope);
        Assert.Equal(RiskLevel.Caution, def.Risk);
        Assert.False(def.RequiresReboot);
        Assert.Empty(def.Registry);
        var script = Assert.IsType<ScriptSet>(def.Script);
        Assert.All(new[] { script.Detect, script.Capture, script.Apply, script.Undo }, Assert.NotNull);
        Assert.Contains("(Get-WindowsReservedStorageState).ReservedStorageState", script.Detect, StringComparison.Ordinal);
        Assert.Contains("Set-WindowsReservedStorageState -State Disabled", script.Apply, StringComparison.Ordinal);
        Assert.DoesNotContain("-Online", script.Apply!, StringComparison.Ordinal);
        Assert.DoesNotContain("ReserveManager", string.Join("\n", script.Detect, script.Capture, script.Apply, script.Undo), StringComparison.Ordinal);
    }

    // ---- Hibernacja: macierz detekcji na mockach ----

    [Theory]
    [InlineData(false, false, null, "NotSupported")]
    [InlineData(true, false, null, "NotApplied")]
    [InlineData(true, false, "1", "NotApplied")]
    [InlineData(true, true, "1", "NotApplied")]
    [InlineData(true, false, "0", "Applied")]
    [InlineData(false, false, "0", "Applied")]
    [InlineData(true, true, "0", "NotApplied")]
    [InlineData(false, true, null, "NotApplied")]
    public void HibernationDetect_MapsCapabilitiesAndRegistryWithoutParsingText(bool s4, bool file, string? enabled, string expected)
    {
        var run = PowerShellHarness.Run(Get("disk.hibernation").Script!.Detect!, HibernationMocks(s4, file, enabled), string.Empty);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(expected, run.LastLine);
    }

    [Theory]
    [InlineData(true, true, "1", "Enabled")]
    [InlineData(true, true, null, "Enabled")]
    [InlineData(false, false, "0", "Disabled")]
    [InlineData(true, false, "0", "Disabled")]
    [InlineData(false, false, null, "Unsupported")]
    public void HibernationCapture_RecordsStateBeforeChange(bool s4, bool file, string? enabled, string expected)
    {
        var run = PowerShellHarness.Run(Get("disk.hibernation").Script!.Capture!, HibernationMocks(s4, file, enabled), string.Empty);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(expected, run.LastLine);
    }

    [Theory]
    [InlineData(true, false, null)]
    [InlineData(true, false, "1")]
    [InlineData(true, true, "0")]
    public void HibernationCapture_WithoutEvidenceOfEnabledHibernation_FailsBeforeAnyChange(bool s4, bool file, string? enabled)
    {
        // SystemS4 = TRUE bez pliku hibernacji i bez jawnego HibernateEnabled = 0 nie dowodzi, że hibernacja była włączona:
        // snapshot "Enabled" kazałby undo włączyć coś, czego nie było.
        var run = PowerShellHarness.Run(Get("disk.hibernation").Script!.Capture!, HibernationMocks(s4, file, enabled), string.Empty);
        Assert.Equal(1, run.ExitCode);
        Assert.Contains("Brak jednoznacznego dowodu", run.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1")]
    [InlineData(null)]
    public void HibernationUndo_WhenAlreadyEnabledWithEvidence_DoesNotCallPowercfg(string? enabled)
    {
        var run = PowerShellHarness.Run(Get("disk.hibernation").Script!.Undo!, HibernationMocks(true, true, enabled), "Enabled");
        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain(run.Output, line => line.StartsWith("powercfg", StringComparison.Ordinal));
    }

    [Fact]
    public void HibernationUndo_AfterHalfDoneApply_StillTurnsHibernationOn()
    {
        // Apply w połowie: HibernateEnabled = 0, ale plik hibernacji jeszcze jest. To nie jest "już włączona" - rollback musi wywołać /h on.
        var run = PowerShellHarness.Run(Get("disk.hibernation").Script!.Undo!, HibernationMocks(true, true, "0"), "Enabled");
        Assert.Equal(0, run.ExitCode);
        Assert.Contains("powercfg /hibernate on", run.Output);
    }

    [Fact]
    public void HibernationUndo_WhenPowercfgSucceedsButNoEvidence_Fails()
    {
        // /h on zwraca 0, ale po nim nadal nie ma pliku hibernacji (S4=false, brak pliku): to nie jest przywrócenie.
        var mocks = HibernationMocks(false, false, "0", powercfgOnRestoresFile: false);
        var run = PowerShellHarness.Run(Get("disk.hibernation").Script!.Undo!, mocks, "Enabled");
        Assert.Equal(1, run.ExitCode);
        Assert.Contains("brak dowodu", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ReservedStorageUndo_WhenStateAlreadyEqualsSnapshot_SucceedsWithoutCallingSet()
    {
        // Rollback po nieudanym apply ("in use"): stan się nie zmienił, więc undo nie może wołać Set (padłoby tym samym błędem).
        var run = PowerShellHarness.Run(
            Get("disk.reserved-storage").Script!.Undo!,
            ReservedMocks("Enabled", setFailure: "This operation is not supported when reserved storage is in use."),
            "Enabled");
        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain(run.Output, line => line.StartsWith("Set ", StringComparison.Ordinal));
    }

    [Fact]
    public void ReservedStorageUndo_SuccessMessage_HasNoStrayBackslash()
    {
        var run = PowerShellHarness.Run(Get("disk.reserved-storage").Script!.Undo!, ReservedMocks("Disabled"), "Enabled");
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("Rezerwa miejsca przywrócona: Enabled.", run.LastLine);
    }

    [Fact]
    public void HibernationApply_OnUnsupportedMachine_FailsWithoutCallingPowercfg()
    {
        var run = PowerShellHarness.Run(Get("disk.hibernation").Script!.Apply!, HibernationMocks(s4: false, file: false, enabled: null), string.Empty);
        Assert.Equal(1, run.ExitCode);
        Assert.DoesNotContain(run.Output, line => line.StartsWith("powercfg", StringComparison.Ordinal));
        Assert.Contains("nie obsługuje", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void HibernationApply_OnSupportedMachine_TurnsOffAndVerifiesState()
    {
        var run = PowerShellHarness.Run(Get("disk.hibernation").Script!.Apply!, HibernationMocks(s4: true, file: true, enabled: "1"), string.Empty);
        Assert.Equal(0, run.ExitCode);
        Assert.Contains("powercfg /hibernate off", run.Output);
    }

    [Fact]
    public void HibernationApply_WhenPowercfgFails_ReportsExitCode()
    {
        var run = PowerShellHarness.Run(Get("disk.hibernation").Script!.Apply!, HibernationMocks(true, true, "1", powercfgExit: 1), string.Empty);
        Assert.Equal(1, run.ExitCode);
        Assert.Contains("kodem 1", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void HibernationApply_WhenStateStillNotApplied_Fails()
    {
        // powercfg zwraca 0, ale plik hibernacji zostaje: detekcja nie mówi Applied, więc to nie jest sukces.
        var run = PowerShellHarness.Run(Get("disk.hibernation").Script!.Apply!, HibernationMocks(true, true, "1", powercfgDisablesFile: false), string.Empty);
        Assert.Equal(1, run.ExitCode);
        Assert.Contains("NotApplied", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void HibernationUndo_WhenSnapshotSaysEnabled_TurnsOn()
    {
        var run = PowerShellHarness.Run(Get("disk.hibernation").Script!.Undo!, HibernationMocks(true, false, "0"), "Enabled");
        Assert.Equal(0, run.ExitCode);
        Assert.Contains("powercfg /hibernate on", run.Output);
    }

    [Theory]
    [InlineData("Disabled")]
    [InlineData("Unsupported")]
    public void HibernationUndo_WhenSnapshotSaysOffOrUnsupported_DoesNotCallPowercfg(string snapshot)
    {
        var run = PowerShellHarness.Run(Get("disk.hibernation").Script!.Undo!, HibernationMocks(true, false, "0"), snapshot);
        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain(run.Output, line => line.StartsWith("powercfg", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("cokolwiek")]
    public void HibernationUndo_WithoutKnownSnapshot_Fails(string snapshot)
    {
        var run = PowerShellHarness.Run(Get("disk.hibernation").Script!.Undo!, HibernationMocks(true, false, "0"), snapshot);
        Assert.Equal(1, run.ExitCode);
        Assert.DoesNotContain(run.Output, line => line.StartsWith("powercfg", StringComparison.Ordinal));
    }

    [Fact]
    public void HibernationUndo_WhenPowercfgFails_ReportsError()
    {
        var run = PowerShellHarness.Run(Get("disk.hibernation").Script!.Undo!, HibernationMocks(true, false, "0", powercfgExit: 1), "Enabled");
        Assert.Equal(1, run.ExitCode);
        Assert.Contains("kodem 1", run.Error, StringComparison.Ordinal);
    }

    // ---- NTFS last access: detekcja po masce dolnych 2 bitów, rejestr tylko przez funkcje pomocnicze (mockowane) ----

    private const string LastAccessValueName = "NtfsDisableLastAccessUpdate";

    // Mock rejestru: Get/Set/Remove-DsoLastAccessValue na zmiennej globalnej. Skrypt produkcyjny pomija własne definicje,
    // gdy te nazwy już istnieją (haczyk testowy), więc test NIGDY nie dotyka prawdziwego rejestru hosta. Wartości jako
    // dziesiętne [uint32] (literał 0x80000001 w PowerShell 5.1 to ujemny Int32).
    private static string LastAccessMocks(long? value, string kind = "DWord", bool setChangesState = true, bool removeChangesState = true)
        => string.Join(
            "\r\n",
            value is null
                ? "$global:LA = $null"
                : $"$global:LA = [pscustomobject]@{{ Kind = '{kind}'; Value = {(kind == "DWord" ? $"[uint32]{value}" : $"'{value}'")} }}",
            "function Get-DsoLastAccessValue { $global:LA }",
            "function Set-DsoLastAccessValue([uint32]$Value) {",
            "  Write-Output ('SET ' + $Value)",
            $"  if ({Ps(setChangesState)}) {{ $global:LA = [pscustomobject]@{{ Kind = 'DWord'; Value = $Value }} }}",
            "}",
            "function Remove-DsoLastAccessValue {",
            "  Write-Output 'REMOVE'",
            $"  if ({Ps(removeChangesState)}) {{ $global:LA = $null }}",
            "}");

    private static ScriptSet LastAccessScript() => Get("disk.ntfs-last-access").Script!;

    private static bool IsRegistryWrite(string line) => line.StartsWith("SET", StringComparison.Ordinal) || line == "REMOVE";

    [Theory]
    [InlineData(2147483649L, "Applied")] // 0x80000001: wartość z `fsutil behavior set disablelastaccess 1`
    [InlineData(1L, "Applied")]
    [InlineData(2147483651L, "Applied")] // 0x80000003: tryb systemowy, aktualizacje wyłączone
    [InlineData(3L, "Applied")]
    [InlineData(2147483650L, "NotApplied")] // 0x80000002: domyślny 26H2
    [InlineData(2147483648L, "NotApplied")] // 0x80000000
    [InlineData(0L, "NotApplied")]
    [InlineData(2L, "NotApplied")]
    [InlineData(null, "NotApplied")]
    public void NtfsLastAccessDetect_UsesLowTwoBitsMask(long? value, string expected)
    {
        var run = PowerShellHarness.Run(LastAccessScript().Detect!, LastAccessMocks(value), string.Empty);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(expected, run.LastLine);
    }

    [Fact]
    public void NtfsLastAccessDetect_NonDWordValue_IsNotApplied()
    {
        var run = PowerShellHarness.Run(LastAccessScript().Detect!, LastAccessMocks(1, kind: "String"), string.Empty);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("NotApplied", run.LastLine);
    }

    [Theory]
    [InlineData(2147483650L, "DWord:2147483650")]
    [InlineData(2147483649L, "DWord:2147483649")]
    [InlineData(0L, "DWord:0")]
    [InlineData(null, "Absent")]
    public void NtfsLastAccessCapture_RecordsTypedValueOrAbsence(long? value, string expected)
    {
        var run = PowerShellHarness.Run(LastAccessScript().Capture!, LastAccessMocks(value), string.Empty);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(expected, run.LastLine);
    }

    [Fact]
    public void NtfsLastAccessCapture_NonDWordValue_FailsBeforeAnyChange()
    {
        var run = PowerShellHarness.Run(LastAccessScript().Capture!, LastAccessMocks(1, kind: "String"), string.Empty);
        Assert.Equal(1, run.ExitCode);
        Assert.Contains("String", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(run.Output, IsRegistryWrite);
    }

    [Theory]
    [InlineData(2147483650L)]
    [InlineData(null)]
    [InlineData(2147483648L)]
    public void NtfsLastAccessApply_WritesFsutilValueAndVerifiesState(long? before)
    {
        var run = PowerShellHarness.Run(LastAccessScript().Apply!, LastAccessMocks(before), string.Empty);
        Assert.Equal(0, run.ExitCode);
        Assert.Contains("SET 2147483649", run.Output);
    }

    [Theory]
    [InlineData(2147483649L)]
    [InlineData(2147483651L)]
    [InlineData(3L)]
    public void NtfsLastAccessApply_WhenAlreadyDisabled_WritesNothing(long before)
    {
        var run = PowerShellHarness.Run(LastAccessScript().Apply!, LastAccessMocks(before), string.Empty);
        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain(run.Output, IsRegistryWrite);
    }

    [Fact]
    public void NtfsLastAccessApply_WhenWriteHasNoEffect_Fails()
    {
        var run = PowerShellHarness.Run(LastAccessScript().Apply!, LastAccessMocks(2147483650L, setChangesState: false), string.Empty);
        Assert.Equal(1, run.ExitCode);
        Assert.Contains(LastAccessValueName, run.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("DWord:2147483650", "SET 2147483650")] // domyślny 0x80000002 wraca
    [InlineData("DWord:2147483648", "SET 2147483648")]
    [InlineData("DWord:2147483651", "SET 2147483651")] // tryb 3 ze snapshotu wraca dokładnie
    [InlineData("Absent", "REMOVE")] // wartości nie było: usuń
    public void NtfsLastAccessUndo_RestoresExactlyTheSnapshot(string snapshot, string expectedCall)
    {
        var run = PowerShellHarness.Run(LastAccessScript().Undo!, LastAccessMocks(2147483649L), snapshot);
        Assert.Equal(0, run.ExitCode);
        Assert.Contains(expectedCall, run.Output);
    }

    [Theory]
    [InlineData("DWord:2147483650", 2147483650L)]
    [InlineData("DWord:2147483649", 2147483649L)]
    [InlineData("Absent", null)]
    public void NtfsLastAccessUndo_WhenAlreadyEqualToSnapshot_ChangesNothing(string snapshot, long? current)
    {
        var run = PowerShellHarness.Run(LastAccessScript().Undo!, LastAccessMocks(current), snapshot);
        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain(run.Output, IsRegistryWrite);
    }

    [Theory]
    [InlineData("")]
    [InlineData("cokolwiek")]
    [InlineData("DWord:abc")]
    [InlineData("DWord:4294967296")]
    [InlineData("DWord:-1")]
    [InlineData("String:2")]
    public void NtfsLastAccessUndo_WithoutValidSnapshot_FailsWithoutChange(string snapshot)
    {
        var run = PowerShellHarness.Run(LastAccessScript().Undo!, LastAccessMocks(2147483649L), snapshot);
        Assert.Equal(1, run.ExitCode);
        Assert.DoesNotContain(run.Output, IsRegistryWrite);
    }

    [Fact]
    public void NtfsLastAccessUndo_WhenWriteHasNoEffect_Fails()
    {
        var run = PowerShellHarness.Run(LastAccessScript().Undo!, LastAccessMocks(2147483649L, setChangesState: false), "DWord:2147483650");
        Assert.Equal(1, run.ExitCode);
        Assert.Contains(LastAccessValueName, run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void NtfsLastAccessUndo_WhenRemoveHasNoEffect_Fails()
    {
        var run = PowerShellHarness.Run(LastAccessScript().Undo!, LastAccessMocks(2147483649L, removeChangesState: false), "Absent");
        Assert.Equal(1, run.ExitCode);
        Assert.Contains(LastAccessValueName, run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void NtfsLastAccess_RegistryIsTouchedOnlyThroughMockableHelpers()
    {
        // Bezpieczniki harnessu nie łapią .NET Registry, więc to jedyna ochrona prawdziwego rejestru hosta (ruling R1).
        // 1) Statycznie: poza blokiem definiującym funkcje pomocnicze (haczyk testowy) skrypt nie zawiera żadnego dostępu do rejestru.
        var script = LastAccessScript();
        foreach (var (step, body) in new[] { ("detect", script.Detect!), ("capture", script.Capture!), ("apply", script.Apply!), ("undo", script.Undo!) })
        {
            var lines = body.Replace("\r\n", "\n").Split('\n');
            Assert.StartsWith("if (-not (Get-Command Get-DsoLastAccessValue", lines[0], StringComparison.Ordinal);
            var end = Array.FindIndex(lines, l => l == "}");
            Assert.True(end > 0, $"{step}: brak końca bloku funkcji pomocniczych");
            var outside = string.Join("\n", lines.Skip(end + 1));
            foreach (var forbidden in new[] { "Registry", "OpenBaseKey", "OpenSubKey", "HKLM", "HKEY", "ItemProperty", "Remove-Item" })
            {
                Assert.DoesNotContain(forbidden, outside, StringComparison.OrdinalIgnoreCase);
            }

            Assert.Contains("OpenBaseKey", string.Join("\n", lines.Take(end + 1)), StringComparison.Ordinal);
        }

        // 2) Dynamicznie: każdy krok kończy się na mockach (wywołania SET/REMOVE widoczne w wyjściu).
        Assert.Contains("SET 2147483649", PowerShellHarness.Run(script.Apply!, LastAccessMocks(2147483650L), string.Empty).Output);
        Assert.Contains("SET 2147483650", PowerShellHarness.Run(script.Undo!, LastAccessMocks(2147483649L), "DWord:2147483650").Output);
        Assert.Contains("REMOVE", PowerShellHarness.Run(script.Undo!, LastAccessMocks(2147483649L), "Absent").Output);
        Assert.Equal("Applied", PowerShellHarness.Run(script.Detect!, LastAccessMocks(1), string.Empty).LastLine);
        Assert.Equal("Absent", PowerShellHarness.Run(script.Capture!, LastAccessMocks(null), string.Empty).LastLine);
    }

    // ---- Rezerwa miejsca: mocki cmdletów DISM ----

    private static string ReservedMocks(string state, string? setFailure = null, bool setChangesState = true)
        => string.Join(
            "\r\n",
            $"$global:R = '{state}'",
            "function Get-WindowsReservedStorageState { [pscustomobject]@{ ReservedStorageState = $global:R; LogLevel = 'WarningsInfo' } }",
            "function Set-WindowsReservedStorageState {",
            "  param([string]$State)",
            "  [Console]::Out.WriteLine('Set ' + $State)",
            setFailure is null ? string.Empty : $"  throw '{setFailure}'",
            setChangesState ? "  $global:R = $State" : string.Empty,
            "}");

    [Theory]
    [InlineData("Enabled", "NotApplied")]
    [InlineData("Disabled", "Applied")]
    public void ReservedStorageDetect_ReadsReservedStorageStateProperty(string state, string expected)
    {
        var run = PowerShellHarness.Run(Get("disk.reserved-storage").Script!.Detect!, ReservedMocks(state), string.Empty);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(expected, run.LastLine);
    }

    [Fact]
    public void ReservedStorageDetect_WithoutCmdlet_IsNotSupported()
    {
        // Brak Get-WindowsReservedStorageState (SKU/obraz bez modułu DISM): NotSupported, a nie błąd. Get-Command przesłonięte,
        // żeby wynik nie zależał od cmdletu hosta; sam cmdlet rzuca, gdyby skrypt mimo to go zawołał.
        var mocks = string.Join(
            "\r\n",
            "function Get-Command { $null }",
            "function Get-WindowsReservedStorageState { throw 'Cmdlet nie powinien być wołany bez wykrycia.' }");
        var run = PowerShellHarness.Run(Get("disk.reserved-storage").Script!.Detect!, mocks, string.Empty);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("NotSupported", run.LastLine);
    }

    [Fact]
    public void ReservedStorageDetect_UnknownValue_IsNotAFalseAnswer()
    {
        var run = PowerShellHarness.Run(Get("disk.reserved-storage").Script!.Detect!, ReservedMocks("Cos"), string.Empty);
        Assert.NotEqual(0, run.ExitCode);
        Assert.NotEqual("Applied", run.LastLine);
        Assert.NotEqual("NotApplied", run.LastLine);
    }

    [Theory]
    [InlineData("Enabled")]
    [InlineData("Disabled")]
    public void ReservedStorageCapture_RecordsReservedStorageStateNotLogLevel(string state)
    {
        var run = PowerShellHarness.Run(Get("disk.reserved-storage").Script!.Capture!, ReservedMocks(state), string.Empty);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(state, run.LastLine);
    }

    [Fact]
    public void ReservedStorageApply_Succeeds_WhenStateBecomesDisabled()
    {
        var run = PowerShellHarness.Run(Get("disk.reserved-storage").Script!.Apply!, ReservedMocks("Enabled"), string.Empty);
        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Set Disabled", run.Output);
    }

    [Fact]
    public void ReservedStorageApply_WhenInUse_FailsWithMessage()
    {
        const string Message = "This operation is not supported when reserved storage is in use.";
        var run = PowerShellHarness.Run(Get("disk.reserved-storage").Script!.Apply!, ReservedMocks("Enabled", setFailure: Message), string.Empty);
        Assert.Equal(1, run.ExitCode);
        Assert.Contains(Message, run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ReservedStorageApply_WhenCmdletSilentlyDoesNothing_Fails()
    {
        var run = PowerShellHarness.Run(Get("disk.reserved-storage").Script!.Apply!, ReservedMocks("Enabled", setChangesState: false), string.Empty);
        Assert.Equal(1, run.ExitCode);
        Assert.Contains("Enabled", run.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Enabled")]
    [InlineData("Disabled")]
    public void ReservedStorageUndo_RestoresStateFromSnapshot(string snapshot)
    {
        var start = snapshot == "Enabled" ? "Disabled" : "Enabled";
        var run = PowerShellHarness.Run(Get("disk.reserved-storage").Script!.Undo!, ReservedMocks(start), snapshot);
        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Set " + snapshot, run.Output);
    }

    [Theory]
    [InlineData("")]
    [InlineData("WarningsInfo")]
    public void ReservedStorageUndo_WithoutValidSnapshot_FailsWithoutChange(string snapshot)
    {
        var run = PowerShellHarness.Run(Get("disk.reserved-storage").Script!.Undo!, ReservedMocks("Disabled"), snapshot);
        Assert.Equal(1, run.ExitCode);
        Assert.DoesNotContain(run.Output, line => line.StartsWith("Set ", StringComparison.Ordinal));
    }

    [Fact]
    public void ReservedStorageUndo_WhenInUse_FailsWithMessage()
    {
        const string Message = "This operation is not supported when reserved storage is in use.";
        var run = PowerShellHarness.Run(Get("disk.reserved-storage").Script!.Undo!, ReservedMocks("Disabled", setFailure: Message), "Enabled");
        Assert.Equal(1, run.ExitCode);
        Assert.Contains(Message, run.Error, StringComparison.Ordinal);
    }

    // ---- Skrypty i dokumenty ----

    [Fact]
    public void DiskScripts_DoNotUseHkcuDrive()
    {
        foreach (var id in new[] { "disk.hibernation", "disk.reserved-storage", "disk.ntfs-last-access" })
        {
            var script = Get(id).Script!;
            Assert.DoesNotContain("HKCU:", string.Join("\n", script.Detect, script.Capture, script.Apply, script.Undo), StringComparison.OrdinalIgnoreCase);
        }
    }
}
