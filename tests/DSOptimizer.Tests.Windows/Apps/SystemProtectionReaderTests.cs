using System;
using System.Management;
using DSOptimizer.Tests.Windows.Support;
using DSOptimizer.Windows.Apps;
using Microsoft.Win32;
using Xunit;

namespace DSOptimizer.Tests.Windows.Apps;

/// <summary>Odczyt stanu ochrony systemu na piaskownicy HKCU (wstrzyknięty korzeń i GUID woluminu); prawdziwej ochrony nic nie zmienia.</summary>
[Trait("Category", "Windows")]
public class SystemProtectionReaderTests
{
    private const string VolumeId = @"\\?\Volume{11111111-2222-3333-4444-555555555555}\";

    private static string Relative(RegistrySandbox sandbox) => sandbox.HkcuPath.Substring(@"HKCU\".Length) + @"\Clients";

    private static SystemProtectionReader Reader(RegistrySandbox sandbox, Func<string?>? volume = null)
        => new(RegistryHive.CurrentUser, Relative(sandbox), volume ?? (() => VolumeId));

    private static void SetClients(RegistrySandbox sandbox, object value, RegistryValueKind kind)
    {
        using var root = sandbox.OpenOrCreate();
        using var clients = root.CreateSubKey("Clients")!;
        clients.SetValue(SystemProtectionReader.ClientsValueName, value, kind);
    }

    [Fact]
    public void Enabled_WhenAnEntryNamesTheSystemVolume()
    {
        using var sandbox = new RegistrySandbox();
        SetClients(
            sandbox,
            new[] { @"\\?\Volume{99999999-0000-0000-0000-000000000000}\:Dane (D%3A)", @"\\?\Volume{11111111-2222-3333-4444-555555555555}\:Windows (C%3A)" },
            RegistryValueKind.MultiString);

        Assert.True(Reader(sandbox).IsEnabled());
    }

    [Fact]
    public void Disabled_WhenOnlyOtherVolumesAreListed()
    {
        using var sandbox = new RegistrySandbox();
        SetClients(sandbox, new[] { @"\\?\Volume{99999999-0000-0000-0000-000000000000}\:Dane (D%3A)" }, RegistryValueKind.MultiString);

        Assert.False(Reader(sandbox).IsEnabled());
    }

    [Fact]
    public void Disabled_WhenValueIsMissing()
    {
        using var sandbox = new RegistrySandbox();
        using (var root = sandbox.OpenOrCreate())
        {
            root.CreateSubKey("Clients")!.Dispose();
        }

        Assert.False(Reader(sandbox).IsEnabled());
    }

    [Fact]
    public void Disabled_WhenKeyIsMissing()
    {
        using var sandbox = new RegistrySandbox();

        Assert.False(Reader(sandbox).IsEnabled());
    }

    [Fact]
    public void Unknown_WhenValueHasWrongType()
    {
        using var sandbox = new RegistrySandbox();
        SetClients(sandbox, VolumeId, RegistryValueKind.String);

        Assert.Null(Reader(sandbox).IsEnabled());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"\\?\Volume\")]
    public void Unknown_WhenSystemVolumeIdIsMissingOrMalformed(string? volume)
    {
        using var sandbox = new RegistrySandbox();
        SetClients(sandbox, new[] { VolumeId + ":Windows (C%3A)" }, RegistryValueKind.MultiString);

        Assert.Null(Reader(sandbox, () => volume).IsEnabled());
    }

    [Fact]
    public void Unknown_WhenVolumeQueryFails()
    {
        using var sandbox = new RegistrySandbox();

        Assert.Null(Reader(sandbox, () => throw new ManagementException("WMI niedostępne.")).IsEnabled());
    }

    [Fact]
    public void Host_ReadOnly_DoesNotThrow()
    {
        // Odczyt prawdziwego HKLM i Win32_Volume hosta – tylko do odczytu; wynik zależy od maszyny.
        var error = Record.Exception(() => new SystemProtectionReader().IsEnabled());

        Assert.Null(error);
    }
}
