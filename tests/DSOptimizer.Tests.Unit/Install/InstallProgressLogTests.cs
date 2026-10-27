using System;
using System.Collections.Generic;
using System.Linq;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Install;
using Xunit;

namespace DSOptimizer.Tests.Unit.Install;

public class InstallProgressLogTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 24, 10, 0, 0, TimeSpan.Zero);

    private static readonly Dictionary<string, string> Titles = new(StringComparer.Ordinal)
    {
        ["install.7zip"] = "7-Zip",
        ["install.vlc"] = "VLC",
    };

    private static BatchStepEvent Progress(string id, string? stage, int? percent, int index = 0)
        => new(index, 2, BatchStepKind.Item, id, BatchStepPhase.Progress, T0, Stage: stage, Percent: percent);

    private static BatchStepEvent Phase(string id, BatchStepPhase phase, int index = 0)
        => new(index, 2, BatchStepKind.Item, id, phase, T0);

    [Fact]
    public void StageChange_OneLine_PercentAloneNoLine()
    {
        var log = new InstallProgressLog(Titles);
        var lines = new[]
        {
            log.OnStep(Phase("install.7zip", BatchStepPhase.Started)),
            log.OnStep(Progress("install.7zip", "Pobieranie", null)),
            log.OnStep(Progress("install.7zip", "Pobieranie", 10)),
            log.OnStep(Progress("install.7zip", "Pobieranie", 45)),
            log.OnStep(Progress("install.7zip", "Pobieranie", 100)),
            log.OnStep(Progress("install.7zip", "Instalowanie", null)),
            log.OnStep(Phase("install.7zip", BatchStepPhase.Finished)),
            log.OnStep(Progress("install.vlc", "Pobieranie", 3, 1)),
            log.OnStep(Progress("install.vlc", "Instalowanie", null, 1)),
        };

        Assert.Equal(
            new[] { "7-Zip: Pobieranie", "7-Zip: Instalowanie", "VLC: Pobieranie", "VLC: Instalowanie" },
            lines.Where(line => line is not null));
    }

    [Fact]
    public void SameLine_NeverTwice()
    {
        // Etap, który wraca do pobierania (np. instalator dociąga składnik): linia „Pobieranie” już była, więc nie wraca.
        var log = new InstallProgressLog(Titles);

        Assert.Equal("7-Zip: Pobieranie", log.OnStep(Progress("install.7zip", "Pobieranie", null)));
        Assert.Equal("7-Zip: Instalowanie", log.OnStep(Progress("install.7zip", "Instalowanie", null)));
        Assert.Null(log.OnStep(Progress("install.7zip", "Pobieranie", 5)));
        Assert.Null(log.OnStep(Progress("install.7zip", "Instalowanie", null)));
    }

    [Fact]
    public void PercentWithoutStage_NoLine()
    {
        var log = new InstallProgressLog(Titles);

        Assert.Null(log.OnStep(Progress("install.7zip", null, 40)));
        Assert.Null(log.OnStep(Progress("install.7zip", " ", null)));
    }

    [Fact]
    public void UnknownId_UsesId()
    {
        var log = new InstallProgressLog(Titles);

        Assert.Equal("winget:Vendor.App: Pobieranie", log.OnStep(Progress("winget:Vendor.App", "Pobieranie", null)));
    }

    [Fact]
    public void NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new InstallProgressLog(null!));
        Assert.Throws<ArgumentNullException>(() => new InstallProgressLog(Titles).OnStep(null!));
    }
}
