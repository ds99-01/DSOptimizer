using System;
using System.Linq;
using DSOptimizer.Windows.Prep;
using Xunit;
using Xunit.Abstractions;

namespace DSOptimizer.Tests.Windows;

/// <summary>Tylko odczyt hosta (CIM); reguły łączenia i wyboru numeru seryjnego testowane na danych, bez systemu.</summary>
[Trait("Category", "Windows")]
public class WindowsPrepReaderTests
{
    private readonly ITestOutputHelper _output;

    public WindowsPrepReaderTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void MergeGraphics_MatchesByPnpIdCaseInsensitive()
    {
        var controllers = new[] { (Name: (string?)"GPU", PnpDeviceId: (string?)"PCI\\VEN_10DE&DEV_2704\\4&1", Inf: (string?)"oem12.inf", Version: (string?)"32.0.1", Date: (DateTime?)null) };
        var signed = new[] { (DeviceId: (string?)"pci\\ven_10de&dev_2704\\4&1", Provider: (string?)"NVIDIA", Date: (DateTime?)new DateTime(2026, 9, 1)) };

        var gpu = Assert.Single(WindowsPrepReader.MergeGraphics(controllers, signed));

        Assert.Equal("GPU", gpu.Name);
        Assert.Equal("oem12.inf", gpu.InfFileName);
        Assert.Equal("32.0.1", gpu.DriverVersion);
        Assert.Equal("NVIDIA", gpu.DriverProvider);
        Assert.Equal(new DateTime(2026, 9, 1), gpu.DriverDate);
    }

    [Fact]
    public void MergeGraphics_ControllerDateWinsOverSignedDriverDate()
    {
        var controllers = new[] { (Name: (string?)"GPU", PnpDeviceId: (string?)"PCI\\A", Inf: (string?)null, Version: (string?)null, Date: (DateTime?)new DateTime(2026, 1, 2)) };
        var signed = new[] { (DeviceId: (string?)"PCI\\A", Provider: (string?)"X", Date: (DateTime?)new DateTime(2025, 5, 5)) };

        var gpu = Assert.Single(WindowsPrepReader.MergeGraphics(controllers, signed));

        Assert.Equal(new DateTime(2026, 1, 2), gpu.DriverDate);
    }

    [Fact]
    public void MergeGraphics_ControllerWithoutSignedDriver_KeepsControllerData()
    {
        var controllers = new[] { (Name: (string?)"Microsoft Basic Display Adapter", PnpDeviceId: (string?)"ROOT\\BASIC", Inf: (string?)"display.inf", Version: (string?)"10.0", Date: (DateTime?)new DateTime(2006, 6, 21)) };
        var signed = new[] { (DeviceId: (string?)"PCI\\OTHER", Provider: (string?)"Other", Date: (DateTime?)null) };

        var gpu = Assert.Single(WindowsPrepReader.MergeGraphics(controllers, signed));

        Assert.Equal("display.inf", gpu.InfFileName);
        Assert.Equal(new DateTime(2006, 6, 21), gpu.DriverDate);
        Assert.Null(gpu.DriverProvider);
    }

    [Fact]
    public void FirstSerial_SkipsPlaceholderAndBlank()
    {
        Assert.Equal("ABC123", WindowsPrepReader.FirstSerial("To Be Filled By O.E.M.", "ABC123"));
        Assert.Equal("ABC123", WindowsPrepReader.FirstSerial("  ", "ABC123"));
        Assert.Equal("ABC123", WindowsPrepReader.FirstSerial("Default string", "ABC123"));
        Assert.Equal("ABC123", WindowsPrepReader.FirstSerial("System Serial Number", "ABC123"));
        Assert.Equal("FIRST", WindowsPrepReader.FirstSerial("FIRST", "SECOND"));
        Assert.Null(WindowsPrepReader.FirstSerial(null, "Default string"));
    }

    [Fact]
    public void EveryRead_DoesNotThrowOnHost()
    {
        var reader = new WindowsPrepReader();

        _ = reader.ReadProduct();
        _ = reader.ReadActivation();
        _ = reader.ReadGraphics();
        _ = reader.ReadProblemDevices();
    }

    [Fact]
    public void HostReadout_PrintsAllParts()
    {
        var reader = new WindowsPrepReader();

        var product = reader.ReadProduct();
        var activation = reader.ReadActivation();
        var graphics = reader.ReadGraphics();
        var devices = reader.ReadProblemDevices();

        _output.WriteLine($"Product: Version={product.Version}; Serial={MaskSerial(product.SerialNumber)}; CpuManufacturer={product.CpuManufacturer}");
        _output.WriteLine($"Activation: LicenseStatus={activation.LicenseStatus}; ProductName={activation.ProductName}");
        foreach (var g in graphics)
        {
            _output.WriteLine($"Gpu: {g.Name}; pnp={g.PnpDeviceId}; inf={g.InfFileName}; ver={g.DriverVersion}; date={g.DriverDate:yyyy-MM-dd}; provider={g.DriverProvider}");
        }

        foreach (var d in devices)
        {
            _output.WriteLine($"Problem: {d.Name}; code={d.ErrorCode}; hwids={string.Join(" | ", d.HardwareIds.Take(3))}");
        }

        Assert.NotNull(graphics);
        Assert.NotNull(devices);
    }

    [Theory]
    [InlineData("PF2KQ7ZT", "*****7ZT")]
    [InlineData("AB", "AB")]
    [InlineData(null, "")]
    public void MaskSerial_KeepsLastThree(string? serial, string expected) => Assert.Equal(expected, MaskSerial(serial));

    /// <summary>Numer seryjny hosta w wyjściu testu: tylko ostatnie 3 znaki, reszta jako gwiazdki.</summary>
    private static string MaskSerial(string? serial)
        => serial is null || serial.Length <= 3 ? serial ?? string.Empty : new string('*', serial.Length - 3) + serial.Substring(serial.Length - 3);
}
