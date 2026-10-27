using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DSOptimizer.Core.Hardware;

namespace DSOptimizer.Core.Prep;

/// <summary>Stan BitLockera dysku systemowego (z <c>Win32_EncryptableVolume</c>).</summary>
public enum BitLockerState
{
    On,
    Encrypting,
    EncryptedProtectionOff,
    Decrypting,
    Off,
    Unknown,
}

/// <summary>Reguły strony „Przygotowanie”: producenci, Podstawowa karta Microsoft, aktywacja, BitLocker, maszyna wirtualna.</summary>
public static class PrepRules
{
    private const string MicrosoftCorporation = "Microsoft Corporation";
    private const string VirtualMachineModel = "Virtual Machine";

    private static readonly Regex PciVendor = new(@"^PCI\\VEN_([0-9A-F]{4})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Nazwy z display.inf / displayoverride.inf (docs/prep/README.md); InfFileName tej karty nie zaobserwowano (R14).
    private static readonly string[] BasicDisplayPrefixes =
    {
        "Microsoft Basic Display Adapter",
        "Podstawowa karta graficzna Microsoft",
        "Podstawowa karta wideo Microsoft",
    };

    public static CpuVendor CpuVendorOf(string? manufacturer) => manufacturer?.Trim() switch
    {
        { } m when string.Equals(m, "GenuineIntel", StringComparison.OrdinalIgnoreCase) => CpuVendor.Intel,
        { } m when string.Equals(m, "AuthenticAMD", StringComparison.OrdinalIgnoreCase) => CpuVendor.Amd,
        _ => CpuVendor.Unknown,
    };

    /// <summary>Producent karty z <c>PCI\VEN_xxxx</c>; inne magistrale (VMBUS, ROOT, USB) = nieznany.</summary>
    public static GpuVendor GpuVendorOf(string? pnpDeviceId)
    {
        var match = PciVendor.Match(pnpDeviceId?.Trim() ?? string.Empty);
        if (!match.Success)
        {
            return GpuVendor.Unknown;
        }

        return match.Groups[1].Value.ToUpperInvariant() switch
        {
            "10DE" => GpuVendor.Nvidia,
            "1002" => GpuVendor.Amd,
            "8086" => GpuVendor.Intel,
            _ => GpuVendor.Unknown,
        };
    }

    /// <summary>Karta bez sterownika producenta: wyłącznie po nazwie (prefiks, bez wielkości liter, po <c>Trim</c>).</summary>
    public static bool IsBasicDisplay(PrepGpu gpu)
    {
        var name = gpu?.Name?.Trim();
        return !string.IsNullOrEmpty(name) && BasicDisplayPrefixes.Any(p => name!.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary><c>SoftwareLicensingProduct.LicenseStatus</c>; brak wiersza (oba pola null) = brak klucza.</summary>
    public static ActivationStatus Activation(ActivationInfo info)
    {
        if (info is null)
        {
            return ActivationStatus.Unknown;
        }

        if (info.LicenseStatus is null && info.ProductName is null)
        {
            return ActivationStatus.NoKey;
        }

        return info.LicenseStatus switch
        {
            1 => ActivationStatus.Licensed,
            0 => ActivationStatus.Unlicensed,
            2 or 3 or 4 or 6 => ActivationStatus.Grace,
            5 => ActivationStatus.Notification,
            _ => ActivationStatus.Unknown,
        };
    }

    /// <summary>Stan woluminu <paramref name="systemDrive"/> (np. <c>C:</c>); brak woluminu albo nieznane kody = <see cref="BitLockerState.Unknown"/>.</summary>
    public static BitLockerState SystemDrive(IReadOnlyList<BitLockerVolume> volumes, string systemDrive)
    {
        var letter = Letter(systemDrive);
        if (volumes is null || letter.Length == 0)
        {
            return BitLockerState.Unknown;
        }

        var volume = volumes.FirstOrDefault(v => v is not null && string.Equals(Letter(v.DriveLetter), letter, StringComparison.OrdinalIgnoreCase));
        if (volume is null)
        {
            return BitLockerState.Unknown;
        }

        return (volume.ConversionStatus, volume.ProtectionStatus) switch
        {
            (2 or 4, _) => BitLockerState.Encrypting,
            (1, 1) => BitLockerState.On,
            (1, 0) => BitLockerState.EncryptedProtectionOff,
            (3 or 5, _) => BitLockerState.Decrypting,
            (0, _) => BitLockerState.Off,
            _ => BitLockerState.Unknown,
        };
    }

    /// <summary>Hyper-V: <c>Microsoft Corporation</c> + <c>Virtual Machine</c> (pomiar VM, docs/prep/README.md). Surface ma tego samego producenta.</summary>
    public static bool IsVirtualMachine(BoardInfo board)
        => board is not null
            && string.Equals(board.SystemManufacturer?.Trim(), MicrosoftCorporation, StringComparison.OrdinalIgnoreCase)
            && string.Equals(board.SystemModel?.Trim(), VirtualMachineModel, StringComparison.OrdinalIgnoreCase);

    private static string Letter(string? drive) => drive?.Trim().TrimEnd('\\') ?? string.Empty;
}
