using System;
using System.Linq;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Cli;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Model;
using DSOptimizer.Tests.Unit.Engine;
using Xunit;

namespace DSOptimizer.Tests.Unit.Cli;

public class CliRunnerTests
{
    private static CliCatalogResult Catalog(string source, params TweakDefinition[] tweaks)
        => new(new LoadedCatalog(tweaks, "sha256:abc"), source, Array.Empty<string>());

    private static CliOptions Options(CliCommand command, string id, RestorePointPolicy policy = RestorePointPolicy.Skip)
        => new() { Command = command, TweakId = id, OutPath = @"C:\o.json", RestorePointPolicy = policy };

    private static CliReport Run(EngineFixture f, CliOptions options, string source = CliCatalog.BuiltInSource, params string[] startup)
        => CliRunner.Run(options, Catalog(source, TestTweaks.TwoValues(), TestTweaks.DisableService(), TestTweaks.Action(), TestTweaks.UserMenuDelay()), f.CreateServices(), startup);

    [Fact]
    public void Run_Detect_ReportsStateMachineUserAndSuccess()
    {
        var report = Run(new EngineFixture(), Options(CliCommand.Detect, "test.two-values"));
        Assert.Equal(CliExitCode.Success, report.Result);
        Assert.Equal(0, report.ExitCode);
        Assert.Equal(DetectionState.NotApplied, report.State);
        Assert.Equal(26300, report.Machine!.Build);
        Assert.Equal("S-1-5-21-1000", report.User!.Sid);
        Assert.True(report.User.IsCertain);
        Assert.Equal("sha256:abc", report.Catalog!.Hash);
        Assert.Equal(CliCatalog.BuiltInSource, report.Catalog.Source);
    }

    [Fact]
    public void Run_DetectByAlias_ResolvesCanonicalId()
    {
        var tweak = TestTweaks.TwoValues() with { Aliases = new[] { "old_id" } };
        var report = CliRunner.Run(Options(CliCommand.Detect, "OLD_ID"), Catalog(CliCatalog.BuiltInSource, tweak), new EngineFixture().CreateServices(), Array.Empty<string>());
        Assert.Equal(CliExitCode.Success, report.Result);
        Assert.Equal("test.two-values", report.TweakId);
    }

    [Theory]
    [InlineData("tools.store-repair")]
    [InlineData("tool_msstore")]
    public void Run_ExecuteStoreRepairByOldId_ResolvesToRepairStore(string oldId)
    {
        // Etap 4b: tools.store-repair przeszło do kategorii Naprawy jako repair.store; stare id i alias v3.2 zostają aliasami. Skrypt podmieniony na marker (nic nie rusza hosta).
        var real = CatalogLoader.Load(DSOptimizer.Catalog.BuiltInCatalog.Sources).Definitions.Single(d => d.Id == "repair.store");
        var store = real with { Script = new ScriptSet { Apply = "RUN" } };
        var f = new EngineFixture();
        f.Scripts.On("RUN", 0);
        var report = CliRunner.Run(Options(CliCommand.Execute, oldId), Catalog(CliCatalog.BuiltInSource, store), f.CreateServices(), Array.Empty<string>());
        Assert.Equal("repair.store", report.TweakId);
        Assert.Equal(TweakOutcome.Executed, report.Outcome);
        Assert.Equal(0, report.ExitCode);
    }

    [Fact]
    public void Run_IdMatchBeatsAliasOfEarlierTweak()
    {
        var first = TestTweaks.TwoValues("test.first") with { Aliases = new[] { "test.second" } };
        var second = TestTweaks.TwoValues("test.second");
        var report = CliRunner.Run(Options(CliCommand.Detect, "test.second"), Catalog(CliCatalog.BuiltInSource, first, second), new EngineFixture().CreateServices(), Array.Empty<string>());
        Assert.Equal("test.second", report.TweakId);
    }

    [Fact]
    public void Run_UnknownId_CatalogError()
    {
        var report = Run(new EngineFixture(), Options(CliCommand.Detect, "nie.ma"));
        Assert.Equal(CliExitCode.CatalogError, report.Result);
        Assert.Equal(3, report.ExitCode);
        Assert.Equal("Nieznany tweak 'nie.ma' w katalogu builtin.", Assert.Single(report.Errors));
    }

    [Fact]
    public void Run_NullCatalog_Throws()
        => Assert.Throws<ArgumentException>(() => CliRunner.Run(Options(CliCommand.Detect, "a.b"),
            new CliCatalogResult(null, "x", new[] { "e" }), new EngineFixture().CreateServices(), Array.Empty<string>()));

