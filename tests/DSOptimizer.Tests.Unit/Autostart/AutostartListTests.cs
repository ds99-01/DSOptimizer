using System;
using System.Collections.Generic;
using System.Linq;
using DSOptimizer.Core.Autostart;
using DSOptimizer.Tests.Unit.Fakes;
using Xunit;

namespace DSOptimizer.Tests.Unit.Autostart;

public class AutostartListTests
{
    private static readonly AutostartKnownCatalog Empty = AutostartKnownCatalog.Load("{ \"entries\": [] }");

    [Fact]
    public void Load_PassesWarningsAndMeasuredAt()
    {
        var measuredAt = new DateTimeOffset(2026, 11, 2, 8, 20, 5, TimeSpan.Zero);
        var slow = Entry("run:user:slow", "Slow", 2_000_000);
        var fast = Entry("run:user:fast", "Fast", 100_000);
        var reader = new FakeAutostartReader(new AutostartReading(
            new[] { fast, slow },
            UserEnvironment.FromVariables(new Dictionary<string, string>()),
            measuredAt,
            new[] { "w1", "w2" }));
        var context = AutostartClassifierTests.Context();

        var list = AutostartList.Load(reader, Empty, context);

        Assert.Equal(measuredAt, list.MeasuredAt);
        Assert.Equal(new[] { "w1", "w2" }, list.Warnings);
        Assert.Equal(new[] { "run:user:slow", "run:user:fast" }, list.Rows.Select(r => r.Id));
        Assert.Same(context, Assert.Single(reader.Contexts));
    }

    [Fact]
    public void Load_NullArguments_Throw()
    {
        var reader = new FakeAutostartReader();
        var context = AutostartClassifierTests.Context();

        Assert.Throws<ArgumentNullException>(() => AutostartList.Load(null!, Empty, context));
        Assert.Throws<ArgumentNullException>(() => AutostartList.Load(reader, null!, context));
        Assert.Throws<ArgumentNullException>(() => AutostartList.Load(reader, Empty, null!));
    }

    private static AutostartEntry Entry(string id, string name, long cpu) => new()
    {
        Id = id,
        Source = AutostartSource.RunUser,
        EntryName = name,
        Name = name,
        Location = "loc",
        FileStatus = AutostartFileStatus.Exists,
        Switch = new AutostartSwitch(AutostartSwitchKind.StartupApproved, "key", name, AutostartSwitchFormat.Valid, AutostartState.Enabled, null, null),
        Measurement = new StartupMeasurement(cpu, 0, new DateTimeOffset(2026, 11, 2, 8, 20, 5, TimeSpan.Zero), false),
    };
}
