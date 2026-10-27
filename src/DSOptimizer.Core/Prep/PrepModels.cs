using System;
using System.Collections.Generic;

namespace DSOptimizer.Core.Prep;

public enum CpuVendor
{
    Unknown,
    Intel,
    Amd,
}

public enum GpuVendor
{
    Unknown,
    Nvidia,
    Amd,
    Intel,
}

public enum ActivationStatus
{
    Licensed,
    Unlicensed,
    Grace,
    Notification,
    NoKey,
    Unknown,
}

/// <summary>Produkt komputera (Win32_ComputerSystemProduct) i producent procesora.</summary>
public sealed record PrepProduct(string? Version, string? SerialNumber, string? CpuManufacturer);

/// <summary>Stan licencji Windows; brak wiersza = <c>LicenseStatus</c> null i <c>ProductName</c> null.</summary>
public sealed record ActivationInfo(uint? LicenseStatus, string? ProductName);

public sealed record PrepGpu(
    string? Name,
    string? PnpDeviceId,
    string? InfFileName,
    string? DriverVersion,
    DateTime? DriverDate,
    string? DriverProvider);

public sealed record ProblemDevice(string? Name, uint ErrorCode, IReadOnlyList<string> HardwareIds);
