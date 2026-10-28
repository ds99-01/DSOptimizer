using System;
using System.Collections.Generic;
using System.Linq;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Hardware;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Prep;
using Xunit;

namespace DSOptimizer.Tests.Unit.Localization;

public class CultureFormattingTests
{
    private static readonly DateTimeOffset Applied =
        new(new DateTime(2026, 10, 28, 14, 5, 9, DateTimeKind.Local));

    private static string BiosLine(DateTime date)
    {
        var snapshot = new PrepSnapshot
        {
            Board = Reading<BoardInfo>.Done(new BoardInfo(
                "ACME", "Model", "1.0", date, FirmwareKind.Uefi, "ACME", "Model")),
        };
        var steps = PrepSteps.Build(snapshot, "C:", settingsOpenable: true);
        return steps.Single(s => s.Kind == PrepStepKind.Computer).Lines.Single(l => l.Label == "BIOS").Value;
    }

    private static string DiskValue()
    {
        var snapshot = new HardwareSnapshot
        {
            Disks = Reading<IReadOnlyList<DiskInfo>>.Done(new[]
            {
                new DiskInfo(3, "Y", 1_500_000_000_000UL, 99, 0, null, null, null),
            }),
        };
        return SpecificationFormatter.Groups(snapshot).SelectMany(g => g.Fields).Single(f => f.Key == "Dysk 3").Value;
    }

    [Fact]
    public void History_Date_PolishUnchanged()
    {
        Assert.Equal("28.10.2026 14:05", HistoryText.Date(Applied));
        Assert.Equal("28.10.2026 14:05:09", HistoryText.Time(Applied));
    }

    [Fact]
    public void History_Date_Polish_KeepsLeadingZeros()
    {
        var early = new DateTimeOffset(new DateTime(2026, 3, 4, 8, 5, 9, DateTimeKind.Local));
        Assert.Equal("04.03.2026 08:05", HistoryText.Date(early));
        Assert.Equal("04.03.2026 08:05:09", HistoryText.Time(early));
    }

    [Fact]
    public void History_Date_English()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("28/10/2026 14:05", HistoryText.Date(Applied));
            Assert.Equal("28/10/2026 14:05:09", HistoryText.Time(Applied));
        }
    }

    [Fact]
    public void Prep_BiosDate_PolishUnchanged() =>
        Assert.Equal("1.0, 04.03.2025", BiosLine(new DateTime(2025, 3, 4)));

    [Fact]
    public void Prep_BiosDate_English()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("1.0, 04/03/2025", BiosLine(new DateTime(2025, 3, 4)));
        }
    }

    [Fact]
    public void Specification_Number_PolishUnchanged() => Assert.Equal("Y 1,5 TB", DiskValue());

    [Fact]
    public void Specification_Number_English()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("Y 1.5 TB", DiskValue());
        }
    }
}
