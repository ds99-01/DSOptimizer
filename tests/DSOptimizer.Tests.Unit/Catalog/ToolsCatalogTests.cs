using System;
using System.IO;
using System.Linq;
using System.Text;
using DSOptimizer.Catalog;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Model;
using DSOptimizer.Tests.Unit.Support;
using Xunit;

namespace DSOptimizer.Tests.Unit.Catalog;

/// <summary>
/// Kategoria tools, pozycja tools.restart-explorer (partia C5, Task 6, ruling R18): zatrzymuje Explorera zalogowanego użytkownika
/// i potwierdza, że winlogon sam go wznowił (nowy PID). Skrypt NIGDY nie uruchamia explorer.exe (błąd v3.2: podniesiony Explorer).
/// Skrypty idą przez harness wyłącznie z mockami helperów (Get-DsoAutoRestartShell, Get-DsoUserShellProcess, Stop-DsoShellProcess);
/// <c>Start-Sleep</c> jest mockiem bez czekania, więc pętla 20 x 0,5 s (limit 10 s) kończy się natychmiast. Bezpieczniki harnessu
/// (Stop-Process, Start-Process, explorer, taskkill) rzucają przy każdym niezamockowanym wywołaniu.
/// </summary>
public class ToolsCatalogTests
{
    private const string Id = "tools.restart-explorer";
    private const string UserSid = "S-1-5-21-0-0-0-1000"; // SID, który harness przekazuje skryptowi jako $UserSid
    private const string OtherSid = "S-1-5-21-0-0-0-1001";
    private const string ShellCommand = "C:\\Windows\\Explorer.EXE";
    private const string FactoryCommand = "C:\\Windows\\explorer.exe /factory,{5BD95610-9434-43C2-886C-57852CC8A120} -Embedding";

    private static TweakDefinition Get()
        => CatalogLoader.Load(BuiltInCatalog.Sources).Definitions.Single(d => d.Id == Id);

    private static string Apply() => Assert.IsType<string>(Get().Script!.Apply);

    private static string AllText(TweakDefinition def) => def.Description + " " + string.Join(" ", def.Warnings);

    // ---- katalog ----

    [Fact]
    public void Catalog_LoadsWithoutValidationErrors()
    {
        var catalog = CatalogLoader.Load(BuiltInCatalog.Sources);
        Assert.Empty(CatalogValidator.Validate(catalog.Definitions));
    }

    [Fact]
    public void Entry_IsSafeActionInToolsCategory_WithLegacyAliasAndLongerTimeoutThanWait()
    {
        var def = Get();
        Assert.Equal("tools", def.Category);
        Assert.Equal(TweakKind.Action, def.Kind);
        Assert.Equal(RiskLevel.Safe, def.Risk);
        Assert.Equal(TweakScope.Machine, def.Scope);
        Assert.False(def.RequiresReboot);
        Assert.Equal(new[] { "tool_restart_explorer" }, def.Aliases);
        Assert.Empty(def.Registry);
        Assert.Empty(def.Services);
        Assert.Null(def.Launch);
        Assert.True(def.Timeout > TimeSpan.FromSeconds(10), "timeoutSeconds musi być większy niż limit czekania na nowy PID (10 s)");
        var script = Assert.IsType<ScriptSet>(def.Script);
        Assert.NotNull(script.Apply);
        Assert.Null(script.Detect);
        Assert.Null(script.Undo);
        Assert.Null(script.Capture);
        var owners = CatalogLoader.Load(BuiltInCatalog.Sources).Definitions.Where(d => d.Aliases.Contains("tool_restart_explorer")).Select(d => d.Id);
        Assert.Equal(new[] { Id }, owners);
    }

