using System;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Settings;
using DSOptimizer.Tests.Windows.Support;
using DSOptimizer.Windows;
using Xunit;

namespace DSOptimizer.Tests.Windows;

[Trait("Category", "Windows")]
public class JsonFileSettingsStoreTests
{
    private static JsonFileSettingsStore Store(TempDirectory temp) => new(temp.AppRoot, new FixedClock());

    [Fact]
    public void Load_NoFile_DefaultsWithoutWarning()
    {
        using var temp = new TempDirectory();

        var result = Store(temp).Load();

        Assert.Equal(AppSettings.Default, result.Settings);
        Assert.Null(result.Warning);
    }

    [Fact]
    public void Save_ThenLoad_RoundTrips()
    {
        using var temp = new TempDirectory();
        var store = Store(temp);
        var settings = new AppSettings { CreateRestorePoint = false, Theme = ThemePreference.Dark };

        store.Save(settings);
        store.Save(settings);

        var result = store.Load();
        Assert.Equal(settings, result.Settings);
        Assert.Null(result.Warning);
        Assert.True(SecureDirectory.IsTrustedFile(store.FilePath));
        Assert.Equal(new[] { "settings.json" }, Directory.GetFiles(temp.Sub("app")).Select(Path.GetFileName).ToArray());
    }

    [Fact]
    public void Load_Corrupt_DefaultsAndKeepsFile()
    {
        using var temp = new TempDirectory();
        var store = Store(temp);
        store.Save(AppSettings.Default);
        File.WriteAllText(store.FilePath, "{");

        var result = store.Load();

        Assert.Equal(AppSettings.Default, result.Settings);
        Assert.StartsWith("Plik ustawień ", result.Warning, StringComparison.Ordinal);
        Assert.EndsWith("Używam ustawień domyślnych; plik zostanie nadpisany przy następnej zmianie.", result.Warning, StringComparison.Ordinal);
        Assert.True(File.Exists(store.FilePath));
    }

    [Fact]
    public void Load_TooLarge_Defaults()
    {
        using var temp = new TempDirectory();
        var store = Store(temp);
        store.Save(AppSettings.Default);
        File.WriteAllText(store.FilePath, new string(' ', JsonFileSettingsStore.MaxFileBytes + 1));

        var result = store.Load();

        Assert.Equal(AppSettings.Default, result.Settings);
        Assert.NotNull(result.Warning);
        Assert.Contains("za duży", result.Warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_Untrusted_SetsAsideAndDefaults()
    {
        using var temp = new TempDirectory();
        var store = Store(temp);
        store.Save(new AppSettings { CreateRestorePoint = false });
        var security = File.GetAccessControl(store.FilePath);
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier("S-1-5-32-545"), FileSystemRights.FullControl, AccessControlType.Allow));
        File.SetAccessControl(store.FilePath, security);

        var result = store.Load();

        Assert.Equal(AppSettings.Default, result.Settings);
        Assert.False(File.Exists(store.FilePath));
        var aside = Assert.Single(Directory.GetFiles(temp.Sub("app"), "settings.json.untrusted-*"));
        Assert.Equal(
            $"Plik ustawień miał niezaufanego właściciela lub uprawnienia i został odłożony: {aside}. Używam ustawień domyślnych.",
            result.Warning);
    }

    [Fact]
    public void Load_IoError_DefaultsWithWarning()
    {
        using var temp = new TempDirectory();
        var store = Store(temp);
        store.Save(AppSettings.Default);
        using var exclusive = new FileStream(store.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var result = store.Load();

        Assert.Equal(AppSettings.Default, result.Settings);
        Assert.NotNull(result.Warning);
    }

    [Fact]
    public void Save_LockedFile_ThrowsSettingsStoreException()
    {
        using var temp = new TempDirectory();
        var store = Store(temp);
        store.Save(AppSettings.Default);
        using var exclusive = new FileStream(store.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var ex = Assert.Throws<SettingsStoreException>(() => store.Save(AppSettings.Default));

        Assert.Contains(store.FilePath, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PeekLanguage_NoRoot_ReturnsNullAndCreatesNothing()
    {
        using var temp = new TempDirectory();

        Assert.Null(Store(temp).PeekLanguage());
        Assert.False(Directory.Exists(temp.Sub("app")));
    }

    [Fact]
    public void PeekLanguage_SavedEnglish_ReturnsEnglish()
    {
        using var temp = new TempDirectory();
        var store = Store(temp);
        store.Save(new AppSettings { Language = UiLanguage.English });

        Assert.Equal(UiLanguage.English, store.PeekLanguage());
    }

    [Fact]
    public void PeekLanguage_Corrupted_ReturnsNullAndLeavesFile()
    {
        using var temp = new TempDirectory();
        var store = Store(temp);
        store.Save(AppSettings.Default);
        File.WriteAllText(store.FilePath, "{");

        Assert.Null(store.PeekLanguage());
        Assert.Equal("{", File.ReadAllText(store.FilePath));
        Assert.Empty(Directory.GetFiles(temp.Sub("app"), "*.untrusted-*"));
    }

    [Fact]
    public void PeekLanguage_Untrusted_ReturnsNullAndDoesNotMove()
    {
        using var temp = new TempDirectory();
        var store = Store(temp);
        store.Save(new AppSettings { Language = UiLanguage.English });
        var security = File.GetAccessControl(store.FilePath);
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier("S-1-5-32-545"), FileSystemRights.FullControl, AccessControlType.Allow));
        File.SetAccessControl(store.FilePath, security);

        Assert.Null(store.PeekLanguage());
        Assert.True(File.Exists(store.FilePath));
        Assert.Empty(Directory.GetFiles(temp.Sub("app"), "*.untrusted-*"));
    }
}
