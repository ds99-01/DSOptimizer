using System;
using System.IO;
using System.Security.Principal;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Journal;
using DSOptimizer.Core.Model;
using DSOptimizer.Tests.Windows.Support;
using DSOptimizer.Windows;
using Xunit;

namespace DSOptimizer.Tests.Windows;

[Trait("Category", "Windows")]
public class EndToEndTests
{
    [Fact]
    public void Engine_OnRealRegistry_AppliesAndUndoesBackToBaseline()
    {
        using var sandbox = new RegistrySandbox();
        using var temp = new TempDirectory();
        var registry = new WindowsRegistry();
        var sid = WindowsIdentity.GetCurrent().User!.Value;
        registry.Write(sandbox.HkuPath(sid), "A", RegValue.FromDWord(5));

        var tweak = new TweakDefinition
        {
            Id = "test.e2e",
            Category = "test",
            Kind = TweakKind.Tweak,
            Title = "Test końcowy",
            Summary = "Test końcowy na prawdziwym rejestrze.",
            Benefit = "Zysk testowy.",
            Risk = RiskLevel.Safe,
            Scope = TweakScope.User,
            Registry = new[]
            {
                new RegistryEntry { Path = sandbox.HkcuPath, Name = "A", Value = RegValue.FromDWord(1), Original = RegValue.FromDWord(0) },
                new RegistryEntry { Path = sandbox.HkcuPath, Name = "B", Value = RegValue.FromString("ż"), Original = RegValue.Absent },
            },
        };
        Assert.Empty(CatalogValidator.Validate(new[] { tweak }));

        var log = new NullLog();
        var clock = new SystemClock();
        var journal = new JsonFileJournalStore(temp.AppRoot, Environment.MachineName, clock);
        var engine = new TweakEngine(
            new EngineServices
            {
                Registry = registry,
                Services = new WindowsServiceManager(registry),
                Scripts = new PowerShellScriptRunner(temp.AppRoot, log),
                Launcher = new ShellProcessLauncher(),
                Journal = journal,
                RestorePoints = new NoRestorePoints(),
                SystemInfo = new WindowsSystemInfo(registry),
                User = new CurrentUserContext(sid),
                Log = log,
                Clock = clock,
            },
            "sha256:e2e");

        Assert.Equal(DetectionState.NotApplied, engine.Detect(tweak));
        Assert.Equal(TweakOutcome.Applied, engine.Apply(tweak).Outcome);
        Assert.Equal(DetectionState.Applied, engine.Detect(tweak));
        Assert.Equal(TweakOutcome.Undone, engine.Undo(tweak).Outcome);

        Assert.Equal(RegValue.FromDWord(5), registry.Read(sandbox.HkcuPath, "A").Value);
        Assert.Equal(ReadStatus.NotFound, registry.Read(sandbox.HkcuPath, "B").Status);
        Assert.Equal(JournalEntryStatus.Undone, Assert.Single(journal.Load().Document.Entries).Status);
        Assert.True(File.Exists(journal.FilePath));
    }

    private sealed class NoRestorePoints : IRestorePointService
    {
        public RestorePointResult Create(string description) => new(RestorePointStatus.Failed, "wyłączone w teście");
    }

    private sealed class CurrentUserContext : IUserContext
    {
        public CurrentUserContext(string sid) => Sid = sid;

        public string Sid { get; }

        public bool IsCertain => true;
    }
}
