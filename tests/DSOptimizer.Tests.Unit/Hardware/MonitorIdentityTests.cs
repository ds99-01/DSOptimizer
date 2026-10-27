using DSOptimizer.Core.Hardware;
using Xunit;

namespace DSOptimizer.Tests.Unit.Hardware;

public class MonitorIdentityTests
{
    private const string Interface = @"\\?\DISPLAY#GSM5B7F#5&1a2b3c4d&0&UID4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";

    [Fact]
    public void Matches_SameMonitor_IsTrue()
        => Assert.True(MonitorIdentity.Matches(@"DISPLAY\GSM5B7F\5&1a2b3c4d&0&UID4352_0", Interface));

    [Fact]
    public void Matches_OtherUid_IsFalse()
        => Assert.False(MonitorIdentity.Matches(@"DISPLAY\GSM5B7F\5&1a2b3c4d&0&UID4357_0", Interface));

    [Fact]
    public void Matches_OtherModel_IsFalse()
        => Assert.False(MonitorIdentity.Matches(@"DISPLAY\LEN63FF\5&1a2b3c4d&0&UID4352_0", Interface));

    [Fact]
    public void Matches_CaseDiffers_IsTrue()
        => Assert.True(MonitorIdentity.Matches(@"display\gsm5b7f\5&1A2B3C4D&0&uid4352_0", Interface.ToUpperInvariant()));

    [Fact]
    public void Matches_WmiNameWithoutIndexSuffix_IsTrue()
        => Assert.True(MonitorIdentity.Matches(@"DISPLAY\GSM5B7F\5&1a2b3c4d&0&UID4352", Interface));

    [Theory]
    [InlineData("", "")]
    [InlineData("", Interface)]
    [InlineData(@"DISPLAY\GSM5B7F\5&1a2b3c4d&0&UID4352_0", "")]
    [InlineData("_0", @"\\?\#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}")]
    public void Matches_EmptyNames_IsFalse(string wmiInstanceName, string deviceInterfaceName)
        => Assert.False(MonitorIdentity.Matches(wmiInstanceName, deviceInterfaceName));

    [Fact]
    public void Matches_Nulls_IsFalse()
        => Assert.False(MonitorIdentity.Matches(null, null));
}
