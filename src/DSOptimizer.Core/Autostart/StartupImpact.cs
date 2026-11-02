namespace DSOptimizer.Core.Autostart;

/// <summary>Wpływ na uruchamianie wg progów Menedżera zadań.</summary>
public enum StartupImpactLevel
{
    Low,
    Medium,
    High,
}

/// <summary>
/// Progi wpływu zgodne z kolumną „Wpływ na uruchamianie” Menedżera zadań (sprawdzone na VM 26H2): wysoki = procesor ponad 1 s albo dysk
/// ponad 3 MB, średni = procesor od 0,3 s albo dysk od 0,3 MB, niski = poniżej obu.
/// </summary>
public static class StartupImpact
{
    public const long HighCpuMicroseconds = 1_000_000;
    public const long MediumCpuMicroseconds = 300_000;
    public const long HighDiskBytes = 3_145_728;
    public const long MediumDiskBytes = 314_573;

    public static StartupImpactLevel Of(long cpuMicroseconds, long diskBytes)
    {
        if (cpuMicroseconds > HighCpuMicroseconds || diskBytes > HighDiskBytes)
        {
            return StartupImpactLevel.High;
        }

        return cpuMicroseconds >= MediumCpuMicroseconds || diskBytes >= MediumDiskBytes ? StartupImpactLevel.Medium : StartupImpactLevel.Low;
    }
}
