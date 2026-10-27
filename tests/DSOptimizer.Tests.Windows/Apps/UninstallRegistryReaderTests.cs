using System;
using System.Linq;
using System.Security.Principal;
using DSOptimizer.Core.Apps;
using DSOptimizer.Tests.Windows.Support;
using DSOptimizer.Windows.Apps;
using Microsoft.Win32;
using Xunit;

namespace DSOptimizer.Tests.Windows.Apps;

[Trait("Category", "Windows")]
public class UninstallRegistryReaderTests
{
    private static string CurrentSid => WindowsIdentity.GetCurrent().User!.Value;

    private static string Relative(RegistrySandbox sandbox) => sandbox.HkcuPath.Substring(@"HKCU\".Length);

    private static UninstallRegistryReader ReaderFor(RegistrySandbox sandbox, Func<string, string?>? indirect = null)
        => new(new[] { (Relative(sandbox), RegistryHive.CurrentUser, RegistryView.Registry64) }, indirect);

    [Fact]
    public void Read_MapsEveryValueIntoModel()
    {
        using var sandbox = new RegistrySandbox();
        using (var root = sandbox.OpenOrCreate())
        using (var key = root.CreateSubKey("Prog1")!)
        {
            key.SetValue("DisplayName", "Program Testowy");
            key.SetValue("Publisher", "Wydawca Sp. z o.o.");
            key.SetValue("DisplayVersion", "1.2.3");
            key.SetValue("UninstallString", @"C:\Program Files\X\unins000.exe /S");
            key.SetValue("QuietUninstallString", @"C:\Program Files\X\unins000.exe /VERYSILENT");
            key.SetValue("WindowsInstaller", 1, RegistryValueKind.DWord);
            key.SetValue("ParentKeyName", "OperatingSystem");
            key.SetValue("ReleaseType", "Update");
            key.SetValue("SystemComponent", 1, RegistryValueKind.DWord);
        }

        var reading = ReaderFor(sandbox).Read();

        Assert.Empty(reading.Warnings);
        var app = Assert.Single(reading.Apps);
        Assert.Equal(sandbox.HkcuPath + @"\Prog1", app.Win32!.KeyPath);
        Assert.Equal("win32:" + sandbox.HkcuPath + @"\Prog1", app.Id);
        Assert.Equal(AppKind.Program, app.Kind);
        Assert.Equal("Program Testowy", app.Name);
        Assert.Equal("Wydawca Sp. z o.o.", app.Publisher);
        Assert.Equal("1.2.3", app.Version);
        Assert.Equal(AppScope.User, app.Scope);
        Assert.Equal(CurrentSid, app.OwnerSid);
        Assert.Null(app.Appx);
        Assert.Equal(@"C:\Program Files\X\unins000.exe /S", app.Win32.UninstallString);
        Assert.Equal(@"C:\Program Files\X\unins000.exe /VERYSILENT", app.Win32.QuietUninstallString);
        Assert.True(app.Win32.WindowsInstaller);
        Assert.Equal("OperatingSystem", app.Win32.ParentKeyName);
        Assert.Equal("Update", app.Win32.ReleaseType);
        Assert.True(app.Win32.SystemComponent);
    }

    [Fact]
    public void Read_MinimalKey_HasNullsAndFalseFlags()
    {
        using var sandbox = new RegistrySandbox();
        using (var root = sandbox.OpenOrCreate())
        using (var key = root.CreateSubKey("Prog1")!)
        {
            key.SetValue("DisplayName", "Tylko nazwa");
        }

        var app = Assert.Single(ReaderFor(sandbox).Read().Apps);

        Assert.Null(app.Publisher);
        Assert.Null(app.Version);
        Assert.Null(app.Win32!.UninstallString);
        Assert.Null(app.Win32.QuietUninstallString);
        Assert.False(app.Win32.WindowsInstaller);
        Assert.False(app.Win32.SystemComponent);
        Assert.Null(app.Win32.ParentKeyName);
        Assert.Null(app.Win32.ReleaseType);
    }

    [Fact]
    public void Read_ExpandString_IsExpanded_AndPlainStringKeepsPercentSigns()
    {
        using var sandbox = new RegistrySandbox();
        using (var root = sandbox.OpenOrCreate())
        {
            using var expand = root.CreateSubKey("Expand")!;
            expand.SetValue("DisplayName", "Rozwijany");
            expand.SetValue("UninstallString", @"%SystemRoot%\System32\x.exe /S", RegistryValueKind.ExpandString);
            using var plain = root.CreateSubKey("Plain")!;
            plain.SetValue("DisplayName", "Zwykły");
            plain.SetValue("UninstallString", @"%SystemRoot%\System32\x.exe /S", RegistryValueKind.String);
        }

        var apps = ReaderFor(sandbox).Read().Apps;

        var expanded = apps.Single(a => a.Name == "Rozwijany").Win32!.UninstallString;
        Assert.Equal(Environment.ExpandEnvironmentVariables(@"%SystemRoot%\System32\x.exe /S"), expanded);
        Assert.DoesNotContain("%", expanded!);
        Assert.Equal(@"%SystemRoot%\System32\x.exe /S", apps.Single(a => a.Name == "Zwykły").Win32!.UninstallString);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Read_MissingOrBlankDisplayName_SkipsEntry(string? displayName)
    {
        using var sandbox = new RegistrySandbox();
        using (var root = sandbox.OpenOrCreate())
        {
            using var key = root.CreateSubKey("Bez")!;
            if (displayName is not null)
            {
                key.SetValue("DisplayName", displayName);
            }

            key.SetValue("UninstallString", @"C:\x.exe");
            using var good = root.CreateSubKey("Dobry")!;
            good.SetValue("DisplayName", "Dobry");
        }

        var reading = ReaderFor(sandbox).Read();

        Assert.Equal("Dobry", Assert.Single(reading.Apps).Name);
        Assert.Empty(reading.Warnings);
    }

    [Fact]
    public void Read_NoDisplayName_UsesLocalizedNameLoadedIndirectly()
    {
        using var sandbox = new RegistrySandbox();
        using (var root = sandbox.OpenOrCreate())
        {
            using var key = root.CreateSubKey("mstsc")!;
            key.SetValue("DisplayName_Localized", @"@%SystemRoot%\system32\mstsc.exe,-4000");
        }

        string? asked = null;
        var apps = ReaderFor(sandbox, source =>
        {
            asked = source;
            return "Połączenie pulpitu zdalnego";
        }).Read().Apps;

        Assert.Equal(@"@%SystemRoot%\system32\mstsc.exe,-4000", asked);
        Assert.Equal("Połączenie pulpitu zdalnego", Assert.Single(apps).Name);
    }

    [Fact]
    public void Read_LocalizedNameCannotBeLoaded_SkipsEntry()
    {
        using var sandbox = new RegistrySandbox();
        using (var root = sandbox.OpenOrCreate())
        {
            using var key = root.CreateSubKey("mstsc")!;
            key.SetValue("DisplayName_Localized", "@x.dll,-1");
        }

        Assert.Empty(ReaderFor(sandbox, _ => null).Read().Apps);
    }

    [Fact]
    public void Read_UserHiveRoot_SetsOwnerSidFromRootAndUserScope()
    {
        using var sandbox = new RegistrySandbox();
        using (var root = sandbox.OpenOrCreate())
        using (var key = root.CreateSubKey("Prog1")!)
        {
            key.SetValue("DisplayName", "Per user");
        }

        var reader = new UninstallRegistryReader(new[] { (CurrentSid + @"\" + Relative(sandbox), RegistryHive.Users, RegistryView.Registry64) });

        var app = Assert.Single(reader.Read().Apps);

        Assert.Equal(AppScope.User, app.Scope);
        Assert.Equal(CurrentSid, app.OwnerSid);
        Assert.Equal(sandbox.HkuPath(CurrentSid) + @"\Prog1", app.Win32!.KeyPath);
    }

    [Fact]
    public void Read_MissingRoot_IsEmptyWithoutWarning()
    {
        using var sandbox = new RegistrySandbox();

        var reading = ReaderFor(sandbox).Read();

        Assert.Empty(reading.Apps);
        Assert.Empty(reading.Warnings);
    }

    [Fact]
    public void Read_SeveralRoots_AreConcatenatedInOrder()
    {
        using var first = new RegistrySandbox();
        using var second = new RegistrySandbox();
        foreach (var (sandbox, name) in new[] { (first, "Pierwszy"), (second, "Drugi") })
        {
            using var root = sandbox.OpenOrCreate();
            using var key = root.CreateSubKey("K")!;
            key.SetValue("DisplayName", name);
        }

        var reader = new UninstallRegistryReader(new[]
        {
            (Relative(first), RegistryHive.CurrentUser, RegistryView.Registry64),
            (Relative(second), RegistryHive.CurrentUser, RegistryView.Registry64),
        });

        Assert.Equal(new[] { "Pierwszy", "Drugi" }, reader.Read().Apps.Select(a => a.Name));
    }

    [Theory]
    [InlineData(RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\K")]
    [InlineData(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\K")]
    [InlineData(RegistryHive.LocalMachine, RegistryView.Registry32, @"software\Microsoft\Windows\CurrentVersion\Uninstall", @"HKLM\software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\K")]
    [InlineData(RegistryHive.Users, RegistryView.Registry64, @"S-1-5-21-1\Software\Microsoft\Windows\CurrentVersion\Uninstall", @"HKU\S-1-5-21-1\Software\Microsoft\Windows\CurrentVersion\Uninstall\K")]
    [InlineData(RegistryHive.CurrentUser, RegistryView.Registry64, @"Software\X", @"HKCU\Software\X\K")]
    public void KeyPathFor_UsesRealRootAndTranslatesWow64View(RegistryHive hive, RegistryView view, string root, string expected)
    {
        Assert.Equal(expected, UninstallRegistryReader.KeyPathFor(hive, view, root, "K"));
    }

    [Fact]
    public void KeyExists_FindsKeyThenLosesIt()
    {
        var sandbox = new RegistrySandbox();
        string path;
        using (var root = sandbox.OpenOrCreate())
        {
            using var key = root.CreateSubKey("Prog1")!;
            path = sandbox.HkcuPath + @"\Prog1";
            Assert.True(UninstallRegistryReader.KeyExists(path));
        }

        sandbox.Dispose();

        Assert.False(UninstallRegistryReader.KeyExists(path));
    }

    [Fact]
    public void KeyExists_UnsupportedRoot_Throws()
    {
        Assert.Throws<ArgumentException>(() => UninstallRegistryReader.KeyExists(@"HKCR\x"));
    }

    [Fact]
    public void ForUser_ReadsHost_ReadOnly_AndEveryEntryKeyIsPresent()
    {
        var sid = CurrentSid;

        var reading = UninstallRegistryReader.ForUser(sid).Read();

        Assert.NotEmpty(reading.Apps);
        Assert.All(reading.Apps, app =>
        {
            var path = app.Win32!.KeyPath;
            Assert.True(path.StartsWith(@"HKLM\", StringComparison.Ordinal) || path.StartsWith(@"HKU\" + sid + @"\", StringComparison.Ordinal), path);
            Assert.True(UninstallRegistryReader.KeyExists(path), path);
            Assert.False(string.IsNullOrWhiteSpace(app.Name));
            if (path.StartsWith(@"HKLM\", StringComparison.Ordinal))
            {
                Assert.Equal(AppScope.Machine, app.Scope);
                Assert.Null(app.OwnerSid);
            }
            else
            {
                Assert.Equal(AppScope.User, app.Scope);
                Assert.Equal(sid, app.OwnerSid);
            }
        });
        Assert.Equal(reading.Apps.Count, reading.Apps.Select(a => a.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
