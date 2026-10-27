using System;
using DSOptimizer.Core.Journal;
using DSOptimizer.Core.Model;
using Xunit;

namespace DSOptimizer.Tests.Unit.Engine;

public class EngineDetectTests
{
    [Fact]
    public void Detect_ReflectsRegistryState()
    {
        var f = new EngineFixture();
        var engine = f.CreateEngine();
        Assert.Equal(DetectionState.NotApplied, engine.Detect(TestTweaks.TwoValues()));

        f.Registry.Set(TestTweaks.Path, "A", RegValue.FromDWord(1));
        Assert.Equal(DetectionState.Partial, engine.Detect(TestTweaks.TwoValues()));

        f.Registry.Set(TestTweaks.Path, "B", RegValue.FromDWord(1));
        Assert.Equal(DetectionState.Applied, engine.Detect(TestTweaks.TwoValues()));
    }

    [Fact]
    public void Detect_BuildOutsideRange_IsNotSupportedWithoutReads()
    {
        var f = new EngineFixture();
        f.SystemInfo.Build = 19045;
        var engine = f.CreateEngine();

        Assert.Equal(DetectionState.NotSupported, engine.Detect(TestTweaks.TwoValues() with { MinBuild = 22000 }));
        f.SystemInfo.Build = 27000;
        Assert.Equal(DetectionState.NotSupported, engine.Detect(TestTweaks.TwoValues() with { MaxBuild = 26999 }));
        Assert.Equal(0, f.Registry.ReadCount);
    }

    [Fact]
    public void Detect_AccessDenied_IsUnknown()
    {
        var f = new EngineFixture();
        f.Registry.DenyRead(TestTweaks.Path, "B");
        Assert.Equal(DetectionState.Unknown, f.CreateEngine().Detect(TestTweaks.TwoValues()));
    }

    [Fact]
    public void Detect_ScriptSaysNotSupported_IsNotSupported()
    {
        var f = new EngineFixture();
        f.Scripts.On("DETECT", 0, "NotSupported");
        Assert.Equal(DetectionState.NotSupported, f.CreateEngine().Detect(TestTweaks.Scripted()));
    }

    [Fact]
    public void Detect_AllServicesMissing_IsNotSupported()
        => Assert.Equal(DetectionState.NotSupported, new EngineFixture().CreateEngine().Detect(TestTweaks.DisableService()));

    [Fact]
    public void Detect_UnexpectedException_IsUnknownAndLogged()
    {
        var f = new EngineFixture();
        f.Scripts.On("DETECT", _ => throw new InvalidOperationException("boom"));
        Assert.Equal(DetectionState.Unknown, f.CreateEngine().Detect(TestTweaks.Scripted()));
        Assert.Contains(f.Log.Lines, line => line.StartsWith("ERROR", StringComparison.Ordinal) && line.Contains("boom"));
    }

    [Fact]
    public void Detect_OnAction_Throws()
        => Assert.Throws<ArgumentException>(() => new EngineFixture().CreateEngine().Detect(TestTweaks.Action()));

    [Fact]
    public void Detect_AppliedPendingRebootEntry_IsPromotedToActive()
    {
        var f = new EngineFixture();
        var document = f.Journal.Current;
        document.Entries.Add(new JournalEntry { TweakId = "test.two-values", Status = JournalEntryStatus.PendingReboot });
        f.Journal.Save(document);
        f.Registry.Set(TestTweaks.Path, "A", RegValue.FromDWord(1));
        f.Registry.Set(TestTweaks.Path, "B", RegValue.FromDWord(1));

        Assert.Equal(DetectionState.Applied, f.CreateEngine().Detect(TestTweaks.TwoValues()));
        var entry = Assert.Single(f.Journal.Current.Entries);
        Assert.Equal(JournalEntryStatus.Active, entry.Status);
        Assert.Equal("detect", Assert.Single(entry.Events).Action);
    }

    [Fact]
    public void Preview_ListsMutatingChangesWithRebootFlag()
    {
        var f = new EngineFixture();
        f.Registry.Set(TestTweaks.Path, "A", RegValue.FromDWord(0));
        var tweak = TestTweaks.TwoValues() with
        {
            RequiresReboot = true,
            Registry = new[]
            {
                TestTweaks.TwoValues().Registry[0],
                new RegistryEntry { Path = TestTweaks.Path, Name = "Check", Value = RegValue.FromDWord(1), DetectOnly = true },
            },
        };

        var change = Assert.Single(f.CreateEngine().Preview(tweak));
        Assert.Equal(TestTweaks.Path + @"\A", change.Description);
        Assert.Equal("0 (0x00000000) [DWord]", change.Current);
        Assert.Equal("1 (0x00000001) [DWord]", change.Target);
        Assert.True(change.RequiresReboot);
    }
}