    [Fact]
    public void Warnings_MentionClosedWindowsTaskbarGapAndUntouchedAutoRestartShell()
    {
        var def = Get();
        Assert.Contains(def.Warnings, w => w.Contains("okna", StringComparison.OrdinalIgnoreCase) && w.Contains("Eksploratora", StringComparison.Ordinal));
        Assert.Contains(def.Warnings, w => w.Contains("pasek zadań", StringComparison.OrdinalIgnoreCase) && w.Contains("pulpit", StringComparison.OrdinalIgnoreCase));
        // Spec opisów 2026-10-26: szczegół techniczny (odczyt AutoRestartShell bez zmiany) w szczegółach technicznych, nie w uwagach.
        Assert.Contains("AutoRestartShell", def.Description, StringComparison.Ordinal);
        Assert.Contains("i jej nie zmienia", def.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("Explorer zrestartowany!", AllText(def), StringComparison.Ordinal);
    }

    [Fact]
    public void Script_NeverStartsAnythingNorWritesRegistry_AndReadsOnlyThroughHelpers()
    {
        var apply = Apply();
        Assert.DoesNotContain("HKCU", apply, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Start-Process", apply, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("taskkill", apply, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SetValue", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("Set-ItemProperty", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("New-ItemProperty", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("Remove-ItemProperty", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("& explorer", apply, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("saps", apply, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, apply.Split(new[] { "Stop-Process" }, StringSplitOptions.None).Length - 1); // tylko w definicji helpera Stop-DsoShellProcess
    }

    [Fact]
    public void Docs_ExistWithManualTaskManagerFindingAndTechNetSource()
    {
        var text = File.ReadAllText(RepoRoot.Combine("docs", "tweaks", Id + ".md"), Encoding.UTF8).Replace("\r\n", "\n");
        Assert.Contains("## Werdykt 26H2\nPortować jako `kind=action`, `risk=safe`", text, StringComparison.Ordinal);
        Assert.Contains("AutoRestartShell", text, StringComparison.Ordinal);
        Assert.Contains("Zakończ zadanie", text, StringComparison.Ordinal);
        Assert.Contains("NIE dało powrotu", text, StringComparison.Ordinal);
        Assert.Contains("/factory", text, StringComparison.Ordinal);
        Assert.Contains("cc776814", text, StringComparison.Ordinal);
        Assert.Contains("Limited", text, StringComparison.Ordinal);
        Assert.Contains("Menedżer zadań", text, StringComparison.Ordinal);
    }

    // ---- skrypt w harnessie ----

    private static string Proc(int processId, string sid, int session, string commandLine)
        => $"[pscustomobject]@{{ ProcessId = {processId}; SessionId = {session}; OwnerSid = '{sid}'; CommandLine = '{commandLine.Replace("'", "''")}' }}";

    /// <summary>Mocki helperów: lista procesów zwracana przed zatrzymaniem (<c>before</c>) i po nim (<c>after</c>).</summary>
    private static string Mocks(string autoRestart, string before, string after, string? stopBody = null, string? pollBody = null)
        => "function Get-DsoAutoRestartShell { " + autoRestart + " }\r\n" +
           "$script:phase = 0; $script:polls = 0; $script:t = 0\r\n" +
           "$script:before = @(" + before + ")\r\n" +
           "$script:after = @(" + after + ")\r\n" +
           "function Get-DsoUserShellProcess { param($OwnerSid) if ($script:phase -eq 0) { $script:before } else { $script:polls++; " + (pollBody ?? "") + "; $script:after } }\r\n" +
           "function Stop-DsoShellProcess { param($ProcessId) " + (stopBody ?? "") + "; $script:phase = 1; Write-Output ('STOP ' + $ProcessId) }\r\n" +
           "function Start-Sleep { }\r\n" +
           // Zegar limitu: każde odpytanie = 0,5 s wirtualnego czasu, więc limit 10 s mija po 20 odpytaniach bez realnego czekania.
           "function Get-DsoElapsedSeconds { $script:t += 0.5; $script:t }";

    private static string Join(params string[] items) => string.Join(", ", items);

    private static ScriptRun Run(string mocks) => PowerShellHarness.Run(Apply(), mocks, string.Empty);

    private static bool Stopped(ScriptRun run) => run.Output.Any(l => l.StartsWith("STOP ", StringComparison.Ordinal));

    [Theory]
    [InlineData("$null", "brak wartości")]
    [InlineData("0", "AutoRestartShell = 0")]
    [InlineData("2", "AutoRestartShell = 2")]
    [InlineData("'1'", "typ String, oczekiwano DWORD")] // REG_SZ "1" nie jest DWORD
    [InlineData("[long]1", "typ Int64, oczekiwano DWORD")] // QWORD
    [InlineData(",[byte[]](1)", "typ Byte[], oczekiwano DWORD")] // BINARY
    public void AutoRestartShell_MissingOrNotOne_FailsWithoutStopping(string value, string expectedFragment)
    {
        var run = Run(Mocks(value, Proc(100, UserSid, 1, ShellCommand), Proc(200, UserSid, 1, ShellCommand)));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains(expectedFragment, run.Error, StringComparison.Ordinal);
        Assert.Contains("winlogon nie wznowi Eksploratora; nic nie zatrzymano", run.Error, StringComparison.Ordinal);
        Assert.False(Stopped(run));
        Assert.DoesNotContain("Bezpiecznik harnessu", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void NoShell_FailsWithoutStopping()
    {
        var run = Run(Mocks("1", string.Empty, Proc(200, UserSid, 1, ShellCommand)));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("brak Eksploratora użytkownika", run.Error, StringComparison.Ordinal);
        Assert.Contains("nic nie zatrzymano", run.Error, StringComparison.Ordinal);
        Assert.Contains("Menedżer zadań > Uruchom nowe zadanie > explorer.exe", run.Error, StringComparison.Ordinal);
        Assert.False(Stopped(run));
    }

    [Fact]
    public void EmptyUserSid_FailsWithoutStopping()
    {
        var run = Run("$UserSid = ''\r\n" + Mocks("1", Proc(100, UserSid, 1, ShellCommand), Proc(200, UserSid, 1, ShellCommand)));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("brak SID zalogowanego użytkownika – nic nie zatrzymano", run.Error, StringComparison.Ordinal);
        Assert.False(Stopped(run));
    }

    [Fact]
    public void ToolsRestartExplorer_English_Message()
    {
        using (L.Use(UiLanguage.English))
        {
            var run = Run(Mocks("1", Proc(100, UserSid, 1, ShellCommand), Proc(250, UserSid, 1, ShellCommand)));

            Assert.Equal(0, run.ExitCode);
            Assert.Contains("File Explorer restarted by winlogon: PID 100 → 250", run.Output);
        }
    }

    [Fact]
    public void ToolsRestartExplorer_NoShell_English_Message()
    {
        using (L.Use(UiLanguage.English))
        {
            var run = Run(Mocks("1", string.Empty, Proc(200, UserSid, 1, ShellCommand)));

            Assert.NotEqual(0, run.ExitCode);
            Assert.Contains(
                "no File Explorer of user " + UserSid + " (shell without /factory); nothing was stopped. Open Task Manager > Run new task > explorer.exe (by hand; DSOptimizer doesn't start anything).",
                run.Error,
                StringComparison.Ordinal);
            Assert.False(Stopped(run));
        }
    }

    [Fact]
    public void ReadErrorRightAfterStop_IsTreatedAsNotYet_AndNextPollSucceeds()
    {
        // Wyścig CIM: proces zniknął między Get-CimInstance a GetOwnerSid (CimException: Not found) w pierwszym odpytaniu po Stop.
        var run = Run(Mocks("1", Proc(100, UserSid, 1, ShellCommand), Proc(250, UserSid, 1, ShellCommand), pollBody: "if ($script:polls -eq 1) { throw 'Not found' }"));

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Explorer zrestartowany przez winlogon: PID 100 → 250", run.Output);
        Assert.Empty(run.Error);
    }

    [Fact]
    public void ReadErrorUntilLimit_FailsWithLastErrorAndTaskManagerInstruction()
    {
        var run = Run(Mocks("1", Proc(100, UserSid, 1, ShellCommand), Proc(250, UserSid, 1, ShellCommand), pollBody: "throw 'Not found'"));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Ostatni błąd odczytu: Not found", run.Error, StringComparison.Ordinal);
        Assert.Contains("Menedżer zadań > Uruchom nowe zadanie > explorer.exe", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void LimitIsMeasuredByClock_ShellReturningAfterFifteenPollsStillCounts_ButNotAfterTwentyFive()
    {
        var back = Proc(250, UserSid, 1, ShellCommand);
        var late = Run(Mocks("1", Proc(100, UserSid, 1, ShellCommand), back, pollBody: "if ($script:polls -lt 15) { $script:after = @() } else { $script:after = @(" + back + ") }"));
        Assert.Equal(0, late.ExitCode);

        var tooLate = Run(Mocks("1", Proc(100, UserSid, 1, ShellCommand), back, pollBody: "if ($script:polls -lt 25) { $script:after = @() } else { $script:after = @(" + back + ") }"));
        Assert.NotEqual(0, tooLate.ExitCode);
        Assert.Contains("w 10 s nie wrócił", tooLate.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyFactoryProcesses_AreTreatedAsNoShell_AndNotStopped()
    {
        var run = Run(Mocks("1", Join(Proc(101, UserSid, 1, FactoryCommand), Proc(102, UserSid, 1, FactoryCommand.ToUpperInvariant())), Proc(200, UserSid, 1, ShellCommand)));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("brak Eksploratora użytkownika", run.Error, StringComparison.Ordinal);
        Assert.False(Stopped(run));
    }

    [Fact]
    public void ShellOfOtherUser_IsNeverStopped()
    {
        var run = Run(Mocks("1", Proc(100, OtherSid, 1, ShellCommand), Proc(200, OtherSid, 1, ShellCommand)));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("brak Eksploratora użytkownika", run.Error, StringComparison.Ordinal);
        Assert.False(Stopped(run));
    }

    [Fact]
    public void ShellsOfUserInSeveralSessions_FailsWithoutStopping_BecauseSessionIsAmbiguous()
    {
        var run = Run(Mocks("1", Join(Proc(100, UserSid, 1, ShellCommand), Proc(150, UserSid, 3, ShellCommand)), Proc(200, UserSid, 1, ShellCommand)));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("kilku sesjach", run.Error, StringComparison.Ordinal);
        Assert.Contains("nic nie zatrzymano", run.Error, StringComparison.Ordinal);
        Assert.False(Stopped(run));
    }

    [Fact]
    public void Success_StopsOnlyUserShell_NotFactoryOrOtherUser_AndReportsPidTransition()
    {
        var before = Join(
            Proc(100, UserSid, 1, ShellCommand),
            Proc(101, UserSid, 1, FactoryCommand),
            Proc(300, OtherSid, 2, ShellCommand));
        var after = Join(
            Proc(101, UserSid, 1, FactoryCommand),
            Proc(300, OtherSid, 2, ShellCommand),
            Proc(250, UserSid, 1, ShellCommand));
        var run = Run(Mocks("1", before, after));

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(new[] { "STOP 100" }, run.Output.Where(l => l.StartsWith("STOP ", StringComparison.Ordinal)));
        Assert.Contains("Explorer zrestartowany przez winlogon: PID 100 → 250", run.Output);
        Assert.Empty(run.Error);
    }

    [Fact]
    public void Success_WithTwoShellsInOneSession_StopsBothAndWaitsForNewPid()
    {
        var run = Run(Mocks("1", Join(Proc(100, UserSid, 1, ShellCommand), Proc(110, UserSid, 1, ShellCommand)), Proc(250, UserSid, 1, ShellCommand)));

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(new[] { "STOP 100", "STOP 110" }, run.Output.Where(l => l.StartsWith("STOP ", StringComparison.Ordinal)));
        Assert.Contains("Explorer zrestartowany przez winlogon: PID 100,110 → 250", run.Output);
    }

    [Fact]
    public void NoReturnWithinLimit_FailsWithTaskManagerInstruction_AndStartsNothing()
    {
        var run = Run(Mocks("1", Proc(100, UserSid, 1, ShellCommand), string.Empty));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("STOP 100", run.Output);
        Assert.Contains("Menedżer zadań > Uruchom nowe zadanie > explorer.exe", run.Error, StringComparison.Ordinal);
        Assert.Contains("10 s", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("Bezpiecznik harnessu", run.Error, StringComparison.Ordinal); // żadnego Start-Process / explorer
        Assert.DoesNotContain("Explorer zrestartowany", string.Join("\n", run.Output), StringComparison.Ordinal);
    }

    [Fact]
    public void NewShellOnlyOfOtherUserOrFactoryOrOtherSessionOrSamePid_DoesNotCountAsReturn()
    {
        var after = Join(
            Proc(100, UserSid, 1, ShellCommand), // ten sam PID (stary), nie nowy
            Proc(260, OtherSid, 1, ShellCommand), // inny użytkownik
            Proc(261, UserSid, 1, FactoryCommand), // proces /factory
            Proc(262, UserSid, 5, ShellCommand)); // inna sesja niż zatrzymana powłoka
        var run = Run(Mocks("1", Proc(100, UserSid, 1, ShellCommand), after));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Menedżer zadań > Uruchom nowe zadanie > explorer.exe", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("Explorer zrestartowany", string.Join("\n", run.Output), StringComparison.Ordinal);
    }

    [Fact]
    public void StopFailure_FailsWithProcessMessage()
    {
        var run = Run(Mocks("1", Proc(100, UserSid, 1, ShellCommand), Proc(250, UserSid, 1, ShellCommand), stopBody: "throw 'odmowa dostępu'"));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("odmowa dostępu", run.Error, StringComparison.Ordinal);
        Assert.Contains("PID 100", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("Explorer zrestartowany", string.Join("\n", run.Output), StringComparison.Ordinal);
    }
}

/// <summary>
/// Kategoria tools, pozycja tools.winutil (Etap 5, Task 4; wcześniej partia C5, D5): handler C# <c>winutil</c> pobiera
/// <c>winutil.ps1</c> z ostatniego wydania GitHuba, sprawdza SHA256 i otwiera go w oknie PowerShell (<c>-File</c>).
/// Testy NIE uruchamiają niczego: sprawdzają wyłącznie dane katalogu (handler, ostrzeżenia, docs).
/// </summary>
public class ToolsWinUtilCatalogTests
{
    private const string Id = "tools.winutil";

    private static TweakDefinition Get()
        => CatalogLoader.Load(BuiltInCatalog.Sources).Definitions.Single(d => d.Id == Id);

    private static string AllText(TweakDefinition def) => def.Summary + " " + def.Benefit + " " + def.Description + " " + string.Join(" ", def.Warnings);

    [Fact]
    public void Entry_IsCautionActionInToolsCategory_WithLegacyAlias_AndWinUtilHandlerOnly()
    {
        var def = Get();
        Assert.Equal("tools", def.Category);
        Assert.Equal(TweakKind.Action, def.Kind);
        Assert.Equal(RiskLevel.Caution, def.Risk);
        Assert.Equal(TweakScope.Machine, def.Scope);
        Assert.False(def.RequiresReboot);
        Assert.Equal(new[] { "tool_winutil" }, def.Aliases);
        Assert.Empty(def.Registry);
        Assert.Empty(def.Services);
        Assert.Null(def.Script);
        Assert.Null(def.Launch);
        Assert.Equal(ActionHandlerNames.WinUtil, def.Handler);
    }

    [Fact]
    public void CatalogFiles_ContainNoIrmIexCommand()
    {
        // Spec 3.3 pkt 4: po wejściu nowej drogi znany wzorzec „irm … | iex” znika z katalogu (skanery łapią go statycznie).
        foreach (var file in Directory.GetFiles(RepoRoot.Combine("src", "DSOptimizer.Catalog", "catalog"), "*.json"))
        {
            var json = File.ReadAllText(file, Encoding.UTF8);
            Assert.DoesNotContain("irm ", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("| iex", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("christitus.com", json, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Warnings_ForeignCodeAsAdmin_ContentNotChecked_NetworkRequired_NoPresets()
    {
        var def = Get();
        Assert.Contains(def.Warnings, w => w.Contains("uruchamiasz cudzy kod z internetu jako admin", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(def.Warnings, w => w.Contains("DSOptimizer nie sprawdza treści", StringComparison.Ordinal));
        Assert.Contains(def.Warnings, w => w.Contains("nie śledzi i nie cofa", StringComparison.Ordinal));
        // Spec opisów 2026-10-26: SHA256, wymóg sieci i brak presetów w szczegółach technicznych (najwyżej 2 uwagi).
        Assert.Contains("SHA256 potwierdza tylko zgodność z wydaniem GitHuba", def.Description, StringComparison.Ordinal);
        Assert.Contains("sieci", def.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("nic się nie otwiera", def.Description, StringComparison.Ordinal);
        Assert.Contains("preset", def.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("deweloperskiej", def.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Description_NamesGitHubReleaseAndSha256_AndPromisesNoEffect()
    {
        var def = Get();
        Assert.Contains("ChrisTitusTech/winutil", def.Description, StringComparison.Ordinal);
        Assert.Contains("winutil.ps1", def.Description, StringComparison.Ordinal);
        Assert.Contains("SHA256", def.Description, StringComparison.Ordinal);
        Assert.Contains("-File", def.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("optymalizuje", AllText(def), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("przyspiesza", AllText(def), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("niczego nie pobiera", AllText(def), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Docs_DescribeGitHubSource_MeasurementAndVmCase()
    {
        var text = File.ReadAllText(RepoRoot.Combine("docs", "tweaks", Id + ".md"), Encoding.UTF8).Replace("\r\n", "\n");
        Assert.Contains("## Werdykt 26H2\nPortować jako `kind=action`, `risk=caution`", text, StringComparison.Ordinal);
        Assert.Contains("releases/latest", text, StringComparison.Ordinal);
        Assert.Contains("digest", text, StringComparison.Ordinal);
        Assert.Contains("RemoteSigned", text, StringComparison.Ordinal);
        Assert.Contains("2026-10-26-etap5-pomiar-winutil.md", text, StringComparison.Ordinal);
        Assert.Contains("tools-winutil-offline", text, StringComparison.Ordinal);
        Assert.Contains("uruchamiasz cudzy kod z internetu jako admin", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WinUtilCode_IsNotInRepoCatalog()
    {
        // Kod WinUtil nie trafia do repo: katalog ma tylko nazwę handlera, reguły źródła są w WinUtilSource.
        var json = File.ReadAllText(RepoRoot.Combine("src", "DSOptimizer.Catalog", "catalog", "tools.json"), Encoding.UTF8);
        Assert.DoesNotContain("windev", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("-EncodedCommand", json, StringComparison.OrdinalIgnoreCase);
    }
}
