using System;
using System.Security.Principal;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Journal;
using DSOptimizer.Core.Model;
using DSOptimizer.Tests.Windows.Support;
using DSOptimizer.Windows;
using Microsoft.Win32;
using Xunit;

namespace DSOptimizer.Tests.Windows;

[Trait("Category", "Windows")]
public class EndToEndCreatedKeyTests
{
    [Fact]
    public void Undo_RemovesKeysCreatedByApply_AndKeepsSandbox()
    {
        using var sandbox = new RegistrySandbox();
        using var temp = new TempDirectory();
        var registry = new WindowsRegistry();
        var sid = WindowsIdentity.GetCurrent().User!.Value;
        using (sandbox.OpenOrCreate())
        {
        }

        var entryPath = sandbox.HkcuPath + @"\Nowy\Klucz";
        var tweak = new TweakDefinition
        {
            Id = "test.e2e-created-key",
            Category = "test",
            Kind = TweakKind.Tweak,
            Title = "Test sprzątania kluczy",
            Risk = RiskLevel.Safe,
            Scope = TweakScope.User,
            Registry = new[]
            {
                new RegistryEntry { Path = entryPath, Name = "A", Value = RegValue.FromDWord(1), Original = RegValue.Absent },
            },
        };

        var engine = CreateEngine(registry, temp, sid);

        Assert.Equal(TweakOutcome.Applied, engine.Apply(tweak).Outcome);
        Assert.Equal(ReadStatus.Found, registry.KeyStatus(entryPath));

        var undo = engine.Undo(tweak);

        Assert.Equal(TweakOutcome.Undone, undo.Outcome);
        Assert.Empty(undo.Warnings);
        Assert.Equal(ReadStatus.NotFound, registry.KeyStatus(sandbox.HkcuPath + @"\Nowy"));
        Assert.Equal(ReadStatus.Found, registry.KeyStatus(sandbox.HkcuPath));
        using var sandboxKey = Registry.CurrentUser.OpenSubKey(sandbox.HkcuPath.Substring(@"HKCU\".Length));
        Assert.NotNull(sandboxKey);
    }

    private static TweakEngine CreateEngine(WindowsRegistry registry, TempDirectory temp, string sid)
    {
        var log = new NullLog();
        var clock = new SystemClock();
        return new TweakEngine(
            new EngineServices
            {
                Registry = registry,
                Services = new WindowsServiceManager(registry),
                Scripts = new PowerShellScriptRunner(temp.AppRoot, log),
                Launcher = new ShellProcessLauncher(),
                Journal = new JsonFileJournalStore(temp.AppRoot, Environment.MachineName, clock),
                RestorePoints = new NoRestorePoints(),
                SystemInfo = new WindowsSystemInfo(registry),
                User = new CurrentUserContext(sid),
                Log = log,
                Clock = clock,
            },
            "sha256:e2e");
    }

    [Fact]
    public void Undo_RemovesNestedMenuKeysUnderStarSegment_WithDefaultValueName_AndKeepsForeignRunas()
    {
        // Kształt wpisów sec.take-ownership-menu w piaskownicy HKCU: segment `*` w ścieżce, pusta nazwa (wartość domyślna), klucze zagnieżdżone.
        using var sandbox = new RegistrySandbox();
        using var temp = new TempDirectory();
        var registry = new WindowsRegistry();
        var sid = WindowsIdentity.GetCurrent().User!.Value;
        var shell = sandbox.HkcuPath + @"\*\shell";
        var root = shell + @"\DSOptimizer.TakeOwnership";
        var runas = root + @"\ExtendedSubCommandsKey\Shell\runas";
        var foreign = shell + @"\runas";
        registry.Write(foreign + @"\command", string.Empty, RegValue.FromString("obce.exe"));

        var tweak = new TweakDefinition
        {
            Id = "test.e2e-menu-shape",
            Category = "test",
            Kind = TweakKind.Tweak,
            Title = "Test kształtu menu",
            Risk = RiskLevel.Safe,
            Scope = TweakScope.User,
            Registry = new[]
            {
                new RegistryEntry { Path = root, Name = "MUIVerb", Value = RegValue.FromString("Przejmij na własność"), Original = RegValue.Absent },
                new RegistryEntry { Path = root, Name = "Extended", Value = RegValue.FromString(string.Empty), Original = RegValue.Absent },
                new RegistryEntry { Path = runas, Name = "HasLUAShield", Value = RegValue.FromString(string.Empty), Original = RegValue.Absent },
                new RegistryEntry { Path = runas + @"\command", Name = string.Empty, Value = RegValue.FromString("cmd.exe /c echo \"%1\""), Original = RegValue.Absent },
            },
        };
        var engine = CreateEngine(registry, temp, sid);

        Assert.Equal(TweakOutcome.Applied, engine.Apply(tweak).Outcome);
        Assert.Equal(DetectionState.Applied, engine.Detect(tweak));
        Assert.Equal(RegValue.FromString(string.Empty), registry.Read(root, "Extended").Value);
        Assert.Equal(RegValue.FromString("cmd.exe /c echo \"%1\""), registry.Read(runas + @"\command", string.Empty).Value);

        var undo = engine.Undo(tweak);

        Assert.Equal(TweakOutcome.Undone, undo.Outcome);
        Assert.Empty(undo.Warnings);
        Assert.Equal(ReadStatus.NotFound, registry.KeyStatus(root));
        Assert.Equal(ReadStatus.Found, registry.KeyStatus(shell));
        Assert.Equal(RegValue.FromString("obce.exe"), registry.Read(foreign + @"\command", string.Empty).Value);
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
