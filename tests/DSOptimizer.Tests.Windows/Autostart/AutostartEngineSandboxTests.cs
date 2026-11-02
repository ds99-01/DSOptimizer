using System;
using System.Linq;
using System.Threading;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Autostart;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Model;
using DSOptimizer.Tests.Windows.Support;
using DSOptimizer.Windows;
using Xunit;

namespace DSOptimizer.Tests.Windows.Autostart;

/// <summary>
/// Silnik autostartu na prawdziwym rejestrze, wyłącznie w <c>HKCU\Software\DSOptimizer.Tests\&lt;guid&gt;</c>: miejsca autostartu z korzeniami
/// konta (<c>\U</c>), maszyny (<c>\M</c>) i klas konta (<c>\C</c>) w piaskownicy, journal w <c>%TEMP%\dso-tests\&lt;guid&gt;</c>.
/// </summary>
[Trait("Category", "Windows")]
public class AutostartEngineSandboxTests
{
    private const string Sid = "S-1-5-21-1-2-3-1001";
    private const string Name = "DsoSandboxItem";
    private const string Family = "DSOptimizer.Sandbox_0000000000000";
    private const string TaskId = "SandboxStartupTask";

    private static readonly DateTimeOffset Now = new(2026, 11, 2, 8, 37, 53, 977, TimeSpan.Zero);

    /// <summary>2026-11-02 08:37:53.977 UTC jako FILETIME (little-endian).</summary>
    private static readonly byte[] NowFiletime = { 0x90, 0x5A, 0x07, 0x74, 0xA5, 0x6A, 0xDD, 0x01 };

    private static readonly AutostartSource[] AllApproved =
    {
        AutostartSource.RunUser, AutostartSource.RunMachine, AutostartSource.Run32, AutostartSource.FolderUser, AutostartSource.FolderCommon,
    };

    private readonly WindowsRegistry _registry = new();

    [Fact]
    public void DisableThenEnable_OnSandbox_ValuesAsMeasured()
    {
        using var sandbox = new RegistrySandbox();
        using var temp = new TempDirectory();
        using (sandbox.OpenOrCreate())
        {
        }

        var locations = Locations(sandbox);
        var key = locations.StartupApprovedKey(AutostartSource.RunUser);
        Assert.Equal(ReadStatus.NotFound, _registry.KeyStatus(key));

        var disabled = Run(temp.Sub("journal-a"), locations, AutostartState.Disabled, ApprovedRow(locations));

        Assert.Equal(TweakOutcome.Applied, disabled.Outcome);
        Assert.Equal(RegValue.FromBinary(new byte[] { 0x03, 0, 0, 0 }.Concat(NowFiletime).ToArray()), _registry.Read(key, Name).Value);

        // Drugi journal bez wpisu tej pozycji – jak po wyłączeniu Menedżerem zadań: włączenie zapisuje wartość, nie cofa wpisu.
        var enabled = Run(temp.Sub("journal-b"), locations, AutostartState.Enabled, ApprovedRow(locations));

        Assert.Equal(TweakOutcome.Applied, enabled.Outcome);
        Assert.Equal(RegValue.FromBinary(new byte[] { 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }), _registry.Read(key, Name).Value);
        Assert.Equal(ReadStatus.Found, _registry.KeyStatus(key));
    }

    [Fact]
    public void DisableThenToggleBack_OnSandbox_ValueRemoved_KeysStay()
    {
        using var sandbox = new RegistrySandbox();
        using var temp = new TempDirectory();
        using (sandbox.OpenOrCreate())
        {
        }

        var locations = Locations(sandbox);
        var key = locations.StartupApprovedKey(AutostartSource.RunMachine);
        var journal = temp.Sub("journal");

        Assert.Equal(TweakOutcome.Applied, Run(journal, locations, AutostartState.Disabled, ApprovedRow(locations, AutostartSource.RunMachine)).Outcome);
        var back = Run(journal, locations, AutostartState.Enabled, ApprovedRow(locations, AutostartSource.RunMachine));

        Assert.Equal(TweakOutcome.Undone, back.Outcome);
        Assert.Equal(ReadStatus.NotFound, _registry.Read(key, Name).Status);
        Assert.Equal(ReadStatus.Found, _registry.KeyStatus(key));
    }

    [Fact]
    public void StartupTask_EnableFromZeroThenToggleBack_OnSandbox_TaskKeyDeleted()
    {
        using var sandbox = new RegistrySandbox();
        using var temp = new TempDirectory();
        var locations = Locations(sandbox);
        var taskKey = locations.StartupTaskKey(Family, TaskId);
        var familyKey = taskKey.Substring(0, taskKey.LastIndexOf('\\'));
        using (var root = sandbox.OpenOrCreate())
        using (root.CreateSubKey(familyKey.Substring(sandbox.HkcuPath.Length + 1)))
        {
        }

        var journal = temp.Sub("journal");

        var enabled = Run(journal, locations, AutostartState.Enabled, TaskRow(taskKey));

        Assert.Equal(TweakOutcome.Applied, enabled.Outcome);
        Assert.Equal(RegValue.FromDWord(2), _registry.Read(taskKey, "State").Value);
        Assert.Equal(RegValue.FromDWord(1), _registry.Read(taskKey, "UserEnabledStartupOnce").Value);

        var back = Run(journal, locations, AutostartState.Disabled, TaskRow(taskKey));

        Assert.Equal(TweakOutcome.Undone, back.Outcome);
        Assert.Equal(ReadStatus.NotFound, _registry.KeyStatus(taskKey));
        Assert.Equal(ReadStatus.Found, _registry.KeyStatus(familyKey));
    }

    private static AutostartLocations Locations(RegistrySandbox sandbox)
        => new(sandbox.HkcuPath + @"\U", sandbox.HkcuPath + @"\M", sandbox.HkcuPath + @"\C", @"C:\unused", @"C:\unused");

    private TweakResult Run(string journalFolder, AutostartLocations locations, AutostartState target, AutostartRow row)
    {
        var clock = new FixedClock { UtcNow = Now };
        var engine = new AutostartEngine(_registry, new JsonFileJournalStore(new SecureRoot(journalFolder), "TEST-PC", clock), new NullLog(), clock);
        var context = new AutostartContext(Sid, true, Sid, true, true, locations, AllApproved, true, true);
        return Assert.Single(engine.Set(new[] { row }, target, context, new AutostartRunOptions(), CancellationToken.None).Results);
    }

    private AutostartRow ApprovedRow(AutostartLocations locations, AutostartSource source = AutostartSource.RunUser)
        => Row(
            AutostartIds.For(source, Name),
            source,
            Name,
            AutostartSwitchReader.Read(_registry, AutostartSwitchKind.StartupApproved, locations.StartupApprovedKey(source), Name, enabledInManifest: false));

    private AutostartRow TaskRow(string taskKey)
        => Row(
            AutostartIds.For(AutostartSource.StartupTask, TaskId, Family),
            AutostartSource.StartupTask,
            TaskId,
            AutostartSwitchReader.Read(_registry, AutostartSwitchKind.StartupTask, taskKey, "State", enabledInManifest: false));

    private static AutostartRow Row(string id, AutostartSource source, string entryName, AutostartSwitch current)
        => new(
            new AutostartEntry
            {
                Id = id,
                Source = source,
                EntryName = entryName,
                Name = entryName,
                Location = "sandbox",
                FileStatus = AutostartFileStatus.Exists,
                Switch = current,
                PackageFamily = source == AutostartSource.StartupTask ? Family : null,
            },
            null,
            AutostartBlocker.None);
}
