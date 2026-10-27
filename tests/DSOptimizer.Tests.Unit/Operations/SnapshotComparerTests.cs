using DSOptimizer.Core.Model;
using DSOptimizer.Core.Operations;
using Xunit;

namespace DSOptimizer.Tests.Unit.Operations;

public class SnapshotComparerTests
{
    [Fact]
    public void Registry_ComparesLocationAndValue()
    {
        var a = SnapshotRecord.ForRegistry(@"HKLM\X", "V", RegValue.FromDWord(1));
        Assert.True(SnapshotComparer.SameState(a, SnapshotRecord.ForRegistry(@"hklm\x", "v", RegValue.FromDWord(1))));
        Assert.False(SnapshotComparer.SameState(a, SnapshotRecord.ForRegistry(@"HKLM\X", "V", RegValue.FromDWord(2))));
        Assert.False(SnapshotComparer.SameState(a, SnapshotRecord.ForScript(null)));
    }

    [Fact]
    public void Service_ComparesStartTypeOnlyWhenItExisted()
    {
        var expected = SnapshotRecord.ForService("S", existed: true, ServiceStartType.Automatic, wasRunning: true);
        Assert.True(SnapshotComparer.SameState(expected, SnapshotRecord.ForService("S", true, ServiceStartType.Automatic, wasRunning: false)));
        Assert.False(SnapshotComparer.SameState(expected, SnapshotRecord.ForService("S", true, ServiceStartType.Manual, true)));
        Assert.True(SnapshotComparer.SameState(SnapshotRecord.ForService("S", false, null, false), expected));
    }

    [Fact]
    public void Script_ComparesCaptureWhenKnown()
    {
        Assert.True(SnapshotComparer.SameState(SnapshotRecord.ForScript(null), SnapshotRecord.ForScript("x")));
        Assert.True(SnapshotComparer.SameState(SnapshotRecord.ForScript("2"), SnapshotRecord.ForScript("2")));
        Assert.False(SnapshotComparer.SameState(SnapshotRecord.ForScript("2"), SnapshotRecord.ForScript("3")));
    }
}
