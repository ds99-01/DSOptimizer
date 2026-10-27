using System;
using System.Linq;
using System.Management;
using DSOptimizer.Windows;
using Xunit;

namespace DSOptimizer.Tests.Windows;

/// <summary>Tylko odczyt listy punktów – testy nigdy nie tworzą punktów przywracania na hoście.</summary>
[Trait("Category", "Windows")]
public class WmiRestorePointApiTests
{
    [SystemRestoreWmiFact]
    public void GetRestorePoints_ReadsListWithoutThrowing_SequenceNumbersUnique()
    {
        var points = new WmiRestorePointApi().GetRestorePoints();

        Assert.NotNull(points);
        Assert.Equal(points.Count, points.Select(p => p.SequenceNumber).Distinct().Count());
        Assert.All(points, p => Assert.NotNull(p.Description));
    }
}

/// <summary>
/// Fakt pomijany z powodem, gdy w <c>root\default</c> nie ma klasy WMI <c>SystemRestore</c> (np. Windows Server na runnerze CI).
/// Wykrywana jest sama klasa, nie typ systemu ani zmienne środowiskowe.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class SystemRestoreWmiFactAttribute : FactAttribute
{
    public SystemRestoreWmiFactAttribute()
    {
        if (!SystemRestoreClassExists())
        {
            Skip = @"Brak klasy WMI root\default:SystemRestore na tym systemie (np. Windows Server na runnerze CI) – odczyt punktów przywracania nie jest tu dostępny.";
        }
    }

    private static bool SystemRestoreClassExists()
    {
        try
        {
            using var cls = new ManagementClass(new ManagementScope(@"\\.\root\default"), new ManagementPath("SystemRestore"), null);
            cls.Get();
            return true;
        }
        catch (ManagementException)
        {
            return false;
        }
    }
}
