using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Model;
using Xunit;

namespace DSOptimizer.Tests.Unit.Engine;

public class EditionSupportTests
{
    private static readonly EditionFamily[] ProAndBusiness = { EditionFamily.Pro, EditionFamily.Business };

    private static TweakDefinition Tweak(EditionFamily[]? editions) => TestTweaks.TwoValues() with { Editions = editions };

    [Fact]
    public void Detect_EditionOutsideList_IsNotSupportedWithoutReads()
    {
        var f = new EngineFixture();
        f.SystemInfo.Edition = "Core";

        Assert.Equal(DetectionState.NotSupported, f.CreateEngine().Detect(Tweak(ProAndBusiness)));
        Assert.Equal(0, f.Registry.ReadCount);
    }

    [Fact]
    public void Apply_EditionOutsideList_IsNotSupportedAndChangesNothing()
    {
        var f = new EngineFixture();
        f.SystemInfo.Edition = "Core";

        var result = f.CreateEngine().Apply(Tweak(ProAndBusiness));

        Assert.Equal(TweakOutcome.NotSupported, result.Outcome);
        Assert.Empty(f.Registry.WriteLog);
    }

    [Theory]
    [InlineData("Professional")]
    [InlineData("unknown")]
    [InlineData("ServerStandard")]
    public void Detect_EditionInListOrUnknown_RunsNormalDetection(string edition)
    {
        var f = new EngineFixture();
        f.SystemInfo.Edition = edition;

        Assert.Equal(DetectionState.NotApplied, f.CreateEngine().Detect(Tweak(ProAndBusiness)));
    }

    [Fact]
    public void Detect_EditionsNull_RunsNormalDetection()
    {
        var f = new EngineFixture();
        f.SystemInfo.Edition = "Core";

        Assert.Equal(DetectionState.NotApplied, f.CreateEngine().Detect(Tweak(null)));
    }

    [Fact]
    public void Detect_BuildNotMet_EditionOk_IsStillNotSupported()
    {
        var f = new EngineFixture();
        f.SystemInfo.Build = 19045;
        f.SystemInfo.Edition = "Professional";

        Assert.Equal(DetectionState.NotSupported, f.CreateEngine().Detect(Tweak(ProAndBusiness) with { MinBuild = 22000 }));
    }
}
