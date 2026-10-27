using System;
using DSOptimizer.Core.Hardware;
using DSOptimizer.Core.Prep;
using Xunit;

namespace DSOptimizer.Tests.Unit.Prep;

public class PrepRulesTests
{
    [Theory]
    [InlineData("GenuineIntel", CpuVendor.Intel)]
    [InlineData("  GenuineIntel ", CpuVendor.Intel)]
    [InlineData("AuthenticAMD", CpuVendor.Amd)]
    [InlineData("authenticamd", CpuVendor.Amd)]
    [InlineData("CentaurHauls", CpuVendor.Unknown)]
    [InlineData("Intel", CpuVendor.Unknown)]
    [InlineData("", CpuVendor.Unknown)]
    [InlineData(null, CpuVendor.Unknown)]
    public void CpuVendorOf_Table(string? manufacturer, CpuVendor expected)
        => Assert.Equal(expected, PrepRules.CpuVendorOf(manufacturer));

    [Theory]
    [InlineData(@"PCI\VEN_10DE&DEV_2704&SUBSYS_00000000&REV_A1\4&1", GpuVendor.Nvidia)]
    [InlineData(@"PCI\VEN_1002&DEV_73BF\4&2", GpuVendor.Amd)]
    [InlineData(@"PCI\VEN_8086&DEV_9A49\3&11583659&0&10", GpuVendor.Intel)]
    [InlineData(@"pci\ven_10de&dev_2704", GpuVendor.Nvidia)]
    [InlineData(@"PCI\VEN_1414&DEV_5353", GpuVendor.Unknown)]
    [InlineData(@"VMBUS\{DA0A7802-E377-4AAC-8E77-0558EB1073F8}\{5620E0C7-8062-4DCE-AEB7-520C7EF76171}", GpuVendor.Unknown)]
    [InlineData(@"ROOT\BASICDISPLAY\0000", GpuVendor.Unknown)]
    [InlineData(@"USB\VID_10DE&PID_0001", GpuVendor.Unknown)]
    [InlineData("", GpuVendor.Unknown)]
    [InlineData(null, GpuVendor.Unknown)]
    public void GpuVendorOf_Table(string? pnpDeviceId, GpuVendor expected)
        => Assert.Equal(expected, PrepRules.GpuVendorOf(pnpDeviceId));