    [Theory]
    [InlineData(CliCommand.Detect)]
    [InlineData(CliCommand.Undo)]
    public void Run_DetectOrUndoOnAction_UsageError(CliCommand command)
    {
        var report = Run(new EngineFixture(), Options(command, "test.action"));
        Assert.Equal(CliExitCode.UsageError, report.Result);
        Assert.Equal(2, report.ExitCode);
        Assert.Equal("test.action", report.TweakId);
        Assert.NotEmpty(report.Errors);
    }

    [Fact]
    public void Run_ExecuteAction_Executed()
    {
        var f = new EngineFixture();
        f.Scripts.On("RUN", 0);
        var report = Run(f, Options(CliCommand.Execute, "test.action"));
        Assert.Equal(TweakOutcome.Executed, report.Outcome);
        Assert.Equal(CliExitCode.Success, report.Result);
        Assert.Equal(0, report.ExitCode);
        Assert.Null(report.State);
        Assert.Null(report.RestorePoint);
    }

    [Fact]
    public void Run_ExecuteAction_ScriptStdoutLandsInReportMessage()
    {
        var f = new EngineFixture();
        f.Scripts.On("RUN", 0, "Zmieniono profil 'X': Public -> Private.");
        var report = Run(f, Options(CliCommand.Execute, "test.action"));
        Assert.Equal(TweakOutcome.Executed, report.Outcome);
        Assert.Equal("Zmieniono profil 'X': Public -> Private.", report.Message);
    }

    [Fact]
    public void Run_ExecuteFailingScript_FailedExit1()
    {
        var f = new EngineFixture();
        f.Scripts.On("RUN", 3);
        var report = Run(f, Options(CliCommand.Execute, "test.action"));
        Assert.Equal(TweakOutcome.Failed, report.Outcome);
        Assert.Equal(CliExitCode.OperationFailed, report.Result);
        Assert.Equal(1, report.ExitCode);
    }

    [Fact]
    public void Run_ExecuteTweak_UsageError()
    {
        var report = Run(new EngineFixture(), Options(CliCommand.Execute, "test.two-values"));
        Assert.Equal(CliExitCode.UsageError, report.Result);
        Assert.Equal("Polecenie execute dotyczy tylko akcji i paneli ('test.two-values' ma kind=tweak).", Assert.Single(report.Errors));
    }

    [Fact]
    public void Run_ApplyAction_UsageErrorSuggestsExecute()
    {
        var report = Run(new EngineFixture(), Options(CliCommand.Apply, "test.action"));
        Assert.Equal(CliExitCode.UsageError, report.Result);
        Assert.Equal("Polecenie apply dotyczy tylko kind=tweak ('test.action' ma kind=action) – użyj --execute.", Assert.Single(report.Errors));
    }

    [Fact]
    public void Run_ExecuteAction_NoRestorePointRequested()
    {
        var f = new EngineFixture();
        f.Scripts.On("RUN", 0);
        var report = Run(f, Options(CliCommand.Execute, "test.action", RestorePointPolicy.Required));
        Assert.Equal(TweakOutcome.Executed, report.Outcome);
        Assert.Null(report.RestorePoint);
        Assert.Empty(f.RestorePoints.Descriptions);
    }

    [Fact]
    public void Run_ApplySuccess_AppliedAndRestorePointNullWhenSkipped()
    {
        var report = Run(new EngineFixture(), Options(CliCommand.Apply, "test.two-values"));
        Assert.Equal(TweakOutcome.Applied, report.Outcome);
        Assert.Equal(DetectionState.Applied, report.FinalState);
        Assert.Null(report.RestorePoint);
        Assert.False(report.Aborted);
        Assert.Equal(0, report.ExitCode);
    }

