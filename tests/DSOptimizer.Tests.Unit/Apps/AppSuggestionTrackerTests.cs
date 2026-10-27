using System;
using DSOptimizer.Core.Apps;
using Xunit;

namespace DSOptimizer.Tests.Unit.Apps;

public class AppSuggestionTrackerTests
{
    private static AppRow Row(string id, AppGroup group = AppGroup.Suggested, AppRemovability removability = AppRemovability.Removable, bool program = false)
    {
        var app = program
            ? new InstalledApp(id, AppKind.Program, id, null, null, AppScope.Machine, null, null,
                new Win32Info(@"HKLM\X\" + id, "\"C:\\x.exe\"", null, false, null, null, false))
            : new InstalledApp(id, AppKind.Store, id, null, null, AppScope.User, "S-1-5-21-1000",
                new AppxInfo(id + "_1_x64__a", id + "_a", id, false, false, false, "Store", false), null);
        var plan = program ? new UninstallPlan(@"C:\x.exe", string.Empty, UninstallMode.Interactive) : null;
        return new AppRow(app, group, null, null, null, null, removability, null, plan);
    }

    [Fact]
    public void FirstRead_TakesSuggestedRemovableOnly()
    {
        var tracker = new AppSuggestionTracker();
        var rows = new[]
        {
            Row("a"),
            Row("b", AppGroup.Other),
            Row("c", AppGroup.Protected, AppRemovability.Protected),
            Row("d", removability: AppRemovability.OtherAccount),
            Row("e", program: true),
        };

        Assert.Equal(new[] { "a", "e" }, tracker.TakeNew(rows));
    }

    [Fact]
    public void SecondRead_SameIds_TakesNothing_EvenAfterManualUnselect()
    {
        var tracker = new AppSuggestionTracker();
        var rows = new[] { Row("a"), Row("b") };
        tracker.TakeNew(rows);

        Assert.Empty(tracker.TakeNew(rows));
    }

    [Fact]
    public void PartialRead_ThenFullRead_TakesOnlyNewIds()
    {
        var tracker = new AppSuggestionTracker();
        Assert.Equal(new[] { "prog" }, tracker.TakeNew(new[] { Row("prog", program: true) }));

        var full = new[] { Row("prog", program: true), Row("store1"), Row("store2") };

        Assert.Equal(new[] { "store1", "store2" }, tracker.TakeNew(full));
        Assert.Empty(tracker.TakeNew(full));
    }

    [Fact]
    public void RowBecomingRemovableLater_IsTakenThen()
    {
        var tracker = new AppSuggestionTracker();
        Assert.Empty(tracker.TakeNew(new[] { Row("a", removability: AppRemovability.UserUncertain) }));

        Assert.Equal(new[] { "a" }, tracker.TakeNew(new[] { Row("a") }));
    }

    [Fact]
    public void RemovableProgramWithoutPlan_NotTaken()
    {
        var tracker = new AppSuggestionTracker();
        var noPlan = Row("p", program: true) with { Plan = null };

        Assert.Empty(tracker.TakeNew(new[] { noPlan }));
    }

    [Fact]
    public void Null_Throws()
        => Assert.Throws<ArgumentNullException>(() => new AppSuggestionTracker().TakeNew(null!));
}
