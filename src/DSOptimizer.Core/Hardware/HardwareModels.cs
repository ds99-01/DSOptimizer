using System;
using System.Collections.Generic;

namespace DSOptimizer.Core.Hardware;

/// <summary>Części specyfikacji odczytywane niezależnie (każda z własnym limitem czasu).</summary>
public enum HardwarePart
{
    Processor,
    Memory,
    Board,
    Graphics,
    Displays,
    Sound,
    Disks,
    Network,
    Windows,
    Tpm,
    SecureBoot,
    Vbs,
    BitLocker,
}

public sealed record CpuInfo(string? Name, int? Cores, int? Threads);

public sealed record MemoryModule(string? Slot, ulong? CapacityBytes, int? SpeedMts, int? SmbiosType, string? Manufacturer, string? PartNumber);

public sealed record MemoryInfo(IReadOnlyList<MemoryModule> Modules, int? SlotCount);

public enum FirmwareKind
{
    Unknown,
    Uefi,
    Legacy,
}

public sealed record BoardInfo(
    string? Manufacturer,
    string? Product,
    string? BiosVersion,
    DateTime? BiosDate,
    FirmwareKind Firmware,
    string? SystemManufacturer,
    string? SystemModel);

public sealed record GpuInfo(string? Name, ulong? MemoryBytes, string? DriverVersion);

public sealed record DisplayInfo(string? Name, int? WidthCm, int? HeightCm, int? Width, int? Height, int? RefreshHz);

public sealed record DiskInfo(
    int? Number,
    string? Model,
    ulong? SizeBytes,
    int? BusType,
    int? MediaType,
    int? TemperatureC,
    string? SerialNumber,
    int? HealthStatus);

public enum NetworkKind
{
    Other,
    Ethernet,
    WiFi,
}

public sealed record NetworkAdapterInfo(string? Name, NetworkKind Kind, bool Connected, ulong? SpeedBps);

public sealed record WindowsInfo(string? Edition, string? DisplayVersion, int Build, int Revision, DateTime? InstallDate);

public sealed record TpmInfo(string? SpecVersion, bool Enabled, bool Activated);

public enum SecureBootState
{
    Unknown,
    On,
    Off,
    LegacyBios,
}

public sealed record VbsInfo(int? Status, bool HvciRunning);

public sealed record BitLockerVolume(string DriveLetter, int? ConversionStatus, int? ProtectionStatus);
