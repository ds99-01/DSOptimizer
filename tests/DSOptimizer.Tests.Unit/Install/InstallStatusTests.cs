using System;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Install;
using Xunit;

namespace DSOptimizer.Tests.Unit.Install;

public class InstallStatusTests
{
    private static InstallEntry Entry(AppMatch match)
        => new("install.x", "Vendor.X", "X", "Opis.", InstallCategory.Tools, match, null, "https://example.com/");

    private static InstalledApp Program(string name, string? publisher, bool systemComponent = false, string? parent = null)
        => new(
            "win32:HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\" + name,
            AppKind.Program,
            name,
            publisher,
            "1.0",
            AppScope.Machine,
            null,
            null,
            new Win32Info("HKLM\\…\\" + name, "C:\\u.exe", null, false, parent, null, systemComponent));

    private static InstalledApp Store(string package)
        => new(
            "appx:" + package + "_8wekyb3d8bbwe",
            AppKind.Store,
            package,
            "Microsoft",
            "1.0",
            AppScope.User,
            "S-1-5-21-1",
            new AppxInfo(package + "_1.0_x64__8wekyb3d8bbwe", package + "_8wekyb3d8bbwe", package, false, false, false, "Store", false),
            null);

    [Fact]
    public void Win32_DisplayNameWildcardAndPublisher_Installed()
    {
        var entry = Entry(new AppMatch(null, "7-Zip*", "Igor Pavlov"));

        Assert.True(InstallStatus.IsInstalled(entry, new[] { Program("Notepad", "X"), Program("7-Zip 26.05 (x64)", "Igor Pavlov") }));
    }

    [Fact]
    public void Win32_PublisherDiffers_NotInstalled()
    {
        var entry = Entry(new AppMatch(null, "7-Zip*", "Igor Pavlov"));

        Assert.False(InstallStatus.IsInstalled(entry, new[] { Program("7-Zip 26.05 (x64)", "Ktoś inny") }));
    }

    [Fact]
    public void Appx_PackageName_Installed()
    {
        var entry = Entry(new AppMatch("Microsoft.PowerToys*", null, null));

        Assert.True(InstallStatus.IsInstalled(entry, new[] { Store("Microsoft.PowerToys.SparseApp") }));
    }

    [Fact]
    public void SystemComponentRuntime_CountsAsInstalled()
    {
        // Review Focus 3: surowy odczyt (przed AppClassifier) liczy także wpisy SystemComponent i ParentKeyName.
        var entry = Entry(new AppMatch(null, "Microsoft Visual C++ * Runtime*", "Microsoft Corporation"));
        var runtime = Program(
            "Microsoft Visual C++ 2022 X64 Minimum Runtime - 14.51.36247",
            "Microsoft Corporation",
            systemComponent: true,
            parent: "{0D3E9E15-DE7A-300B-96F1-B4AF12B96488}");

        Assert.True(InstallStatus.IsInstalled(entry, new[] { runtime }));
    }

    [Fact]
    public void NoMatch_NotInstalled()
    {
        var entry = Entry(new AppMatch(null, "VLC media player", "VideoLAN"));

        Assert.False(InstallStatus.IsInstalled(entry, new[] { Program("VLC media player 4", "VideoLAN"), Store("VideoLAN.VLC") }));
        Assert.False(InstallStatus.IsInstalled(entry, Array.Empty<InstalledApp>()));
    }

    [Fact]
    public void NullArguments_Throw()
    {
        var entry = Entry(new AppMatch(null, "X", null));

        Assert.Throws<ArgumentNullException>(() => InstallStatus.IsInstalled(null!, Array.Empty<InstalledApp>()));
        Assert.Throws<ArgumentNullException>(() => InstallStatus.IsInstalled(entry, null!));
    }
}
