using System;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Repair;
using Xunit;

namespace DSOptimizer.Tests.Unit.Repair;

public class ServicingGuardTests
{
    private sealed class FakeActivity : IServicingActivity
    {
        public FakeActivity(string? name) => Name = name;

        public string? Name { get; }

        public int Calls { get; private set; }

        public string? ActiveOperation()
        {
            Calls++;
            return Name;
        }
    }

    [Fact]
    public void ActiveServicing_RefusedBeforeAnyStep()
    {
        var result = ServicingGuard.Check(new FakeActivity("dism.exe"));

        Assert.NotNull(result);
        Assert.Equal(TweakOutcome.Refused, result!.Outcome);
        Assert.Equal("Poprzednia operacja serwisowania jeszcze trwa – spróbuj za chwilę.", result.Message);
        Assert.Contains(result.Warnings, w => w.Contains("dism.exe"));
        Assert.False(result.RequiresReboot);
    }

    [Fact]
    public void NoActivity_Null()
    {
        var activity = new FakeActivity(null);

        Assert.Null(ServicingGuard.Check(activity));
        Assert.Equal(1, activity.Calls);
    }

    [Fact]
    public void NullActivity_Throws()
        => Assert.Throws<ArgumentNullException>(() => ServicingGuard.Check(null!));
}