    [Fact]
    public void Run_ApplyRequiredRestorePointFails_AbortedWithCode()
    {
        var f = new EngineFixture();
        f.RestorePoints.Next = new RestorePointResult(RestorePointStatus.Failed, "wyłączona", 1058u);
        var report = Run(f, Options(CliCommand.Apply, "test.two-values", RestorePointPolicy.Required));
        Assert.True(report.Aborted);
        Assert.Equal(CliExitCode.OperationFailed, report.Result);
        Assert.Equal(TweakOutcome.Skipped, report.Outcome);
        Assert.Equal(1058u, report.RestorePoint!.Code);
        Assert.Contains("Punkt przywracania nie powstał (Failed): wyłączona", report.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_ApplyRestorePointCreated_ReportsCreatedAndDescription()
    {
        var f = new EngineFixture();
        f.RestorePoints.Next = new RestorePointResult(RestorePointStatus.Created, null);
        var report = Run(f, Options(CliCommand.Apply, "test.two-values", RestorePointPolicy.IfAvailable));
        Assert.Equal(RestorePointStatus.Created, report.RestorePoint!.Status);
        Assert.Equal("DSOptimizer – przed test.two-values", Assert.Single(f.RestorePoints.Descriptions));
    }

    [Fact]
    public void Run_ApplyIfAvailableRestorePointFails_SucceedsWithBatchWarningBeforeResultWarning()
    {
        var f = new EngineFixture();
        f.User.IsCertain = false;
        f.RestorePoints.Next = new RestorePointResult(RestorePointStatus.Failed, "wyłączona", 1058u);
        var report = Run(f, Options(CliCommand.Apply, "test.menu-delay", RestorePointPolicy.IfAvailable));
        Assert.Equal(0, report.ExitCode);
        Assert.False(report.Aborted);
        Assert.Equal(RestorePointStatus.Failed, report.RestorePoint!.Status);
        Assert.Equal(2, report.Warnings.Count);
        Assert.StartsWith("Punkt przywracania nie powstał (Failed)", report.Warnings[0], StringComparison.Ordinal);
        Assert.StartsWith("Nie udało się pewnie ustalić", report.Warnings[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Run_ApplyRolledBack_OperationFailedWithMessage()
    {
        var f = new EngineFixture();
        f.Services.Add("SysMain", ServiceStartType.Automatic, running: true);
        f.Services.FailStop("SysMain");
        var report = Run(f, Options(CliCommand.Apply, "test.service"));
        Assert.Equal(TweakOutcome.FailedRolledBack, report.Outcome);
        Assert.Equal(1, report.ExitCode);
        Assert.False(string.IsNullOrEmpty(report.Message));
    }

    [Fact]
    public void Run_UndoAfterApply_Undone()
    {
        var f = new EngineFixture();
        Run(f, Options(CliCommand.Apply, "test.two-values"));
        var report = Run(f, Options(CliCommand.Undo, "test.two-values"));
        Assert.Equal(TweakOutcome.Undone, report.Outcome);
        Assert.Equal(0, report.ExitCode);
    }

    [Fact]
    public void Run_UndoIncomplete_IncompleteExitCode()
    {
        var f = new EngineFixture();
        Run(f, Options(CliCommand.Apply, "test.two-values"));
        f.Registry.FailWrite(TestTweaks.Path, "A");
        var report = Run(f, Options(CliCommand.Undo, "test.two-values"));
        Assert.Equal(TweakOutcome.UndoIncomplete, report.Outcome);
        Assert.Equal(CliExitCode.Incomplete, report.Result);
        Assert.NotEmpty(report.NotRestored);
    }

    [Fact]
    public void Run_Warnings_StartupFirstThenExternalCatalogThenJournal_Deduplicated()
    {
        var f = new EngineFixture();
        f.Journal.LoadWarning = "journal odłożony";
        var report = Run(f, Options(CliCommand.Apply, "test.two-values"), @"C:\dso\k.json", "start-1", "start-1");
        Assert.Equal("start-1", report.Warnings[0]);
        Assert.StartsWith("Użyto zewnętrznego katalogu: C:\\dso\\k.json (sha256:abc)", report.Warnings[1], StringComparison.Ordinal);
        Assert.Contains("journal odłożony", report.Warnings);
        Assert.Equal(report.Warnings.Count, report.Warnings.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Run_ExternalCatalogWarning_PresentEvenWhenDetectFailsOrUnknownId()
    {
        var unknown = Run(new EngineFixture(), Options(CliCommand.Detect, "nie.ma"), @"C:\dso\k.json");
        Assert.Contains(unknown.Warnings, w => w.StartsWith("Użyto zewnętrznego katalogu", StringComparison.Ordinal));
        var usage = Run(new EngineFixture(), Options(CliCommand.Detect, "test.action"), @"C:\dso\k.json");
        Assert.Contains(usage.Warnings, w => w.StartsWith("Użyto zewnętrznego katalogu", StringComparison.Ordinal));
        var builtIn = Run(new EngineFixture(), Options(CliCommand.Detect, "test.two-values"));
        Assert.Empty(builtIn.Warnings);
    }

    [Fact]
    public void Failure_SetsCodeErrorsAndOptions()
    {
        var at = DateTimeOffset.UtcNow;
        var report = CliRunner.Failure(CliExitCode.CatalogError, Options(CliCommand.Detect, "a.b"), new[] { "zły plik" }, new[] { "w" }, at, at);
        Assert.Equal(3, report.ExitCode);
        Assert.Equal(CliCommand.Detect, report.Command);
        Assert.Equal("zły plik", Assert.Single(report.Errors));
        Assert.Equal("w", Assert.Single(report.Warnings));
    }

    [Fact]
    public void Failure_NullOptions_LeavesCommandAndTweakEmpty()
    {
        var at = DateTimeOffset.UtcNow;
        var report = CliRunner.Failure(CliExitCode.UsageError, null, new[] { "x" }, Array.Empty<string>(), at, at);
        Assert.Null(report.Command);
        Assert.Null(report.TweakId);
        Assert.Equal(2, report.ExitCode);
    }
}