    [Theory]
    [InlineData("Microsoft Basic Display Adapter", true)]
    [InlineData("  microsoft basic display adapter ", true)]
    [InlineData("Microsoft Basic Display Adapter (Low Resolution)", true)]
    [InlineData("Microsoft Basic Display Adapter (No ACPI support)", true)]
    [InlineData("Podstawowa karta graficzna Microsoft", true)]
    [InlineData("Podstawowa karta wideo Microsoft (niska rozdzielczość)", true)]
    [InlineData("Podstawowa karta wideo Microsoft (bez obsługi interfejsu ACPI)", true)]
    [InlineData("Microsoft Hyper-V Video", false)]
    [InlineData("NVIDIA GeForce RTX 4060", false)]
    [InlineData("Microsoft Basic Render Driver", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsBasicDisplay_ByNameOnly(string? name, bool expected)
    {
        // InfFileName celowo ignorowany (R14): nie zaobserwowano go dla tej karty.
        Assert.Equal(expected, PrepRules.IsBasicDisplay(new PrepGpu(name, null, "display.inf", null, null, null)));
        Assert.Equal(expected, PrepRules.IsBasicDisplay(new PrepGpu(name, null, null, null, null, null)));
    }

    [Fact]
    public void IsBasicDisplay_InfFileNameAloneIsNotEnough()
        => Assert.False(PrepRules.IsBasicDisplay(new PrepGpu("Karta X", @"PCI\VEN_10DE&DEV_1", "display.inf", null, null, null)));

    [Theory]
    [InlineData(1u, "Windows(R), Professional edition", ActivationStatus.Licensed)]
    [InlineData(0u, "Windows(R), Professional edition", ActivationStatus.Unlicensed)]
    [InlineData(2u, "x", ActivationStatus.Grace)]
    [InlineData(3u, "x", ActivationStatus.Grace)]
    [InlineData(4u, "x", ActivationStatus.Grace)]
    [InlineData(6u, "x", ActivationStatus.Grace)]
    [InlineData(5u, "x", ActivationStatus.Notification)]
    [InlineData(7u, "x", ActivationStatus.Unknown)]
    [InlineData(99u, null, ActivationStatus.Unknown)]
    [InlineData(null, null, ActivationStatus.NoKey)]
    [InlineData(null, "Windows(R), Professional edition", ActivationStatus.Unknown)]
    public void Activation_AllCodes(uint? status, string? productName, ActivationStatus expected)
        => Assert.Equal(expected, PrepRules.Activation(new ActivationInfo(status, productName)));

    [Theory]
    [InlineData(1, 1, BitLockerState.On)]
    [InlineData(1, 0, BitLockerState.EncryptedProtectionOff)]
    [InlineData(1, null, BitLockerState.Unknown)]
    [InlineData(2, 0, BitLockerState.Encrypting)]
    [InlineData(2, 1, BitLockerState.Encrypting)]
    [InlineData(4, 0, BitLockerState.Encrypting)]
    [InlineData(3, 0, BitLockerState.Decrypting)]
    [InlineData(5, 1, BitLockerState.Decrypting)]
    [InlineData(0, 0, BitLockerState.Off)]
    [InlineData(null, null, BitLockerState.Unknown)]
    [InlineData(9, 0, BitLockerState.Unknown)]
    public void SystemDrive_AllBranches(int? conversion, int? protection, BitLockerState expected)
    {
        var volumes = new[] { new BitLockerVolume("D:", 1, 1), new BitLockerVolume("C:", conversion, protection) };

        Assert.Equal(expected, PrepRules.SystemDrive(volumes, "C:"));
    }

    [Theory]
    [InlineData("C:")]
    [InlineData("c:")]
    [InlineData(@"C:\")]
    [InlineData(" C: ")]
    public void SystemDrive_MatchesLetterLoosely(string systemDrive)
        => Assert.Equal(BitLockerState.On, PrepRules.SystemDrive(new[] { new BitLockerVolume(" c: ", 1, 1) }, systemDrive));

    [Fact]
    public void SystemDrive_NoSystemVolume_Unknown()
    {
        var volumes = new[] { new BitLockerVolume("D:", 1, 1) };

        Assert.Equal(BitLockerState.Unknown, PrepRules.SystemDrive(volumes, "C:"));
        Assert.Equal(BitLockerState.Unknown, PrepRules.SystemDrive(Array.Empty<BitLockerVolume>(), "C:"));
        Assert.Equal(BitLockerState.Unknown, PrepRules.SystemDrive(null!, "C:"));
        Assert.Equal(BitLockerState.Unknown, PrepRules.SystemDrive(volumes, null!));
    }

    [Theory]
    [InlineData("Microsoft Corporation", "Virtual Machine", true)]
    [InlineData(" microsoft corporation ", " virtual machine ", true)]
    [InlineData("Microsoft Corporation", "Surface Laptop 5", false)]
    [InlineData("LENOVO", "Virtual Machine", false)]
    [InlineData("QEMU", "Standard PC (Q35 + ICH9, 2009)", false)]
    [InlineData(null, null, false)]
    public void IsVirtualMachine_Table(string? manufacturer, string? model, bool expected)
    {
        var board = new BoardInfo("Microsoft Corporation", "Virtual Machine", "Hyper-V UEFI Release v4.1", null, FirmwareKind.Uefi, manufacturer, model);

        Assert.Equal(expected, PrepRules.IsVirtualMachine(board));
    }

    [Fact]
    public void IsVirtualMachine_Null_False() => Assert.False(PrepRules.IsVirtualMachine(null!));

    [Fact]
    public void Activation_Null_Unknown() => Assert.Equal(ActivationStatus.Unknown, PrepRules.Activation(null!));

    [Fact]
    public void IsBasicDisplay_Null_False() => Assert.False(PrepRules.IsBasicDisplay(null!));
}
