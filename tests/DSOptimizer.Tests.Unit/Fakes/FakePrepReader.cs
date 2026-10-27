using System;
using System.Collections.Generic;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Prep;

namespace DSOptimizer.Tests.Unit.Fakes;

/// <summary>Domyślnie zwraca stałe wartości; każdy odczyt można podmienić (rzucający albo blokujący).</summary>
public sealed class FakePrepReader : IPrepReader
{
    public Func<PrepProduct> Product { get; set; } = () => new PrepProduct("Test", "SN123", "GenuineIntel");

    public Func<ActivationInfo> Activation { get; set; } = () => new ActivationInfo(1, "Windows 11 Pro");

    public Func<IReadOnlyList<PrepGpu>> Graphics { get; set; } = () => new[]
    {
        new PrepGpu("RTX 5080", "PCI\\VEN_10DE&DEV_2C02", "oem1.inf", "32.0.15.0", new DateTime(2026, 1, 2), "NVIDIA"),
    };

    public Func<IReadOnlyList<ProblemDevice>> Devices { get; set; } = () => Array.Empty<ProblemDevice>();

    public PrepProduct ReadProduct() => Product();

    public ActivationInfo ReadActivation() => Activation();

    public IReadOnlyList<PrepGpu> ReadGraphics() => Graphics();

    public IReadOnlyList<ProblemDevice> ReadProblemDevices() => Devices();
}
