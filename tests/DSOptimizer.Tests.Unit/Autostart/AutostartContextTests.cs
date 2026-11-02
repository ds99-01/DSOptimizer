using System;
using DSOptimizer.Core.Autostart;
using DSOptimizer.Tests.Unit.Fakes;
using Xunit;

namespace DSOptimizer.Tests.Unit.Autostart;

public class AutostartContextTests
{
    private const string Sid = "S-1-5-21-1-2-3-1001";

    [Fact]
    public void Create_BothHivesLoaded()
    {
        var registry = new FakeRegistry();
        registry.CreateKey(@"HKU\" + Sid);
        registry.CreateKey(@"HKU\" + Sid + "_Classes");

        var context = AutostartContext.Create(new FakeUserContext { Sid = Sid, IsCertain = true }, "S-1-5-21-1-2-3-500", registry);

        Assert.Equal(Sid, context.UserSid);
        Assert.True(context.UserCertain);
        Assert.Equal("S-1-5-21-1-2-3-500", context.ProcessSid);
        Assert.True(context.UserHiveLoaded);
        Assert.True(context.ClassesHiveLoaded);
        Assert.Equal(AutostartLocations.ForUser(Sid), context.Locations);
        Assert.Equal(AutostartEvidence.WriteFromZeroConfirmed, context.WriteFromZeroConfirmed);
    }

    [Fact]
    public void Create_HivesMissingOrDenied_NotLoaded()
    {
        var registry = new FakeRegistry();
        registry.CreateKey(@"HKU\" + Sid + "_Classes");
        registry.DenyKey(@"HKU\" + Sid + "_Classes");

        var context = AutostartContext.Create(new FakeUserContext { Sid = Sid, IsCertain = false }, Sid, registry);

        Assert.False(context.UserCertain);
        Assert.False(context.UserHiveLoaded);
        Assert.False(context.ClassesHiveLoaded);
    }

    [Fact]
    public void Create_NullArguments_Throw()
    {
        var registry = new FakeRegistry();
        var user = new FakeUserContext { Sid = Sid };
        Assert.Throws<ArgumentNullException>(() => AutostartContext.Create(null!, Sid, registry));
        Assert.Throws<ArgumentNullException>(() => AutostartContext.Create(user, null!, registry));
        Assert.Throws<ArgumentNullException>(() => AutostartContext.Create(user, Sid, null!));
    }
}
