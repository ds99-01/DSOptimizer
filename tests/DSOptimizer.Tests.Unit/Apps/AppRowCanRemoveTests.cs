using DSOptimizer.Core.Apps;
using Xunit;

namespace DSOptimizer.Tests.Unit.Apps;

public class AppRowCanRemoveTests
{
    private static readonly InstalledApp Program = new("win32:x", AppKind.Program, "X", null, null, AppScope.Machine, null, null,
        new Win32Info(@"HKLM\X", "\"C:\\x.exe\"", null, false, null, null, false));

    private static readonly InstalledApp Store = new("appx:y", AppKind.Store, "Y", null, null, AppScope.User, "S-1-5-21-1000",
        new AppxInfo("y_1_x64__a", "y_a", "y", false, false, false, "Store", false), null);

    private static readonly UninstallPlan Plan = new(@"C:\x.exe", string.Empty, UninstallMode.Quiet);

    [Fact]
    public void RemovableWithData_CanRemove()
    {
        Assert.True(new AppRow(Program, AppGroup.Other, null, null, null, null, AppRemovability.Removable, null, Plan).CanRemove);
        Assert.True(new AppRow(Store, AppGroup.Suggested, null, null, null, null, AppRemovability.Removable, null, null).CanRemove);
    }

    [Fact]
    public void ProgramWithoutPlan_CannotRemove()
        => Assert.False(new AppRow(Program, AppGroup.Other, null, null, null, null, AppRemovability.Removable, null, null).CanRemove);

    [Fact]
    public void StoreWithoutAppx_CannotRemove()
        => Assert.False(new AppRow(Store with { Appx = null }, AppGroup.Other, null, null, null, null, AppRemovability.Removable, null, null).CanRemove);

    [Fact]
    public void ProtectedGroupOrBlocked_CannotRemove()
    {
        Assert.False(new AppRow(Store, AppGroup.Protected, null, null, null, null, AppRemovability.Removable, null, null).CanRemove);
        Assert.False(new AppRow(Store, AppGroup.Other, null, null, null, null, AppRemovability.OtherAccount, "x", null).CanRemove);
    }
}
