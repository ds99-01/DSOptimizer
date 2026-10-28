using System.Globalization;
using System.Runtime.CompilerServices;

namespace DSOptimizer.Tests.Unit.Support;

/// <summary>
/// Stała kultura procesu testów (pl-PL), ustawiana przy ładowaniu modułu, żeby wyniki nie zależały od ustawień komputera.
/// </summary>
internal static class TestCulture
{
#pragma warning disable CA2255 // Inicjalizator modułu w zestawie testów jest tu celowy: kultura ma być ustawiona przed każdym testem.
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Install()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("pl-PL");
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("pl-PL");
    }
}
